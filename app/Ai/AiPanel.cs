using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

namespace VNdev.App.Ai;

/// <summary>
/// Khung chat trợ lý bên phải editor. Chỉ vẽ lại những gì
/// <see cref="AiConversation"/> đang giữ — không tự giữ trạng thái nào, nên
/// dựng lại bao nhiêu lần (hoàn tác, áp dụng đề xuất) cũng không mất gì.
/// </summary>
public partial class AiPanel : PanelContainer
{
    private readonly ProjectSession _session;
    private readonly Func<string> _context;
    private readonly Action _close;

    private VBoxContainer _list = null!;
    private ScrollContainer _scroll = null!;
    private TextEdit _input = null!;
    private Button _send = null!;
    private OptionButton _provider = null!;
    private Control _quick = null!;
    private readonly Dictionary<ChatItem, Control> _views = new();
    private bool _stickToBottom = true;

    private AiConversation Chat => _session.Ai;

    public AiPanel() : this(null!, () => "", () => { }) { }

    public AiPanel(ProjectSession session, Func<string> context, Action close)
    {
        _session = session;
        _context = context;
        _close = close;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(400, 0);
        var style = new StyleBoxFlat { BgColor = Palette.Panel, BorderColor = Palette.Line, BorderWidthLeft = 1 };
        style.SetContentMarginAll(10);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        AddChild(root);

        root.AddChild(BuildHeader());

        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.GetVScrollBar().ValueChanged += _ =>
        {
            var bar = _scroll.GetVScrollBar();
            _stickToBottom = bar.Value >= bar.MaxValue - bar.Page - 24;
        };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(_list);
        root.AddChild(_scroll);

        _quick = BuildQuickPrompts();
        root.AddChild(_quick);
        root.AddChild(BuildInput());

        Chat.Changed += OnChanged;
        AppSettings.Changed += RefreshProviders;
        RebuildAll();
    }

    public override void _ExitTree()
    {
        Chat.Changed -= OnChanged;
        AppSettings.Changed -= RefreshProviders;
    }

    public void FocusInput() => _input.GrabFocus();

    // ================= Đầu panel =================

    private Control BuildHeader()
    {
        var row = new HBoxContainer();
        var title = new Label { Text = "✦  Trợ lý" };
        title.AddThemeColorOverride("font_color", Palette.Accent);
        title.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(title);

        _provider = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false, ClipText = true, TooltipText = "Nhà cung cấp và model đang dùng" };
        _provider.ItemSelected += OnProviderPicked;
        row.AddChild(_provider);
        RefreshProviders();

        var reset = new Button { Text = "＋", Flat = true, TooltipText = "Cuộc trò chuyện mới" };
        reset.Pressed += () =>
        {
            if (Chat.Items.Count == 0) return;
            Dialogs.Confirm(this, "Cuộc trò chuyện mới", "Xoá cuộc trò chuyện hiện tại và bắt đầu lại? Những gì đã áp dụng vào dự án vẫn giữ nguyên.", "Bắt đầu lại", Chat.Reset);
        };
        row.AddChild(reset);

