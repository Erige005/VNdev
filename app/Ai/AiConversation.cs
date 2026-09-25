using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VNdev.App.Ai;

public enum ChatItemKind { User, Assistant, Activity, Proposal, Error }

/// <summary>Một mục hiện trong khung chat — khác lượt API: một lượt có thể sinh nhiều mục.</summary>
public sealed class ChatItem
{
    public ChatItemKind Kind { get; init; }
    public string Text { get; set; } = string.Empty;
    public bool Streaming { get; set; }
    public bool Thinking { get; set; }
    public Proposal? Proposal { get; init; }

    /// <summary>Lỗi về khoá/provider — khung chat hiện thêm nút mở Cài đặt.</summary>
    public bool SettingsHint { get; init; }
}

/// <summary>
/// Một cuộc trò chuyện với trợ lý, gắn với dự án đang mở.
/// </summary>
/// <remarks>
/// Sống trong <see cref="ProjectSession"/> chứ không trong panel: áp dụng một
/// đề xuất hay bấm Ctrl+Z đều dựng lại màn hình editor, và cuộc trò chuyện —
/// kể cả câu trả lời đang stream dở — phải còn nguyên sau đó.
/// </remarks>
public sealed class AiConversation
{
    private const int MaxSteps = 24;

    private readonly ProjectSession _session;
    private readonly ProjectTools _tools;
    private readonly List<ChatTurn> _turns = new();
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _decision;
    private Proposal? _pending;

    public List<ChatItem> Items { get; } = new();
    public bool Busy => _cts is not null;

    /// <summary>Có mục mới hoặc mục cũ đổi nội dung. null = dựng lại toàn bộ.</summary>
    public event Action<ChatItem?>? Changed;

    /// <summary>Một đề xuất vừa được ghi vào dự án — editor cần dựng lại để hiện thay đổi.</summary>
    public event Action? ProjectChanged;

    public AiConversation(ProjectSession session)
    {
        _session = session;
        _tools = new ProjectTools(session);
    }

    public void Reset()
    {
        Stop();
        _turns.Clear();
        Items.Clear();
        Changed?.Invoke(null);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _decision?.TrySetResult(false);
    }

    /// <summary>Người dùng bấm Áp dụng hoặc Bỏ qua trên thẻ đề xuất đang chờ.</summary>
    public void Decide(Proposal proposal, bool apply)
    {
        if (proposal != _pending) return;
        _decision?.TrySetResult(apply);
    }

