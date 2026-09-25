using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace VNdev.App;

/// <summary>
/// Cửa sổ Cài đặt theo docs/design/ui-ux.html: danh mục bên trái, nội dung bên
/// phải. Mọi thay đổi áp dụng ngay, không có nút "Lưu" — cùng nguyên tắc "thấy
/// trước, sửa sau" với phần còn lại của app.
/// </summary>
public partial class SettingsDialog : AcceptDialog
{
    private static readonly string[] Pages = { "◈  Chung", "▤  Giao diện", "✦  AI Providers", "⌨  Phím tắt", "↗  Xuất bản" };
    private static readonly float[] Scales = { 0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f, 1.75f, 2f };

    private ItemList _nav = null!;
    private MarginContainer _content = null!;
    private int _page;

    /// <summary>Hành động đang chờ người dùng bấm phím mới, null nếu không chờ.</summary>
    private string? _capturing;
    private Button? _captureButton;

    public SettingsDialog() { }

    public SettingsDialog(int page)
    {
        _page = page;
    }

    public override void _Ready()
    {
        Title = "Cài đặt";
        OkButtonText = "Xong";
        MinSize = new Vector2I(820, 560);
        Confirmed += QueueFree;
        Canceled += QueueFree;

        var root = new HBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        _nav = new ItemList { CustomMinimumSize = new Vector2(190, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        foreach (var p in Pages) _nav.AddItem(p);
        _nav.ItemSelected += i => ShowPage((int)i);
        root.AddChild(_nav);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var bg = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2 });
        bg.AddChild(scroll);
        root.AddChild(bg);

        _content = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" }) _content.AddThemeConstantOverride($"margin_{side}", 20);
        scroll.AddChild(_content);

        // Bắt phím ở tầng _input của chính viewport cửa sổ này, trước khi nút
        // hay menu kịp xử lý — không thì bấm Ctrl+Z để gán phím sẽ hoàn tác luôn.
        root.AddChild(new SettingsInputCatcher(OnCapturedInput));

        ShowPage(_page);
    }

    private void ShowPage(int index)
    {
        _page = index;
        _nav.Select(index);
        StopCapture();
        foreach (var child in _content.GetChildren()) child.QueueFree();

        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 10);
        _content.AddChild(box);