        var close = new Button { Text = "✕", Flat = true, TooltipText = WithKeys("Đóng trợ lý", Shortcuts.AiPanel) };
        close.Pressed += _close;
        row.AddChild(close);
        return row;
    }

    private void RefreshProviders()
    {
        if (!IsInstanceValid(_provider)) return;
        _provider.Clear();
        var s = AppSettings.Current;
        var active = s.ActiveProvider();
        // Chỉ hiện dịch vụ đã dùng được (có khoá, đã chọn model) — chọn một dịch
        // vụ chưa kết nối ở đây chỉ dẫn tới lỗi ngay câu hỏi đầu tiên.
        var ready = 0;
        for (var i = 0; i < s.AiProviders.Count; i++)
        {
            var p = s.AiProviders[i];
            if ((p.NeedsKey && !CredentialStore.Has(p.Id)) || string.IsNullOrWhiteSpace(p.Model)) continue;
            _provider.AddItem($"{p.Name} · {p.Model}", i);
            ready++;
            if (p == active) _provider.Select(_provider.ItemCount - 1);
        }
        if (ready > 0) _provider.AddSeparator();
        _provider.AddItem(ready == 0 ? "⚙  Kết nối một AI…" : "⚙  Thêm hoặc đổi dịch vụ AI…", 1000);
        if (ready == 0 || active is null || _provider.Selected < 0) _provider.Select(ready == 0 ? _provider.GetItemIndex(1000) : 0);
    }

    private void OnProviderPicked(long index)
    {
        var id = _provider.GetItemId((int)index);
        if (id == 1000)
        {
            RefreshProviders();
            Main.Instance?.OpenSettings(2);
            return;
        }
        AppSettings.Current.ActiveAiProvider = AppSettings.Current.AiProviders[id].Id;
        AppSettings.Apply();
    }

    // ================= Gợi ý nhanh =================

    private static readonly (string Label, string Prompt)[] QuickPrompts =
    {
        ("💡 Ý tưởng cho chương này", "Đọc chương đang mở rồi gợi ý cho mình vài hướng phát triển tiếp theo — tình tiết, xung đột, bước ngoặt. Đưa vài hướng khác nhau để mình chọn."),
        ("✍ Trau chuốt thoại cảnh này", "Đọc cảnh đang mở, góp ý về hành văn: chỗ nào thoại chưa tự nhiên, sai giọng nhân vật, sai xưng hô, hay giọng văn dịch. Sau đó đề xuất bản sửa."),
        ("🔀 Gợi ý rẽ nhánh", "Ở chỗ mình đang đứng trong truyện, gợi ý một lựa chọn cho người chơi có ý nghĩa thật sự — mỗi phương án dẫn tới đâu, ảnh hưởng biến nào. Bàn ý tưởng trước, chưa cần sửa."),
        ("🎭 Giọng nhân vật", "Xem các nhân vật và thoại của họ, cho mình biết giọng từng người đã rõ và khác nhau chưa, xưng hô có nhất quán không. Nhân vật nào chưa có hồ sơ giọng thì đề xuất viết."),
        ("🔍 Soát nhất quán", "Soát dự án tìm chỗ vô lý hoặc mâu thuẫn: tính cách, mốc thời gian, xưng hô, biến dùng sai, nhánh cụt. Liệt kê ngắn gọn theo mức độ nghiêm trọng."),
    };

    private Control BuildQuickPrompts()
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        foreach (var (label, prompt) in QuickPrompts)
        {
            var chip = new Button { Text = label, TooltipText = prompt, FocusMode = FocusModeEnum.None };
            chip.AddThemeFontSizeOverride("font_size", 12);
            chip.Pressed += () => Send(prompt);
            flow.AddChild(chip);
        }
        return flow;
    }

    // ================= Ô nhập =================

    private Control BuildInput()
    {
        var box = new VBoxContainer();
        _input = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, 76),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "Hỏi, bàn ý tưởng hoặc nhờ viết…\nEnter để gửi · Shift+Enter xuống dòng",
        };
        _input.GuiInput += ev =>
        {
            if (ev is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter, ShiftPressed: false } )
            {
                Send(_input.Text);
                _input.AcceptEvent();
            }
        };
        box.AddChild(_input);

        var row = new HBoxContainer();
        // Công tắc đặt ngay dưới ô nhập: giao cho trợ lý làm một mạch (dựng cả
        // chương) thì bật, muốn soát từng thay đổi thì tắt — không phải vào Cài đặt.
        var auto = new CheckBox
        {
            Text = "Tự áp dụng",
            ButtonPressed = AppSettings.Current.AiAutoApply,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Bật: trợ lý tự ghi thay đổi, không hỏi từng lần.\nTắt: mỗi thay đổi hiện thẻ trước/sau để bạn bấm Áp dụng.\nBật hay tắt thì mọi thay đổi vẫn Ctrl+Z được.",
            FocusMode = FocusModeEnum.None,
        };
        auto.AddThemeFontSizeOverride("font_size", 12);
        auto.Toggled += on => { AppSettings.Current.AiAutoApply = on; AppSettings.Save(); };
        row.AddChild(auto);
        _send = new Button { Text = "Gửi  ➤", FocusMode = FocusModeEnum.None };
        _send.Pressed += () =>
        {
            if (Chat.Busy) Chat.Stop();
            else Send(_input.Text);
        };
        row.AddChild(_send);
        box.AddChild(row);
        return box;
    }

    private void Send(string text)
    {
        if (Chat.Busy || string.IsNullOrWhiteSpace(text)) return;
        _input.Text = "";
        _stickToBottom = true;
        Chat.Send(text, _context());
    }

    // ================= Danh sách tin =================

    private void OnChanged(ChatItem? item)
    {
        if (item is null || !_views.TryGetValue(item, out var view) || !Chat.Items.Contains(item))
        {
            RefreshProviders();
            RebuildAll();
            return;
        }
        var fresh = BuildItem(item);
        var index = view.GetIndex();
        _list.RemoveChild(view);
        view.QueueFree();
        _list.AddChild(fresh);
        _list.MoveChild(fresh, index);
        _views[item] = fresh;
        UpdateChrome();
        ScrollDown();
    }

    private void RebuildAll()
    {
        foreach (var child in _list.GetChildren()) child.QueueFree();
        _views.Clear();

        if (Chat.Items.Count == 0) _list.AddChild(BuildEmptyState());
        foreach (var item in Chat.Items)
        {
            var view = BuildItem(item);
            _views[item] = view;
            _list.AddChild(view);
        }
        UpdateChrome();
        ScrollDown();
    }

    private void UpdateChrome()
    {
        _send.Text = Chat.Busy ? "■  Dừng" : "Gửi  ➤";
        _send.TooltipText = Chat.Busy ? "Dừng câu trả lời đang viết" : "Gửi (Enter)";
        _quick.Visible = !Chat.Busy;
    }

    private void ScrollDown()
    {
        if (!_stickToBottom) return;
        Callable.From(() =>
        {
            if (!IsInstanceValid(_scroll)) return;
            var bar = _scroll.GetVScrollBar();
            _scroll.ScrollVertical = (int)bar.MaxValue;
        }).CallDeferred();
    }

    private Control BuildEmptyState()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        var hasProvider = AppSettings.Current.AiProviders.Any(p => (!p.NeedsKey || CredentialStore.Has(p.Id)) && !string.IsNullOrWhiteSpace(p.Model));

        var title = new Label { Text = hasProvider ? "Cùng viết nhé!" : "Kết nối một AI để bắt đầu", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        title.AddThemeColorOverride("font_color", Palette.Text);
        title.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(title);

        var body = new Label
        {
            Text = hasProvider
                ? "Trợ lý đọc được toàn bộ dự án — chương, cảnh, nhân vật, biến — và biết bạn đang mở chỗ nào. Nhờ nó bàn ý tưởng, trau chuốt câu chữ, giữ giọng nhân vật, gợi ý rẽ nhánh.\n\nMọi thay đổi đều hiện bản trước/sau để bạn duyệt. Áp dụng xong vẫn Ctrl+Z được."
                : "VNdev không kèm sẵn AI nào. Dán API key của bạn — Claude, ChatGPT, Gemini, DeepSeek, OpenRouter… — hoặc dùng Ollama chạy ngay trên máy.\n\nKhoá được cất trong Windows Credential Manager, không nằm trong thư mục dự án.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddThemeColorOverride("font_color", Palette.Text2);
        box.AddChild(body);

        if (!hasProvider)
        {
            var add = new Button { Text = "＋  Thêm API key", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            add.Pressed += () => Main.Instance?.OpenSettings(2);
            box.AddChild(add);
        }
        return box;
    }

    private Control BuildItem(ChatItem item) => item.Kind switch
    {
        ChatItemKind.User => Bubble(item.Text, user: true),
        ChatItemKind.Assistant => Assistant(item),
        ChatItemKind.Activity => Activity(item.Text),
        ChatItemKind.Proposal => ProposalCard(item.Proposal!),
        _ => ErrorView(item),
    };

    private static Control Bubble(string text, bool user)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", user ? 40 : 0);
        var panel = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = user ? new Color(Palette.Accent, 0.22f) : Palette.Panel2 };
        sb.SetCornerRadiusAll(10);
        sb.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", sb);
        panel.AddChild(Rich(Markdown.ToBbcode(text)));
        margin.AddChild(panel);
        return margin;
    }

    private static Control Assistant(ChatItem item)
    {
        if (item.Text.Length == 0)
        {
            return Activity(item.Thinking ? "✦ đang suy nghĩ…" : "✦ đang viết…");
        }
        var text = Markdown.ToBbcode(item.Text) + (item.Streaming ? $" [color=#{Palette.Accent.ToHtml(false)}]▍[/color]" : "");
        var box = new VBoxContainer();
        box.AddChild(Rich(text));
        return box;
    }

    private static Control Activity(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", Palette.Text3);
        label.AddThemeFontSizeOverride("font_size", 12);
        return label;
    }

    private Control ErrorView(ChatItem item)
    {
        var panel = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = new Color(Palette.Err, 0.12f), BorderColor = Palette.Err, BorderWidthLeft = 3 };
        sb.SetCornerRadiusAll(6);
        sb.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", sb);
        var box = new VBoxContainer();
        var label = new Label { Text = item.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", Palette.Text);
        box.AddChild(label);
        if (item.SettingsHint)
        {
            var open = new Button { Text = "Mở Cài đặt → AI Providers", SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            open.Pressed += () => Main.Instance?.OpenSettings(2);
            box.AddChild(open);
        }
        panel.AddChild(box);
        return panel;
    }

    private Control ProposalCard(Proposal p)
    {
        var panel = new PanelContainer();
        var border = p.State switch
        {
            ProposalState.Applied => Palette.Ok,
            ProposalState.Rejected => Palette.Text3,
            ProposalState.Failed => Palette.Err,
            _ => Palette.Accent,
        };
        var sb = new StyleBoxFlat { BgColor = Palette.Panel2, BorderColor = border };
        sb.SetBorderWidthAll(1);
        sb.BorderWidthLeft = 3;
        sb.SetCornerRadiusAll(8);
        sb.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", sb);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        var title = new Label { Text = $"✎  {p.Title}", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        title.AddThemeColorOverride("font_color", Palette.Text);
        box.AddChild(title);
        if (p.Summary.Length > 0)
        {
            var summary = new Label { Text = p.Summary, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            summary.AddThemeColorOverride("font_color", Palette.Text2);
            summary.AddThemeFontSizeOverride("font_size", 13);
            box.AddChild(summary);
        }

        var diff = new StringBuilder();
        foreach (var line in p.Diff)
        {
            var t = Markdown.Escape(line.Text);
            diff.AppendLine(line.Kind switch
            {
                DiffKind.Removed => $"[color=#ff5f6d]−  [s]{t}[/s][/color]",
                DiffKind.Added => $"[color=#3ecf8e]+  {t}[/color]",
                DiffKind.Note => $"[color=#a0a6b8][i]{t}[/i][/color]",
                _ => $"[color=#6b7386]   {t}[/color]",
            });
        }
        var diffBox = new PanelContainer();
        var dsb = new StyleBoxFlat { BgColor = Palette.Bg };
        dsb.SetCornerRadiusAll(6);
        dsb.SetContentMarginAll(8);
        diffBox.AddThemeStyleboxOverride("panel", dsb);
        var rich = Rich(diff.ToString().TrimEnd());
        rich.AddThemeFontSizeOverride("normal_font_size", 13);
        diffBox.AddChild(rich);
        box.AddChild(diffBox);

        switch (p.State)
        {
            case ProposalState.Pending:
                var row = new HBoxContainer();
                var apply = new Button { Text = "✓  Áp dụng" };
                var applyStyle = new StyleBoxFlat { BgColor = new Color(Palette.Ok, 0.22f), BorderColor = Palette.Ok };
                applyStyle.SetBorderWidthAll(1);
                applyStyle.SetCornerRadiusAll(6);
                applyStyle.SetContentMarginAll(8);
                apply.AddThemeStyleboxOverride("normal", applyStyle);
                apply.Pressed += () => Chat.Decide(p, true);
                row.AddChild(apply);
                var skip = new Button { Text = "Bỏ qua" };
                skip.Pressed += () => Chat.Decide(p, false);
                row.AddChild(skip);
                box.AddChild(row);
                break;
            case ProposalState.Applied:
                box.AddChild(StateLabel(p.AutoApplied ? "✓ Đã tự áp dụng — Ctrl+Z để hoàn tác" : "✓ Đã áp dụng — Ctrl+Z để hoàn tác", Palette.Ok));
                break;
            case ProposalState.Rejected:
                box.AddChild(StateLabel("Đã bỏ qua", Palette.Text3));
                break;
            case ProposalState.Failed:
                box.AddChild(StateLabel($"Không áp dụng được: {p.FailReason}", Palette.Err));
                break;
        }
        return panel;
    }

    private static Label StateLabel(string text, Color color)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    private static RichTextLabel Rich(string bbcode)
    {
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Text = bbcode,
        };
        rich.AddThemeColorOverride("default_color", Palette.Text);
        return rich;
    }

    private static string WithKeys(string label, string action)
        => Shortcuts.Display(action) is { Length: > 0 } keys ? $"{label}  ({keys})" : label;
}

