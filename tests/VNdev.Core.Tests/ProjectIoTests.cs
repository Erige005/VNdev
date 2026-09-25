using VNdev.Core;
using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.Core.Tests;

public class ProjectIoTests
{
    [Test]
    public void Du_an_moi_co_san_mot_chuong_va_mot_ket_thuc()
    {
        var loaded = ProjectIo.CreateNew("Sakura no Kioku", "Nam", new[] { "vi" });

        var graph = Check.Single(loaded.Graphs.Values);
        var node = Check.Single(graph.Nodes);
        Check.IsType<EndingNode>(node);
        Check.Equal(node.Id, graph.Entry);
        Check.SequenceEqual(new[] { graph.Id }, loaded.Project.Graphs);
    }

    [Test]
    public void Sinh_id_da_bo_dau_tieng_viet()
    {
        var loaded = ProjectIo.CreateNew("Ký ức mùa hè", null, new[] { "vi" });
        Check.Equal("ky_uc_mua_he", loaded.Project.Id);
    }

    [Test]
    public void Dung_ngon_ngu_dau_tien_lam_ngon_ngu_chinh()
    {
        var loaded = ProjectIo.CreateNew("Test", null, new[] { "ja", "vi" });
        Check.Equal("Test", loaded.Project.Title["ja"]);
        Check.Equal("ja", loaded.Project.PrimaryLocale);
    }

    [Test]
    public void Nap_lai_dung_nhung_gi_da_ghi()
    {
        var fs = new MemoryFileSystem();
        ProjectIo.Scaffold(fs, "Sakura no Kioku", "Nam", new[] { "vi" });

        var loaded = ProjectIo.Load(fs);

        Check.Equal("Sakura no Kioku", loaded.Project.Title["vi"]);
        Check.Equal("Nam", loaded.Project.Author);
        Check.Single(loaded.Graphs);
        Check.Empty(loaded.Characters);
    }

    [Test]
    public void Tao_du_thu_muc_chuan()
    {
        var fs = new MemoryFileSystem();
        ProjectIo.Scaffold(fs, "Test", null, new[] { "vi" });

        Check.Contains("scenes", fs.Directories);
        Check.Contains("story", fs.Directories);
        Check.Contains("assets/backgrounds", fs.Directories);
        Check.Contains("assets/sprites", fs.Directories);
    }

    [Test]
    public void Giu_nguyen_du_lieu_qua_mot_vong_ghi_va_nap()
    {
        var fs = new MemoryFileSystem();
        var loaded = ProjectIo.Scaffold(fs, "Test", null, new[] { "vi", "ja" });

        var graph = loaded.Graphs.Values.First();
        graph.Nodes.Add(new ChoiceNode
        {
            Id = "choice_1",
            Position = new GraphPosition(120, 80),
            Label = "Bắt chuyện?",
            Prompt = new Localized("vi", "Bắt chuyện với cô ấy?"),
            Options =
            {
                new ChoiceOption
                {
                    Id = "opt_a",
                    Text = new Localized("vi", "Chào hỏi").Set("ja", "あいさつする"),
                    Effects = { new Effect { Variable = "affection", Op = AssignOp.Add, Value = VarValue.Of(10d) } },
                    Next = "ending_ket_thuc",
                },
            },
        });
        graph.Entry = "choice_1";

        loaded.Project.Variables.Add(new VariableDef
        {
            Id = "affection",
            Type = VariableType.Number,
            Initial = VarValue.Of(0d),
        });

        ProjectIo.SaveAll(fs, loaded);
        var reloaded = ProjectIo.Load(fs);

        var reloadedGraph = reloaded.Graphs.Values.First();
        var choice = Check.IsType<ChoiceNode>(reloadedGraph.Find("choice_1"));

        Check.Equal("Bắt chuyện với cô ấy?", choice.Prompt!["vi"]);
        Check.Equal("あいさつする", choice.Options[0].Text["ja"]);
        Check.Equal(AssignOp.Add, choice.Options[0].Effects[0].Op);
        Check.Equal(10d, choice.Options[0].Effects[0].Value!.Value.AsNumber());
        Check.Equal("ending_ket_thuc", choice.Options[0].Next);
        Check.Equal(120, choice.Position.X);
    }

    [Test]
    public void Giu_nguyen_dieu_kien_long_nhau_qua_json()
    {
        var fs = new MemoryFileSystem();
        var loaded = ProjectIo.Scaffold(fs, "Test", null, new[] { "vi" });

        loaded.Graphs.Values.First().Nodes.Add(new ConditionNode
        {
            Id = "cond_1",
            Condition = new AndCondition
            {
                Operands =
                {
                    new CompareCondition { Variable = "a", Op = ComparisonOp.Gte, Value = VarValue.Of(5d) },
                    new NotCondition
                    {
                        Operand = new CompareCondition
                        {
                            Variable = "b",
                            Op = ComparisonOp.Eq,
                            Value = VarValue.Of(true),
                        },
                    },
                },
            },
        });

        ProjectIo.SaveAll(fs, loaded);
        var reloaded = ProjectIo.Load(fs);

        var cond = Check.IsType<ConditionNode>(reloaded.Graphs.Values.First().Find("cond_1"));
        var and = Check.IsType<AndCondition>(cond.Condition);
        Check.Equal(2, and.Operands.Count);

        var not = Check.IsType<NotCondition>(and.Operands[1]);
        var inner = Check.IsType<CompareCondition>(not.Operand);
        Check.Equal("b", inner.Variable);
        Check.True(inner.Value.AsBoolean());
    }

