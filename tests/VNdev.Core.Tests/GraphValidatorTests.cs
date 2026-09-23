using VNdev.Core.Model;
using VNdev.Core.Validation;

namespace VNdev.Core.Tests;

public class GraphValidatorTests
{
    private static readonly VariableDef Affection = new()
    {
        Id = "affection",
        Type = VariableType.Number,
        Initial = VarValue.Of(0d),
    };

    private static StoryGraph GraphOf(string entry, params StoryNode[] nodes) => new()
    {
        Id = "chapter_1",
        Title = new Localized("vi", "Chương thử"),
        Entry = entry,
        Nodes = nodes.ToList(),
    };

    private static EndingNode Ending(string id = "end") => new()
    {
        Id = id,
        Name = new Localized("vi", "Hết"),
    };

    private static IReadOnlyCollection<VariableDef> NoVars => Array.Empty<VariableDef>();

    [Test]
    public void Do_thi_hop_le_thi_khong_bao_loi()
    {
        var graph = GraphOf("a",
            new SceneNode { Id = "a", Scene = "scene_a", Next = "end" },
            Ending());

        Check.Empty(GraphValidator.Validate(graph, NoVars));
    }

    [Test]
    public void Phat_hien_node_mo_coi()
    {
        var graph = GraphOf("a",
            new SceneNode { Id = "a", Scene = "scene_a", Next = "end" },
            new SceneNode { Id = "lonely", Scene = "scene_b", Next = "end" },
            Ending());

        var issues = GraphValidator.Validate(graph, NoVars);
        var orphan = Check.Single(issues, i => i.Code == IssueCode.OrphanNode);
        Check.Equal("lonely", orphan.NodeId);
    }

    [Test]
    public void Node_diem_vao_khong_bi_coi_la_mo_coi()
    {
        var graph = GraphOf("start",
            new SceneNode { Id = "start", Scene = "scene_a", Next = "end" },
            Ending());

        Check.DoesNotContain(GraphValidator.Validate(graph, NoVars), i => i.Code == IssueCode.OrphanNode);
    }

    [Test]
    public void Phat_hien_nhanh_cut_khong_dan_toi_ket_thuc()
    {
        var choice = new ChoiceNode
        {
            Id = "choice",
            Options =
            {
                new ChoiceOption { Id = "o1", Text = new Localized("vi", "Đi tiếp"), Next = "end" },
                new ChoiceOption { Id = "o2", Text = new Localized("vi", "Rẽ ngang"), Next = "stuck" },
            },
        };

        var graph = GraphOf("choice",
            choice,
            new SceneNode { Id = "stuck", Scene = "scene_x", Next = "stuck" },
            Ending());

        var deadEnds = GraphValidator.Validate(graph, NoVars)
            .Where(i => i.Code == IssueCode.DeadEnd)
            .Select(i => i.NodeId)
            .ToList();

        Check.Contains("stuck", deadEnds);
        Check.DoesNotContain("choice", deadEnds);
    }

    [Test]
    public void Phat_hien_cong_ra_chua_noi()
    {
        var graph = GraphOf("a",
            new SceneNode { Id = "a", Scene = "scene_a", Next = null },
            Ending());

        Check.Contains(GraphValidator.Validate(graph, NoVars), i => i.Code == IssueCode.UnconnectedPort);
    }

    [Test]
    public void Phat_hien_lien_ket_toi_node_khong_ton_tai()
    {
        var graph = GraphOf("a",
            new SceneNode { Id = "a", Scene = "scene_a", Next = "khong_co" },
            Ending());

        var issue = Check.Single(GraphValidator.Validate(graph, NoVars), i => i.Code == IssueCode.MissingTarget);
        Check.Equal(IssueSeverity.Error, issue.Severity);
    }

    [Test]
    public void Phat_hien_bien_chua_khai_bao_trong_dieu_kien()
    {
        var graph = GraphOf("cond",
            new ConditionNode
            {
                Id = "cond",
                Condition = new CompareCondition
                {
                    Variable = "chua_khai_bao",
                    Op = ComparisonOp.Gte,
                    Value = VarValue.Of(50d),
                },
                WhenTrue = "end",
                WhenFalse = "end",
            },
            Ending());

        var issue = Check.Single(
            GraphValidator.Validate(graph, new[] { Affection }),
            i => i.Code == IssueCode.UndeclaredVariable);

        Check.Equal("chua_khai_bao", issue.Field);
    }