/// <summary>
/// Đổi phần Markdown hay gặp trong câu trả lời AI sang BBCode của Godot.
/// Chỉ lo những thứ AI thật sự dùng (đậm, nghiêng, mã, tiêu đề, gạch đầu dòng)
/// — viết trình đọc Markdown đầy đủ là quá tay cho một khung chat.
/// </summary>
public static class Markdown
{
    public static string Escape(string text) => text.Replace("[", "[lb]");

    public static string ToBbcode(string markdown)
    {
        var sb = new StringBuilder();
        var inCode = false;
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                sb.Append(inCode ? "[code]" : "[/code]\n");
                continue;
            }
            if (inCode)
            {
                sb.Append(Escape(raw)).Append('\n');
                continue;
            }

            var line = Escape(raw);
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                sb.Append("[b]").Append(Inline(heading.Groups[2].Value)).Append("[/b]\n");
                continue;
            }
            var bullet = Regex.Match(line, @"^(\s*)[-*+]\s+(.*)$");
            if (bullet.Success)
            {
                var indent = new string(' ', bullet.Groups[1].Value.Length);
                sb.Append(indent).Append("• ").Append(Inline(bullet.Groups[2].Value)).Append('\n');
                continue;
            }
            if (Regex.IsMatch(line, @"^\s*(---|\*\*\*)\s*$"))
            {
                sb.Append("[color=#343a49]────────────[/color]\n");
                continue;
            }
            var quote = Regex.Match(line, @"^>\s?(.*)$");
            if (quote.Success)
            {
                sb.Append("[color=#a0a6b8][i]").Append(Inline(quote.Groups[1].Value)).Append("[/i][/color]\n");
                continue;
            }
            sb.Append(Inline(line)).Append('\n');
        }
        if (inCode) sb.Append("[/code]");
        return sb.ToString().TrimEnd('\n');
    }

    private static string Inline(string s)
    {
        s = Regex.Replace(s, @"`([^`]+)`", "[code]$1[/code]");
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "[b]$1[/b]");
        s = Regex.Replace(s, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", "[i]$1[/i]");
        s = Regex.Replace(s, @"(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)", "[i]$1[/i]");
        return s;
    }
}