    [Test]
    public void Bao_loi_ro_rang_khi_file_json_hong()
    {
        var fs = new MemoryFileSystem();
        fs.WriteText("project.json", "{ đây không phải json");

        var ex = Check.Throws<ProjectLoadException>(() => ProjectIo.Load(fs));
        Check.Contains("sai cấu trúc", ex.Message);
    }

    [Test]
    public void Json_giu_nguyen_chu_co_dau_khong_escape()
    {
        var json = ProjectIo.Serialize(new Localized("vi", "Ánh nắng đầu ngày"));
        Check.Contains("Ánh nắng đầu ngày", json);
        Check.DoesNotContain("\\u", json);
    }

    [Test]
    public void Serialize_cho_ket_qua_giong_het_nhau_qua_nhieu_lan_goi()
    {
        var value = new Localized("vi", "a").Set("ja", "b");
        Check.Equal(ProjectIo.Serialize(value), ProjectIo.Serialize(value));
    }

    [Test]
    public void Chan_duong_dan_di_ra_ngoai_thu_muc_du_an()
    {
        var root = Path.Combine(Path.GetTempPath(), "vndev_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fs = new DiskFileSystem(root);
            Check.Throws<InvalidOperationException>(() => fs.WriteText("../ngoai.txt", "x"));
            Check.Throws<InvalidOperationException>(() => fs.ReadText("scenes/../../etc/passwd"));
            Check.Throws<InvalidOperationException>(() => fs.DeleteFile("../ngoai.txt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class FileSystemTests
{
    [Test]
    public void Xoa_file_tren_dia_va_bo_qua_file_khong_ton_tai()
    {
        var root = Path.Combine(Path.GetTempPath(), "vndev_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fs = new DiskFileSystem(root);
            fs.WriteText("story/a.graph.json", "{}");
            fs.DeleteFile("story/a.graph.json");
            Check.False(fs.Exists("story/a.graph.json"));
            fs.DeleteFile("story/a.graph.json");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Xoa_file_trong_bo_nho()
    {
        var fs = new MemoryFileSystem();
        fs.WriteText("story/a.graph.json", "{}");
        fs.DeleteFile("story/a.graph.json");
        Check.False(fs.Exists("story/a.graph.json"));
    }
}

public class StageAtTests
{
    [Test]
    public void Nguoi_dang_noi_sang_len_nguoi_con_lai_mo_di()
    {
        var scene = new SceneData
        {
            Id = "s",
            Stage =
            {
                new StageActor { Character = "yuki", Expression = "normal", Anchor = StageAnchor.Left },
                new StageActor { Character = "mei", Expression = "normal", Anchor = StageAnchor.Right },
            },
            Lines =
            {
                new DialogueLine { Id = "l1", Speaker = "yuki", Text = new Localized("vi", "A") },
                new DialogueLine { Id = "l2", Speaker = "mei", Text = new Localized("vi", "B") },
            },
        };

        var atFirst = scene.StageAt(0).ToDictionary(a => a.Character);
        Check.Equal(1f, atFirst["yuki"].Dim);
        Check.Equal(StageAnchors.DimInactive, atFirst["mei"].Dim);

        var atSecond = scene.StageAt(1).ToDictionary(a => a.Character);
        Check.Equal(StageAnchors.DimInactive, atSecond["yuki"].Dim);
        Check.Equal(1f, atSecond["mei"].Dim);
    }

    [Test]
    public void Doi_bieu_cam_tai_dong_thoai_duoc_ap_dung()
    {
        var scene = new SceneData
        {
            Id = "s",
            Stage = { new StageActor { Character = "yuki", Expression = "normal" } },
            Lines =
            {
                new DialogueLine { Id = "l1", Speaker = "yuki", Expression = "happy", Text = new Localized("vi", "A") },
            },
        };

        Check.Equal("happy", scene.StageAt(0).Single().Expression);
    }

    [Test]
    public void Khong_lam_hong_trang_thai_goc_cua_canh()
    {
        var scene = new SceneData
        {
            Id = "s",
            Stage = { new StageActor { Character = "yuki", Expression = "normal" } },
            Lines =
            {
                new DialogueLine { Id = "l1", Speaker = "yuki", Expression = "happy", Text = new Localized("vi", "A") },
            },
        };

        scene.StageAt(0);
        Check.Equal("normal", scene.Stage[0].Expression);
        Check.Null(scene.Stage[0].Dim);
    }
}

public class IdsTests
{
    [Test]
    public void Slugify_bo_dau_va_chuan_hoa()
    {
        var cases = new (string Input, string Expected)[]
        {
            ("Ký ức mùa hè", "ky_uc_mua_he"),
            ("Sân trường buổi sáng", "san_truong_buoi_sang"),
            ("Đường phố", "duong_pho"),
            ("  nhiều   khoảng trắng  ", "nhieu_khoang_trang"),
            ("Chương 1: Gặp gỡ!", "chuong_1_gap_go"),
            ("", ""),
        };

        foreach (var (input, expected) in cases)
        {
            var actual = Ids.Slugify(input);
            Check.True(actual == expected, $"Slugify(\"{input}\") cho \"{actual}\", mong đợi \"{expected}\".");
        }
    }

    [Test]
    public void Make_tranh_trung_voi_id_da_co()
    {
        var taken = new HashSet<string> { "scene_canh" };
        var id = Ids.Make("scene", "cảnh", taken);
        Check.NotEqual("scene_canh", id);
        Check.StartsWith("scene_canh_", id);
    }

    [Test]
    public void Make_giu_nguyen_khi_chua_trung()
    {
        Check.Equal("scene_canh", Ids.Make("scene", "cảnh"));
    }
}
