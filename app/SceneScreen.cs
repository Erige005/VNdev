using System;
using System.IO;
using System.Linq;
using Godot;
using VNdev.Core;
using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Chặng 5 — Trình soạn cảnh. Khung sân khấu đúng tỷ lệ 16:9, hiện nền và
/// nhân vật thật; chọn dòng thoại nào thì sân khấu hiện đúng trạng thái tại
/// dòng đó bằng <see cref="SceneData.StageAt"/> — đây là thứ thay thế việc
/// phải bấm Chơi thử chỉ để xem một câu thoại trông ra sao.
/// </summary>
public partial class SceneScreen : Control
{
    private const float FrameWidth = 640f;
    private const float FrameHeight = FrameWidth * 9f / 16f;

    private readonly LoadedProject _loaded;
    private readonly IFileSystem _fs;
    private readonly string _projectDir;

    private ItemList _sceneList = null!;
    private ItemList _lineList = null!;
    private Control _stage = null!;
    private VBoxContainer _lineInspector = null!;
    private OptionButton _bgPicker = null!;

    private string? _currentSceneId;
    private int _selectedLine = -1;

    public SceneScreen(LoadedProject loaded, IFileSystem fs, string projectDir)
    {
        _loaded = loaded;
        _fs = fs;
        _projectDir = projectDir;
    }

