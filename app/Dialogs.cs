using System;
using System.Linq;
using Godot;

namespace VNdev.App;

/// <summary>
/// Hộp thoại và thông báo dùng chung. Gom một chỗ để mọi hộp thoại trong app
/// có cùng kích thước, cùng thứ tự nút và cùng cách đóng bằng Esc.
/// </summary>
public static class Dialogs
{
    public const string AppVersion = "0.9.0";
    public const string RepoUrl = "https://github.com/Erige005/VNdev";

    /// <summary>Hỏi có/không. Nút nguy hiểm tô đỏ để không bấm nhầm theo thói quen.</summary>
    public static void Confirm(Node host, string title, string message, string okText, Action onOk, bool danger = false)
    {
        var dialog = new ConfirmationDialog
        {
            Title = title,
            DialogText = message,
            OkButtonText = okText,
            CancelButtonText = "Huỷ",
            DialogAutowrap = true,
            MinSize = new Vector2I(420, 0),
        };
        if (danger) dialog.GetOkButton().AddThemeColorOverride("font_color", Palette.Err);
        dialog.Confirmed += () => { onOk(); dialog.QueueFree(); };
        dialog.Canceled += dialog.QueueFree;
        host.AddChild(dialog);
        dialog.PopupCentered();
        // Nút an toàn nhận focus trước: Enter theo phản xạ không được xoá mất chương.
        if (danger) dialog.GetCancelButton().GrabFocus();
    }

    /// <summary>Xin một dòng chữ, ví dụ tên chương mới.</summary>
    public static void Prompt(Node host, string title, string label, string initial, Action<string> onOk)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = "Đồng ý", CancelButtonText = "Huỷ", MinSize = new Vector2I(420, 0) };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = label });
        var edit = new LineEdit { Text = initial, SelectAllOnFocus = true };
        box.AddChild(edit);
        dialog.AddChild(box);
        dialog.RegisterTextEnter(edit);

        void Submit()
        {
            var text = edit.Text.Trim();
            if (text.Length > 0) onOk(text);
            dialog.QueueFree();
        }

        dialog.Confirmed += Submit;
        dialog.Canceled += dialog.QueueFree;
        host.AddChild(dialog);
        dialog.PopupCentered();
        edit.GrabFocus();
    }

    public static void About(Node host)
    {
        var dialog = new AcceptDialog { Title = "Giới thiệu VNdev", OkButtonText = "Đóng", MinSize = new Vector2I(440, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);

        var name = new Label { Text = "VNdev" };
        name.AddThemeFontSizeOverride("font_size", 30);
        name.AddThemeColorOverride("font_color", Palette.Text);
        box.AddChild(name);

        var version = new Label { Text = $"Phiên bản {AppVersion}" };
        version.AddThemeColorOverride("font_color", Palette.Accent);
        box.AddChild(version);

        box.AddChild(new Label
        {
            Text = "Công cụ tạo visual novel trực quan — kéo thả cốt truyện, soạn cảnh có xem trước, chơi thử ngay trong app.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(400, 0),
        });

        var engine = Engine.GetVersionInfo();
        var tech = new Label { Text = $"Godot {engine["string"]} · .NET {System.Environment.Version}" };
        tech.AddThemeColorOverride("font_color", Palette.Text3);
        box.AddChild(tech);

        var link = new LinkButton { Text = RepoUrl, Uri = RepoUrl };
        box.AddChild(link);

        dialog.AddChild(box);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        host.AddChild(dialog);
        dialog.PopupCentered();
    }

    public static void ShortcutList(Node host)
    {
        var dialog = new AcceptDialog { Title = "Phím tắt", OkButtonText = "Đóng", MinSize = new Vector2I(460, 520) };
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(440, 460) };
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(grid);

        foreach (var group in Shortcuts.All.GroupBy(a => a.Group))
        {
            var header = new Label { Text = group.Key.ToUpperInvariant() };
            header.AddThemeColorOverride("font_color", Palette.Text3);
            grid.AddChild(header);
            grid.AddChild(new Control());

            foreach (var action in group)
            {
                grid.AddChild(new Label { Text = action.Label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                var keys = new Label { Text = Shortcuts.Display(action.Id) is { Length: > 0 } k ? k : "—" };
                keys.AddThemeColorOverride("font_color", Palette.Text);
                grid.AddChild(keys);
            }
        }

        var hint = new Label { Text = "Đổi phím ở Tệp → Cài đặt → Phím tắt." };
        hint.AddThemeColorOverride("font_color", Palette.Text3);
        var box = new VBoxContainer();
        box.AddChild(scroll);
        box.AddChild(hint);

        dialog.AddChild(box);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        host.AddChild(dialog);
        dialog.PopupCentered();
    }
}

/// <summary>
/// Thông báo nhỏ hiện vài giây ở góc dưới rồi tự tắt — cho những việc cần
/// báo "đã xong" mà không đáng bắt người dùng bấm OK.
/// </summary>
public partial class ToastLayer : CanvasLayer
{
    private VBoxContainer _stack = null!;

    public static ToastLayer? Instance { get; private set; }

    public enum Kind { Info, Ok, Error }

    public override void _Ready()
    {
        Instance = this;
        Layer = 100;

        var anchor = new MarginContainer();
        anchor.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        anchor.MouseFilter = Control.MouseFilterEnum.Ignore;
        anchor.AddThemeConstantOverride("margin_bottom", 44);
        anchor.AddThemeConstantOverride("margin_right", 20);
        AddChild(anchor);

        _stack = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _stack.AddThemeConstantOverride("separation", 6);
        anchor.AddChild(_stack);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public static void Show(string text, Kind kind = Kind.Info) => Instance?.Push(text, kind);

    private void Push(string text, Kind kind)
    {
        // Tối đa ba thông báo cùng lúc — bấm Ctrl+Z liên tục không được phủ kín màn hình.
        while (_stack.GetChildCount() >= 3) _stack.GetChild(0).Free();

        var color = kind switch { Kind.Ok => Palette.Ok, Kind.Error => Palette.Err, _ => Palette.Accent };
        var box = new StyleBoxFlat { BgColor = Palette.Panel3, BorderColor = color, BorderWidthLeft = 3 };
        box.SetCornerRadiusAll(6);
        box.SetContentMarginAll(10);
        box.ShadowColor = new Color(0, 0, 0, 0.4f);
        box.ShadowSize = 6;

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", box);
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", Palette.Text);
        panel.AddChild(label);
        _stack.AddChild(panel);

        var tween = panel.CreateTween();
        panel.Modulate = new Color(1, 1, 1, 0);
        tween.TweenProperty(panel, "modulate:a", 1f, 0.15f);
        tween.TweenInterval(kind == Kind.Error ? 5f : 2.5f);
        tween.TweenProperty(panel, "modulate:a", 0f, 0.4f);
        tween.TweenCallback(Callable.From(panel.QueueFree));
    }
}