        switch (index)
        {
            case 0: BuildGeneral(box); break;
            case 1: BuildAppearance(box); break;
            case 2: BuildAiProviders(box); break;
            case 3: BuildShortcuts(box); break;
            case 4: BuildComingSoon(box, "Xuất bản",
                "Đóng gói dự án thành game chạy độc lập cho Windows (.exe) và web, chọn biểu tượng, tên file và ngôn ngữ đi kèm.");
                break;
        }
    }

    // ---------- Chung ----------

    private void BuildGeneral(VBoxContainer box)
    {
        var s = AppSettings.Current;
        Heading(box, "Khởi động");
        Check(box, "Tự mở lại dự án gần nhất khi khởi động app", s.ReopenLastProject, v => s.ReopenLastProject = v);

        Heading(box, "Chỉnh sửa");
        Check(box, "Hỏi xác nhận trước khi xoá node", s.ConfirmDeleteNodes, v => s.ConfirmDeleteNodes = v);
        Hint(box, "Xoá nhầm vẫn lấy lại được bằng Ctrl+Z, kể cả khi tắt hỏi.");

        Heading(box, "Ngôn ngữ giao diện");
        var lang = new OptionButton { CustomMinimumSize = new Vector2(240, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        lang.AddItem("Tiếng Việt");
        foreach (var other in new[] { "English", "日本語", "中文", "ไทย" })
        {
            lang.AddItem($"{other}  (sắp có)");
            lang.SetItemDisabled(lang.ItemCount - 1, true);
        }
        lang.Selected = 0;
        box.AddChild(lang);

        Heading(box, "Dữ liệu");
        var clear = new Button { Text = $"Xoá danh sách dự án gần đây ({s.RecentProjects.Count})", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        clear.Disabled = s.RecentProjects.Count == 0;
        clear.Pressed += () =>
        {
            s.RecentProjects.Clear();
            AppSettings.Apply();
            ToastLayer.Show("Đã xoá danh sách dự án gần đây.", ToastLayer.Kind.Ok);
            ShowPage(_page);
        };
        box.AddChild(clear);

        var row = new HBoxContainer();
        var path = new Label { Text = $"Cài đặt lưu tại: {OS.GetUserDataDir()}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.Arbitrary };
        path.AddThemeColorOverride("font_color", Palette.Text3);
        row.AddChild(path);
        var open = new Button { Text = "Mở thư mục" };
        open.Pressed += () => OS.ShellOpen(OS.GetUserDataDir());
        row.AddChild(open);
        box.AddChild(row);
    }

    // ---------- Giao diện ----------

    private void BuildAppearance(VBoxContainer box)
    {
        var s = AppSettings.Current;

        Heading(box, "Kích thước");
        var scale = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, CustomMinimumSize = new Vector2(160, 0) };
        foreach (var v in Scales) scale.AddItem($"{v * 100:0}%");
        scale.Selected = Array.FindIndex(Scales, v => Math.Abs(v - s.UiScale) < 0.01f) is var i and >= 0 ? i : 2;
        scale.ItemSelected += idx => { s.UiScale = Scales[idx]; AppSettings.Apply(); };
        Labeled(box, "Tỉ lệ giao diện", scale);

        var font = new SpinBox { MinValue = 11, MaxValue = 24, Step = 1, Value = s.FontSize, Suffix = "px", CustomMinimumSize = new Vector2(120, 0) };
        font.ValueChanged += v => { s.FontSize = (int)v; AppSettings.Apply(); };
        Labeled(box, "Cỡ chữ", font);

        Heading(box, "Màu nhấn");
        var swatches = new HBoxContainer();
        swatches.AddThemeConstantOverride("separation", 10);
        foreach (var (name, hex) in Palette.AccentPresets)
        {
            var color = Color.FromHtml(hex);
            var selected = string.Equals(hex, s.AccentColor, StringComparison.OrdinalIgnoreCase);
            var sb = new StyleBoxFlat { BgColor = color, BorderColor = selected ? Palette.Text : color };
            sb.SetCornerRadiusAll(16);
            sb.SetBorderWidthAll(selected ? 3 : 0);
            var btn = new Button { CustomMinimumSize = new Vector2(32, 32), TooltipText = name };
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" }) btn.AddThemeStyleboxOverride(state, sb);
            btn.Pressed += () =>
            {
                s.AccentColor = hex;
                AppSettings.Apply();
                ShowPage(_page);
            };
            swatches.AddChild(btn);
        }
        box.AddChild(swatches);
        Hint(box, "Màu nhấn dùng cho nút đang bật, node đang chọn và ô đang gõ.");

        Heading(box, "Đồ thị cốt truyện");
        Check(box, "Hiện lưới nền", s.ShowGrid, v => s.ShowGrid = v);
        Check(box, "Bắt dính node theo lưới khi kéo", s.SnapToGrid, v => s.SnapToGrid = v);
        Check(box, "Hiện bản đồ thu nhỏ (minimap)", s.ShowMinimap, v => s.ShowMinimap = v);

        box.AddChild(new HSeparator());
        var reset = new Button { Text = "Khôi phục giao diện mặc định", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        reset.Pressed += () =>
        {
            var d = new AppSettings();
            s.UiScale = d.UiScale;
            s.FontSize = d.FontSize;
            s.AccentColor = d.AccentColor;
            s.ShowGrid = d.ShowGrid;
            s.SnapToGrid = d.SnapToGrid;
            s.ShowMinimap = d.ShowMinimap;
            AppSettings.Apply();
            ShowPage(_page);
        };
        box.AddChild(reset);
    }

    // ---------- AI Providers ----------

    /// <summary>Dịch vụ đang mở rộng để dán khoá — giữ qua các lần vẽ lại trang.</summary>
    private static string? _expandedAi;


    private void BuildAiProviders(VBoxContainer box)
    {
        Check(box, "Cho trợ lý tự áp dụng thay đổi, không hỏi từng lần", AppSettings.Current.AiAutoApply, v => AppSettings.Current.AiAutoApply = v);
        Hint(box, "Tắt: mỗi thay đổi của trợ lý hiện thẻ trước/sau để bạn bấm Áp dụng. Bật: trợ lý làm một mạch. Bật hay tắt thì vẫn Ctrl+Z được. Có công tắc giống vậy ngay dưới khung chat.");
        box.AddChild(new HSeparator());
        Hint(box, "Chọn dịch vụ bạn có tài khoản, dán API key là dùng được. Khoá được cất trong Windows Credential Manager — không ghi vào file cài đặt hay thư mục dự án, nên đưa dự án lên Git không lộ khoá.");

        var active = AppSettings.Current.ActiveProvider();
        foreach (var preset in Ai.Providers.Presets)
        {
            var config = AppSettings.Current.AiProviders.FirstOrDefault(c => Ai.Providers.PresetFor(c) == preset);
            box.AddChild(PresetRow(preset, config, config is not null && config == active));
        }

        // Dịch vụ tự nhập địa chỉ — cho ai dùng máy chủ riêng hoặc dịch vụ chưa có trong danh sách.
        var customs = AppSettings.Current.AiProviders.Where(c => Ai.Providers.PresetFor(c) is null).ToList();
        box.AddChild(new HSeparator());
        Heading(box, "Dịch vụ khác (chuẩn OpenAI)");
        foreach (var c in customs) box.AddChild(ProviderCard(c, c == active));
        var addCustom = new Button { Text = "＋  Thêm dịch vụ tự nhập địa chỉ", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        addCustom.Pressed += () =>
        {
            AppSettings.Current.AiProviders.Add(new AiProviderConfig { Name = "Dịch vụ tuỳ chỉnh", Kind = AiProviderKind.OpenAiCompatible });
            AppSettings.Apply();
            ShowPage(_page);
        };
        box.AddChild(addCustom);
    }

    private AiProviderConfig EnsureConfig(Ai.ProviderPreset preset)
    {
        var s = AppSettings.Current;
        var config = s.AiProviders.FirstOrDefault(c => Ai.Providers.PresetFor(c) == preset);
        if (config is not null) return config;
        config = new AiProviderConfig
        {
            Id = preset.Id,
            Name = preset.Name,
            Kind = preset.Kind,
            BaseUrl = preset.BaseUrl,
            NeedsKey = preset.NeedsKey,
            Model = preset.Kind == AiProviderKind.Anthropic ? Ai.Providers.ClaudeModels[0] : "",
        };
        s.AiProviders.Add(config);
        return config;
    }

    private static bool IsReady(AiProviderConfig? c)
        => c is not null && (!c.NeedsKey || Ai.CredentialStore.Has(c.Id)) && c.Model.Length > 0;

    private Control PresetRow(Ai.ProviderPreset preset, AiProviderConfig? config, bool isActive)
    {
        var expanded = _expandedAi == preset.Id;
        var hasKey = config is not null && (!preset.NeedsKey || Ai.CredentialStore.Has(config.Id));

        var panel = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = expanded ? Palette.Panel : Palette.Panel2, BorderColor = isActive && IsReady(config) ? Palette.Accent : Palette.Line };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(8);
        sb.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", sb);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var head = new HBoxContainer();
        var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        names.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = $"{preset.Name}   ·   {preset.Vendor}" };
        title.AddThemeColorOverride("font_color", Palette.Text);
        names.AddChild(title);
        var blurb = new Label { Text = preset.Blurb };
        blurb.AddThemeColorOverride("font_color", Palette.Text3);
        blurb.AddThemeFontSizeOverride("font_size", 12);
        names.AddChild(blurb);
        head.AddChild(names);

        if (isActive && IsReady(config))
        {
            var badge = new Label { Text = "● Đang dùng" };
            badge.AddThemeColorOverride("font_color", Palette.Ok);
            head.AddChild(badge);
        }
        else if (hasKey)
        {
            var badge = new Label { Text = "✓ Đã kết nối" };
            badge.AddThemeColorOverride("font_color", Palette.Text2);
            head.AddChild(badge);
            if (IsReady(config))
            {
                var use = new Button { Text = "Dùng cái này" };
                use.Pressed += () => { AppSettings.Current.ActiveAiProvider = config!.Id; AppSettings.Apply(); ShowPage(_page); };
                head.AddChild(use);
            }
        }

        var toggle = new Button { Text = expanded ? "Thu gọn ▴" : hasKey ? "Sửa ▾" : "Kết nối ▾" };
        toggle.Pressed += () => { _expandedAi = expanded ? null : preset.Id; ShowPage(_page); };
        head.AddChild(toggle);
        box.AddChild(head);

        if (expanded) BuildPresetDetails(box, preset);
        return panel;
    }

    private void BuildPresetDetails(VBoxContainer box, Ai.ProviderPreset preset)
    {
        var config = AppSettings.Current.AiProviders.FirstOrDefault(c => Ai.Providers.PresetFor(c) == preset);
        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        status.AddThemeFontSizeOverride("font_size", 12);

        if (preset.NeedsKey)
        {
            var saved = config is null ? null : Ai.CredentialStore.Get(config.Id);
            var row = new HBoxContainer();
            var key = new LineEdit
            {
                Secret = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                PlaceholderText = saved is null ? $"Dán API key {preset.Name} vào đây" : $"Đã lưu: {Ai.CredentialStore.Mask(saved)} — dán khoá mới để thay",
            };
            row.AddChild(key);
            var save = new Button { Text = "Lưu khoá" };
            row.AddChild(save);
            var get = new Button { Text = "Lấy key ↗", TooltipText = preset.KeyUrl };
            get.Pressed += () => OS.ShellOpen(preset.KeyUrl);
            row.AddChild(get);
            box.AddChild(row);

            async void SaveKey()
            {
                var k = key.Text.Trim();
                if (k.Length == 0) return;
                var c = EnsureConfig(preset);
                try
                {
                    Ai.CredentialStore.Set(c.Id, k);
                }
                catch (Exception ex)
                {
                    status.Text = ex.Message;
                    status.AddThemeColorOverride("font_color", Palette.Err);
                    return;
                }
                key.Text = "";
                AppSettings.Save();
                await FetchModels(c, status);
                if (IsInstanceValid(status)) ToastLayer.Show(status.Text, status.Text.StartsWith('✓') ? ToastLayer.Kind.Ok : ToastLayer.Kind.Error);
                // Dịch vụ đầu tiên người dùng kết nối thì dùng luôn — không bắt họ
                // tìm thêm một nút "Dùng cái này" nữa.
                if (!IsReady(AppSettings.Current.ActiveProvider()) && IsReady(c)) AppSettings.Current.ActiveAiProvider = c.Id;
                AppSettings.Apply();
                if (IsInstanceValid(this)) ShowPage(_page);
            }
            save.Pressed += SaveKey;
            key.TextSubmitted += _ => SaveKey();

            if (saved is not null)
            {
                var forget = new Button { Text = "Gỡ khoá khỏi máy", Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
                forget.AddThemeColorOverride("font_color", Palette.Err);
                forget.Pressed += () => Dialogs.Confirm(this, "Gỡ khoá", $"Xoá khoá {preset.Name} khỏi máy này?", "Gỡ khoá", () =>
                {
                    Ai.CredentialStore.Delete(config!.Id);
                    if (AppSettings.Current.ActiveAiProvider == config.Id) AppSettings.Current.ActiveAiProvider = null;
                    AppSettings.Apply();
                    ShowPage(_page);
                }, danger: true);
                box.AddChild(forget);
            }
        }
        else
        {
            var hint = new Label
            {
                Text = "Cài Ollama, tải một model (ví dụ gõ lệnh \"ollama pull\" kèm tên model) và để Ollama chạy, rồi bấm Tải danh sách model bên dưới.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            hint.AddThemeColorOverride("font_color", Palette.Text2);
            box.AddChild(hint);
            var get = new Button { Text = "Tải Ollama ↗", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
            get.Pressed += () => OS.ShellOpen(preset.KeyUrl);
            box.AddChild(get);
        }

        var canList = config is not null && (!preset.NeedsKey || Ai.CredentialStore.Has(config.Id));
        if (!canList && preset.NeedsKey)
        {
            status.Text = "Dán khoá rồi bấm Lưu khoá — app sẽ tự tải danh sách model.";
            status.AddThemeColorOverride("font_color", Palette.Text3);
            box.AddChild(status);
            return;
        }

        var modelRow = new HBoxContainer();
        var label = new Label { Text = "Model", CustomMinimumSize = new Vector2(70, 0) };
        label.AddThemeColorOverride("font_color", Palette.Text3);
        modelRow.AddChild(label);

        var pick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var models = new List<string>();
        if (config is not null && config.KnownModels.Count > 0) models.AddRange(config.KnownModels);
        else if (preset.Kind == AiProviderKind.Anthropic) models.AddRange(Ai.Providers.ClaudeModels);
        if (config is not null && config.Model.Length > 0 && !models.Contains(config.Model)) models.Insert(0, config.Model);

        if (models.Count == 0) pick.AddItem("— bấm ↻ để tải danh sách —");
        else if (config is null || config.Model.Length == 0) pick.AddItem("— chọn model —");
        foreach (var m in models) pick.AddItem(m);
        var current = config?.Model ?? "";
        for (var i = 0; i < pick.ItemCount; i++) if (pick.GetItemText(i) == current) pick.Select(i);
        pick.ItemSelected += i =>
        {
            var chosen = pick.GetItemText((int)i);
            if (chosen.StartsWith('—')) return;
            var c = EnsureConfig(preset);
            c.Model = chosen;
            if (!IsReady(AppSettings.Current.ActiveProvider())) AppSettings.Current.ActiveAiProvider = c.Id;
            AppSettings.Apply();
            ShowPage(_page);
        };
        modelRow.AddChild(pick);

        var refresh = new Button { Text = "↻", TooltipText = "Tải lại danh sách model từ dịch vụ — cũng là cách kiểm tra khoá có đúng không" };
        refresh.Pressed += async () =>
        {
            var c = EnsureConfig(preset);
            await FetchModels(c, status);
            if (IsInstanceValid(this) && status.Text.StartsWith('✓')) ShowPage(_page);
        };
        modelRow.AddChild(refresh);
        box.AddChild(modelRow);

        if (config is not null && config.Model.Length == 0)
        {
            status.Text = "Chọn một model trong danh sách là dùng được.";
            status.AddThemeColorOverride("font_color", Palette.Warn);
        }
        box.AddChild(status);

        // Có khoá mà chưa từng tải danh sách (bản cũ không lưu) — tự tải luôn khi
        // người dùng mở ra, thay vì bắt họ biết phải bấm ↻.
        if (config is not null && config.KnownModels.Count == 0)
        {
            Callable.From(async () =>
            {
                await FetchModels(config, status);
                if (IsInstanceValid(this) && config.KnownModels.Count > 0) ShowPage(_page);
            }).CallDeferred();
        }
    }

    private static async System.Threading.Tasks.Task FetchModels(AiProviderConfig config, Label status)
    {
        status.Text = "Đang hỏi dịch vụ danh sách model…";
        status.AddThemeColorOverride("font_color", Palette.Text3);
        try
        {
            var models = Ai.Providers.ChatModelsOnly(await Ai.Providers.Create(config).ListModels(default));
            config.KnownModels = models;
            AppSettings.Save();
            if (!IsInstanceValid(status)) return;
            status.Text = $"✓ Kết nối được — {models.Count} model.";
            status.AddThemeColorOverride("font_color", Palette.Ok);
        }
        catch (Ai.AiException ex)
        {
            if (!IsInstanceValid(status)) return;
            status.Text = ex.Message;
            status.AddThemeColorOverride("font_color", Palette.Err);
        }
    }

    private Control ProviderCard(AiProviderConfig p, bool isActive)
    {
        var panel = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = Palette.Panel, BorderColor = isActive ? Palette.Accent : Palette.Line };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(8);
        sb.SetContentMarginAll(12);
        panel.AddThemeStyleboxOverride("panel", sb);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var top = new HBoxContainer();
        var name = new LineEdit { Text = p.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Tên hiển thị" };
        name.TextChanged += t => { p.Name = t; AppSettings.Save(); };
        name.FocusExited += () => AppSettings.Apply();
        top.AddChild(name);
        if (isActive)
        {
            var badge = new Label { Text = "● Đang dùng" };
            badge.AddThemeColorOverride("font_color", Palette.Ok);
            top.AddChild(badge);
        }
        else
        {
            var use = new Button { Text = "Dùng cái này" };
            use.Pressed += () => { AppSettings.Current.ActiveAiProvider = p.Id; AppSettings.Apply(); ShowPage(_page); };
            top.AddChild(use);
        }
        var remove = new Button { Text = "Xoá", Flat = true, TooltipText = "Xoá nhà cung cấp và khoá của nó" };
        remove.Pressed += () => Dialogs.Confirm(this, "Xoá nhà cung cấp", $"Xoá \"{p.Name}\" và khoá API đã lưu của nó?", "Xoá", () =>
        {
            Ai.CredentialStore.Delete(p.Id);
            AppSettings.Current.AiProviders.Remove(p);
            if (AppSettings.Current.ActiveAiProvider == p.Id) AppSettings.Current.ActiveAiProvider = AppSettings.Current.AiProviders.FirstOrDefault()?.Id;
            AppSettings.Apply();
            ShowPage(_page);
        }, danger: true);
        top.AddChild(remove);
        box.AddChild(top);

        var kind = p.Kind == AiProviderKind.Anthropic ? "Anthropic (SDK chính thức)" : "Chuẩn OpenAI";
        var url = new LineEdit { Text = p.BaseUrl, PlaceholderText = "https://…/v1", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        url.TextChanged += t => { p.BaseUrl = t.Trim(); AppSettings.Save(); };
        CardRow(box, $"Địa chỉ API · {kind}", url);

        if (p.NeedsKey)
        {
            var keyRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var saved = Ai.CredentialStore.Get(p.Id);
            var key = new LineEdit
            {
                Secret = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                PlaceholderText = saved is null ? "Dán API key vào đây" : $"Đã lưu: {Ai.CredentialStore.Mask(saved)} — dán khoá mới để thay",
            };
            keyRow.AddChild(key);
            var save = new Button { Text = "Lưu khoá" };
            void SaveKey()
            {
                var k = key.Text.Trim();
                if (k.Length == 0) return;
                try
                {
                    Ai.CredentialStore.Set(p.Id, k);
                    ToastLayer.Show($"Đã lưu khoá cho {p.Name}.", ToastLayer.Kind.Ok);
                    ShowPage(_page);
                }
                catch (Exception ex)
                {
                    ToastLayer.Show(ex.Message, ToastLayer.Kind.Error);
                }
            }
            save.Pressed += SaveKey;
            key.TextSubmitted += _ => SaveKey();
            keyRow.AddChild(save);
            CardRow(box, "API key", keyRow);
        }

        var modelRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var model = new LineEdit { Text = p.Model, PlaceholderText = "tên model, hoặc bấm Tải danh sách", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        model.TextChanged += t => { p.Model = t.Trim(); AppSettings.Save(); };
        model.FocusExited += () => AppSettings.Apply();
        modelRow.AddChild(model);

        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        status.AddThemeFontSizeOverride("font_size", 12);
        if (p.NeedsKey && !Ai.CredentialStore.Has(p.Id))
        {
            status.Text = "Chưa có khoá — dán khoá rồi bấm Lưu khoá.";
            status.AddThemeColorOverride("font_color", Palette.Warn);
        }

        var pick = new MenuButton { Text = "Tải danh sách ▾", Flat = false, TooltipText = "Hỏi nhà cung cấp danh sách model tài khoản của bạn dùng được — cũng là cách kiểm tra khoá có đúng không" };
        var popup = pick.GetPopup();
        if (p.Kind == AiProviderKind.Anthropic) foreach (var m in Ai.Providers.ClaudeModels) popup.AddItem(m);
        popup.IndexPressed += i =>
        {
            var chosen = popup.GetItemText((int)i);
            if (chosen.StartsWith('(')) return;
            model.Text = chosen;
            p.Model = chosen;
            AppSettings.Apply();
        };
        pick.AboutToPopup += async () =>
        {
            status.Text = "Đang hỏi nhà cung cấp…";
            status.AddThemeColorOverride("font_color", Palette.Text3);
            try
            {
                var models = await Ai.Providers.Create(p).ListModels(default);
                if (!IsInstanceValid(status)) return;
                popup.Clear();
                foreach (var m in models.Take(200)) popup.AddItem(m);
                if (models.Count == 0) popup.AddItem("(không có model nào)");
                status.Text = $"✓ Kết nối được — {models.Count} model.";
                status.AddThemeColorOverride("font_color", Palette.Ok);
            }
            catch (Ai.AiException ex)
            {
                if (!IsInstanceValid(status)) return;
                status.Text = ex.Message;
                status.AddThemeColorOverride("font_color", Palette.Err);
            }
        };
        modelRow.AddChild(pick);
        CardRow(box, "Model", modelRow);
        box.AddChild(status);
        return panel;
    }

    private static void CardRow(VBoxContainer box, string label, Control control)
    {
        var l = new Label { Text = label };
        l.AddThemeColorOverride("font_color", Palette.Text3);
        l.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(l);
        box.AddChild(control);
    }


    // ---------- Phím tắt ----------

    private void BuildShortcuts(VBoxContainer box)
    {
        Hint(box, "Bấm vào ô phím rồi nhấn tổ hợp mới. Esc để huỷ, Backspace để bỏ gán.");

        foreach (var group in Shortcuts.All.GroupBy(a => a.Group))
        {
            Heading(box, group.Key);
            foreach (var action in group)
            {
                var row = new HBoxContainer();
                row.AddChild(new Label { Text = action.Label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

                var custom = AppSettings.Current.Shortcuts.ContainsKey(action.Id);
                var keyBtn = new Button { Text = KeyText(action.Id), CustomMinimumSize = new Vector2(170, 0), ToggleMode = true };
                if (custom) keyBtn.AddThemeColorOverride("font_color", Palette.Accent);
                keyBtn.Pressed += () => StartCapture(action.Id, keyBtn);
                row.AddChild(keyBtn);

                var reset = new Button { Text = "↺", TooltipText = $"Về mặc định ({(action.DefaultKeys.Length > 0 ? action.DefaultKeys : "không gán")})", Disabled = !custom };
                reset.Pressed += () =>
                {
                    AppSettings.Current.Shortcuts.Remove(action.Id);
                    AppSettings.Apply();
                    ShowPage(_page);
                };
                row.AddChild(reset);
                box.AddChild(row);
            }
        }

        box.AddChild(new HSeparator());
        var resetAll = new Button { Text = "Khôi phục tất cả phím tắt mặc định", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, Disabled = AppSettings.Current.Shortcuts.Count == 0 };
        resetAll.Pressed += () =>
        {
            AppSettings.Current.Shortcuts.Clear();
            AppSettings.Apply();
            ShowPage(_page);
        };
        box.AddChild(resetAll);
    }

    private static string KeyText(string id) => Shortcuts.Display(id) is { Length: > 0 } k ? k : "— chưa gán —";

    private void StartCapture(string id, Button button)
    {
        StopCapture();
        _capturing = id;
        _captureButton = button;
        button.Text = "Bấm phím…";
        button.ButtonPressed = true;
    }

    private void StopCapture()
    {
        if (_captureButton is not null && IsInstanceValid(_captureButton) && _capturing is not null)
        {
            _captureButton.Text = KeyText(_capturing);
            _captureButton.ButtonPressed = false;
        }
        _capturing = null;
        _captureButton = null;
    }

    private bool OnCapturedInput(InputEvent ev)
    {
        if (_capturing is null || ev is not InputEventKey { Pressed: true } key || Shortcuts.IsModifierOnly(key)) return false;

        var id = _capturing;
        if (key.Keycode == Key.Escape && !key.CtrlPressed && !key.AltPressed && !key.ShiftPressed)
        {
            StopCapture();
            return true;
        }

        var keys = key.Keycode == Key.Backspace && !key.CtrlPressed && !key.AltPressed && !key.ShiftPressed
            ? ""
            : Shortcuts.FromEvent(key);

        var conflict = Shortcuts.ConflictWith(id, keys);
        if (conflict is not null)
        {
            // Gỡ phím khỏi hành động cũ thay vì từ chối: người dùng đã chủ đích
            // bấm tổ hợp này, bắt họ đi gỡ chỗ khác trước là thêm một vòng vô ích.
            AppSettings.Current.Shortcuts[conflict.Id] = "";
            ToastLayer.Show($"{keys} trước đây là của \"{conflict.Label}\" — đã gỡ khỏi đó.");
        }

        var defaults = Shortcuts.Find(id).DefaultKeys;
        if (string.Equals(keys, defaults, StringComparison.OrdinalIgnoreCase)) AppSettings.Current.Shortcuts.Remove(id);
        else AppSettings.Current.Shortcuts[id] = keys;

        _capturing = null;
        _captureButton = null;
        AppSettings.Apply();
        ShowPage(_page);
        return true;
    }

    // ---------- Sắp có ----------

    private static void BuildComingSoon(VBoxContainer box, string title, string description)
    {
        Heading(box, title);
        var badge = new Label { Text = "SẮP CÓ" };
        badge.AddThemeColorOverride("font_color", Palette.Warn);
        box.AddChild(badge);
        box.AddChild(new Label { Text = description, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(520, 0) });
        Hint(box, "Xem thứ tự các phần sắp làm trong docs/LO-TRINH.md.");
    }

    // ---------- khối dựng dùng chung ----------

    private static void Heading(VBoxContainer box, string text)
    {
        var lbl = new Label { Text = text.ToUpperInvariant() };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        lbl.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(lbl);
    }

    private static void Hint(VBoxContainer box, string text)
    {
        var lbl = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        box.AddChild(lbl);
    }

    private static void Check(VBoxContainer box, string text, bool value, Action<bool> onChanged)
    {
        var check = new CheckBox { Text = text, ButtonPressed = value };
        check.Toggled += v => { onChanged(v); AppSettings.Apply(); };
        box.AddChild(check);
    }

    private static void Labeled(VBoxContainer box, string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(160, 0) });
        row.AddChild(control);
        box.AddChild(row);
    }
}

/// <summary>Nút vô hình chỉ để nhận sự kiện _input trong viewport của cửa sổ Cài đặt.</summary>
public partial class SettingsInputCatcher : Control
{
    private readonly Func<InputEvent, bool> _handler;

    public SettingsInputCatcher() : this(_ => false) { }

    public SettingsInputCatcher(Func<InputEvent, bool> handler)
    {
        _handler = handler;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Input(InputEvent ev)
    {
        if (_handler(ev)) GetViewport().SetInputAsHandled();
    }
}
