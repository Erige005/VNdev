using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace VNdev.App.Ai;

/// <summary>
/// Gọi Claude qua SDK chính thức của Anthropic, luôn ở chế độ stream để chữ
/// hiện dần ra khung chat thay vì bắt người dùng nhìn màn hình trống.
/// </summary>
/// <remarks>
/// Dùng nhánh beta của SDK vì cơ chế dự phòng khi bị từ chối (fallbacks) chỉ
/// có ở đó: nếu Claude Opus 5 từ chối một đoạn truyện có yếu tố bạo lực chẳng
/// hạn, máy chủ tự chạy lại bằng model khác thay vì trả về câu trả lời rỗng.
/// </remarks>
public sealed class AnthropicProvider : IChatProvider
{
    public const string Kind = "anthropic";

    private readonly AiProviderConfig _config;
    private readonly AnthropicClient _client;

    public AnthropicProvider(AiProviderConfig config, string apiKey)
    {
        _config = config;
        _client = string.IsNullOrWhiteSpace(config.BaseUrl) || config.BaseUrl.TrimEnd('/') == "https://api.anthropic.com"
            ? new AnthropicClient { ApiKey = apiKey }
            : new AnthropicClient { ApiKey = apiKey, BaseUrl = config.BaseUrl.TrimEnd('/') };
    }

    private string Model => string.IsNullOrWhiteSpace(_config.Model) ? "claude-opus-5" : _config.Model.Trim();

    public async Task<List<string>> ListModels(CancellationToken ct)
    {
        try
        {
            var page = await _client.Models.List();
            return page.Items.Select(m => m.ID).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Translate(ex);
        }
    }

    public async IAsyncEnumerable<StreamEvent> Stream(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var parameters = BuildParams(request);
        var blocks = new SortedDictionary<long, BlockBuilder>();
        var stopReason = "end_turn";

        var enumerator = _client.Beta.Messages.CreateStreaming(parameters, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                BetaRawMessageStreamEvent ev;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    ev = enumerator.Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw Translate(ex);
                }

                if (ev.TryPickContentBlockStart(out var start))
                {
                    var b = new BlockBuilder();
                    var block = start.ContentBlock;
                    if (block.TryPickBetaText(out var text)) { b.Kind = "text"; b.Text.Append(text.Text); }
                    else if (block.TryPickBetaThinking(out var thinking)) { b.Kind = "thinking"; b.Text.Append(thinking.Thinking); b.Signature = thinking.Signature; }
                    else if (block.TryPickBetaRedactedThinking(out var redacted)) { b.Kind = "redacted"; b.Data = redacted.Data; }
                    else if (block.TryPickBetaToolUse(out var tool)) { b.Kind = "tool"; b.ToolId = tool.ID; b.ToolName = tool.Name; }
                    else if (block.TryPickBetaFallback(out _)) { b.Kind = "fallback"; }
                    else b.Kind = "other";
                    blocks[start.Index] = b;

                    if (b.Kind == "thinking") yield return new ThinkingStarted();
                    if (b.Kind == "text" && b.Text.Length > 0) yield return new TextDelta(b.Text.ToString());
                }
                else if (ev.TryPickContentBlockDelta(out var delta) && blocks.TryGetValue(delta.Index, out var target))
                {
                    var d = delta.Delta;
                    if (d.TryPickText(out var t))
                    {
                        target.Text.Append(t.Text);
                        yield return new TextDelta(t.Text);
                    }
                    else if (d.TryPickThinking(out var th)) target.Text.Append(th.Thinking);
                    else if (d.TryPickSignature(out var sig)) target.Signature = sig.Signature;
                    else if (d.TryPickInputJson(out var json)) target.Json.Append(json.PartialJson);
                }
                else if (ev.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason is { } reason)
                {
                    stopReason = reason.Raw() ?? stopReason;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        yield return new TurnFinished(BuildTurn(blocks.Values.ToList()), stopReason);
    }

    private MessageCreateParams BuildParams(ChatRequest request)
    {
        var tools = request.Tools.Select(ToTool).ToList();
        var messages = request.Turns.Select(ToMessage).ToList();

        // Cơ chế dự phòng chỉ bật cho model có danh sách dự phòng do Anthropic
        // công bố; bật cho model khác thì máy chủ trả lỗi 400.
        var withFallback = Model is "claude-opus-5" or "claude-fable-5-1";

        return new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 64000,
            // Prompt hệ thống và bộ công cụ cố định suốt cuộc trò chuyện, nên bật
            // cache: từ lượt thứ hai, phần đầu gửi lại gần như miễn phí.
            CacheControl = new BetaCacheControlEphemeral(),
            System = new List<BetaTextBlockParam> { new() { Text = request.System } },
            Tools = tools,
            Messages = messages,
            // Dạng danh sách chỉ định sẵn model dự phòng; bản SDK hiện tại chưa có
            // kiểu cho dạng "default" nên chưa dùng được.
            Betas = withFallback ? ["server-side-fallback-2026-06-01"] : null,
            Fallbacks = withFallback ? (BetaFallbacksParam)new List<BetaFallbackParam> { new() { Model = "claude-opus-4-8" } } : null,
        };
    }