    public async void Send(string text, string context)
    {
        if (Busy || string.IsNullOrWhiteSpace(text)) return;

        var config = AppSettings.Current.ActiveProvider();
        if (config is null)
        {
            AddItem(new ChatItem { Kind = ChatItemKind.Error, Text = "Chưa có nhà cung cấp AI nào. Thêm API key ở Cài đặt → AI Providers.", SettingsHint = true });
            return;
        }

        IChatProvider provider;
        try
        {
            provider = Providers.Create(config);
        }
        catch (AiException ex)
        {
            AddItem(new ChatItem { Kind = ChatItemKind.Error, Text = ex.Message, SettingsHint = true });
            return;
        }

        AddItem(new ChatItem { Kind = ChatItemKind.User, Text = text.Trim() });
        // Ngữ cảnh gắn vào chính lượt này và không bao giờ sửa lại về sau, để
        // phần lịch sử phía trước giữ nguyên từng byte — cache prompt mới ăn.
        _turns.Add(new ChatTurn { Role = ChatRole.User, Text = $"{text.Trim()}\n\n<ngu_canh_hien_tai>\n{context}\n</ngu_canh_hien_tai>" });

        _cts = new CancellationTokenSource();
        try
        {
            await RunLoop(provider, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AddItem(new ChatItem { Kind = ChatItemKind.Activity, Text = "⏹ Đã dừng." });
            RepairAfterStop();
        }
        catch (AiException ex)
        {
            AddItem(new ChatItem { Kind = ChatItemKind.Error, Text = ex.Message, SettingsHint = ex.Message.Contains("Cài đặt", StringComparison.Ordinal) });
            RepairAfterStop();
        }
        catch (Exception ex)
        {
            AddItem(new ChatItem { Kind = ChatItemKind.Error, Text = $"Lỗi không mong đợi: {ex.Message}" });
            RepairAfterStop();
        }
        finally
        {
            foreach (var item in Items.Where(i => i.Streaming))
            {
                item.Streaming = false;
                item.Thinking = false;
                Changed?.Invoke(item);
            }
            _cts?.Dispose();
            _cts = null;
            Changed?.Invoke(null);
        }
    }

    private async Task RunLoop(IChatProvider provider, CancellationToken ct)
    {
        for (var step = 0; step < MaxSteps; step++)
        {
            var item = AddItem(new ChatItem { Kind = ChatItemKind.Assistant, Streaming = true, Thinking = true });
            var request = new ChatRequest { System = SystemPrompt.Text, Turns = _turns.ToList(), Tools = ProjectTools.Specs };

            TurnFinished? finished = null;
            await foreach (var ev in provider.Stream(request, ct))
            {
                switch (ev)
                {
                    case TextDelta d:
                        item.Text += d.Text;
                        item.Thinking = false;
                        Changed?.Invoke(item);
                        break;
                    case ThinkingStarted:
                        item.Thinking = true;
                        Changed?.Invoke(item);
                        break;
                    case TurnFinished f:
                        finished = f;
                        break;
                }
            }

            item.Streaming = false;
            item.Thinking = false;
            if (item.Text.Length == 0) Items.Remove(item);
            Changed?.Invoke(item.Text.Length == 0 ? null : item);

            if (finished is null) return;
            _turns.Add(finished.Turn);

            if (finished.StopReason == "refusal")
            {
                AddItem(new ChatItem { Kind = ChatItemKind.Error, Text = "Model từ chối trả lời yêu cầu này. Thử diễn đạt lại, hoặc đổi sang model khác ở góc trên khung chat." });
                return;
            }

            if (finished.Turn.ToolCalls.Count == 0)
            {
                if (finished.StopReason == "max_tokens") AddItem(new ChatItem { Kind = ChatItemKind.Activity, Text = "… câu trả lời dài quá giới hạn và bị cắt. Gõ \"tiếp\" để viết nốt." });
                return;
            }

            // Mọi công cụ trong một lượt trả kết quả chung một lượt người dùng —
            // tách ra nhiều lượt thì model dần thôi gọi song song.
            var results = new ChatTurn { Role = ChatRole.User };
            foreach (var call in finished.Turn.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                results.ToolResults.Add(await RunTool(call, ct));
            }
            _turns.Add(results);
        }

        AddItem(new ChatItem { Kind = ChatItemKind.Activity, Text = "Trợ lý đã làm nhiều bước liền — dừng lại ở đây. Gõ tiếp nếu muốn nó làm nữa." });
    }

    private async Task<ToolResult> RunTool(ToolCall call, CancellationToken ct)
    {
        var outcome = _tools.Execute(call);
        AddItem(new ChatItem { Kind = ChatItemKind.Activity, Text = outcome.Activity });

        if (outcome.Proposal is not { } proposal)
        {
            return new ToolResult(call.Id, outcome.Result ?? "", outcome.IsError);
        }

        AddItem(new ChatItem { Kind = ChatItemKind.Proposal, Proposal = proposal });
        bool apply;
        if (AppSettings.Current.AiAutoApply)
        {
            proposal.AutoApplied = true;
            apply = true;
        }
        else
        {
            _pending = proposal;
            _decision = new TaskCompletionSource<bool>();
            using (ct.Register(() => _decision.TrySetCanceled()))
            {
                apply = await _decision.Task;
            }
            _pending = null;
        }

        if (!apply)
        {
            proposal.State = ProposalState.Rejected;
            Changed?.Invoke(null);
            return new ToolResult(call.Id, "Người dùng đã BỎ QUA đề xuất này, dự án không đổi. Hỏi họ muốn chỉnh thế nào nếu cần.", false);
        }

        _tools.LastCreated = null;
        var error = proposal.Apply();
        if (error is not null)
        {
            proposal.State = ProposalState.Failed;
            proposal.FailReason = error;
            Changed?.Invoke(null);
            return new ToolResult(call.Id, $"Không áp dụng được: {error}", true);
        }

        proposal.State = ProposalState.Applied;
        // Chốt ngay thành một bước hoàn tác riêng: Ctrl+Z gỡ đúng thay đổi của AI,
        // không dính với việc người dùng gõ tay ngay trước đó.
        _session.Commit();
        Changed?.Invoke(null);
        ProjectChanged?.Invoke();
        var applied = proposal.AutoApplied ? "Đã tự áp dụng (người dùng bật chế độ tự áp dụng), dự án đã được cập nhật." : "Người dùng đã ÁP DỤNG đề xuất, dự án đã được cập nhật.";
        return new ToolResult(call.Id, _tools.LastCreated is { } created ? $"{applied} {created}" : applied, false);
    }

    /// <summary>
    /// Dừng giữa chừng có thể để lại lượt AI đang gọi công cụ mà chưa có kết
    /// quả — API sẽ từ chối cả cuộc trò chuyện ở lượt sau. Bù kết quả "đã huỷ".
    /// </summary>
    private void RepairAfterStop()
    {
        _pending = null;
        if (_turns.Count == 0 || _turns[^1].Role != ChatRole.Assistant || _turns[^1].ToolCalls.Count == 0) return;
        var results = new ChatTurn { Role = ChatRole.User };
        foreach (var call in _turns[^1].ToolCalls) results.ToolResults.Add(new ToolResult(call.Id, "Người dùng đã dừng trước khi công cụ chạy xong.", true));
        _turns.Add(results);
    }

    private ChatItem AddItem(ChatItem item)
    {
        Items.Add(item);
        Changed?.Invoke(item);
        return item;
    }
}

/// <summary>Prompt hệ thống. Giữ cố định từng chữ để cache prompt của provider ăn.</summary>
public static class SystemPrompt
{
    public const string Text = """
Bạn là trợ lý sáng tác nằm ngay trong VNdev — công cụ làm visual novel. Người dùng là tác giả; bạn là người cùng viết ngồi cạnh họ.

Việc chính của bạn, theo thứ tự ưu tiên:
1. Cùng lên ý tưởng: tình tiết, xung đột, bước ngoặt, động cơ nhân vật, nhánh truyện và các kết thúc. Khi được hỏi ý tưởng, đưa 2–4 hướng khác hẳn nhau (mỗi hướng một hai câu, nói rõ nó làm câu chuyện thay đổi thế nào) rồi hỏi họ thích hướng nào, thay vì chốt hộ một đáp án.
2. Hành văn: làm lời thoại tự nhiên, đúng giọng từng nhân vật, đúng nhịp của thể loại visual novel.
3. Giữ câu chuyện nhất quán: tính cách, cách xưng hô, mốc thời gian, biến và nhánh truyện không mâu thuẫn nhau.

Về hành văn tiếng Việt (khi dự án viết bằng tiếng Việt):
- Xưng hô là linh hồn của thoại tiếng Việt. Giữ đúng cặp xưng hô giữa hai nhân vật (anh–em, tớ–cậu, mày–tao, cô–em…), chỉ đổi khi quan hệ thật sự đổi và chính sự thay đổi đó là một tình tiết.
- Viết như người thật nói: câu ngắn, có tiểu từ tình thái (nhé, mà, chứ, à, hả, đấy) đúng chỗ, tránh giọng văn dịch ("Tôi không thể tin được điều này", "Bạn đang làm gì vậy?" khi hai người thân nhau).
- Mỗi nhân vật một giọng: từ cửa miệng, độ dài câu, mức lịch sự. Đọc hồ sơ giọng (get_character) trước khi viết thoại cho ai.
- Một khung thoại visual novel nên ngắn — thường một đến ba câu. Lời dẫn gọn, gợi hình, ưu tiên cho thấy hơn là kể lể cảm xúc.
- Tránh sáo ngữ và câu thừa thãi; không nhồi quá nhiều dấu chấm lửng và chấm than.
Nếu dự án viết bằng ngôn ngữ khác, áp dụng tinh thần tương tự cho ngôn ngữ đó. Luôn trả lời người dùng bằng tiếng Việt trừ khi họ viết bằng ngôn ngữ khác.

Cách làm việc với dự án — bạn là agent, không chỉ là người góp ý:
- Mỗi tin nhắn có kèm <ngu_canh_hien_tai> cho biết người dùng đang mở chương, cảnh hay node nào. "Cảnh này", "chỗ này" là nói về chỗ đó.
- Đọc trước khi làm: dùng get_project_overview, get_chapter, get_scene, get_character, list_assets để xem đúng hiện trạng thay vì đoán. Đừng bịa id — lấy id từ kết quả đọc, hoặc từ kết quả công cụ vừa tạo ra.
- Bạn làm được mọi việc trong app bằng công cụ: tạo và sửa nhân vật (kể cả biểu cảm, gán ảnh sprite), tạo và sắp xếp chương, tạo cảnh kèm nền, nhạc, nhân vật đứng sẵn và lời thoại, sửa thoại, dựng đồ thị (thêm, sửa, xoá, nối node, đặt điểm bắt đầu), khai báo biến. ĐỪNG bảo người dùng tự làm tay những việc công cụ làm được — cứ làm, rồi báo lại.
- Thứ duy nhất người dùng phải tự làm là chép file ảnh và nhạc vào thư mục assets; khi thiếu thì nói rõ cần file gì, đặt vào thư mục nào.
- Làm theo thứ tự phụ thuộc: tạo nhân vật trước khi viết thoại cho họ, khai báo biến trước khi dùng trong lựa chọn, tạo cảnh (create_scene) trước khi thêm node scene trỏ tới nó (edit_graph). Một chương thường dựng xong bằng một lần edit_graph.
- Mỗi thay đổi hiện thành thẻ trước/sau; người dùng bấm Áp dụng hoặc Bỏ qua (hoặc đã bật tự áp dụng). Chỉ nói "đã làm xong" khi kết quả công cụ báo đã áp dụng. Bị bỏ qua thì hỏi người dùng muốn khác đi thế nào, đừng gửi lại y hệt.
- Việc sáng tạo lớn — xương sống cốt truyện, bí mật, kết thúc — hỏi người dùng trước khi tự quyết. Việc dựng, xếp, điền chi tiết theo ý đã chốt thì cứ làm.
- Khi đang bàn ý tưởng, trả lời bằng lời trước; bắt tay dựng khi người dùng muốn.

Giọng của bạn: thân thiện, thẳng thắn, ngắn gọn như một biên tập viên có tâm. Góp ý cụ thể, có ví dụ; khen đúng chỗ, chê có lý do. Không liệt kê dài dòng khi vài câu là đủ.
""";
}
