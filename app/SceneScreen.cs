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
    private OptionButton _bgmPicker = null!;

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

        var bgmLabel = new Label { Text = "Nhạc:" };
        bgmLabel.AddThemeColorOverride("font_color", Palette.Text3);
        bar.AddChild(bgmLabel);

        _bgmPicker = new OptionButton();
        _bgmPicker.ItemSelected += idx =>
        {
            if (_currentSceneId is null) return;
            var scene = _loaded.Scenes[_currentSceneId];
            var bgms = AssetIndex.Bgm(_fs);
            var src = idx == 0 ? null : bgms[(int)idx - 1];
            scene.Bgm = src == null ? null : new AudioCue { Src = src };
            SaveCurrentScene();
        };
        bar.AddChild(_bgmPicker);

        return bar;
    }

    private void RefreshBackgroundPicker()
    {
        _bgPicker.Clear();
        _bgPicker.AddItem("— không nền —");
        var backgrounds = AssetIndex.Backgrounds(_fs);
        var currentBg = _currentSceneId is not null ? _loaded.Scenes[_currentSceneId].Background : null;
        var selectedBg = 0;
        for (var i = 0; i < backgrounds.Count; i++)
        {
            _bgPicker.AddItem(Path.GetFileName(backgrounds[i]));
            if (backgrounds[i] == currentBg) selectedBg = i + 1;
        }
        _bgPicker.Selected = selectedBg;

        _bgmPicker.Clear();
        _bgmPicker.AddItem("— không nhạc —");
        var bgms = AssetIndex.Bgm(_fs);
        var currentBgm = _currentSceneId is not null ? _loaded.Scenes[_currentSceneId].Bgm?.Src : null;
        var selectedBgm = 0;
        for (var i = 0; i < bgms.Count; i++)
        {
            _bgmPicker.AddItem(Path.GetFileName(bgms[i]));
            if (bgms[i] == currentBgm) selectedBgm = i + 1;
        }
        _bgmPicker.Selected = selectedBgm;
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
        // Sân khấu đầu cảnh chỉ soạn được khi không chọn dòng nào; không có nút
        // này thì đã bấm vào một dòng là hết đường quay lại, trừ khi đổi cảnh.
        var opening = new Button { Text = "Sân khấu đầu cảnh", TooltipText = "Bỏ chọn dòng để soạn ai đứng sẵn khi cảnh mở ra" };
        opening.Pressed += () =>
        {
            _selectedLine = -1;
            _lineList.DeselectAll();
            RedrawStage();
            RefreshLineInspector();
        };
        header.AddChild(opening);
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

        if (_currentSceneId is null) return;
        var scene = _loaded.Scenes[_currentSceneId];

        if (_selectedLine < 0)
        {
            var hint = new Label { Text = "SÂN KHẤU ĐẦU CẢNH" };
            hint.AddThemeColorOverride("font_color", Palette.Text3);
            _lineInspector.AddChild(hint);

            BuildActorListEditor(scene.Stage, isChange: false);
            return;
        }

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

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 16) };
        _lineInspector.AddChild(spacer);
        var stageTitle = new Label { Text = "THAY ĐỔI SÂN KHẤU" };
        stageTitle.AddThemeColorOverride("font_color", Palette.Text3);
        _lineInspector.AddChild(stageTitle);

        // Chỉ tạo danh sách khi người dùng thật sự thêm thay đổi: ghi "stageChanges": []
        // vào mọi dòng chỉ vì bấm chọn dòng đó sẽ làm Git báo thay đổi vô cớ.
        var changes = line.StageChanges ?? new List<StageActor>();
        BuildActorListEditor(changes, isChange: true, onChanged: () => line.StageChanges = changes.Count == 0 ? null : changes);
    }

    private static readonly (StageAnchor Anchor, string Label)[] AnchorChoices =
    {
        (StageAnchor.FarLeft, "Rìa trái"),
        (StageAnchor.Left, "Trái"),
        (StageAnchor.Center, "Giữa"),
        (StageAnchor.Right, "Phải"),
        (StageAnchor.FarRight, "Rìa phải"),
    };

    /// <summary>
    /// Soạn một danh sách diễn viên — sân khấu đầu cảnh hoặc thay đổi tại một dòng.
    /// </summary>
    /// <remarks>
    /// Với thay đổi tại dòng, "giữ nguyên" biểu cảm/vị trí là lựa chọn hợp lệ
    /// (để trống trường đó) vì <see cref="SceneData.StageAt"/> gộp thay đổi lên
    /// trạng thái đang có; sân khấu đầu cảnh thì không có gì để giữ nên luôn
    /// phải chọn cụ thể.
    /// </remarks>
    private void BuildActorListEditor(List<StageActor> actors, bool isChange, Action? onChanged = null)
    {
        void Commit()
        {
            onChanged?.Invoke();
            SaveCurrentScene();
        }

        var charIds = _loaded.Project.Characters;
        if (charIds.Count == 0)
        {
            var hint = new Label
            {
                Text = "Dự án chưa có nhân vật. Tạo nhân vật ở tab Nhân vật rồi quay lại đây.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            hint.AddThemeColorOverride("font_color", Palette.Text3);
            _lineInspector.AddChild(hint);
            return;
        }

        for (var i = 0; i < actors.Count; i++)
        {
            var actor = actors[i];
            var actorIndex = i;
            _loaded.Characters.TryGetValue(actor.Character, out var character);

            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2, ContentMarginLeft = 8, ContentMarginTop = 8, ContentMarginRight = 8, ContentMarginBottom = 8 });
            var box = new VBoxContainer();
            panel.AddChild(box);

            var headerBox = new HBoxContainer();
            var charBtn = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            for (var c = 0; c < charIds.Count; c++)
            {
                charBtn.AddItem(CharacterName(charIds[c]));
                // Một nhân vật chỉ đứng một chỗ: StageAt gom theo id nhân vật, chọn
                // trùng thì mục sau âm thầm đè mục trước mà người dùng không hay.
                if (charIds[c] != actor.Character && actors.Any(a => a.Character == charIds[c])) charBtn.SetItemDisabled(c, true);
            }
            charBtn.Selected = charIds.IndexOf(actor.Character);
            charBtn.ItemSelected += idx =>
            {
                actor.Character = charIds[(int)idx];
                var entering = !isChange || !OnStageBefore(actor.Character);
                actor.Expression = entering ? DefaultExpressionOf(actor.Character) : string.Empty;
                if (entering) actor.Anchor ??= StageAnchor.Center;
                Commit();
                RefreshLineInspector();
                RedrawStage();
            };

            var removeBtn = new Button { Text = "✕", TooltipText = "Bỏ mục này" };
            removeBtn.Pressed += () =>
            {
                actors.RemoveAt(actorIndex);
                Commit();
                RefreshLineInspector();
                RedrawStage();
            };
            headerBox.AddChild(charBtn);
            headerBox.AddChild(removeBtn);
            box.AddChild(headerBox);

            if (isChange)
            {
                var leave = new CheckBox { Text = "Rời sân khấu tại dòng này", ButtonPressed = actor.Exit is not null };
                leave.Toggled += on =>
                {
                    actor.Exit = on ? new Motion() : null;
                    Commit();
                    RefreshLineInspector();
                    RedrawStage();
                };
                box.AddChild(leave);
                if (actor.Exit is not null)
                {
                    _lineInspector.AddChild(panel);
                    continue;
                }
            }

            var expressions = character?.Expressions.Select(e => e.Id).ToList() ?? new List<string>();
            var exprBtn = new OptionButton();
            var exprOffset = isChange ? 1 : 0;
            if (isChange) exprBtn.AddItem("— giữ biểu cảm —");
            foreach (var e in expressions) exprBtn.AddItem(e);
            if (exprBtn.ItemCount == 0)
            {
                exprBtn.AddItem("— chưa có biểu cảm —");
                exprBtn.Disabled = true;
            }
            var exprIdx = expressions.IndexOf(actor.Expression);
            exprBtn.Selected = exprIdx >= 0 ? exprIdx + exprOffset : 0;
            exprBtn.ItemSelected += idx =>
            {
                var at = (int)idx - exprOffset;
                actor.Expression = at >= 0 && at < expressions.Count ? expressions[at] : string.Empty;
                Commit();
                RedrawStage();
            };
            box.AddChild(exprBtn);

            var anchorBtn = new OptionButton();
            var anchorOffset = isChange ? 1 : 0;
            if (isChange) anchorBtn.AddItem("— giữ vị trí —");
            foreach (var (_, label) in AnchorChoices) anchorBtn.AddItem(label);
            var anchorIdx = System.Array.FindIndex(AnchorChoices, x => x.Anchor == actor.Anchor);
            anchorBtn.Selected = anchorIdx >= 0 ? anchorIdx + anchorOffset : (isChange ? 0 : 2);
            anchorBtn.ItemSelected += idx =>
            {
                var at = (int)idx - anchorOffset;
                actor.Anchor = at >= 0 ? AnchorChoices[at].Anchor : null;
                Commit();
                RedrawStage();
            };
            box.AddChild(anchorBtn);

            _lineInspector.AddChild(panel);
        }

        var free = charIds.FirstOrDefault(id => actors.All(a => a.Character != id));
        var addBtn = new Button
        {
            Text = isChange ? "+ Thêm thay đổi" : "+ Thêm nhân vật lên sân khấu",
            Disabled = free is null,
            TooltipText = free is null ? "Mọi nhân vật đều đã có trong danh sách." : "",
        };
        addBtn.Pressed += () =>
        {
            if (free is null) return;
            // Nhân vật chưa có mặt thì phải bước ra với biểu cảm và chỗ đứng cụ
            // thể; đang đứng sẵn thì mặc định "giữ nguyên", chỉ đổi cái người
            // dùng chọn.
            var entering = !isChange || !OnStageBefore(free);
            actors.Add(new StageActor
            {
                Character = free,
                Expression = entering ? DefaultExpressionOf(free) : string.Empty,
                Anchor = entering ? FreeAnchor(isChange ? StageBefore() : actors) : null,
            });
            Commit();
            RefreshLineInspector();
            RedrawStage();
        };
        _lineInspector.AddChild(addBtn);
    }

    /// <summary>Sân khấu ngay trước dòng đang chọn — nền để gộp thay đổi của dòng này lên.</summary>
    private List<StageActor> StageBefore()
    {
        if (_currentSceneId is null) return new List<StageActor>();
        var scene = _loaded.Scenes[_currentSceneId];
        return _selectedLine > 0 ? scene.StageAt(_selectedLine - 1) : scene.Stage;
    }

    private bool OnStageBefore(string characterId) => StageBefore().Any(a => a.Character == characterId);

    /// <summary>
    /// Chỗ đứng chưa ai chiếm, ưu tiên giữa rồi tản ra hai bên — để thêm nhân
    /// vật thứ hai không bị đè đúng lên người thứ nhất ở giữa sân khấu.
    /// </summary>
    private static StageAnchor FreeAnchor(IEnumerable<StageActor> occupied)
    {
        var taken = occupied.Select(a => a.Anchor).ToHashSet();
        var order = new[] { StageAnchor.Center, StageAnchor.Left, StageAnchor.Right, StageAnchor.FarLeft, StageAnchor.FarRight };
        return order.FirstOrDefault(a => !taken.Contains(a), StageAnchor.Center);
    }

    private string DefaultExpressionOf(string characterId)
    {
        if (!_loaded.Characters.TryGetValue(characterId, out var c)) return string.Empty;
        if (c.FindExpression(c.DefaultExpression) is not null) return c.DefaultExpression;
        return c.Expressions.FirstOrDefault()?.Id ?? string.Empty;
    }

    private string CharacterName(string id)
        => _loaded.Characters.TryGetValue(id, out var c) ? c.DisplayName[_loaded.Project.PrimaryLocale] ?? id : id;

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
