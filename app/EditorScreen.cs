using System;
using System.Linq;
using Godot;
using VNdev.Core;
using VNdev.Core.Io;
using VNdev.Core.Model;
using VNdev.Core.Validation;

namespace VNdev.App;

/// <summary>
/// Màn hình chính: ba cột theo docs/design/ui-ux.html — cây chương và bảng
/// lỗi bên trái, Story Graph ở giữa, thuộc tính node đang chọn bên phải.
/// </summary>
public partial class EditorScreen : Control
{
    private readonly LoadedProject _loaded;
    private readonly DiskFileSystem _fs;
    private readonly string _projectDir;

    private string _currentGraphId;
    private GraphEdit _graphEdit = null!;
    private GraphSync _graphSync = null!;
    private ItemList _chapterList = null!;
    private ItemList _errorList = null!;
    private Label _errorCountLabel = null!;
    private VBoxContainer _inspectorBox = null!;
    private Label _titleLabel = null!;

    private Control _storyBody = null!;
    private SceneScreen _sceneScreen = null!;
    private Control _characterScreen = null!;
    private Control _assetScreen = null!;

    private Control[] _tabScreens = null!;
    private Button[] _tabButtons = null!;

    public event Action? BackToWelcome;

    public EditorScreen(LoadedProject loaded, DiskFileSystem fs, string projectDir)
    {
        _loaded = loaded;
        _fs = fs;
        _projectDir = projectDir;
        _currentGraphId = loaded.Project.Graphs.FirstOrDefault() ?? "";
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(BuildToolbar());

        // Hai màn hình chồng lên nhau trong cùng một vùng, đổi qua lại bằng
        // Visible thay vì dựng lại — GraphEdit giữ nguyên trạng thái pan/zoom
        // và GraphSync không phải khởi tạo lại mỗi lần chuyển tab.
        var workspace = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddChild(workspace);

        _storyBody = new HBoxContainer();
        _storyBody.SetAnchorsPreset(LayoutPreset.FullRect);
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

        LoadGraphIntoEditor(_currentGraphId);
        RefreshChapterList();
        ShowTab(0);
    }

    private void ShowTab(int index)
    {
        for (var i = 0; i < _tabScreens.Length; i++)
        {
            _tabScreens[i].Visible = i == index;
            _tabButtons[i].ButtonPressed = i == index;
        }

        // Biến/nhân vật/cảnh có thể đã đổi ở tab khác — kiểm tra lại đồ thị
        // hiện tại mỗi khi quay về tab Cốt truyện.
        if (index == 0) RevalidateAndRefreshErrors();
    }

    private void OpenScene(string sceneId)
    {
        ShowTab(1);
        _sceneScreen.OpenOrCreate(sceneId);
    }

