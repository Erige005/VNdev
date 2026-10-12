using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Khung sân khấu có hiệu ứng: chuyển cảnh, nhân vật vào/ra/dời chỗ, hiệu ứng
/// màn hình theo dòng, particle theo cảnh.
/// </summary>
/// <remarks>
/// Trình soạn cảnh và màn Chơi thử dùng chung đúng lớp này — "canvas là game
/// thật": chọn một dòng trong editor là thấy y hệt chuyển động người chơi sẽ
/// thấy, không phải bấm Chơi thử chỉ để xem nhân vật trượt vào ra sao.
///
/// Các lớp vẽ, từ dưới lên: nền đặc → nội dung (nền cảnh + nhân vật, vẽ lại mỗi
/// dòng) → particle (sống suốt cảnh, không reset mỗi dòng) → cảnh cũ đang
/// chuyển đi → phủ màu → loé sáng → màn đen của chuyển cảnh.
/// </remarks>
public partial class StageView : Control
{
    private readonly LoadedProject _loaded;
    private readonly string _projectDir;

    private Control _content = null!;
    private Control _particles = null!;
    private Control _prev = null!;
    private ColorRect _tint = null!;
    private ColorRect _flash = null!;
    private ColorRect _cover = null!;
    private ShaderMaterial _transitionMat = null!;

    private SceneData? _scene;
    private int _line = -1;
    private ParticleEffect? _builtParticles;
    private Vector2 _builtParticlesFor;

    private Tween? _transitionTween;
    private Tween? _kenBurnsTween;
    private Tween? _shakeTween;
    private Tween? _flashTween;
    private readonly List<Tween> _actorTweens = new();

    /// <summary>
    /// Đang chuyển cảnh thì giữ chuyển động nhân vật lại, chờ cảnh mới hiện rõ
    /// rồi mới cho họ bước ra — diễn dưới lớp cảnh cũ thì người xem không thấy gì.
    /// </summary>
    private bool _holdTweens;

    /// <summary>Báo khi chuyển cảnh chạy xong (hoặc bị bỏ qua) — màn chơi chờ cái này mới hiện hộp thoại.</summary>
    public event Action? TransitionFinished;

    public bool TransitionRunning => _transitionTween is not null && _transitionTween.IsValid() && _transitionTween.IsRunning();

    public StageView(LoadedProject loaded, string projectDir)
    {
        _loaded = loaded;
        _projectDir = projectDir;
    }

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        // Mipmap cho mọi ảnh con: ảnh nền gốc thường to gấp mấy lần khung, thu nhỏ
        // không mipmap thì răng cưa; và hiệu ứng nhoè cần mipmap để lấy mẫu mờ.
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;

        AddChild(FullRect(new ColorRect { Color = new Color(0.04f, 0.05f, 0.07f) }));
        _content = FullRect(new Control());
        _particles = FullRect(new Control());
        _prev = FullRect(new Control());
        _tint = FullRect(new ColorRect { Visible = false });
        _flash = FullRect(new ColorRect { Visible = false });
        _cover = FullRect(new ColorRect { Color = Colors.Black, Visible = false });
        foreach (var layer in new[] { _content, _particles, _prev, _tint, _flash, _cover }) AddChild(layer);

        _transitionMat = new ShaderMaterial { Shader = new Shader { Code = TransitionShader } };
        _prev.Material = _transitionMat;

