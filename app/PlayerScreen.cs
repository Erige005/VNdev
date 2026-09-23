using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Chặng 6 — bộ thông dịch chạy truyện. Đi qua <see cref="StoryGraph"/> đúng
/// như bộ kiểm tra ở <c>VNdev.Core.Validation</c> đã xác nhận là hợp lệ, xử
/// lý biến/điều kiện, hiện cảnh/hộp thoại/lựa chọn/kết thúc.
/// </summary>
/// <remarks>
/// Bộ dịch nằm ở tầng Godot chứ không phải VNdev.Core: nó cần vẽ hình, phát
/// nhạc — những việc chỉ engine làm được. Core chỉ cấp dữ liệu đã được xác
/// nhận hợp lệ; đây là "engine" mà nguyên tắc "canvas là game thật" nói tới.
/// </remarks>
public partial class PlayerScreen : Control
{
    private readonly LoadedProject _loaded;
    private readonly string _projectDir;
    private readonly Action _onClose;

    private Dictionary<string, VarValue> _vars = new();
    private StoryGraph _graph = null!;
    private SceneNode? _currentSceneNode;
    private SceneData? _currentScene;
    private int _lineIndex;
    private bool _typing;
    private float _typeProgress;

    private Control _stage = null!;
    private Control _overlay = null!;
    private PanelContainer _textBox = null!;
    private Label _speakerLabel = null!;
    private RichTextLabel _textLabel = null!;
    private VBoxContainer _choiceBox = null!;
    private PanelContainer _endingPanel = null!;
    private Label _endingLabel = null!;

    private AudioStreamPlayer _bgmPlayer = null!;
    private AudioStreamPlayer _sfxPlayer = null!;

    public PlayerScreen(LoadedProject loaded, string projectDir, Action onClose)
    {
        _loaded = loaded;
        _projectDir = projectDir;
        _onClose = onClose;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.85f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var frameHolder = new VBoxContainer();
        center.AddChild(frameHolder);

        var closeRow = new HBoxContainer();
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(760, 0) };
        var close = new Button { Text = "✕ Đóng" };
        close.Pressed += () => _onClose();
        closeRow.AddChild(spacer);
        closeRow.AddChild(close);
        frameHolder.AddChild(closeRow);

        var aspect = new AspectRatioContainer { Ratio = 16f / 9f, CustomMinimumSize = new Vector2(760, 428) };
        frameHolder.AddChild(aspect);

        _stage = new Control();
        _stage.GuiInput += OnStageInput;
        var frameBg = new ColorRect { Color = new Color(0.04f, 0.05f, 0.07f) };
        frameBg.SetAnchorsPreset(LayoutPreset.FullRect);
        _stage.AddChild(frameBg);
        aspect.AddChild(_stage);

        // Lớp phủ tách riêng khỏi _stage: StageRenderer.Draw() xoá sạch con
        // của _stage mỗi khi sang dòng mới để vẽ lại nền/nhân vật — hộp
        // thoại, lựa chọn, màn kết thúc phải sống ở chỗ khác thì mới không bị
        // xoá theo. AspectRatioContainer canh cả hai lớp đúng cùng một khung
        // 16:9 nên chúng luôn khớp pixel với sân khấu bên dưới.
        _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        aspect.AddChild(_overlay);

        BuildTextBox();
        BuildChoiceBox();
        BuildEndingPanel();

        _bgmPlayer = new AudioStreamPlayer();
        _sfxPlayer = new AudioStreamPlayer();
        AddChild(_bgmPlayer);
        AddChild(_sfxPlayer);

