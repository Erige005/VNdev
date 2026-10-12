using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.Core.Tests;

public class StageStepTests
{
    private static SceneData TwoActorScene() => new()
    {
        Id = "s",
        Stage =
        {
            new StageActor { Character = "yuki", Expression = "normal", Anchor = StageAnchor.Left },
            new StageActor { Character = "mei", Expression = "normal", Anchor = StageAnchor.Right, Enter = new Motion { Type = MotionType.SlideLeft } },
        },
        Lines =
        {
            new DialogueLine { Id = "l1", Text = new Localized("vi", "A") },
            new DialogueLine
            {
                Id = "l2", Text = new Localized("vi", "B"),
                StageChanges = new()
                {
                    new StageActor { Character = "kai", Expression = "cuoi", Anchor = StageAnchor.Center, Enter = new Motion { Type = MotionType.Bounce } },
                    new StageActor { Character = "yuki", Anchor = StageAnchor.FarLeft },
                },
            },
            new DialogueLine
            {
                Id = "l3", Text = new Localized("vi", "C"),
                StageChanges = new() { new StageActor { Character = "mei", Exit = new Motion { Type = MotionType.SlideRight } } },
            },
        },
    };

    [Test]
    public void Nhan_vat_dung_san_co_hieu_ung_vao_thi_buoc_ra_o_dong_dau()
    {
        var steps = TwoActorScene().StepsAt(0).ToDictionary(s => s.Character);

        Check.NotNull(steps["yuki"].Before);
        Check.Null(steps["yuki"].Enter);
        Check.Null(steps["mei"].Before);
        Check.Equal(MotionType.SlideLeft, steps["mei"].Enter!.Type);
    }

    [Test]
    public void Thay_doi_tai_dong_cho_biet_ai_buoc_ra_va_ai_doi_cho()
    {
        var steps = TwoActorScene().StepsAt(1).ToDictionary(s => s.Character);

        Check.Equal(3, steps.Count);
        Check.Null(steps["kai"].Before);
        Check.Equal(MotionType.Bounce, steps["kai"].Enter!.Type);
        Check.Equal(StageAnchor.Left, steps["yuki"].Before!.Anchor);
        Check.Equal(StageAnchor.FarLeft, steps["yuki"].After!.Anchor);
        Check.Null(steps["mei"].Enter);
    }

    [Test]
    public void Nhan_vat_roi_di_giu_trang_thai_cu_va_hieu_ung_ra()
    {
        var mei = TwoActorScene().StepsAt(2).Single(s => s.Character == "mei");

        Check.Null(mei.After);
        Check.Equal(StageAnchor.Right, mei.Before!.Anchor);
        Check.Equal(MotionType.SlideRight, mei.Exit!.Type);
    }

    [Test]
    public void Hieu_ung_canh_ghi_ra_json_on_dinh()
    {
        var fs = new MemoryFileSystem();
        var scene = TwoActorScene();
        scene.BackgroundTransition = new Transition { Type = TransitionType.WipeLeft, Duration = 0.8f };
        scene.Particles = new ParticleEffect { Preset = ParticlePreset.Petals, Density = 1.5f };
        scene.Lines[0].ScreenEffect = new ScreenEffect { Type = ScreenEffectType.Shake, Intensity = 0.6f };

        ProjectIo.SaveScene(fs, scene);
        var first = fs.ReadText(ProjectPaths.Scene("s"));
        Check.True(first.Contains("\"type\": \"wipeLeft\""), "Kiểu chuyển cảnh phải ghi bằng chữ camelCase.");
        Check.True(first.Contains("\"preset\": \"petals\""), "Particle phải ghi bằng chữ camelCase.");
        Check.True(first.Contains("\"intensity\": 0.6"), "Cường độ ghi gọn, không ra 0.60000002.");

        var loaded = ProjectIo.Deserialize<SceneData>(first, "s");
        ProjectIo.SaveScene(fs, loaded);
        Check.Equal(first, fs.ReadText(ProjectPaths.Scene("s")));
    }
}