    private Control BuildToolbar()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 46) };
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2 });

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        bar.AddChild(row);

        var back = new Button { Text = "‹ Dự án khác" };
        back.Pressed += () => BackToWelcome?.Invoke();
        row.AddChild(back);

        row.AddChild(new VSeparator());

        var tabLabels = new[] { "⬡ Cốt truyện", "▣ Cảnh", "☺ Nhân vật", "▤ Asset" };
        _tabButtons = new Button[tabLabels.Length];
        for (var i = 0; i < tabLabels.Length; i++)
        {
            var index = i;
            var btn = new Button { Text = tabLabels[i], ToggleMode = true, ButtonPressed = i == 0 };
            btn.Pressed += () => ShowTab(index);
            row.AddChild(btn);
            _tabButtons[i] = btn;
        }

        row.AddChild(new VSeparator());

        AddToolButton(row, "+ Cảnh", () => AddNode(new SceneNode()));
        AddToolButton(row, "+ Lựa chọn", () => AddNode(new ChoiceNode()));
        AddToolButton(row, "+ Điều kiện", () => AddNode(new ConditionNode()));
        AddToolButton(row, "+ Biến", () => AddNode(new VariableNode()));
        AddToolButton(row, "+ Nhảy", () => AddNode(new JumpNode()));
        AddToolButton(row, "+ Kết thúc", () => AddNode(new EndingNode()));
        AddToolButton(row, "+ Ghi chú", () => AddNode(new CommentNode()));

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);

        _titleLabel = new Label();
        _titleLabel.AddThemeColorOverride("font_color", Palette.Text2);
        row.AddChild(_titleLabel);
        UpdateTitleLabel();

        var play = new Button { Text = "▶ Chơi thử" };
        play.AddThemeColorOverride("font_color", Palette.Ok);
        play.Pressed += OpenPlayer;
        row.AddChild(play);

        return bar;
    }

    private void OpenPlayer()
    {
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

    private static void AddToolButton(HBoxContainer row, string text, Action action)
    {
        var btn = new Button { Text = text };
        btn.Pressed += action;
        row.AddChild(btn);
    }

    private Control BuildLeftPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(200, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel });

        var box = new VBoxContainer();
        panel.AddChild(box);

        box.AddChild(SectionHeader("Chương"));
        _chapterList = new ItemList { CustomMinimumSize = new Vector2(0, 160), SizeFlagsVertical = SizeFlags.ExpandFill };
        _chapterList.ItemSelected += idx => SwitchChapter((int)idx);
        box.AddChild(_chapterList);

        var addChapter = new Button { Text = "+ Thêm chương" };
        addChapter.Pressed += AddChapter;
        box.AddChild(addChapter);

        box.AddChild(new HSeparator());

        var errHeader = new HBoxContainer();
        errHeader.AddChild(SectionHeader("Lỗi"));
        _errorCountLabel = new Label();
        _errorCountLabel.AddThemeColorOverride("font_color", Palette.Warn);
        errHeader.AddChild(_errorCountLabel);
        box.AddChild(errHeader);

        _errorList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _errorList.ItemSelected += idx => JumpToIssue((int)idx);
        box.AddChild(_errorList);

        return panel;
    }

    private Control BuildCenterPanel()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2 });

        _graphEdit = new GraphEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            RightDisconnects = true,
        };
        panel.AddChild(_graphEdit);
        return panel;
    }

    private Control BuildRightPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(260, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel });

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(scroll);

        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(box);

        box.AddChild(SectionHeader("Thuộc tính"));

        _inspectorBox = new VBoxContainer();
        _inspectorBox.AddThemeConstantOverride("separation", 6);
        box.AddChild(_inspectorBox);

        var hint = new Label { Text = "Chọn một node trên canvas để chỉnh thuộc tính.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        hint.AddThemeColorOverride("font_color", Palette.Text3);
        _inspectorBox.AddChild(hint);

        return panel;
    }

    private static Label SectionHeader(string text)
    {
        var lbl = new Label { Text = text.ToUpperInvariant() };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        return lbl;
    }

    private void LoadGraphIntoEditor(string graphId)
    {
        if (!_loaded.Graphs.TryGetValue(graphId, out var graph)) return;

        _graphSync = new GraphSync(_graphEdit, graph);
        _graphSync.Changed += OnGraphChanged;
        _graphSync.SelectionChanged += OnSelectionChanged;

        RevalidateAndRefreshErrors();
        UpdateTitleLabel();
    }

    private void UpdateTitleLabel()
    {
        if (_titleLabel is null) return;
        var title = _loaded.Project.Title[_loaded.Project.PrimaryLocale] ?? _loaded.Project.Id;
        var chapter = _loaded.Graphs.TryGetValue(_currentGraphId, out var g)
            ? g.Title[_loaded.Project.PrimaryLocale] ?? g.Id
            : _currentGraphId;
        _titleLabel.Text = $"{title} — {chapter}";
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
            foreach (var child in _inspectorBox.GetChildren()) child.QueueFree();
            var hint = new Label { Text = "Chọn một node trên canvas để chỉnh thuộc tính.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            hint.AddThemeColorOverride("font_color", Palette.Text3);
            _inspectorBox.AddChild(hint);
            return;
        }

        NodeInspector.Populate(_inspectorBox, _loaded.Project, node, () =>
        {
            _graphSync.RefreshNode(node);
            OnGraphChanged();
        }, OpenScene);
    }

    private void AddNode(StoryNode node)
    {
        var taken = _loaded.Graphs[_currentGraphId].Nodes.Select(n => n.Id);
        node.Id = Ids.Make(PrefixFor(node), LabelFor(node), taken);
        node.Position = new GraphPosition(80 + Random.Shared.Next(0, 200), 80 + Random.Shared.Next(0, 200));

        if (node is EndingNode ending) ending.Name = new Localized(_loaded.Project.PrimaryLocale, "Kết thúc mới");
        if (node is ChoiceNode choice) choice.Prompt = new Localized(_loaded.Project.PrimaryLocale, "Câu hỏi mới");

        _graphSync.AddNode(node);
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
    }

    private void RefreshChapterList()
    {
        _chapterList.Clear();
        var locale = _loaded.Project.PrimaryLocale;
        foreach (var id in _loaded.Project.Graphs)
        {
            var graph = _loaded.Graphs[id];
            var label = graph.Title[locale] ?? graph.Id;
            _chapterList.AddItem(label);
        }

        var idx = _loaded.Project.Graphs.IndexOf(_currentGraphId);
        if (idx >= 0) _chapterList.Select(idx);
    }

    private void SwitchChapter(int index)
    {
        if (index < 0 || index >= _loaded.Project.Graphs.Count) return;
        _currentGraphId = _loaded.Project.Graphs[index];
        LoadGraphIntoEditor(_currentGraphId);
    }

    private void RevalidateAndRefreshErrors()
    {
        if (!_loaded.Graphs.TryGetValue(_currentGraphId, out var graph)) return;

        var issues = GraphValidator.Validate(graph, _loaded.Project.Variables, _loaded.Scenes, _loaded.Characters);

        _errorList.Clear();
        foreach (var issue in issues)
        {
            _errorList.AddItem(issue.Message);
        }
        _errorCountLabel.Text = issues.Count == 0 ? "" : issues.Count.ToString();

        UpdateTitleLabel();

        // Lưu lại issues để JumpToIssue tra được đúng node khi người dùng bấm.
        _lastIssues = issues;
    }

    private System.Collections.Generic.List<ValidationIssue> _lastIssues = new();

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
                _graphEdit.ScrollOffset = gn.PositionOffset - _graphEdit.Size / 2f;
            }
        }

        var node = _loaded.Graphs[_currentGraphId].Find(issue.NodeId);
        OnSelectionChanged(node);
    }
}
