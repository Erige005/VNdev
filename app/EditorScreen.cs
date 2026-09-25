using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VNdev.Core;
using VNdev.Core.Io;
using VNdev.Core.Model;
using VNdev.Core.Validation;

namespace VNdev.App;

/// <summary>
/// Những gì cần nhớ để dựng lại màn hình đúng như cũ sau khi hoàn tác hoặc đổi
/// cài đặt — người dùng không được thấy canvas nhảy về góc hay tab tự đổi.
/// </summary>
public sealed record EditorViewState(int Tab, string GraphId, Vector2 Scroll, float Zoom, string? SceneId);

/// <summary>
/// Màn hình chính: ba cột theo docs/design/ui-ux.html — cây chương và bảng
/// lỗi bên trái, Story Graph ở giữa, thuộc tính node đang chọn bên phải.
/// Menu và thanh công cụ ở trên, thanh trạng thái ở dưới.
/// </summary>
public partial class EditorScreen : Control
{
    private readonly ProjectSession _session;
    private readonly LoadedProject _loaded;
    private readonly IFileSystem _fs;
    private readonly string _projectDir;
    private readonly EditorViewState? _restore;

    private string _currentGraphId;
    private GraphEdit _graphEdit = null!;
    private GraphSync? _graphSync;
    private ItemList _chapterList = null!;
    private ItemList _errorList = null!;
    private Label _errorCountLabel = null!;
    private VBoxContainer _inspectorBox = null!;
    private Label _chapterTitle = null!;

    private Control _storyBody = null!;
    private SceneScreen _sceneScreen = null!;
    private Control _characterScreen = null!;
    private Control _assetScreen = null!;

    private Control[] _tabScreens = null!;
    private Button[] _tabButtons = null!;
    private int _tab;

    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Label _savedLabel = null!;
    private Button _issuesButton = null!;
    private Label _zoomLabel = null!;
    private Godot.Timer _commitTimer = null!;

    private readonly List<(PopupMenu Menu, int Id, string Action)> _menuShortcuts = new();
    private PopupMenu _editMenu = null!;
    private PopupMenu _viewMenu = null!;
    private PopupMenu _recentMenu = null!;

    private List<ValidationIssue> _lastIssues = new();

    private Ai.AiPanel? _aiPanel;
    private Control _aiHost = null!;
    private Button _aiButton = null!;

    /// <summary>Node đã sao chép, dạng JSON để dán sang chương khác vẫn là bản độc lập.</summary>
    private static string? _clipboard;

    public event Action? CloseRequested;
    public event Action? NewProjectRequested;
    public event Action<string>? OpenProjectRequested;
    public event Action<EditorViewState>? ReloadRequested;

    public EditorScreen(ProjectSession session, EditorViewState? restore = null)
    {
        _session = session;
        _loaded = session.Loaded;
        _fs = session.Fs;
        _projectDir = session.Dir;
        _restore = restore;
        _currentGraphId = restore is not null && _loaded.Graphs.ContainsKey(restore.GraphId)
            ? restore.GraphId
            : _loaded.Project.Graphs.FirstOrDefault() ?? "";
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        root.AddChild(BuildToolbar());

        // Các màn hình chồng lên nhau trong cùng một vùng, đổi qua lại bằng
        // Visible thay vì dựng lại — GraphEdit giữ nguyên trạng thái pan/zoom
        // và GraphSync không phải khởi tạo lại mỗi lần chuyển tab.
        // Thân chính chia hai: vùng làm việc theo tab, và khung trợ lý AI bên
        // phải dùng chung cho cả bốn tab — đang soạn cảnh hay dựng đồ thị đều
        // hỏi được, không phải chuyển màn hình.
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        root.AddChild(body);

        var workspace = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(workspace);

        _aiHost = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(_aiHost);

        _storyBody = new HBoxContainer();
        _storyBody.SetAnchorsPreset(LayoutPreset.FullRect);
        _storyBody.AddThemeConstantOverride("separation", 0);
        workspace.AddChild(_storyBody);

        _storyBody.AddChild(BuildLeftPanel());
        _storyBody.AddChild(BuildCenterPanel());
        _storyBody.AddChild(BuildRightPanel());

        _sceneScreen = new SceneScreen(_loaded, _fs, _projectDir);
        _sceneScreen.SetAnchorsPreset(LayoutPreset.FullRect);
        _sceneScreen.Visible = false;
        workspace.AddChild(_sceneScreen);

        _characterScreen = new CharacterScreen(_loaded, _fs, OnGraphChanged);
        _characterScreen.SetAnchorsPreset(LayoutPreset.FullRect);
        _characterScreen.Visible = false;
        workspace.AddChild(_characterScreen);

        _assetScreen = new AssetScreen(_loaded, _fs, _projectDir);
        _assetScreen.SetAnchorsPreset(LayoutPreset.FullRect);
        _assetScreen.Visible = false;
        workspace.AddChild(_assetScreen);

        _tabScreens = new[] { _storyBody, _sceneScreen, _characterScreen, _assetScreen };

        root.AddChild(BuildStatusBar());

        // Gom các lần ghi liên tiếp thành một bước hoàn tác: đồng hồ đếm lại từ
        // đầu mỗi lần ghi, chỉ khi người dùng ngừng tay mới chốt.
        _commitTimer = new Godot.Timer { OneShot = true, WaitTime = 0.7 };
        _commitTimer.Timeout += () => { _session.Commit(); UpdateUndoUi(); };
        AddChild(_commitTimer);

        _session.Saved += OnSessionSaved;
        _session.Ai.ProjectChanged += OnAiChangedProject;
        AppSettings.Changed += OnSettingsChanged;
        SetAiPanel(AppSettings.Current.AiPanelOpen, focus: false);

        LoadGraphIntoEditor(_currentGraphId);
        RefreshChapterList();
        ApplyGraphSettings();

        ShowTab(_restore?.Tab ?? 0);
        if (_restore is not null)
        {
            if (_restore.SceneId is not null) _sceneScreen.TrySelect(_restore.SceneId);
            // Đợi một khung hình để GraphEdit có kích thước thật rồi mới đặt lại
            // vị trí cuộn, không thì Godot kẹp giá trị về 0.
            Callable.From(() =>
            {
                _graphEdit.Zoom = _restore.Zoom;
                _graphEdit.ScrollOffset = _restore.Scroll;
            }).CallDeferred();
        }

        UpdateSavedLabel();
        UpdateUndoUi();
    }