        Start();
    }

    public override void _Process(double delta)
    {
        if (!_typing) return;

        var speed = _loaded.Project.Text.TypewriterSpeed;
        if (speed <= 0)
        {
            _typeProgress = 1f;
        }
        else
        {
            var totalChars = _textLabel.GetParsedText().Length;
            if (totalChars == 0) totalChars = 1;
            _typeProgress += (float)(delta * speed) / totalChars;
        }

        if (_typeProgress >= 1f)
        {
            _typeProgress = 1f;
            _typing = false;
        }
        _textLabel.VisibleRatio = _typeProgress;
    }

    private void OnStageInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Advance();
        }
    }

    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Keycode: Key.Space or Key.Enter })
        {
            Advance();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Advance()
    {
        if (_choiceBox.Visible || _endingPanel.Visible) return;

        if (_typing)
        {
            _typing = false;
            _typeProgress = 1f;
            _textLabel.VisibleRatio = 1f;
            return;
        }

        _lineIndex++;
        ShowLine();
    }

    // ---------- vòng lặp thông dịch ----------

    private void Start()
    {
        _vars = _loaded.Project.Variables.ToDictionary(v => v.Id, v => v.Initial);
        var firstGraphId = _loaded.Project.Graphs.FirstOrDefault();
        if (firstGraphId is null || !_loaded.Graphs.TryGetValue(firstGraphId, out var graph))
        {
            ShowStopped("Dự án chưa có chương nào để chơi thử.");
            return;
        }
        _graph = graph;
        GoToNode(_graph.Entry);
    }

    private void GoToNode(string? id)
    {
        if (id is null)
        {
            ShowStopped("Nhánh này chưa nối tới đâu cả — bảng lỗi ở Cốt truyện sẽ chỉ đúng chỗ.");
            return;
        }

        var node = _graph.Find(id);
        if (node is null)
        {
            ShowStopped($"Không tìm thấy node \"{id}\".");
            return;
        }

        switch (node)
        {
            case SceneNode scene: EnterScene(scene); break;
            case ChoiceNode choice: ShowChoice(choice); break;
            case ConditionNode condition: GoToNode(Eval(condition.Condition) ? condition.WhenTrue : condition.WhenFalse); break;
            case VariableNode variable:
                foreach (var effect in variable.Effects) Apply(effect);
                GoToNode(variable.Next);
                break;
            case JumpNode jump: GoToNode(jump.Target); break;
            case EndingNode ending: ShowEnding(ending); break;
            default: ShowStopped("Gặp một node không chạy được (ghi chú)."); break;
        }
    }

    private void EnterScene(SceneNode sceneNode)
    {
        if (!_loaded.Scenes.TryGetValue(sceneNode.Scene, out var scene))
        {
            ShowStopped($"Thiếu file cảnh \"{sceneNode.Scene}\".");
            return;
        }

        _currentSceneNode = sceneNode;
        _currentScene = scene;
        _lineIndex = 0;

        if (!string.IsNullOrEmpty(scene.Bgm?.Src))
        {
            var stream = StageRenderer.LoadAudio(Path.Combine(_projectDir, scene.Bgm.Src));
            if (stream is not null)
            {
                _bgmPlayer.Stream = stream;
                _bgmPlayer.VolumeDb = Mathf.LinearToDb(_loaded.Project.Audio.BgmVolume * (scene.Bgm.Volume ?? 1f));
                _bgmPlayer.Play();
            }
        }
        else
        {
            _bgmPlayer.Stop();
        }

        ShowLine();
    }

    private void ShowLine()
    {
        if (_currentScene is null || _currentSceneNode is null) return;

        if (_lineIndex >= _currentScene.Lines.Count)
        {
            GoToNode(_currentSceneNode.Next);
            return;
        }

        var line = _currentScene.Lines[_lineIndex];
        var locale = _loaded.Project.PrimaryLocale;

        StageRenderer.Draw(_stage, _loaded, _projectDir, _currentScene, _currentScene.StageAt(_lineIndex), keepFirst: 1);

        _textBox.Visible = true;
        if (line.Speaker is not null && _loaded.Characters.TryGetValue(line.Speaker, out var speaker))
        {
            _speakerLabel.Visible = true;
            _speakerLabel.Text = speaker.DisplayName[locale] ?? line.Speaker;
            _speakerLabel.AddThemeColorOverride("font_color", Color.FromHtml(speaker.Color));
        }
        else
        {
            _speakerLabel.Visible = false;
        }

        _textLabel.Text = line.Text[locale] ?? "";
        _typeProgress = 0f;
        _typing = true;
        _textLabel.VisibleRatio = 0f;

        if (!string.IsNullOrEmpty(line.Sfx))
        {
            var stream = StageRenderer.LoadAudio(Path.Combine(_projectDir, line.Sfx));
            if (stream is not null)
            {
                _sfxPlayer.Stream = stream;
                _sfxPlayer.VolumeDb = Mathf.LinearToDb(_loaded.Project.Audio.SfxVolume);
                _sfxPlayer.Play();
            }
        }
    }

    private void ShowChoice(ChoiceNode choice)
    {
        _textBox.Visible = false;
        _choiceBox.Visible = true;
        foreach (var child in _choiceBox.GetChildren()) child.QueueFree();

        var locale = _loaded.Project.PrimaryLocale;
        var prompt = new Label { Text = choice.Prompt?[locale] ?? "" };
        prompt.AddThemeColorOverride("font_color", Palette.Text);
        _choiceBox.AddChild(prompt);

        foreach (var option in choice.Options)
        {
            if (option.ShowIf is not null && !Eval(option.ShowIf)) continue;

            var btn = new Button { Text = option.Text[locale] ?? option.Id };
            btn.Pressed += () =>
            {
                foreach (var effect in option.Effects) Apply(effect);
                _choiceBox.Visible = false;
                GoToNode(option.Next);
            };
            _choiceBox.AddChild(btn);
        }
    }

    private void ShowEnding(EndingNode ending)
    {
        _textBox.Visible = false;
        _choiceBox.Visible = false;
        _bgmPlayer.Stop();

        _endingPanel.Visible = true;
        _endingLabel.Text = $"★ {ending.Name[_loaded.Project.PrimaryLocale] ?? ending.DisplayName}";
    }

    private void ShowStopped(string message)
    {
        _textBox.Visible = true;
        _speakerLabel.Visible = false;
        _textLabel.Text = message;
        _typing = false;
        _textLabel.VisibleRatio = 1f;
    }

    // ---------- biến / điều kiện ----------

    private VarValue GetVar(string name) => _vars.TryGetValue(name, out var v) ? v : VarValue.Of(0d);

    private bool Eval(Condition condition) => condition switch
    {
        CompareCondition c => Compare(GetVar(c.Variable), c.Op, c.Value),
        AndCondition a => a.Operands.All(Eval),
        OrCondition o => o.Operands.Any(Eval),
        NotCondition n => !Eval(n.Operand),
        _ => true,
    };

    private static bool Compare(VarValue a, ComparisonOp op, VarValue b) => op switch
    {
        ComparisonOp.Eq => a.Equals(b),
        ComparisonOp.Neq => !a.Equals(b),
        ComparisonOp.Gt => a.AsNumber() > b.AsNumber(),
        ComparisonOp.Gte => a.AsNumber() >= b.AsNumber(),
        ComparisonOp.Lt => a.AsNumber() < b.AsNumber(),
        ComparisonOp.Lte => a.AsNumber() <= b.AsNumber(),
        _ => false,
    };

    private void Apply(Effect effect)
    {
        var current = GetVar(effect.Variable);
        var value = effect.Value ?? VarValue.Of(0d);
        _vars[effect.Variable] = effect.Op switch
        {
            AssignOp.Set => value,
            AssignOp.Add => VarValue.Of(current.AsNumber() + value.AsNumber()),
            AssignOp.Sub => VarValue.Of(current.AsNumber() - value.AsNumber()),
            AssignOp.Mul => VarValue.Of(current.AsNumber() * value.AsNumber()),
            AssignOp.Div => value.AsNumber() != 0 ? VarValue.Of(current.AsNumber() / value.AsNumber()) : current,
            AssignOp.Toggle => VarValue.Of(!current.AsBoolean()),
            _ => current,
        };
    }

    // ---------- UI tĩnh ----------

    private void BuildTextBox()
    {
        _textBox = new PanelContainer { CustomMinimumSize = new Vector2(0, 120) };
        _textBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.09f, 0.85f),
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        });
        _textBox.AnchorLeft = 0.03f;
        _textBox.AnchorRight = 0.97f;
        _textBox.AnchorTop = 0.72f;
        _textBox.AnchorBottom = 0.96f;

        var box = new VBoxContainer();
        _textBox.AddChild(box);

        _speakerLabel = new Label();
        _speakerLabel.AddThemeColorOverride("font_color", Palette.Accent2);
        box.AddChild(_speakerLabel);

        _textLabel = new RichTextLabel
        {
            BbcodeEnabled = false,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _textLabel.AddThemeColorOverride("default_color", Palette.Text);
        box.AddChild(_textLabel);

        _overlay.AddChild(_textBox);
    }

    private void BuildChoiceBox()
    {
        _choiceBox = new VBoxContainer { Visible = false };
        _choiceBox.AnchorLeft = 0.25f;
        _choiceBox.AnchorRight = 0.75f;
        _choiceBox.AnchorTop = 0.35f;
        _choiceBox.AnchorBottom = 0.9f;
        _choiceBox.AddThemeConstantOverride("separation", 8);
        _overlay.AddChild(_choiceBox);
    }

    private void BuildEndingPanel()
    {
        _endingPanel = new PanelContainer { Visible = false };
        _endingPanel.SetAnchorsPreset(LayoutPreset.FullRect);
        _endingPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.03f, 0.03f, 0.05f, 0.92f) });

        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        _endingPanel.AddChild(box);

        _endingLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _endingLabel.AddThemeColorOverride("font_color", Palette.NodeEnding);
        _endingLabel.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(_endingLabel);

        var closeBtn = new Button { Text = "Đóng", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        closeBtn.Pressed += () => _onClose();
        box.AddChild(closeBtn);

        _overlay.AddChild(_endingPanel);
    }
}
