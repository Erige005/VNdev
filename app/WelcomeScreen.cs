using System;
using System.IO;
using System.Linq;
using Godot;

namespace VNdev.App;

/// <summary>
/// Màn hình đầu tiên khi mở app: dự án gần đây bên phải, Tạo dự án mới / Mở
/// dự án bên trái. Không có dự án mẫu nào nạp sẵn — người dùng phải tự tạo
/// hoặc tự chọn thư mục, giống Ren'Py và VS Code (xem CLAUDE.md, ràng buộc số 5).
/// </summary>
public partial class WelcomeScreen : Control
{
    /// <summary>Người dùng muốn mở thư mục dự án này.</summary>
    public event Action<string>? OpenRequested;

    /// <summary>Tạo dự án mới: thư mục, tên, tác giả.</summary>
    public event Action<string, string, string?>? CreateRequested;

    private LineEdit _titleEdit = null!;
    private LineEdit _authorEdit = null!;
    private Label _errorLabel = null!;
    private VBoxContainer _recentBox = null!;

    public override void _Ready()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 16);
        AddChild(margin);

        var layout = new VBoxContainer();
        margin.AddChild(layout);

        // Hàng trên: nút Cài đặt và Giới thiệu ở góc phải, như các app desktop khác.
        var top = new HBoxContainer();
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var settings = new Button { Text = "⚙  Cài đặt", Flat = true, TooltipText = $"Cài đặt ({Shortcuts.Display(Shortcuts.Settings)})" };
        settings.Pressed += () => Main.Instance?.OpenSettings();
        top.AddChild(settings);
        var about = new Button { Text = "ⓘ  Giới thiệu", Flat = true };
        about.Pressed += () => Dialogs.About(this);
        top.AddChild(about);
        layout.AddChild(top);

        var center = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        layout.AddChild(center);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 48);
        center.AddChild(columns);

        columns.AddChild(BuildStartColumn());
        columns.AddChild(BuildRecentColumn());

        var footer = new Label { Text = $"VNdev {Dialogs.AppVersion}  ·  F1: phím tắt", HorizontalAlignment = HorizontalAlignment.Center };
        footer.AddThemeColorOverride("font_color", Palette.Text3);
        footer.AddThemeFontSizeOverride("font_size", 12);
        layout.AddChild(footer);

        RefreshRecent();
    }

    private Control BuildStartColumn()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        box.AddThemeConstantOverride("separation", 12);

        var title = new Label { Text = "VNdev" };
        title.AddThemeColorOverride("font_color", Palette.Text);
        title.AddThemeFontSizeOverride("font_size", 44);
        box.AddChild(title);

        var subtitle = new Label { Text = "Công cụ tạo visual novel trực quan — kéo thả cốt truyện, soạn cảnh, chơi thử ngay.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        subtitle.AddThemeColorOverride("font_color", Palette.Text2);
        box.AddChild(subtitle);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        box.AddChild(Heading("Dự án mới"));

        _titleEdit = new LineEdit { PlaceholderText = "Tên dự án, ví dụ: Ký ức mùa hè" };
        _titleEdit.TextSubmitted += _ => OnCreatePressed();
        box.AddChild(_titleEdit);
        _authorEdit = new LineEdit { PlaceholderText = "Tác giả (không bắt buộc)" };
        _authorEdit.TextSubmitted += _ => OnCreatePressed();
        box.AddChild(_authorEdit);

        var createBtn = new Button { Text = "+  Tạo dự án mới…", TooltipText = $"Chọn một thư mục trống để tạo dự án ({Shortcuts.Display(Shortcuts.NewProject)})" };
        var accent = new StyleBoxFlat { BgColor = Palette.Accent };
        accent.SetCornerRadiusAll(6);
        accent.SetContentMarginAll(10);
        createBtn.AddThemeStyleboxOverride("normal", accent);
        var accentHover = (StyleBoxFlat)accent.Duplicate();
        accentHover.BgColor = Palette.Accent.Lightened(0.12f);
        createBtn.AddThemeStyleboxOverride("hover", accentHover);
        createBtn.Pressed += OnCreatePressed;
        box.AddChild(createBtn);

        box.AddChild(new HSeparator());

        var openBtn = new Button { Text = "📂  Mở dự án có sẵn…", TooltipText = $"Chọn thư mục có file project.json ({Shortcuts.Display(Shortcuts.OpenProject)})" };
        openBtn.Pressed += ShowOpenDialog;
        box.AddChild(openBtn);

        _errorLabel = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _errorLabel.AddThemeColorOverride("font_color", Palette.Err);
        box.AddChild(_errorLabel);

        return box;
    }

    private Control BuildRecentColumn()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 64) });
        box.AddChild(Heading("Mở gần đây"));

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 360), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _recentBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _recentBox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_recentBox);
        box.AddChild(scroll);
        return box;
    }

    private void RefreshRecent()
    {
        foreach (var child in _recentBox.GetChildren()) child.QueueFree();

        var recents = AppSettings.Current.RecentProjects;
        if (recents.Count == 0)
        {
            var empty = new Label
            {
                Text = "Chưa mở dự án nào.\nDự án bạn tạo hoặc mở sẽ hiện ở đây để lần sau mở nhanh.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            empty.AddThemeColorOverride("font_color", Palette.Text3);
            _recentBox.AddChild(empty);
            return;
        }

        foreach (var recent in recents)
        {
            var exists = Directory.Exists(recent.Path);
            var row = new HBoxContainer();

            var card = new Button { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 58), Disabled = !exists };
            card.TooltipText = exists ? recent.Path : $"Không còn thư mục này:\n{recent.Path}";
            card.Pressed += () => OpenRequested?.Invoke(recent.Path);

            var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            text.SetAnchorsPreset(LayoutPreset.FullRect);
            text.OffsetLeft = 12;
            text.OffsetTop = 8;
            text.AddThemeConstantOverride("separation", 2);
            var name = new Label { Text = recent.Title, MouseFilter = MouseFilterEnum.Ignore };
            name.AddThemeColorOverride("font_color", exists ? Palette.Text : Palette.Text3);
            text.AddChild(name);
            var sub = new Label
            {
                Text = exists ? $"{Shorten(recent.Path)}  ·  {Ago(recent.LastOpened)}" : "Không tìm thấy thư mục — đã bị chuyển hoặc xoá",
                MouseFilter = MouseFilterEnum.Ignore,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                CustomMinimumSize = new Vector2(360, 0),
            };
            sub.AddThemeColorOverride("font_color", exists ? Palette.Text3 : Palette.Err);
            sub.AddThemeFontSizeOverride("font_size", 12);
            text.AddChild(sub);
            card.AddChild(text);
            row.AddChild(card);

            var remove = new Button { Text = "✕", Flat = true, TooltipText = "Bỏ khỏi danh sách (không xoá thư mục)" };
            remove.Pressed += () =>
            {
                AppSettings.Current.RemoveRecent(recent.Path);
                RefreshRecent();
            };
            row.AddChild(remove);

            _recentBox.AddChild(row);
        }
    }

    private static string Shorten(string path)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
    }

    private static string Ago(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalMinutes < 1) return "vừa xong";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} phút trước";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} giờ trước";
        if (span.TotalDays < 2) return "hôm qua";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} ngày trước";
        return time.ToString("dd/MM/yyyy");
    }

    private static Label Heading(string text)
    {
        var lbl = new Label { Text = text.ToUpperInvariant() };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        lbl.AddThemeFontSizeOverride("font_size", 12);
        return lbl;
    }

    public void FocusCreate()
    {
        _titleEdit.GrabFocus();
        _titleEdit.SelectAll();
    }

    public void ShowError(string message) => _errorLabel.Text = message;

    private void OnCreatePressed()
    {
        var title = _titleEdit.Text.Trim();
        if (title.Length == 0)
        {
            _errorLabel.Text = "Nhập tên dự án trước khi chọn thư mục.";
            _titleEdit.GrabFocus();
            return;
        }
        _errorLabel.Text = "";

        var author = _authorEdit.Text.Trim();
        ShowFolderDialog("Chọn thư mục trống để tạo dự án", dir => CreateRequested?.Invoke(dir, title, author.Length == 0 ? null : author));
    }

    public void ShowOpenDialog()
    {
        _errorLabel.Text = "";
        ShowFolderDialog("Chọn thư mục dự án đã có", dir => OpenRequested?.Invoke(dir));
    }

    private void ShowFolderDialog(string title, Action<string> onSelected)
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = title,
        };
        // Mở ở thư mục cha của dự án gần nhất — người dùng thường để các dự án cạnh nhau.
        var last = AppSettings.Current.RecentProjects.FirstOrDefault();
        if (last is not null && Directory.GetParent(last.Path) is { Exists: true } parent) dialog.CurrentDir = parent.FullName;

        AddChild(dialog);
        dialog.DirSelected += dir =>
        {
            dialog.QueueFree();
            onSelected(dir);
        };
        dialog.Canceled += dialog.QueueFree;
        dialog.PopupCentered(new Vector2I(760, 500));
    }
}