    private static BetaToolUnion ToTool(ToolSpec spec)
    {
        var properties = new Dictionary<string, JsonElement>();
        if (spec.Schema["properties"] is JsonObject props)
        {
            foreach (var (name, node) in props) properties[name] = JsonSerializer.SerializeToElement(node);
        }
        var required = spec.Schema["required"] is JsonArray req
            ? req.Select(r => r!.GetValue<string>()).ToList()
            : new List<string>();

        return new BetaTool
        {
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = new() { Properties = properties, Required = required },
            // Chữ trong đề xuất sửa thoại có thể dài — stream luôn phần tham số
            // thay vì đợi máy chủ gom đủ rồi mới gửi một cục.
            EagerInputStreaming = true,
        };
    }

    private static BetaMessageParam ToMessage(ChatTurn turn)
    {
        if (turn.Role == ChatRole.Assistant)
        {
            if (turn.Native is List<BetaContentBlockParam> native && turn.NativeProviderKind == Kind)
            {
                return new BetaMessageParam { Role = Role.Assistant, Content = native };
            }

            var content = new List<BetaContentBlockParam>();
            if (turn.Text.Length > 0) content.Add(new BetaTextBlockParam { Text = turn.Text });
            foreach (var call in turn.ToolCalls)
            {
                content.Add(new BetaToolUseBlockParam { ID = call.Id, Name = call.Name, Input = ToInput(call.Arguments) });
            }
            if (content.Count == 0) content.Add(new BetaTextBlockParam { Text = "…" });
            return new BetaMessageParam { Role = Role.Assistant, Content = content };
        }

        var user = new List<BetaContentBlockParam>();
        foreach (var result in turn.ToolResults)
        {
            user.Add(new BetaToolResultBlockParam { ToolUseID = result.CallId, Content = result.Content, IsError = result.IsError });
        }
        if (turn.Text.Length > 0) user.Add(new BetaTextBlockParam { Text = turn.Text });
        return new BetaMessageParam { Role = Role.User, Content = user };
    }

    private static Dictionary<string, JsonElement> ToInput(JsonObject args)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (k, v) in args) dict[k] = JsonSerializer.SerializeToElement(v);
        return dict;
    }

    /// <summary>
    /// Ráp các khối đã stream thành một lượt trả lời hoàn chỉnh.
    /// </summary>
    /// <remarks>
    /// Nếu giữa chừng có chuyển sang model dự phòng, các khối suy nghĩ và gọi
    /// công cụ nằm TRƯỚC điểm chuyển là của model đã từ chối — không được gửi
    /// lại, cũng không được chạy công cụ đó. Chữ thường thì giữ.
    /// </remarks>
    private static ChatTurn BuildTurn(List<BlockBuilder> blocks)
    {
        var lastFallback = blocks.FindLastIndex(b => b.Kind == "fallback");
        var native = new List<BetaContentBlockParam>();
        var turn = new ChatTurn { Role = ChatRole.Assistant, NativeProviderKind = Kind };
        var text = new StringBuilder();

        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var beforeSwitch = i < lastFallback;
            switch (b.Kind)
            {
                case "text":
                    native.Add(new BetaTextBlockParam { Text = b.Text.ToString() });
                    text.Append(b.Text);
                    break;
                case "thinking" when !beforeSwitch:
                    native.Add(new BetaThinkingBlockParam { Thinking = b.Text.ToString(), Signature = b.Signature ?? "" });
                    break;
                case "redacted" when !beforeSwitch:
                    native.Add(new BetaRedactedThinkingBlockParam { Data = b.Data ?? "" });
                    break;
                case "tool" when !beforeSwitch:
                    var args = ParseArgs(b.Json.ToString());
                    turn.ToolCalls.Add(new ToolCall(b.ToolId!, b.ToolName!, args ?? new JsonObject { ["__invalid_json"] = b.Json.ToString() }));
                    native.Add(new BetaToolUseBlockParam { ID = b.ToolId!, Name = b.ToolName!, Input = ToInput(args ?? new JsonObject()) });
                    break;
            }
        }

        turn.Text = text.ToString();
        turn.Native = native;
        return turn;
    }

    private static JsonObject? ParseArgs(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            // Stream tham số sớm nghĩa là JSON có thể bị cắt dở (hết max_tokens) —
            // báo lỗi cho AI tự gửi lại thay vì chạy công cụ với dữ liệu thiếu.
            return null;
        }
    }

    private static AiException Translate(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException => new AiException("Khoá API Anthropic không đúng hoặc đã bị thu hồi. Kiểm tra lại ở Cài đặt → AI Providers.", ex),
        AnthropicRateLimitException => new AiException("Anthropic báo gửi quá nhiều yêu cầu hoặc hết hạn mức. Đợi một lát rồi thử lại.", ex),
        AnthropicNotFoundException => new AiException("Không tìm thấy model này — kiểm tra tên model trong Cài đặt → AI Providers.", ex),
        AnthropicBadRequestException bad => new AiException($"Anthropic từ chối yêu cầu: {bad.Message}", ex),
        Anthropic5xxException => new AiException("Máy chủ Anthropic đang lỗi hoặc quá tải. Thử lại sau ít phút.", ex),
        AnthropicIOException => new AiException("Không kết nối được tới Anthropic — kiểm tra mạng.", ex),
        AnthropicApiException api => new AiException($"Lỗi từ Anthropic: {api.Message}", ex),
        AiException ai => ai,
        _ => new AiException($"Lỗi khi gọi Claude: {ex.Message}", ex),
    };

    private sealed class BlockBuilder
    {
        public string Kind = "other";
        public readonly StringBuilder Text = new();
        public readonly StringBuilder Json = new();
        public string? Signature;
        public string? Data;
        public string? ToolId;
        public string? ToolName;
    }
}
