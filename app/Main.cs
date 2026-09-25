using System;
using System.Linq;
using Godot;

namespace VNdev.App;

/// <summary>
/// Nút gốc của app: chuyển giữa màn hình chào và editor, giữ cài đặt, cửa sổ
/// và phiên dự án đang mở. Không chứa logic dự án. Đúng nguyên tắc "một cửa
/// sổ duy nhất" trong docs/design/ui-ux.html.
/// </summary>
public partial class Main : Control
{
    private static readonly Vector2I MinWindowSize = new(1100, 680);

    private Control? _current;
    private ProjectSession? _session;
    private ColorRect _bg = null!;
    private string _lastAccent = "";

    public static Main? Instance { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
        AppSettings.Load();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        AppSettings.Changed -= OnSettingsChanged;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        RestoreWindow();
        ApplySettings();
        _lastAccent = AppSettings.Current.AccentColor;
        AppSettings.Changed += OnSettingsChanged;

        _bg = new ColorRect { Color = Palette.Bg, MouseFilter = MouseFilterEnum.Ignore };
        _bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_bg);

        AddChild(new ToastLayer());

        // Mở lại dự án gần nhất nếu người dùng bật — vẫn đúng ràng buộc "app khởi
        // động trống": không có dự án mẫu, chỉ mở lại thứ chính họ đã mở.
        var last = AppSettings.Current.RecentProjects.FirstOrDefault();
        if (AppSettings.Current.ReopenLastProject && last is not null && System.IO.Directory.Exists(last.Path))
        {
            OpenProject(last.Path);
            if (_session is not null) return;
        }
        ShowWelcome();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) SaveWindow();
    }

    /// <summary>
    /// Phím tắt cấp app — chạy được cả ở màn hình chào, nơi không có thanh menu.
    /// Ở editor, các phím này đã gắn vào menu nên bỏ qua để không chạy hai lần.
    /// </summary>
    public override void _ShortcutInput(InputEvent ev)
    {
        if (_current is not WelcomeScreen welcome) return;

        if (Shortcuts.Matches(Shortcuts.Settings, ev)) OpenSettings();
        else if (Shortcuts.Matches(Shortcuts.Fullscreen, ev)) ToggleFullscreen();
        else if (Shortcuts.Matches(Shortcuts.Quit, ev)) Quit();
        else if (Shortcuts.Matches(Shortcuts.ShortcutList, ev)) Dialogs.ShortcutList(this);
        else if (Shortcuts.Matches(Shortcuts.NewProject, ev)) welcome.FocusCreate();
        else if (Shortcuts.Matches(Shortcuts.OpenProject, ev)) welcome.ShowOpenDialog();
        else return;

        GetViewport().SetInputAsHandled();
    }

    // ---------- màn hình ----------

    private void ShowWelcome(bool focusCreate = false)
    {
        _session = null;
        var welcome = new WelcomeScreen();
        welcome.OpenRequested += OpenProject;
        welcome.CreateRequested += CreateProject;
        SwitchTo(welcome);
        SetWindowTitle(null);
        if (focusCreate) welcome.FocusCreate();
    }

    private void OpenProject(string dir)
    {
        var session = ProjectOpener.Open(dir, out var error);
        if (session is null)
        {
            ToastLayer.Show(error ?? "Không mở được dự án.", ToastLayer.Kind.Error);
            if (_current is WelcomeScreen w) w.ShowError(error ?? "");
            return;
        }
        ShowEditor(session, null);
        ToastLayer.Show($"Đã mở \"{session.Title}\".", ToastLayer.Kind.Ok);
    }

    private void CreateProject(string dir, string title, string? author)
    {
        var session = ProjectOpener.Create(dir, title, author, out var error);
        if (session is null)
        {
            if (_current is WelcomeScreen w) w.ShowError(error ?? "");
            return;
        }
        ShowEditor(session, null);
        ToastLayer.Show($"Đã tạo dự án \"{session.Title}\".", ToastLayer.Kind.Ok);
    }

    private void ShowEditor(ProjectSession session, EditorViewState? view)
    {
        _session = session;
        var editor = new EditorScreen(session, view);
        editor.CloseRequested += () => ShowWelcome();
        editor.NewProjectRequested += () => ShowWelcome(focusCreate: true);
        editor.OpenProjectRequested += OpenProject;
        editor.ReloadRequested += state => ShowEditor(session, state);
        SwitchTo(editor);
        SetWindowTitle(session.Title);
    }

    private void SwitchTo(Control screen)
    {
        if (_current is not null)
        {
            RemoveChild(_current);
            _current.QueueFree();
        }

        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
        // Toast phải nằm trên cùng — nó là CanvasLayer nên thứ tự con không ảnh
        // hưởng, còn nền thì luôn ở dưới.
        MoveChild(_bg, 0);
        _current = screen;
    }

    private void SetWindowTitle(string? project)
        => GetWindow().Title = project is null ? "VNdev" : $"{project} — VNdev";

    // ---------- lệnh cấp app ----------

    public void OpenSettings(int page = 0)
    {
        if (GetNodeOrNull("SettingsDialog") is SettingsDialog existing)
        {
            existing.GrabFocus();
            return;
        }
        var dialog = new SettingsDialog(page) { Name = "SettingsDialog" };
        AddChild(dialog);
        dialog.PopupCentered();
    }

    public void ToggleFullscreen()
    {
        var window = GetWindow();
        window.Mode = window.Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Maximized : Window.ModeEnum.Fullscreen;
    }

    public void Quit()
    {
        SaveWindow();
        GetTree().Quit();
    }

    // ---------- cài đặt ----------

    private void ApplySettings()
    {
        Theme = AppTheme.Build();
        GetWindow().ContentScaleFactor = AppSettings.Current.UiScale;
    }

    private void OnSettingsChanged()
    {
        ApplySettings();

        // Màu nhấn đã được tô thẳng vào vài chỗ lúc dựng màn hình (logo, nút đang
        // bật) — đổi màu thì dựng lại màn hình hiện tại, giữ nguyên chỗ đang xem.
        if (AppSettings.Current.AccentColor != _lastAccent)
        {
            _lastAccent = AppSettings.Current.AccentColor;
            if (_current is EditorScreen editor && _session is not null) ShowEditor(_session, editor.CaptureViewState());
            else if (_current is WelcomeScreen) ShowWelcome();
        }
    }

    // ---------- cửa sổ ----------

    private void RestoreWindow()
    {
        var window = GetWindow();
        window.MinSize = MinWindowSize;

        var saved = AppSettings.Current.Window;
        if (saved is null || saved.Width < MinWindowSize.X || saved.Height < MinWindowSize.Y)
        {
            var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            var size = new Vector2I(Math.Min(1440, screen.Size.X - 80), Math.Min(900, screen.Size.Y - 80));
            window.Size = size;
            window.Position = screen.Position + (screen.Size - size) / 2;
            return;
        }

        // Màn hình phụ đã rút ra thì toạ độ cũ nằm ngoài mọi màn hình — kiểm tra
        // trước, không thì cửa sổ mở ra ở chỗ không ai nhìn thấy.
        var rect = new Rect2I(saved.X, saved.Y, saved.Width, saved.Height);
        var visible = Enumerable.Range(0, DisplayServer.GetScreenCount())
            .Any(i => DisplayServer.ScreenGetUsableRect(i).Intersects(rect));
        window.Size = rect.Size;
        if (visible) window.Position = rect.Position;
        else window.MoveToCenter();
        if (saved.Maximized) window.Mode = Window.ModeEnum.Maximized;
    }

    private void SaveWindow()
    {
        var window = GetWindow();
        var maximized = window.Mode is Window.ModeEnum.Maximized or Window.ModeEnum.Fullscreen;
        var previous = AppSettings.Current.Window;
        // Đang phóng to thì kích thước hiện tại là cả màn hình — giữ kích thước
        // thường cũ để lần sau bỏ phóng to còn về đúng cỡ.
        AppSettings.Current.Window = maximized && previous is not null
            ? new WindowState { X = previous.X, Y = previous.Y, Width = previous.Width, Height = previous.Height, Maximized = true }
            : new WindowState { X = window.Position.X, Y = window.Position.Y, Width = window.Size.X, Height = window.Size.Y, Maximized = maximized };
        AppSettings.Save();
    }
}