        Resized += OnResized;
    }

    private static T FullRect<T>(T c) where T : Control
    {
        c.MouseFilter = MouseFilterEnum.Ignore;
        c.SetAnchorsPreset(LayoutPreset.FullRect);
        return c;
    }

    // ================= API =================

    /// <summary>
    /// Mở một cảnh và hiện dòng <paramref name="line"/> (-1 là sân khấu đầu cảnh).
    /// </summary>
    /// <param name="transition">Chuyển cảnh từ thứ đang hiện sang cảnh này; null là cắt thẳng.</param>
    /// <param name="fromBlack">Cảnh cũ là màn đen — dùng cho cảnh đầu tiên và khi xem thử trong editor.</param>
    public void ShowScene(SceneData? scene, int line, Transition? transition = null, bool fromBlack = false)
    {
        FinishTransition();
        var animate = transition is not null && transition.Type != TransitionType.Cut && transition.Duration > 0;
        if (animate) CaptureOld(fromBlack);

        _scene = scene;
        _line = line;
        // Cảnh mới luôn bắt đầu với nền phẳng: không để zoom chậm của cảnh trước
        // dính sang cảnh sau dù hai cảnh dùng chung ảnh nền.
        ResetBackground();
        RebuildParticles(force: true);
        _holdTweens = animate;
        Render(scene is not null && line >= 0 && line < scene.Lines.Count ? scene.StepsAt(line) : null);
        ApplyScreenEffect(animate: !animate);

        if (animate) PlayTransition(transition!);
    }

    /// <summary>
    /// Hiện dòng <paramref name="line"/> của cảnh đang mở.
    /// </summary>
    /// <param name="animate">
    /// Diễn chuyển động từ dòng trước sang dòng này (nhân vật vào/ra, rung, loé).
    /// Tắt khi chỉ vẽ lại vì người dùng sửa thuộc tính, không thì mỗi lần đổi
    /// một ô nhân vật lại trượt vào lại từ đầu.
    /// </param>
    /// <param name="resetBackground">Bỏ zoom chậm đang có — editor nhảy dòng lung tung nên luôn bỏ; màn chơi đi tuần tự thì giữ.</param>
    public void ShowLine(int line, bool animate, bool resetBackground = false)
    {
        _line = line;
        if (resetBackground) ResetBackground();
        List<StageStep>? steps = null;
        if (animate && _scene is not null)
        {
            // Sân khấu đầu cảnh (editor bỏ chọn dòng): diễn cách người đứng sẵn
            // xuất hiện khi cảnh vừa mở, để chọn "Trượt vào" là thấy ngay.
            steps = line >= 0
                ? _scene.StepsAt(line)
                : _scene.Stage.Where(a => a.Enter is not null).Select(a => new StageStep(a.Character, null, a, a.Enter, null)).ToList();
        }
        Render(steps);
        ApplyScreenEffect(animate);
    }

    /// <summary>Vẽ lại cảnh đang mở sau khi dữ liệu đổi (nền, particle, sân khấu), không diễn chuyển động.</summary>
    public void Refresh()
    {
        RebuildParticles(force: true);
        Render(steps: null);
        ApplyScreenEffect(animate: false);
    }

    /// <summary>Tua hết chuyển động đang chạy về trạng thái cuối — người chơi bấm khi chưa diễn xong.</summary>
    public void SkipAnimations()
    {
        FinishTransition();
        foreach (var t in _actorTweens.Where(t => t.IsValid())) t.CustomStep(1000);
        _actorTweens.Clear();
    }

    // ================= nội dung: nền + nhân vật =================

    private void Render(List<StageStep>? steps)
    {
        foreach (var t in _actorTweens.Where(t => t.IsValid())) t.Kill();
        _actorTweens.Clear();

        var actors = _scene is null ? new List<StageActor>()
            : _line >= 0 && _line < _scene.Lines.Count ? _scene.StageAt(_line)
            : _scene.Stage.Select(a => a.Clone()).ToList();

        // Cỡ nhân vật theo chiều cao khung này chứ không theo lớp nội dung: lúc
        // khung vừa đổi cỡ, lớp con chưa kịp đổi theo, đo nó là ra nhân vật cỡ 0.
        var drawn = StageRenderer.Draw(_content, _loaded, _projectDir, _scene, actors, frameHeight: Size.Y);
        if (drawn.Background is { } bg)
        {
            bg.PivotOffset = Size / 2f;
            var blur = _scene is not null && _line >= 0 && _line < _scene.Lines.Count
                && _scene.Lines[_line].ScreenEffect is { Type: ScreenEffectType.BlurBackground } fx ? fx : null;
            bg.Material = blur is null ? null : BlurMaterial(blur.Intensity ?? 0.6f);
        }
        if (steps is not null) AnimateSteps(steps, drawn.Actors);
    }

    private void AnimateSteps(List<StageStep> steps, Dictionary<string, Control> nodes)
    {
        var w = Size.X;
        var h = Size.Y;
        foreach (var step in steps)
        {
            if (step.After is not null && nodes.TryGetValue(step.Character, out var node))
            {
                if (step.Before is null)
                {
                    if (step.Enter is { Type: not MotionType.None } enter) PlayMotion(node, enter, entering: true, w, h);
                }
                else
                {
                    var dx = (RatioOf(step.Before) - RatioOf(step.After)) * w;
                    if (Math.Abs(dx) > 0.5f) Track(Shift(node, new Vector2(dx, 0), Vector2.Zero, 0.35f, Tween.TransitionType.Sine));

                    // Sáng/tối theo người nói chuyển mượt, không giật từng dòng.
                    var from = step.Before.Dim ?? 1f;
                    var to = step.After.Dim ?? 1f;
                    if (Math.Abs(from - to) > 0.01f)
                    {
                        node.Modulate = new Color(from, from, from, 1);
                        var t = CreateTween();
                        t.TweenProperty(node, "modulate", new Color(to, to, to, 1), 0.2f);
                        Track(t);
                    }
                }
            }
            else if (step.Before is not null && step.Exit is { Type: not MotionType.None } exit)
            {
                // Nhân vật đã rời khỏi trạng thái mới nên không có node — dựng một
                // bản "bóng" theo trạng thái cũ để diễn cảnh đi ra rồi bỏ.
                var ghost = StageRenderer.BuildActor(step.Before, _loaded, _projectDir, h);
                _content.AddChild(ghost);
                var t = PlayMotion(ghost, exit, entering: false, w, h);
                t.TweenCallback(Callable.From(ghost.QueueFree));
            }
        }
    }

    private static float RatioOf(StageActor a) => a.X ?? (a.Anchor is { } an ? StageAnchors.Ratio(an) : 0.5f);

    /// <summary>
    /// Diễn một <see cref="Motion"/>. Hướng trượt là hướng chuyển động: "trượt
    /// sang trái" khi vào là từ bên phải đi vào, khi ra là đi ra mép trái.
    /// </summary>
    private Tween PlayMotion(Control node, Motion motion, bool entering, float w, float h)
    {
        var dur = Math.Max(0.05f, motion.Duration);
        var dir = motion.Type switch
        {
            MotionType.SlideLeft => new Vector2(-1, 0),
            MotionType.SlideRight => new Vector2(1, 0),
            MotionType.SlideUp => new Vector2(0, -1),
            MotionType.SlideDown => new Vector2(0, 1),
            _ => Vector2.Zero,
        };
        var dist = dir * new Vector2(w * 0.25f, h * 0.35f);

        var dim = node.Modulate;
        var t = CreateTween().SetParallel();
        if (motion.Type == MotionType.Bounce)
        {
            // Nảy: vào thì rơi từ trên xuống nảy lên; ra thì nhún lên rồi biến mất.
            var hop = new Vector2(0, -h * 0.08f);
            if (entering) t.TweenMethod(OffsetSetter(node), hop, Vector2.Zero, dur).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
            else t.TweenMethod(OffsetSetter(node), Vector2.Zero, hop, dur).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        }
        else if (dir != Vector2.Zero)
        {
            if (entering) t.TweenMethod(OffsetSetter(node), -dist, Vector2.Zero, dur).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            else t.TweenMethod(OffsetSetter(node), Vector2.Zero, dist, dur).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        }

        node.Modulate = new Color(dim.R, dim.G, dim.B, entering ? 0f : 1f);
        t.TweenProperty(node, "modulate:a", entering ? 1f : 0f, dur);
        t.SetParallel(false);
        Track(t);
        return t;
    }

    private Tween Shift(Control node, Vector2 from, Vector2 to, float dur, Tween.TransitionType trans)
    {
        var t = CreateTween();
        t.TweenMethod(OffsetSetter(node), from, to, dur).SetTrans(trans).SetEase(Tween.EaseType.InOut);
        return t;
    }

    /// <summary>
    /// Dời node bằng cách cộng thêm vào offset gốc, không đụng anchor — vị trí
    /// đứng của nhân vật tính theo anchor tỉ lệ khung, giữ nguyên thì đổi cỡ cửa
    /// sổ giữa chừng vẫn đứng đúng chỗ.
    /// </summary>
    private static Callable OffsetSetter(Control node)
    {
        var baseL = node.OffsetLeft;
        var baseR = node.OffsetRight;
        var baseT = node.OffsetTop;
        var baseB = node.OffsetBottom;
        return Callable.From<Vector2>(d =>
        {
            if (!IsInstanceValid(node)) return;
            node.OffsetLeft = baseL + d.X;
            node.OffsetRight = baseR + d.X;
            node.OffsetTop = baseT + d.Y;
            node.OffsetBottom = baseB + d.Y;
        });
    }

    private void Track(Tween t)
    {
        if (_holdTweens) t.Pause();
        _actorTweens.Add(t);
    }

    // ================= hiệu ứng màn hình theo dòng =================

    private void ApplyScreenEffect(bool animate)
    {
        var fx = _scene is not null && _line >= 0 && _line < _scene.Lines.Count ? _scene.Lines[_line].ScreenEffect : null;

        // Phủ màu chỉ sống trong dòng của nó: sang dòng khác là hết.
        _tint.Visible = fx?.Type == ScreenEffectType.Tint;
        if (fx?.Type == ScreenEffectType.Tint)
        {
            var c = ParseColor(fx.Color, new Color(1f, 0.62f, 0.3f));
            _tint.Color = new Color(c.R, c.G, c.B, 0.15f + 0.45f * (fx.Intensity ?? 0.5f));
        }

        if (!animate || fx is null) return;
        var intensity = Math.Clamp(fx.Intensity ?? 0.5f, 0.05f, 1f);

        switch (fx.Type)
        {
            case ScreenEffectType.Shake:
            {
                _shakeTween?.Kill();
                var dur = fx.Duration ?? 0.5f;
                var amp = Size.Y * 0.035f * intensity;
                var rng = new RandomNumberGenerator();
                _shakeTween = CreateTween();
                _shakeTween.TweenMethod(Callable.From<float>(p =>
                {
                    // Biên độ tắt dần về 0 để khung dừng đúng chỗ, không lệch vài pixel.
                    var a = amp * (1f - p);
                    var off = new Vector2(rng.RandfRange(-a, a), rng.RandfRange(-a, a));
                    _content.Position = off;
                    _particles.Position = off;
                }), 0f, 1f, dur);
                _shakeTween.TweenCallback(Callable.From(() => { _content.Position = Vector2.Zero; _particles.Position = Vector2.Zero; }));
                break;
            }
            case ScreenEffectType.Flash:
            {
                _flashTween?.Kill();
                var c = ParseColor(fx.Color, Colors.White);
                _flash.Color = new Color(c.R, c.G, c.B, intensity);
                _flash.Visible = true;
                _flashTween = CreateTween();
                _flashTween.TweenProperty(_flash, "color:a", 0f, fx.Duration ?? 0.45f).SetEase(Tween.EaseType.Out);
                _flashTween.TweenCallback(Callable.From(() => _flash.Visible = false));
                break;
            }
            case ScreenEffectType.KenBurns:
            {
                if (BackgroundNode() is not { } bg) break;
                _kenBurnsTween?.Kill();
                _kenBurnsTween = CreateTween();
                _kenBurnsTween.TweenProperty(bg, "scale", Vector2.One * (1f + 0.15f * intensity), fx.Duration ?? 6f)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                break;
            }
        }
    }

    private TextureRect? BackgroundNode() => _content.GetNodeOrNull<TextureRect>(StageRenderer.BackgroundName);

    private void ResetBackground()
    {
        _kenBurnsTween?.Kill();
        if (BackgroundNode() is { } bg) bg.Scale = Vector2.One;
    }

    private static Color ParseColor(string? html, Color fallback)
        => !string.IsNullOrEmpty(html) && Color.HtmlIsValid(html) ? Color.FromHtml(html) : fallback;

    private static ShaderMaterial BlurMaterial(float intensity)
    {
        var mat = new ShaderMaterial { Shader = new Shader { Code = BlurShader } };
        mat.SetShaderParameter("lod", 1f + 3.5f * Math.Clamp(intensity, 0f, 1f));
        return mat;
    }

    // ================= chuyển cảnh =================

    /// <summary>
    /// Chuyển những gì đang hiện sang lớp "cảnh cũ" để vẽ cảnh mới bên dưới rồi
    /// cho cảnh cũ tan đi. Dời chính các node chứ không chụp màn hình: chụp sẽ
    /// dính cả hộp thoại, và không chạy được khi kiểm thử không có cửa sổ.
    /// </summary>
    private void CaptureOld(bool fromBlack)
    {
        ClearPrev();
        if (fromBlack || _content.GetChildCount() == 0)
        {
            _prev.AddChild(FullRect(new ColorRect { Color = Colors.Black, UseParentMaterial = true }));
            return;
        }
        foreach (var child in _content.GetChildren().OfType<CanvasItem>().ToList())
        {
            _content.RemoveChild(child);
            _prev.AddChild(child);
            child.UseParentMaterial = true;
        }
    }

    private void ClearPrev()
    {
        foreach (var child in _prev.GetChildren())
        {
            _prev.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void PlayTransition(Transition tr)
    {
        var mode = tr.Type switch
        {
            TransitionType.WipeLeft => 1,
            TransitionType.WipeRight => 2,
            TransitionType.WipeUp => 3,
            TransitionType.WipeDown => 4,
            TransitionType.Pixelate => 5,
            TransitionType.Blur => 6,
            _ => 0,
        };
        _transitionMat.SetShaderParameter("mode", mode);
        _transitionMat.SetShaderParameter("progress", 0f);
        _transitionMat.SetShaderParameter("stage_rect", new Vector4(GlobalPosition.X, GlobalPosition.Y, Math.Max(1, Size.X), Math.Max(1, Size.Y)));
        var mask = tr.Type == TransitionType.Dissolve && !string.IsNullOrEmpty(tr.Mask)
            ? StageRenderer.LoadTexture(Path.Combine(_projectDir, tr.Mask)) : null;
        _transitionMat.SetShaderParameter("has_mask", mask is not null);
        if (mask is not null) _transitionMat.SetShaderParameter("mask_tex", mask);

        _transitionTween = CreateTween();
        if (tr.Type == TransitionType.Fade)
        {
            // Mờ qua đen: tối dần trên cảnh cũ, tới giữa thì bỏ cảnh cũ, sáng dần ra cảnh mới.
            _cover.Visible = true;
            _cover.Color = new Color(0, 0, 0, 0);
            _transitionTween.TweenProperty(_cover, "color:a", 1f, tr.Duration / 2f);
            _transitionTween.TweenCallback(Callable.From(ClearPrev));
            _transitionTween.TweenProperty(_cover, "color:a", 0f, tr.Duration / 2f);
        }
        else
        {
            _transitionTween.TweenMethod(Callable.From<float>(p => _transitionMat.SetShaderParameter("progress", p)), 0f, 1f, tr.Duration)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }
        _transitionTween.TweenCallback(Callable.From(EndTransition));
    }

    private void FinishTransition()
    {
        if (TransitionRunning) _transitionTween!.CustomStep(1000);
    }

    private void EndTransition()
    {
        ClearPrev();
        _cover.Visible = false;
        _transitionTween = null;
        if (_holdTweens)
        {
            _holdTweens = false;
            foreach (var t in _actorTweens.Where(t => t.IsValid())) t.Play();
            ApplyScreenEffect(animate: true);
        }
        TransitionFinished?.Invoke();
    }

    // ================= particle =================

    private void OnResized()
    {
        if (Size.X <= 0 || Size.Y <= 0) return;
        _transitionMat?.SetShaderParameter("stage_rect", new Vector4(GlobalPosition.X, GlobalPosition.Y, Size.X, Size.Y));
        // Nhân vật tính cỡ theo chiều cao khung lúc vẽ — đổi cỡ thì vẽ lại, không
        // thì nhân vật giữ cỡ cũ trong khung đã to ra. Đang chờ chuyển cảnh thì
        // dựng lại cả chuyển động bước ra — màn chơi mở cảnh đầu khi khung còn
        // cỡ 0, khung giãn ra ngay sau đó, vẽ lại trơn thì mất cảnh bước ra.
        Render(_holdTweens && _scene is not null && _line >= 0 && _line < _scene.Lines.Count ? _scene.StepsAt(_line) : null);
        ApplyScreenEffect(animate: false);
        RebuildParticles(force: false);
    }

    private void RebuildParticles(bool force)
    {
        var fx = _scene?.Particles;
        if (!force && fx == _builtParticles && Size.DistanceTo(_builtParticlesFor) < 1f) return;
        _builtParticles = fx;
        _builtParticlesFor = Size;

        foreach (var child in _particles.GetChildren()) child.QueueFree();
        if (fx is null || Size.X <= 0 || Size.Y <= 0) return;
        _particles.AddChild(ParticlePresets.Build(fx, Size));
    }

    // ================= shader =================

    /// <summary>
    /// Shader cho lớp cảnh cũ. Các node con dùng chung material của lớp
    /// (UseParentMaterial) nên toạ độ quét tính theo khung sân khấu chứ không
    /// theo từng ảnh — nền và nhân vật bị quét cùng một đường.
    /// </summary>
    private const string TransitionShader = """
        shader_type canvas_item;
        uniform int mode = 0;
        uniform float progress = 0.0;
        uniform vec4 stage_rect = vec4(0.0, 0.0, 1.0, 1.0);
        uniform bool has_mask = false;
        uniform sampler2D mask_tex : filter_linear;
        varying vec2 world;
        varying vec4 vcol;

        void vertex() {
            world = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
            vcol = COLOR;
        }

        void fragment() {
            vec2 local = (world - stage_rect.xy) / stage_rect.zw;
            vec4 c;
            if (mode == 5) {
                // Ô vuông tính theo pixel màn hình (fwidth) để nền và nhân vật vỡ cùng cỡ ô.
                // Lấy mẫu ở mipmap 0: toạ độ nhảy bậc giữa các ô làm GPU tưởng ảnh bị
                // thu nhỏ cực mạnh, tự chọn mipmap mờ nhất và vẽ ra vệt lưới ở mép ô.
                vec2 cell = fwidth(UV) * max(1.0, progress * 90.0);
                c = textureLod(TEXTURE, (floor(UV / cell) + 0.5) * cell, 0.0);
            } else if (mode == 6) {
                float lod = progress * 7.0;
                vec2 d = TEXTURE_PIXEL_SIZE * pow(2.0, lod);
                c = textureLod(TEXTURE, UV, lod) * 0.4;
                c += textureLod(TEXTURE, UV + vec2(d.x, 0.0), lod) * 0.15;
                c += textureLod(TEXTURE, UV - vec2(d.x, 0.0), lod) * 0.15;
                c += textureLod(TEXTURE, UV + vec2(0.0, d.y), lod) * 0.15;
                c += textureLod(TEXTURE, UV - vec2(0.0, d.y), lod) * 0.15;
            } else {
                c = texture(TEXTURE, UV);
            }
            c *= vcol;
            float a = 1.0;
            float edge = 0.04;
            if (mode == 0) {
                a = has_mask ? smoothstep(progress * 1.1 - 0.1, progress * 1.1, texture(mask_tex, local).r) : 1.0 - progress;
            } else if (mode == 1) {
                a = smoothstep(0.0, edge, (1.0 - progress * (1.0 + edge)) - local.x);
            } else if (mode == 2) {
                a = smoothstep(0.0, edge, local.x - progress * (1.0 + edge));
            } else if (mode == 3) {
                a = smoothstep(0.0, edge, (1.0 - progress * (1.0 + edge)) - local.y);
            } else if (mode == 4) {
                a = smoothstep(0.0, edge, local.y - progress * (1.0 + edge));
            } else {
                a = 1.0 - smoothstep(0.45, 1.0, progress);
            }
            COLOR = vec4(c.rgb, c.a * a);
        }
        """;

    private const string BlurShader = """
        shader_type canvas_item;
        uniform float lod = 2.0;
        varying vec4 vcol;
        void vertex() { vcol = COLOR; }
        void fragment() {
            // Lấy 5 mẫu quanh điểm ở mức mipmap thấp để mờ đều, không ra ô vuông.
            vec2 d = TEXTURE_PIXEL_SIZE * pow(2.0, lod);
            vec4 c = textureLod(TEXTURE, UV, lod) * 0.4;
            c += textureLod(TEXTURE, UV + vec2(d.x, 0.0), lod) * 0.15;
            c += textureLod(TEXTURE, UV - vec2(d.x, 0.0), lod) * 0.15;
            c += textureLod(TEXTURE, UV + vec2(0.0, d.y), lod) * 0.15;
            c += textureLod(TEXTURE, UV - vec2(0.0, d.y), lod) * 0.15;
            COLOR = c * vcol;
        }
        """;
}