    public override void _ExitTree()
    {
        // Phiên dự án sống lâu hơn màn hình này (hoàn tác dựng màn hình mới), nên
        // phải tự gỡ đăng ký — không thì phiên gọi vào màn hình đã bị giải phóng.
        _session.Saved -= OnSessionSaved;
        _session.Ai.ProjectChanged -= OnAiChangedProject;
        AppSettings.Changed -= OnSettingsChanged;
    }

    public override void _Process(double delta)
    {
        var text = $"{Mathf.RoundToInt(_graphEdit.Zoom * 100)}%";
        if (_zoomLabel.Text != text) _zoomLabel.Text = text;
    }

    public EditorViewState CaptureViewState()
        => new(_tab, _currentGraphId, _graphEdit.ScrollOffset, _graphEdit.Zoom, _sceneScreen.CurrentSceneId);

    private bool PlayerOpen => GetNodeOrNull("PlayerOverlay") is not null;

    // ================= Tab =================

    private void ShowTab(int index)
    {
        _tab = index;
        for (var i = 0; i < _tabScreens.Length; i++)
        {
            _tabScreens[i].Visible = i == index;
            _tabButtons[i].ButtonPressed = i == index;
        }
        for (var i = 0; i < 4; i++) _viewMenu.SetItemChecked(_viewMenu.GetItemIndex(100 + i), i == index);

        // Biến/nhân vật/cảnh có thể đã đổi ở tab khác — kiểm tra lại đồ thị
        // hiện tại mỗi khi quay về tab Cốt truyện.
        if (index == 0) RevalidateAndRefreshErrors();
    }

    private void OpenScene(string sceneId)
    {
        ShowTab(1);
        _sceneScreen.OpenOrCreate(sceneId);
    }

    // ================= Thanh công cụ + menu =================

