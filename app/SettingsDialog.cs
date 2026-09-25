using System;
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
            case 2: BuildComingSoon(box, "AI Providers",
                "Kết nối Claude, ChatGPT, Gemini, DeepSeek hoặc Ollama chạy local, rồi gán model riêng cho từng việc: viết thoại, dịch hàng loạt, kiểm tra nhất quán. Khoá API sẽ lưu trong Windows Credential Manager, không nằm trong file dự án.");
                break;
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