    [Test]
    public void Chap_nhan_bien_da_khai_bao_ke_ca_long_trong_and_or()
    {
        var graph = GraphOf("cond",
            new ConditionNode
            {
                Id = "cond",
                Condition = new AndCondition
                {
                    Operands =
                    {
                        new CompareCondition { Variable = "affection", Op = ComparisonOp.Gte, Value = VarValue.Of(50d) },
                        new NotCondition
                        {
                            Operand = new CompareCondition
                            {
                                Variable = "affection",
                                Op = ComparisonOp.Eq,
                                Value = VarValue.Of(99d),
                            },
                        },
                    },
                },
                WhenTrue = "end",
                WhenFalse = "end",
            },
            Ending());

        Check.DoesNotContain(
            GraphValidator.Validate(graph, new[] { Affection }),
            i => i.Code == IssueCode.UndeclaredVariable);
    }

    [Test]
    public void Phat_hien_bien_chua_khai_bao_trong_hieu_ung_cua_lua_chon()
    {
        var graph = GraphOf("choice",
            new ChoiceNode
            {
                Id = "choice",
                Options =
                {
                    new ChoiceOption
                    {
                        Id = "o1",
                        Text = new Localized("vi", "Chào hỏi"),
                        Effects = { new Effect { Variable = "bien_la", Op = AssignOp.Add, Value = VarValue.Of(10d) } },
                        Next = "end",
                    },
                },
            },
            Ending());

        Check.Contains(GraphValidator.Validate(graph, NoVars), i => i.Code == IssueCode.UndeclaredVariable);
    }

    [Test]
    public void Phat_hien_diem_vao_khong_ton_tai()
    {
        var graph = GraphOf("khong_ton_tai", Ending());
        Check.Contains(GraphValidator.Validate(graph, NoVars), i => i.Code == IssueCode.MissingEntry);
    }

    [Test]
    public void Phat_hien_id_trung()
    {
        var graph = GraphOf("a",
            new SceneNode { Id = "a", Scene = "scene_a", Next = "end" },
            new SceneNode { Id = "a", Scene = "scene_b", Next = "end" },
            Ending());

        Check.Contains(GraphValidator.Validate(graph, NoVars), i => i.Code == IssueCode.DuplicateId);
    }

    [Test]
    public void Canh_bao_khi_chuong_khong_co_ket_thuc()
    {
        var graph = GraphOf("a", new SceneNode { Id = "a", Scene = "scene_a", Next = "a" });
        Check.Contains(GraphValidator.Validate(graph, NoVars), i => i.Code == IssueCode.NoEnding);
    }

    [Test]
    public void Bo_qua_node_ghi_chu_khi_kiem_tra_mo_coi_va_nhanh_cut()
    {
        var graph = GraphOf("a",
            new SceneNode { Id = "a", Scene = "scene_a", Next = "end" },
            new CommentNode { Id = "note", Text = "Nhớ sửa đoạn này" },
            Ending());

        Check.DoesNotContain(GraphValidator.Validate(graph, NoVars), i => i.NodeId == "note");
    }

    [Test]
    public void Phat_hien_bieu_cam_khong_ton_tai_trong_canh()
    {
        var character = new CharacterData
        {
            Id = "yuki",
            DisplayName = new Localized("vi", "Yuki"),
            Expressions = { new Expression { Id = "normal" } },
            DefaultExpression = "normal",
        };

        var scene = new SceneData
        {
            Id = "scene_a",
            Lines =
            {
                new DialogueLine { Id = "l1", Speaker = "yuki", Expression = "khong_co", Text = new Localized("vi", "A") },
            },
        };

        var graph = GraphOf("a",
            new SceneNode { Id = "a", Scene = "scene_a", Next = "end" },
            Ending());

        var issues = GraphValidator.Validate(
            graph, NoVars,
            new Dictionary<string, SceneData> { ["scene_a"] = scene },
            new Dictionary<string, CharacterData> { ["yuki"] = character });

        Check.Contains(issues, i => i.Code == IssueCode.MissingExpression);
    }
}