    private Control BuildToolbar()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 44) };
        var style = new StyleBoxFlat { BgColor = Palette.Panel2, BorderColor = Palette.Line, BorderWidthBottom = 1 };
        style.SetContentMarginAll(4);
        style.ContentMarginLeft = 8;
        style.ContentMarginRight = 8;
        bar.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        bar.AddChild(row);

        var logo = new Label { Text = "VNdev" };
        logo.AddThemeColorOverride("font_color", Palette.Accent);
        logo.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(logo);

        row.AddChild(BuildMenuBar());
        row.AddChild(new VSeparator());

        _undoButton = IconButton(row, "↶", "Hoàn tác", Shortcuts.Undo, Undo);
        _redoButton = IconButton(row, "↷", "Làm lại", Shortcuts.Redo, Redo);

        // Tab chế độ nằm giữa toolbar như bản thiết kế — hai khoảng giãn hai bên đẩy nó vào giữa.
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var tabLabels = new[] { "⬡  Cốt truyện", "▣  Cảnh", "◉  Nhân vật", "▤  Asset" };
        var tabActions = new[] { Shortcuts.TabStory, Shortcuts.TabScene, Shortcuts.TabCharacter, Shortcuts.TabAsset };
        _tabButtons = new Button[tabLabels.Length];
        var tabGroup = new HBoxContainer();
        tabGroup.AddThemeConstantOverride("separation", 2);
        for (var i = 0; i < tabLabels.Length; i++)
        {
            var index = i;
            var btn = new Button { Text = tabLabels[i], ToggleMode = true, ButtonPressed = i == 0, FocusMode = FocusModeEnum.None };
            btn.TooltipText = WithKeys(tabLabels[i].Trim(), tabActions[i]);
            btn.Pressed += () => ShowTab(index);
            tabGroup.AddChild(btn);
            _tabButtons[i] = btn;
        }
        row.AddChild(tabGroup);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _aiButton = new Button { Text = "✦  Trợ lý", ToggleMode = true, FocusMode = FocusModeEnum.None, TooltipText = WithKeys("Mở khung trợ lý AI", Shortcuts.AiPanel) };
        _aiButton.Pressed += () => SetAiPanel(_aiButton.ButtonPressed, focus: true);
        row.AddChild(_aiButton);

        var play = new Button { Text = "▶  Chơi thử", FocusMode = FocusModeEnum.None, TooltipText = WithKeys("Chơi thử từ điểm bắt đầu của chương đầu tiên", Shortcuts.Play) };
        var playStyle = new StyleBoxFlat { BgColor = new Color(Palette.Ok, 0.18f), BorderColor = new Color(Palette.Ok, 0.6f) };
        playStyle.SetBorderWidthAll(1);
        playStyle.SetCornerRadiusAll(6);
        playStyle.SetContentMarginAll(8);
        play.AddThemeStyleboxOverride("normal", playStyle);
        play.AddThemeColorOverride("font_color", Palette.Ok);
        play.Pressed += OpenPlayer;
        row.AddChild(play);

        return bar;
    }

    private static Button IconButton(HBoxContainer row, string icon, string label, string action, Action onPressed)
    {
        var btn = new Button { Text = icon, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(36, 0) };
        btn.AddThemeFontSizeOverride("font_size", 20);
        btn.TooltipText = WithKeys(label, action);
        btn.Pressed += onPressed;
        row.AddChild(btn);
        return btn;
    }

    private static string WithKeys(string label, string action)
        => Shortcuts.Display(action) is { Length: > 0 } keys ? $"{label}  ({keys})" : label;

    private MenuBar BuildMenuBar()
    {
        var menuBar = new MenuBar { Flat = true, PreferGlobalMenu = false };

        var file = AddMenu(menuBar, "Tệp");
        AddItem(file, 0, "Dự án mới…", Shortcuts.NewProject);
        AddItem(file, 1, "Mở dự án…", Shortcuts.OpenProject);
        _recentMenu = new PopupMenu { Name = "Recent" };
        _recentMenu.IdPressed += OnRecentPressed;
        file.AddChild(_recentMenu);
        file.AddSubmenuNodeItem("Mở gần đây", _recentMenu);
        file.AddSeparator();
        AddItem(file, 2, "Mở thư mục dự án", Shortcuts.ShowInExplorer);
        AddItem(file, 3, "Đóng dự án", Shortcuts.CloseProject);
        file.AddSeparator();
        AddItem(file, 4, "Cài đặt…", Shortcuts.Settings);
        file.AddSeparator();
        AddItem(file, 5, "Thoát", Shortcuts.Quit);
        file.AboutToPopup += RefreshRecentMenu;
        file.IdPressed += id =>
        {
            switch (id)
            {
                case 0: NewProjectRequested?.Invoke(); break;
                case 1: ShowOpenDialog(); break;
                case 2: OS.ShellOpen(_projectDir); break;
                case 3: CloseRequested?.Invoke(); break;
                case 4: Main.Instance?.OpenSettings(); break;
                case 5: Main.Instance?.Quit(); break;
            }
        };

        _editMenu = AddMenu(menuBar, "Sửa");
        AddItem(_editMenu, 0, "Hoàn tác", Shortcuts.Undo);
        AddItem(_editMenu, 1, "Làm lại", Shortcuts.Redo);
        _editMenu.AddSeparator();
        AddItem(_editMenu, 2, "Sao chép node", Shortcuts.Copy);
        AddItem(_editMenu, 3, "Dán node", Shortcuts.Paste);
        AddItem(_editMenu, 4, "Nhân bản node", Shortcuts.Duplicate);
        AddItem(_editMenu, 5, "Xoá node đang chọn", Shortcuts.Delete);
        AddItem(_editMenu, 6, "Chọn tất cả node", Shortcuts.SelectAll);
        _editMenu.AddSeparator();
        var addMenu = new PopupMenu { Name = "AddNode" };
        foreach (var (id, label) in NodeTypes.Select((t, i) => (i, t.Label))) addMenu.AddItem(label, id);
        addMenu.IdPressed += id => { ShowTab(0); AddNode(NodeTypes[(int)id].Make()); };
        _editMenu.AddChild(addMenu);
        _editMenu.AddSubmenuNodeItem("Thêm node", addMenu);
        _editMenu.AboutToPopup += UpdateEditMenu;
        _editMenu.IdPressed += id =>
        {
            switch (id)
            {
                case 0: Undo(); break;
                case 1: Redo(); break;
                case 2: CopySelection(); break;
                case 3: Paste(); break;
                case 4: DuplicateSelection(); break;
                case 5: DeleteSelection(); break;
                case 6: SelectAll(); break;
            }
        };

        _viewMenu = AddMenu(menuBar, "Xem");
        AddCheck(_viewMenu, 100, "Cốt truyện", Shortcuts.TabStory, radio: true);
        AddCheck(_viewMenu, 101, "Cảnh", Shortcuts.TabScene, radio: true);
        AddCheck(_viewMenu, 102, "Nhân vật", Shortcuts.TabCharacter, radio: true);
        AddCheck(_viewMenu, 103, "Asset", Shortcuts.TabAsset, radio: true);
        _viewMenu.AddSeparator();
        AddItem(_viewMenu, 0, "Phóng to", Shortcuts.ZoomIn);
        AddItem(_viewMenu, 1, "Thu nhỏ", Shortcuts.ZoomOut);
        AddItem(_viewMenu, 2, "Vừa khung", Shortcuts.ZoomFit);
        _viewMenu.AddSeparator();
        _viewMenu.AddCheckItem("Hiện lưới nền", 3);
        _viewMenu.AddCheckItem("Hiện bản đồ thu nhỏ", 4);
        _viewMenu.AddSeparator();
        AddItem(_viewMenu, 5, "Toàn màn hình", Shortcuts.Fullscreen);
        AddItem(_viewMenu, 6, "Trợ lý AI", Shortcuts.AiPanel);
        _viewMenu.AboutToPopup += () =>
        {
            _viewMenu.SetItemChecked(_viewMenu.GetItemIndex(3), AppSettings.Current.ShowGrid);
            _viewMenu.SetItemChecked(_viewMenu.GetItemIndex(4), AppSettings.Current.ShowMinimap);
        };
        _viewMenu.IdPressed += id =>
        {
            switch (id)
            {
                case >= 100 and <= 103: ShowTab((int)id - 100); break;
                case 0: ZoomBy(1.2f); break;
                case 1: ZoomBy(1 / 1.2f); break;
                case 2: ZoomToFit(); break;
                case 3: AppSettings.Current.ShowGrid = !AppSettings.Current.ShowGrid; AppSettings.Apply(); break;
                case 4: AppSettings.Current.ShowMinimap = !AppSettings.Current.ShowMinimap; AppSettings.Apply(); break;
                case 5: Main.Instance?.ToggleFullscreen(); break;
                case 6: SetAiPanel(_aiPanel is null, focus: true); break;
            }
        };

        var run = AddMenu(menuBar, "Chạy");
        AddItem(run, 0, "Chơi thử", Shortcuts.Play);
        run.IdPressed += _ => OpenPlayer();

        var help = AddMenu(menuBar, "Trợ giúp");
        AddItem(help, 0, "Danh sách phím tắt", Shortcuts.ShortcutList);
        help.AddItem("Hướng dẫn sử dụng", 1);
        help.AddItem("Báo lỗi / góp ý", 2);
        help.AddSeparator();
        help.AddItem("Giới thiệu VNdev", 3);
        help.IdPressed += id =>
        {
            switch (id)
            {
                case 0: Dialogs.ShortcutList(this); break;
                case 1: OS.ShellOpen($"{Dialogs.RepoUrl}/blob/main/docs/CHAY-DU-AN.md"); break;
                case 2: OS.ShellOpen($"{Dialogs.RepoUrl}/issues"); break;
                case 3: Dialogs.About(this); break;
            }
        };

        return menuBar;
    }

    private static PopupMenu AddMenu(MenuBar bar, string title)
    {
        var menu = new PopupMenu();
        bar.AddChild(menu);
        bar.SetMenuTitle(bar.GetMenuCount() - 1, title);
        return menu;
    }

    private void AddItem(PopupMenu menu, int id, string label, string action)
    {
        menu.AddItem(label, id);
        _menuShortcuts.Add((menu, id, action));
        ApplyShortcut(menu, id, action);
    }

    private void AddCheck(PopupMenu menu, int id, string label, string action, bool radio)
    {
        if (radio) menu.AddRadioCheckItem(label, id); else menu.AddCheckItem(label, id);
        _menuShortcuts.Add((menu, id, action));
        ApplyShortcut(menu, id, action);
    }

    private static void ApplyShortcut(PopupMenu menu, int id, string action)
    {
        var index = menu.GetItemIndex(id);
        var shortcut = Shortcuts.Make(action);
        if (shortcut is null) menu.SetItemShortcut(index, new Shortcut(), global: true);
        else menu.SetItemShortcut(index, shortcut, global: true);
    }

    private void RefreshRecentMenu()
    {
        _recentMenu.Clear();
        var recents = AppSettings.Current.RecentProjects
            .Where(r => !string.Equals(System.IO.Path.GetFullPath(r.Path), System.IO.Path.GetFullPath(_projectDir), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (recents.Count == 0)
        {
            _recentMenu.AddItem("(chưa có dự án nào khác)", 999);
            _recentMenu.SetItemDisabled(0, true);
            return;
        }
        for (var i = 0; i < recents.Count; i++)
        {
            _recentMenu.AddItem($"{recents[i].Title}   —   {recents[i].Path}", i);
            _recentMenu.SetItemMetadata(i, recents[i].Path);
        }
    }

    private void OnRecentPressed(long id)
    {
        var index = _recentMenu.GetItemIndex((int)id);
        var path = _recentMenu.GetItemMetadata(index).AsString();
        if (!string.IsNullOrEmpty(path)) OpenProjectRequested?.Invoke(path);
    }

    private void ShowOpenDialog()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Chọn thư mục dự án đã có",
        };
        AddChild(dialog);
        dialog.DirSelected += dir => { dialog.QueueFree(); OpenProjectRequested?.Invoke(dir); };
        dialog.Canceled += dialog.QueueFree;
        dialog.PopupCentered(new Vector2I(760, 500));
    }

    private void UpdateEditMenu()
    {
        var onStory = _tab == 0;
        var hasSelection = onStory && _graphSync is not null && _graphSync.SelectedIds().Count > 0;
        _editMenu.SetItemDisabled(_editMenu.GetItemIndex(0), !_session.CanUndo);
        _editMenu.SetItemDisabled(_editMenu.GetItemIndex(1), !_session.CanRedo);
        _editMenu.SetItemDisabled(_editMenu.GetItemIndex(2), !hasSelection);
        _editMenu.SetItemDisabled(_editMenu.GetItemIndex(3), !onStory || _clipboard is null);
        _editMenu.SetItemDisabled(_editMenu.GetItemIndex(4), !hasSelection);
        _editMenu.SetItemDisabled(_editMenu.GetItemIndex(5), !hasSelection);
        _editMenu.SetItemDisabled(_editMenu.GetItemIndex(6), !onStory);
    }

    private void OnSettingsChanged()
    {
        foreach (var (menu, id, action) in _menuShortcuts) ApplyShortcut(menu, id, action);
        ApplyGraphSettings();
    }

    private void ApplyGraphSettings()
    {
        var s = AppSettings.Current;
        _graphEdit.ShowGrid = s.ShowGrid;
        _graphEdit.SnappingEnabled = s.SnapToGrid;
        _graphEdit.MinimapEnabled = s.ShowMinimap;
    }

    // ================= Thanh trạng thái =================

    private Control BuildStatusBar()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 26) };
        var style = new StyleBoxFlat { BgColor = Palette.Panel, BorderColor = Palette.Line, BorderWidthTop = 1 };
        style.ContentMarginLeft = 10;
        style.ContentMarginRight = 10;
        style.ContentMarginTop = 2;
        style.ContentMarginBottom = 2;
        bar.AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        bar.AddChild(row);

        _savedLabel = new Label();
        _savedLabel.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(_savedLabel);

        _issuesButton = new Button { Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Xem danh sách lỗi ở tab Cốt truyện" };
        _issuesButton.AddThemeFontSizeOverride("font_size", 12);
        _issuesButton.Pressed += () => ShowTab(0);
        row.AddChild(_issuesButton);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _zoomLabel = new Label { TooltipText = "Mức phóng của đồ thị" , MouseFilter = MouseFilterEnum.Pass };
        _zoomLabel.AddThemeFontSizeOverride("font_size", 12);
        _zoomLabel.AddThemeColorOverride("font_color", Palette.Text3);
        row.AddChild(_zoomLabel);

        var path = new Button { Text = _projectDir, Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Mở thư mục dự án trong Explorer" };
        path.AddThemeFontSizeOverride("font_size", 12);
        path.AddThemeColorOverride("font_color", Palette.Text3);
        path.Pressed += () => OS.ShellOpen(_projectDir);
        row.AddChild(path);

        return bar;
    }

    private void OnSessionSaved()
    {
        _commitTimer.Start();
        UpdateSavedLabel();
        UpdateUndoUi();
    }

    private void UpdateSavedLabel()
    {
        if (_session.LastSaved is { } t)
        {
            _savedLabel.Text = $"●  Đã lưu lúc {t:HH:mm:ss}";
            _savedLabel.AddThemeColorOverride("font_color", Palette.Ok);
        }
        else
        {
            _savedLabel.Text = "●  Tự động lưu khi sửa";
            _savedLabel.AddThemeColorOverride("font_color", Palette.Text3);
        }
    }

    private void UpdateUndoUi()
    {
        _undoButton.Disabled = !_session.CanUndo;
        _redoButton.Disabled = !_session.CanRedo;
    }

    // ================= Trợ lý AI =================

    private void SetAiPanel(bool open, bool focus)
    {
        if (open && _aiPanel is null)
        {
            _aiPanel = new Ai.AiPanel(_session, DescribeContext, () => SetAiPanel(false, false)) { SizeFlagsVertical = SizeFlags.ExpandFill };
            _aiHost.AddChild(_aiPanel);
        }
        else if (!open && _aiPanel is not null)
        {
            _aiPanel.QueueFree();
            _aiPanel = null;
        }
        _aiButton.ButtonPressed = open;
        if (open && focus) Callable.From(() => _aiPanel?.FocusInput()).CallDeferred();

        if (AppSettings.Current.AiPanelOpen != open)
        {
            AppSettings.Current.AiPanelOpen = open;
            AppSettings.Save();
        }
    }

    /// <summary>
    /// Mô tả chỗ người dùng đang đứng, gửi kèm mỗi câu hỏi — để "cảnh này",
    /// "chỗ này" trong câu hỏi có nghĩa mà không bắt người dùng gõ id.
    /// </summary>
    private string DescribeContext()
    {
        var locale = _loaded.Project.PrimaryLocale;
        var lines = new List<string>();
        var tab = new[] { "Cốt truyện", "Cảnh", "Nhân vật", "Asset" }[_tab];
        lines.Add($"Tab đang mở: {tab}");

        if (_loaded.Graphs.TryGetValue(_currentGraphId, out var graph))
        {
            lines.Add($"Chương đang mở: {graph.Title[locale] ?? graph.Id} (id: {graph.Id})");
            var selected = _graphSync?.SelectedIds() ?? Array.Empty<string>();
            foreach (var id in selected.Take(5))
            {
                var node = graph.Find(id);
                if (node is null) continue;
                var extra = node is SceneNode sn ? $", cảnh: {sn.Scene}" : "";
                lines.Add($"Node đang chọn: {node.DisplayName} (id: {node.Id}, loại: {node.GetType().Name.Replace("Node", "")}{extra})");
            }
        }

        if (_tab == 1 && _sceneScreen.CurrentSceneId is { } sceneId && _loaded.Scenes.TryGetValue(sceneId, out var scene))
        {
            lines.Add($"Cảnh đang mở: {sceneId} ({scene.Lines.Count} dòng)");
            var sel = _sceneScreen.SelectedLine;
            if (sel >= 0 && sel < scene.Lines.Count) lines.Add($"Dòng đang chọn: {sel} — \"{scene.Lines[sel].Text[locale]}\"");
        }
        return string.Join("\n", lines);
    }

    private void OnAiChangedProject()
    {
        // Đề xuất vừa được ghi thẳng vào dữ liệu — dựng lại màn hình để đồ thị,
        // danh sách cảnh và bảng lỗi hiện đúng. Khung chat không mất vì nó đọc
        // từ phiên dự án.
        ToastLayer.Show("✦  Đã áp dụng đề xuất của trợ lý — Ctrl+Z để hoàn tác.", ToastLayer.Kind.Ok);
        ReloadRequested?.Invoke(CaptureViewState());
    }

    // ================= Hoàn tác =================

    private void Undo()
    {
        if (PlayerOpen) return;
        if (!_session.Undo())
        {
            ToastLayer.Show("Không còn gì để hoàn tác.");
            return;
        }
        ToastLayer.Show("↶  Đã hoàn tác");
        ReloadRequested?.Invoke(CaptureViewState());
    }

    private void Redo()
    {
        if (PlayerOpen) return;
        if (!_session.Redo())
        {
            ToastLayer.Show("Không còn gì để làm lại.");
            return;
        }
        ToastLayer.Show("↷  Đã làm lại");
        ReloadRequested?.Invoke(CaptureViewState());
    }

    // ================= Chơi thử =================

    private void OpenPlayer()
    {
        if (PlayerOpen) return;
        var player = new PlayerScreen(_loaded, _projectDir, ClosePlayer);
        player.SetAnchorsPreset(LayoutPreset.FullRect);
        player.Name = "PlayerOverlay";
        AddChild(player);
    }

    private void ClosePlayer()
    {
        var overlay = GetNodeOrNull("PlayerOverlay");
        overlay?.QueueFree();
    }

    // ================= Cột trái: chương + lỗi =================

    private Control BuildLeftPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(220, 0) };
        var style = new StyleBoxFlat { BgColor = Palette.Panel, BorderColor = Palette.Line, BorderWidthRight = 1 };
        style.SetContentMarginAll(8);
        panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        box.AddChild(SectionHeader("Chương"));
        _chapterList = new ItemList { CustomMinimumSize = new Vector2(0, 160), SizeFlagsVertical = SizeFlags.ExpandFill, TooltipText = "Nhấp đúp để đổi tên, chuột phải để xem thêm" };
        _chapterList.ItemSelected += idx => SwitchChapter((int)idx);
        _chapterList.ItemActivated += idx => RenameChapter((int)idx);
        _chapterList.ItemClicked += (idx, pos, button) =>
        {
            if (button == (long)MouseButton.Right) ShowChapterMenu((int)idx);
        };
        box.AddChild(_chapterList);

        var addChapter = new Button { Text = "+ Thêm chương", TooltipText = "Tạo chương mới, có sẵn một node Kết thúc" };
        addChapter.Pressed += AddChapter;
        box.AddChild(addChapter);

        box.AddChild(new HSeparator());

        var errHeader = new HBoxContainer();
        errHeader.AddChild(SectionHeader("Lỗi"));
        _errorCountLabel = new Label();
        _errorCountLabel.AddThemeColorOverride("font_color", Palette.Warn);
        errHeader.AddChild(_errorCountLabel);
        box.AddChild(errHeader);

        _errorList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, TooltipText = "Bấm vào một lỗi để tới đúng node" };
        _errorList.ItemSelected += idx => JumpToIssue((int)idx);
        box.AddChild(_errorList);

        return panel;
    }

    private void ShowChapterMenu(int index)
    {
        var menu = new PopupMenu();
        menu.AddItem("Đổi tên…", 0);
        menu.AddItem("Chuyển lên", 1);
        menu.AddItem("Chuyển xuống", 2);
        menu.AddSeparator();
        menu.AddItem("Xoá chương…", 3);
        var count = _loaded.Project.Graphs.Count;
        menu.SetItemDisabled(menu.GetItemIndex(1), index == 0);
        menu.SetItemDisabled(menu.GetItemIndex(2), index == count - 1);
        menu.SetItemDisabled(menu.GetItemIndex(3), count <= 1);
        if (count <= 1) menu.SetItemTooltip(menu.GetItemIndex(3), "Dự án cần ít nhất một chương.");
        menu.IdPressed += id =>
        {
            switch (id)
            {
                case 0: RenameChapter(index); break;
                case 1: MoveChapter(index, -1); break;
                case 2: MoveChapter(index, +1); break;
                case 3: DeleteChapter(index); break;
            }
        };
        menu.PopupHide += menu.QueueFree;
        AddChild(menu);
        menu.Position = (Vector2I)GetViewport().GetMousePosition();
        menu.Popup();
    }

    private void RenameChapter(int index)
    {
        if (index < 0 || index >= _loaded.Project.Graphs.Count) return;
        var graph = _loaded.Graphs[_loaded.Project.Graphs[index]];
        var locale = _loaded.Project.PrimaryLocale;
        Dialogs.Prompt(this, "Đổi tên chương", "Tên chương", graph.Title[locale] ?? graph.Id, name =>
        {
            graph.Title[locale] = name;
            ProjectIo.SaveGraph(_fs, graph);
            RefreshChapterList();
            UpdateChapterTitle();
        });
    }

    private void MoveChapter(int index, int delta)
    {
        var list = _loaded.Project.Graphs;
        var target = index + delta;
        if (target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
        ProjectIo.SaveProject(_fs, _loaded.Project);
        RefreshChapterList();
        if (target == 0) ToastLayer.Show("Chương đầu tiên là nơi Chơi thử bắt đầu.");
    }

    private void DeleteChapter(int index)
    {
        if (_loaded.Project.Graphs.Count <= 1) return;
        var id = _loaded.Project.Graphs[index];
        var graph = _loaded.Graphs[id];
        var name = graph.Title[_loaded.Project.PrimaryLocale] ?? id;

        Dialogs.Confirm(this, "Xoá chương",
            $"Xoá chương \"{name}\" cùng {graph.Nodes.Count} node trong đó?\n\nCác cảnh (lời thoại) không bị xoá. Có thể lấy lại bằng Ctrl+Z.",
            "Xoá chương", () =>
            {
                _loaded.Project.Graphs.RemoveAt(index);
                _loaded.Graphs.Remove(id);
                ProjectIo.SaveProject(_fs, _loaded.Project);
                _fs.DeleteFile(ProjectPaths.Graph(id));

                if (_currentGraphId == id) _currentGraphId = _loaded.Project.Graphs[Math.Max(0, index - 1)];
                LoadGraphIntoEditor(_currentGraphId);
                RefreshChapterList();
                ToastLayer.Show($"Đã xoá chương \"{name}\".", ToastLayer.Kind.Ok);
            }, danger: true);
    }

    // ================= Cột giữa: đồ thị =================

    private static readonly (string Label, string Icon, Func<StoryNode> Make, string Tip)[] NodeTypes =
    {
        ("Cảnh", "▣", () => new SceneNode(), "Một đoạn truyện có nền, nhân vật và lời thoại"),
        ("Lựa chọn", "◈", () => new ChoiceNode(), "Người chơi chọn một hướng, mỗi phương án có thể đổi biến"),
        ("Điều kiện", "◇", () => new ConditionNode(), "Tự rẽ nhánh theo giá trị biến"),
        ("Biến", "≡", () => new VariableNode(), "Thay đổi biến rồi đi tiếp"),
        ("Nhảy", "→", () => new JumpNode(), "Nhảy tới node khác, tránh dây nối rối"),
        ("Kết thúc", "★", () => new EndingNode(), "Điểm kết của một tuyến truyện"),
        ("Ghi chú", "✎", () => new CommentNode(), "Ghi chú cho bạn, không ảnh hưởng game"),
    };

    private Control BuildCenterPanel()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2 });

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        panel.AddChild(box);

        // Thanh riêng của đồ thị: tên chương và các nút thêm node, đúng như bản
        // thiết kế — nút thêm node chỉ có nghĩa ở tab Cốt truyện nên không nằm
        // trên toolbar chung.
        var header = new PanelContainer();
        var hs = new StyleBoxFlat { BgColor = Palette.Panel2, BorderColor = Palette.Line, BorderWidthBottom = 1 };
        hs.SetContentMarginAll(6);
        hs.ContentMarginLeft = 10;
        header.AddThemeStyleboxOverride("panel", hs);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        header.AddChild(row);

        _chapterTitle = new Label();
        _chapterTitle.AddThemeColorOverride("font_color", Palette.Text);
        row.AddChild(_chapterTitle);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });

        foreach (var type in NodeTypes)
        {
            var btn = new Button { Text = $"+ {type.Label}", TooltipText = type.Tip, FocusMode = FocusModeEnum.None };
            var color = GraphSync.TypeColor(type.Make());
            btn.AddThemeColorOverride("font_color", color.Lightened(0.35f));
            var make = type.Make;
            btn.Pressed += () => AddNode(make());
            row.AddChild(btn);
        }

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var fit = new Button { Text = "⊡ Vừa khung", FocusMode = FocusModeEnum.None, TooltipText = WithKeys("Thu phóng để thấy toàn bộ đồ thị", Shortcuts.ZoomFit) };
        fit.Pressed += ZoomToFit;
        row.AddChild(fit);

        box.AddChild(header);

        _graphEdit = new GraphEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            RightDisconnects = true,
            // Lưới và minimap bật tắt ở Cài đặt/menu Xem để nhớ qua các lần mở,
            // nên ẩn các nút có sẵn của GraphEdit — bấm ở đó sẽ không được lưu.
            ShowGridButtons = false,
            ShowMinimapButton = false,
            ShowArrangeButton = false,
            ZoomMin = 0.25f,
            ZoomMax = 2f,
            MinimapSize = new Vector2(200, 130),
            MinimapOpacity = 0.55f,
        };
        box.AddChild(_graphEdit);
        return panel;
    }

    private void ZoomBy(float factor)
    {
        if (_tab != 0) ShowTab(0);
        var center = _graphEdit.Size / 2f;
        var graphPoint = (_graphEdit.ScrollOffset + center) / _graphEdit.Zoom;
        _graphEdit.Zoom = Mathf.Clamp(_graphEdit.Zoom * factor, _graphEdit.ZoomMin, _graphEdit.ZoomMax);
        _graphEdit.ScrollOffset = graphPoint * _graphEdit.Zoom - center;
    }

    private void ZoomToFit()
    {
        if (_tab != 0) ShowTab(0);
        var nodes = _graphEdit.GetChildren().OfType<GraphNode>().ToList();
        if (nodes.Count == 0) return;

        var bounds = new Rect2(nodes[0].PositionOffset, nodes[0].Size);
        foreach (var gn in nodes.Skip(1)) bounds = bounds.Merge(new Rect2(gn.PositionOffset, gn.Size));
        bounds = bounds.Grow(60);

        var view = _graphEdit.Size;
        var zoom = Mathf.Clamp(Mathf.Min(view.X / bounds.Size.X, view.Y / bounds.Size.Y), _graphEdit.ZoomMin, 1.25f);
        _graphEdit.Zoom = zoom;
        _graphEdit.ScrollOffset = bounds.GetCenter() * zoom - view / 2f;
    }

    // ================= Cột phải: thuộc tính =================

    private Control BuildRightPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        var style = new StyleBoxFlat { BgColor = Palette.Panel, BorderColor = Palette.Line, BorderWidthLeft = 1 };
        style.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", style);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);

        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(box);

        box.AddChild(SectionHeader("Thuộc tính"));

        _inspectorBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _inspectorBox.AddThemeConstantOverride("separation", 6);
        box.AddChild(_inspectorBox);

        ShowInspectorHint();
        return panel;
    }

    private void ShowInspectorHint()
    {
        foreach (var child in _inspectorBox.GetChildren()) child.QueueFree();
        var hint = new Label
        {
            Text = "Chọn một node trên canvas để chỉnh thuộc tính.\n\nMẹo:\n• Kéo từ cổng bên phải sang node khác để nối dây.\n• Kéo đầu dây ở cổng vào ra chỗ trống để gỡ dây.\n• Lăn chuột để cuộn, Ctrl + lăn để phóng to thu nhỏ, giữ chuột giữa để kéo canvas.\n• Kéo chuột trên chỗ trống để chọn nhiều node.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeColorOverride("font_color", Palette.Text3);
        _inspectorBox.AddChild(hint);
    }

    private static Label SectionHeader(string text)
    {
        var lbl = new Label { Text = text.ToUpperInvariant() };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        lbl.AddThemeFontSizeOverride("font_size", 12);
        return lbl;
    }

    // ================= Đồ thị: nạp, sửa =================

    private void LoadGraphIntoEditor(string graphId)
    {
        if (!_loaded.Graphs.TryGetValue(graphId, out var graph)) return;

        // Dùng lại một GraphSync cho mọi chương: tạo mới mỗi lần đổi chương thì
        // GraphEdit giữ handler của bản cũ, và một phím Delete sẽ hỏi xoá hai lần.
        if (_graphSync is null)
        {
            _graphSync = new GraphSync(_graphEdit, graph);
            _graphSync.Changed += OnGraphChanged;
            _graphSync.SelectionChanged += OnSelectionChanged;
            _graphSync.DeleteRequested += RequestDelete;
            _graphSync.DuplicateRequested += DuplicateSelection;
            _graphSync.CopyRequested += CopySelection;
            _graphSync.PasteRequested += Paste;
        }
        else
        {
            _graphSync.Rebuild(graph);
        }

        ShowInspectorHint();
        RevalidateAndRefreshErrors();
        UpdateChapterTitle();
    }

    private void UpdateChapterTitle()
    {
        var locale = _loaded.Project.PrimaryLocale;
        var chapter = _loaded.Graphs.TryGetValue(_currentGraphId, out var g) ? g.Title[locale] ?? g.Id : _currentGraphId;
        _chapterTitle.Text = $"⬡  {chapter}";
    }

    private void OnGraphChanged()
    {
        if (_loaded.Graphs.TryGetValue(_currentGraphId, out var graph))
        {
            ProjectIo.SaveGraph(_fs, graph);
        }
        RevalidateAndRefreshErrors();
    }

    private void OnSelectionChanged(StoryNode? node)
    {
        if (node is null)
        {
            ShowInspectorHint();
            return;
        }

        var graph = _loaded.Graphs[_currentGraphId];
        NodeInspector.Populate(_inspectorBox, _loaded.Project, graph, node, () =>
        {
            _graphSync!.RefreshNode(node);
            OnGraphChanged();
        }, OpenScene, () =>
        {
            _graphSync!.SetEntry(node.Id);
            ToastLayer.Show($"▶  \"{node.DisplayName}\" là điểm bắt đầu của chương.", ToastLayer.Kind.Ok);
            // Vẽ lại panel để nút đổi thành nhãn "Điểm bắt đầu" — người dùng
            // cần thấy ngay là thao tác đã ăn.
            OnSelectionChanged(node);
        });
    }

    private void AddNode(StoryNode node)
    {
        if (_graphSync is null) return;
        var taken = _loaded.Graphs[_currentGraphId].Nodes.Select(n => n.Id);
        node.Id = Ids.Make(PrefixFor(node), LabelFor(node), taken);

        // Đặt node mới ở giữa vùng đang nhìn thay vì toạ độ cố định — canvas đã
        // cuộn đi xa thì node mới xuất hiện ngoài tầm mắt, trông như bấm không ăn.
        var view = _graphEdit.Size.X > 0 ? _graphEdit.Size : new Vector2(800, 500);
        var center = (_graphEdit.ScrollOffset + view / 2f) / _graphEdit.Zoom;
        node.Position = new GraphPosition(center.X - 90 + Random.Shared.Next(-40, 40), center.Y - 40 + Random.Shared.Next(-40, 40));

        if (node is EndingNode ending) ending.Name = new Localized(_loaded.Project.PrimaryLocale, "Kết thúc mới");
        if (node is ChoiceNode choice) choice.Prompt = new Localized(_loaded.Project.PrimaryLocale, "Câu hỏi mới");

        _graphSync.AddNode(node);
        _graphSync.SelectOnly(new[] { node.Id });
        OnSelectionChanged(node);
        OnGraphChanged();
    }

    private static string PrefixFor(StoryNode node) => node switch
    {
        SceneNode => "scene",
        ChoiceNode => "choice",
        ConditionNode => "cond",
        VariableNode => "var",
        JumpNode => "jump",
        EndingNode => "ending",
        CommentNode => "note",
        _ => "node",
    };

    private static string LabelFor(StoryNode node) => node switch
    {
        SceneNode => "canh moi",
        ChoiceNode => "lua chon moi",
        ConditionNode => "dieu kien moi",
        VariableNode => "bien moi",
        JumpNode => "nhay moi",
        EndingNode => "ket thuc moi",
        CommentNode => "ghi chu moi",
        _ => "node moi",
    };

    // ================= Sửa: xoá, sao chép, dán, nhân bản =================

    private static bool TextFieldFocused(Control self)
        => self.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;

    private void RequestDelete(IReadOnlyList<string> ids)
    {
        if (_graphSync is null || ids.Count == 0) return;
        var graph = _loaded.Graphs[_currentGraphId];

        void DoDelete()
        {
            _graphSync.DeleteNodes(ids);
            ShowInspectorHint();
            ToastLayer.Show(ids.Count == 1 ? "Đã xoá 1 node — Ctrl+Z để lấy lại." : $"Đã xoá {ids.Count} node — Ctrl+Z để lấy lại.");
        }

        if (!AppSettings.Current.ConfirmDeleteNodes)
        {
            DoDelete();
            return;
        }

        var names = ids.Select(id => graph.Find(id)?.DisplayName ?? id).ToList();
        var what = names.Count == 1 ? $"node \"{names[0]}\"" : $"{names.Count} node";
        var entryNote = ids.Contains(graph.Entry) ? "\n\nNode này đang là điểm bắt đầu của chương — xoá xong nhớ đặt điểm bắt đầu mới." : "";
        Dialogs.Confirm(this, "Xoá node", $"Xoá {what}?{entryNote}\n\n(Tắt câu hỏi này ở Cài đặt → Chung.)", "Xoá", DoDelete, danger: true);
    }

    private void DeleteSelection()
    {
        if (_tab != 0 || _graphSync is null || TextFieldFocused(this)) return;
        RequestDelete(_graphSync.SelectedIds());
    }

    private void SelectAll()
    {
        if (_tab != 0 || _graphSync is null || TextFieldFocused(this)) return;
        _graphSync.SelectOnly(_loaded.Graphs[_currentGraphId].Nodes.Select(n => n.Id).ToList());
    }

    private void CopySelection()
    {
        if (_tab != 0 || _graphSync is null || TextFieldFocused(this)) return;
        var graph = _loaded.Graphs[_currentGraphId];
        var nodes = _graphSync.SelectedIds().Select(graph.Find).OfType<StoryNode>().ToList();
        if (nodes.Count == 0) return;
        _clipboard = ProjectIo.Serialize(nodes);
        ToastLayer.Show(nodes.Count == 1 ? "Đã sao chép 1 node." : $"Đã sao chép {nodes.Count} node.");
    }

    private void Paste()
    {
        if (_tab != 0 || _graphSync is null || _clipboard is null || TextFieldFocused(this)) return;
        var nodes = ProjectIo.Deserialize<List<StoryNode>>(_clipboard, "clipboard");
        InsertCopies(nodes, new Vector2(60, 60));
    }

    private void DuplicateSelection()
    {
        if (_tab != 0 || _graphSync is null || TextFieldFocused(this)) return;
        var graph = _loaded.Graphs[_currentGraphId];
        var selected = _graphSync.SelectedIds().Select(graph.Find).OfType<StoryNode>().ToList();
        if (selected.Count == 0) return;
        var copies = ProjectIo.Deserialize<List<StoryNode>>(ProjectIo.Serialize(selected), "duplicate");
        InsertCopies(copies, new Vector2(40, 40));
    }

    /// <summary>
    /// Thêm bản sao vào chương đang mở: cấp id mới, và nối lại dây giữa các bản
    /// sao với nhau thay vì với bản gốc — sao chép hai node đang nối với nhau
    /// thì bản sao cũng phải nối với nhau.
    /// </summary>
    private void InsertCopies(List<StoryNode> copies, Vector2 offset)
    {
        var graph = _loaded.Graphs[_currentGraphId];
        var taken = new HashSet<string>(graph.Nodes.Select(n => n.Id));
        var remap = new Dictionary<string, string>();

        foreach (var node in copies)
        {
            var newId = Ids.Make(PrefixFor(node), LabelFor(node), taken);
            taken.Add(newId);
            remap[node.Id] = newId;
            node.Id = newId;
            node.Position = new GraphPosition(node.Position.X + offset.X, node.Position.Y + offset.Y);
        }

        foreach (var node in copies)
        {
            foreach (var (handle, target) in node.Outgoing().ToList())
            {
                if (target is null) continue;
                if (remap.TryGetValue(target, out var mapped)) GraphSync.SetOutgoing(node, handle ?? "", mapped);
                else if (graph.Find(target) is null) GraphSync.SetOutgoing(node, handle ?? "", null);
            }
        }

        _graphSync!.AddNodes(copies);
        ShowInspectorHint();
        ToastLayer.Show(copies.Count == 1 ? "Đã thêm 1 bản sao." : $"Đã thêm {copies.Count} bản sao.");
    }

    // ================= Chương =================

    private void AddChapter()
    {
        var takenIds = _loaded.Project.Graphs;
        var id = Ids.Make("chapter", $"chuong {takenIds.Count + 1}", takenIds);

        var ending = new EndingNode
        {
            Id = Ids.Make("ending", "ket thuc", Array.Empty<string>()),
            Position = new GraphPosition(240, 160),
            Name = new Localized(_loaded.Project.PrimaryLocale, "Kết thúc"),
        };
        var graph = new StoryGraph
        {
            Id = id,
            Title = new Localized(_loaded.Project.PrimaryLocale, $"Chương {takenIds.Count + 1}"),
            Entry = ending.Id,
            Nodes = { ending },
        };

        _loaded.Project.Graphs.Add(id);
        _loaded.Graphs[id] = graph;
        ProjectIo.SaveProject(_fs, _loaded.Project);
        ProjectIo.SaveGraph(_fs, graph);

        RefreshChapterList();
        SwitchChapter(_loaded.Project.Graphs.Count - 1);
        ShowTab(0);
    }

    private void RefreshChapterList()
    {
        _chapterList.Clear();
        var locale = _loaded.Project.PrimaryLocale;
        for (var i = 0; i < _loaded.Project.Graphs.Count; i++)
        {
            var graph = _loaded.Graphs[_loaded.Project.Graphs[i]];
            var label = graph.Title[locale] ?? graph.Id;
            _chapterList.AddItem($"{i + 1}.  {label}");
            _chapterList.SetItemTooltip(i, $"{label}\n{graph.Nodes.Count} node · id: {graph.Id}");
        }

        var idx = _loaded.Project.Graphs.IndexOf(_currentGraphId);
        if (idx >= 0) _chapterList.Select(idx);
    }

    private void SwitchChapter(int index)
    {
        if (index < 0 || index >= _loaded.Project.Graphs.Count) return;
        _currentGraphId = _loaded.Project.Graphs[index];
        _chapterList.Select(index);
        LoadGraphIntoEditor(_currentGraphId);
    }

    // ================= Lỗi =================

    private void RevalidateAndRefreshErrors()
    {
        if (!_loaded.Graphs.TryGetValue(_currentGraphId, out var graph)) return;

        var issues = GraphValidator.Validate(graph, _loaded.Project.Variables, _loaded.Scenes, _loaded.Characters);

        _errorList.Clear();
        foreach (var issue in issues)
        {
            var icon = issue.Severity switch { IssueSeverity.Error => "✖", IssueSeverity.Warning => "▲", _ => "•" };
            var i = _errorList.AddItem($"{icon}  {issue.Message}");
            _errorList.SetItemCustomFgColor(i, issue.Severity == IssueSeverity.Error ? Palette.Err : issue.Severity == IssueSeverity.Warning ? Palette.Warn : Palette.Text2);
            _errorList.SetItemTooltip(i, issue.Message);
        }
        if (issues.Count == 0)
        {
            var i = _errorList.AddItem("✓  Không có lỗi nào.");
            _errorList.SetItemCustomFgColor(i, Palette.Ok);
            _errorList.SetItemSelectable(i, false);
        }

        var errors = issues.Count(x => x.Severity == IssueSeverity.Error);
        var warnings = issues.Count(x => x.Severity == IssueSeverity.Warning);
        _errorCountLabel.Text = issues.Count == 0 ? "" : issues.Count.ToString();

        if (issues.Count == 0)
        {
            _issuesButton.Text = "✓ Không có lỗi";
            _issuesButton.AddThemeColorOverride("font_color", Palette.Ok);
        }
        else
        {
            _issuesButton.Text = $"✖ {errors} lỗi   ▲ {warnings} cảnh báo";
            _issuesButton.AddThemeColorOverride("font_color", errors > 0 ? Palette.Err : Palette.Warn);
        }

        UpdateChapterTitle();

        // Lưu lại issues để JumpToIssue tra được đúng node khi người dùng bấm.
        _lastIssues = issues;
    }

    private void JumpToIssue(int index)
    {
        if (index < 0 || index >= _lastIssues.Count) return;
        var issue = _lastIssues[index];
        if (issue.NodeId is null) return;

        foreach (var gn in _graphEdit.GetChildren().OfType<GraphNode>())
        {
            gn.Selected = gn.Name == issue.NodeId;
            if (gn.Selected)
            {
                var center = gn.PositionOffset + gn.Size / 2f;
                _graphEdit.ScrollOffset = center * _graphEdit.Zoom - _graphEdit.Size / 2f;
            }
        }

        var node = _loaded.Graphs[_currentGraphId].Find(issue.NodeId);
        OnSelectionChanged(node);
    }
}