    public override void _Ready()
    {
        var root = new HBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(BuildSceneListPanel());

        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var centerPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        centerPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2 });
        centerPanel.AddChild(center);
        root.AddChild(centerPanel);

        center.AddChild(BuildSceneToolbar());
        center.AddChild(BuildStage());
        center.AddChild(BuildLineList());

        root.AddChild(BuildInspectorPanel());

        RefreshSceneList();
        if (_loaded.Scenes.Count > 0) SelectScene(_loaded.Scenes.Keys.First());
    }

    // ---------- danh sách cảnh ----------

    private Control BuildSceneListPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(180, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel });

        var box = new VBoxContainer();
        panel.AddChild(box);

        var header = new Label { Text = "CẢNH" };
        header.AddThemeColorOverride("font_color", Palette.Text3);
        box.AddChild(header);

        _sceneList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _sceneList.ItemSelected += idx => SelectScene(_loaded.Scenes.Keys.ElementAt((int)idx));
        box.AddChild(_sceneList);

        var add = new Button { Text = "+ Thêm cảnh" };
        add.Pressed += AddScene;
        box.AddChild(add);

        return panel;
    }

    private void RefreshSceneList()
    {
        _sceneList.Clear();
        foreach (var id in _loaded.Scenes.Keys) _sceneList.AddItem(id);
        if (_currentSceneId is not null)
        {
            var idx = _loaded.Scenes.Keys.ToList().IndexOf(_currentSceneId);
            if (idx >= 0) _sceneList.Select(idx);
        }
    }

    private void AddScene()
    {
        var id = Ids.Make("scene", $"canh moi {_loaded.Scenes.Count + 1}", _loaded.Scenes.Keys);
        var scene = new SceneData { Id = id };
        _loaded.Scenes[id] = scene;
        ProjectIo.SaveScene(_fs, scene);
        RefreshSceneList();
        SelectScene(id);
    }

    public string? CurrentSceneId => _currentSceneId;
    public int SelectedLine => _selectedLine;

    /// <summary>
    /// Chọn lại cảnh sau khi màn hình được dựng lại (hoàn tác, đổi cài đặt).
    /// Khác <see cref="OpenOrCreate"/> ở chỗ không tạo cảnh mới: hoàn tác việc
    /// tạo cảnh mà lại tạo lại nó thì hoàn tác vô nghĩa.
    /// </summary>
    public void TrySelect(string sceneId)
    {
        if (!_loaded.Scenes.ContainsKey(sceneId)) return;
        SelectScene(sceneId);
        RefreshSceneList();
    }

    public void OpenOrCreate(string sceneId)
    {
        if (!_loaded.Scenes.ContainsKey(sceneId))
        {
            var scene = new SceneData { Id = sceneId };
            _loaded.Scenes[sceneId] = scene;
            ProjectIo.SaveScene(_fs, scene);
            RefreshSceneList();
        }
        SelectScene(sceneId);
    }

    private void SelectScene(string id)
    {
        _currentSceneId = id;
        _selectedLine = -1;
        RefreshBackgroundPicker();
        RefreshLineList();
        RedrawStage();
        RefreshSceneList();
    }

    // ---------- toolbar cảnh ----------

    private Control BuildSceneToolbar()
    {
        var bar = new HBoxContainer { CustomMinimumSize = new Vector2(0, 36) };
        bar.AddThemeConstantOverride("separation", 8);

        var bgLabel = new Label { Text = "Nền:" };
        bgLabel.AddThemeColorOverride("font_color", Palette.Text3);
        bar.AddChild(bgLabel);

        _bgPicker = new OptionButton();
        _bgPicker.ItemSelected += idx =>
        {
            if (_currentSceneId is null) return;
            var scene = _loaded.Scenes[_currentSceneId];
            var backgrounds = AssetIndex.Backgrounds(_fs);
            scene.Background = idx == 0 ? null : backgrounds[(int)idx - 1];
            SaveCurrentScene();
            RedrawStage();
        };
        bar.AddChild(_bgPicker);

        return bar;
    }

    private void RefreshBackgroundPicker()
    {
        _bgPicker.Clear();
        _bgPicker.AddItem("— không nền —");
        var backgrounds = AssetIndex.Backgrounds(_fs);
        var current = _currentSceneId is not null ? _loaded.Scenes[_currentSceneId].Background : null;
        var selected = 0;
        for (var i = 0; i < backgrounds.Count; i++)
        {
            _bgPicker.AddItem(Path.GetFileName(backgrounds[i]));
            if (backgrounds[i] == current) selected = i + 1;
        }
        _bgPicker.Selected = selected;
    }

    // ---------- sân khấu ----------

    private Control BuildStage()
    {
        var aspect = new AspectRatioContainer
        {
            Ratio = 16f / 9f,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        _stage = new Control { CustomMinimumSize = new Vector2(FrameWidth, FrameHeight) };
        var stageBg = new ColorRect { Color = new Color(0.04f, 0.05f, 0.07f) };
        stageBg.SetAnchorsPreset(LayoutPreset.FullRect);
        _stage.AddChild(stageBg);

        aspect.AddChild(_stage);
        return aspect;
    }

    private void RedrawStage()
    {
        if (_currentSceneId is null || !_loaded.Scenes.TryGetValue(_currentSceneId, out var scene))
        {
            StageRenderer.Draw(_stage, _loaded, _projectDir, null, System.Array.Empty<StageActor>(), keepFirst: 1);
            return;
        }

        var actors = _selectedLine >= 0 ? scene.StageAt(_selectedLine) : scene.Stage;
        StageRenderer.Draw(_stage, _loaded, _projectDir, scene, actors, keepFirst: 1);
    }

    // ---------- danh sách lời thoại ----------

    private Control BuildLineList()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(0, 160) };

        var header = new HBoxContainer();
        var lbl = new Label { Text = "LỜI THOẠI" };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        header.AddChild(lbl);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(spacer);
        var up = new Button { Text = "↑" };
        up.Pressed += () => MoveLine(-1);
        var down = new Button { Text = "↓" };
        down.Pressed += () => MoveLine(1);
        var add = new Button { Text = "+ Thêm dòng" };
        add.Pressed += AddLine;
        var remove = new Button { Text = "Xoá dòng" };
        remove.Pressed += RemoveLine;
        header.AddChild(up);
        header.AddChild(down);
        header.AddChild(add);
        header.AddChild(remove);
        box.AddChild(header);

        _lineList = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _lineList.ItemSelected += idx =>
        {
            _selectedLine = (int)idx;
            RedrawStage();
            RefreshLineInspector();
        };
        box.AddChild(_lineList);

        return box;
    }

    private void RefreshLineList()
    {
        _lineList.Clear();
        if (_currentSceneId is null) return;
        var scene = _loaded.Scenes[_currentSceneId];
        foreach (var line in scene.Lines)
        {
            var speaker = line.Speaker is null ? "Dẫn truyện" : (_loaded.Characters.TryGetValue(line.Speaker, out var c) ? c.DisplayName[_loaded.Project.PrimaryLocale] ?? line.Speaker : line.Speaker);
            var text = line.Text[_loaded.Project.PrimaryLocale] ?? "";
            _lineList.AddItem($"{speaker}: {text}");
        }
        if (_selectedLine >= 0 && _selectedLine < scene.Lines.Count) _lineList.Select(_selectedLine);
        RefreshLineInspector();
    }

    private void AddLine()
    {
        if (_currentSceneId is null) return;
        var scene = _loaded.Scenes[_currentSceneId];
        var id = Ids.Make("line", $"dong {scene.Lines.Count + 1}", scene.Lines.Select(l => l.Id));
        scene.Lines.Add(new DialogueLine { Id = id, Text = new Localized(_loaded.Project.PrimaryLocale, "") });
        _selectedLine = scene.Lines.Count - 1;
        SaveCurrentScene();
        RefreshLineList();
        RedrawStage();
    }

    private void RemoveLine()
    {
        if (_currentSceneId is null || _selectedLine < 0) return;
        var scene = _loaded.Scenes[_currentSceneId];
        if (_selectedLine >= scene.Lines.Count) return;
        scene.Lines.RemoveAt(_selectedLine);
        _selectedLine = Math.Min(_selectedLine, scene.Lines.Count - 1);
        SaveCurrentScene();
        RefreshLineList();
        RedrawStage();
    }

    private void MoveLine(int delta)
    {
        if (_currentSceneId is null || _selectedLine < 0) return;
        var scene = _loaded.Scenes[_currentSceneId];
        var target = _selectedLine + delta;
        if (target < 0 || target >= scene.Lines.Count) return;
        (scene.Lines[_selectedLine], scene.Lines[target]) = (scene.Lines[target], scene.Lines[_selectedLine]);
        _selectedLine = target;
        SaveCurrentScene();
        RefreshLineList();
        RedrawStage();
    }

    // ---------- panel phải: thuộc tính dòng ----------

    private Control BuildInspectorPanel()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(260, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel });

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(scroll);

        _lineInspector = new VBoxContainer();
        _lineInspector.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_lineInspector);

        return panel;
    }

    private void RefreshLineInspector()
    {
        foreach (var child in _lineInspector.GetChildren()) child.QueueFree();

        if (_currentSceneId is null || _selectedLine < 0)
        {
            var hint = new Label { Text = "Chọn một dòng thoại để chỉnh.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            hint.AddThemeColorOverride("font_color", Palette.Text3);
            _lineInspector.AddChild(hint);
            return;
        }

        var scene = _loaded.Scenes[_currentSceneId];
        if (_selectedLine >= scene.Lines.Count) return;
        var line = scene.Lines[_selectedLine];

        var speakerBtn = new OptionButton();
        speakerBtn.AddItem("— Dẫn truyện —");
        var charIds = _loaded.Project.Characters;
        for (var i = 0; i < charIds.Count; i++) speakerBtn.AddItem(_loaded.Characters[charIds[i]].DisplayName[_loaded.Project.PrimaryLocale] ?? charIds[i]);
        speakerBtn.Selected = line.Speaker is null ? 0 : charIds.IndexOf(line.Speaker) + 1;
        speakerBtn.ItemSelected += idx =>
        {
            line.Speaker = idx == 0 ? null : charIds[(int)idx - 1];
            SaveCurrentScene();
            RefreshLineList();
            RedrawStage();
        };
        AddLabeled(_lineInspector, "Người nói", speakerBtn);

        var exprBtn = new OptionButton();
        var expressions = line.Speaker is not null && _loaded.Characters.TryGetValue(line.Speaker, out var spk)
            ? spk.Expressions.Select(e => e.Id).ToList()
            : new System.Collections.Generic.List<string>();
        exprBtn.AddItem("— giữ nguyên —");
        foreach (var e in expressions) exprBtn.AddItem(e);
        exprBtn.Selected = line.Expression is null ? 0 : Math.Max(0, expressions.IndexOf(line.Expression) + 1);
        exprBtn.Disabled = expressions.Count == 0;
        exprBtn.ItemSelected += idx =>
        {
            line.Expression = idx == 0 ? null : expressions[(int)idx - 1];
            SaveCurrentScene();
            RedrawStage();
        };
        AddLabeled(_lineInspector, "Biểu cảm", exprBtn);

        var textEdit = new TextEdit { Text = line.Text[_loaded.Project.PrimaryLocale] ?? "", CustomMinimumSize = new Vector2(0, 100) };
        textEdit.TextChanged += () =>
        {
            line.Text[_loaded.Project.PrimaryLocale] = textEdit.Text;
            SaveCurrentScene();
            RefreshLineListLabelOnly();
        };
        AddLabeled(_lineInspector, "Nội dung", textEdit);
    }

    private void RefreshLineListLabelOnly()
    {
        if (_currentSceneId is null || _selectedLine < 0) return;
        var scene = _loaded.Scenes[_currentSceneId];
        if (_selectedLine >= scene.Lines.Count) return;
        var line = scene.Lines[_selectedLine];
        var speaker = line.Speaker is null ? "Dẫn truyện" : (_loaded.Characters.TryGetValue(line.Speaker, out var c) ? c.DisplayName[_loaded.Project.PrimaryLocale] ?? line.Speaker : line.Speaker);
        _lineList.SetItemText(_selectedLine, $"{speaker}: {line.Text[_loaded.Project.PrimaryLocale]}");
    }

    private static void AddLabeled(VBoxContainer container, string label, Control control)
    {
        var lbl = new Label { Text = label };
        lbl.AddThemeColorOverride("font_color", Palette.Text3);
        container.AddChild(lbl);
        container.AddChild(control);
    }

    private void SaveCurrentScene()
    {
        if (_currentSceneId is null) return;
        ProjectIo.SaveScene(_fs, _loaded.Scenes[_currentSceneId]);
    }
}
