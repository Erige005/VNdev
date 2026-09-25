using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using VNdev.Core;
using VNdev.Core.Io;
using VNdev.Core.Model;
using VNdev.Core.Validation;

namespace VNdev.App.Ai;

public enum DiffKind { Context, Removed, Added, Note }

public sealed record DiffLine(DiffKind Kind, string Text);

public enum ProposalState { Pending, Applied, Rejected, Failed }

/// <summary>
/// Một thay đổi AI muốn làm, chờ người dùng duyệt — đúng nguyên tắc "AI đề
/// xuất, người quyết" trong CLAUDE.md: AI không bao giờ tự ghi vào dự án.
/// </summary>
public sealed class Proposal
{
    public required string Title { get; init; }
    public string Summary { get; init; } = string.Empty;
    public required List<DiffLine> Diff { get; init; }

    /// <summary>Ghi thay đổi. Trả về null nếu thành công, hoặc lý do thất bại.</summary>
    public required Func<string?> Apply { get; init; }

    public ProposalState State { get; set; } = ProposalState.Pending;

    /// <summary>Áp dụng luôn không hỏi, vì người dùng bật "tự áp dụng".</summary>
    public bool AutoApplied { get; set; }
    public string? FailReason { get; set; }
}

/// <summary>Kết quả chạy một công cụ: hoặc có ngay, hoặc phải chờ người dùng duyệt.</summary>
public sealed record ToolOutcome(string? Result, bool IsError, Proposal? Proposal, string Activity);

/// <summary>
/// Các công cụ trợ lý dùng để đọc và đề xuất sửa dự án.
/// </summary>
/// <remarks>
/// Luôn đọc <see cref="ProjectSession.Loaded"/> tại thời điểm gọi thay vì giữ
/// bản sao: người dùng có thể hoàn tác hay sửa tay giữa hai lượt chat, và AI
/// phải thấy đúng trạng thái mới nhất.
///
/// Tên công cụ tiếng Anh vì mọi model đều được huấn luyện với tên dạng đó;
/// mô tả tiếng Việt để AI hiểu đúng ngữ cảnh truyện Việt.
/// </remarks>
public sealed partial class ProjectTools
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ProjectSession _session;

    public ProjectTools(ProjectSession session) { _session = session; }

    private LoadedProject L => _session.Loaded;
    private string Locale => L.Project.PrimaryLocale;

    public static readonly HashSet<string> WriteTools = new()
    {
        "propose_dialogue_edit", "propose_choice_edit", "propose_branch",
        "propose_variable", "propose_character_voice", "propose_story_premise",
    };

    // ================= Khai báo =================

    // Hồ sơ giọng và biến giờ đi qua update_character / manage_variable (xem
    // ProjectTools.Agent.cs) — bỏ hai công cụ cũ khỏi danh sách gửi AI để nó
    // không phân vân giữa hai cách làm cùng một việc.
    public static IReadOnlyList<ToolSpec> Specs { get; } = BuildSpecs()
        .Where(t => t.Name is not ("propose_variable" or "propose_character_voice"))
        .Concat(AgentSpecs())
        .ToList();

    private static List<ToolSpec> BuildSpecs()
    {
        static JsonObject Obj(JsonObject props, params string[] required) => new()
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
        };
        static JsonObject Str(string d) => new() { ["type"] = "string", ["description"] = d };
        static JsonObject Int(string d) => new() { ["type"] = "integer", ["description"] = d };

        var lineSchema = Obj(new JsonObject
        {
            ["speaker"] = Str("id nhân vật nói câu này; bỏ trống hoặc \"\" nếu là lời dẫn truyện"),
            ["expression"] = Str("id biểu cảm của nhân vật (tuỳ chọn, phải có trong danh sách biểu cảm)"),
            ["text"] = Str("nội dung câu thoại"),
        }, "text");

        var effectSchema = Obj(new JsonObject
        {
            ["variable"] = Str("id biến đã khai báo"),
            ["op"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("set", "add", "sub", "toggle") },
            ["value"] = new JsonObject { ["description"] = "giá trị (số, true/false hoặc chuỗi); bỏ trống với toggle" },
        }, "variable", "op");

        return new List<ToolSpec>
        {
            new("get_project_overview",
                "Xem tổng quan dự án: tiền đề truyện, các chương, nhân vật, biến, cảnh và số lỗi. Gọi đầu tiên khi chưa biết gì về dự án.",
                Obj(new JsonObject())),
            new("get_chapter",
                "Đọc đồ thị cốt truyện của một chương: mọi node (cảnh, lựa chọn, điều kiện, biến, nhảy, kết thúc), các nhánh nối với nhau thế nào, điểm bắt đầu, và lỗi đồ thị.",
                Obj(new JsonObject { ["chapter_id"] = Str("id chương") }, "chapter_id")),
            new("get_scene",
                "Đọc toàn bộ lời thoại một cảnh, có số thứ tự từng dòng (index bắt đầu từ 0), người nói và biểu cảm.",
                Obj(new JsonObject { ["scene_id"] = Str("id cảnh") }, "scene_id")),
            new("get_character",
                "Đọc hồ sơ một nhân vật: tên, biểu cảm có sẵn, hồ sơ giọng (tính cách, cách xưng hô, cách nói).",
                Obj(new JsonObject { ["character_id"] = Str("id nhân vật") }, "character_id")),

            new("propose_dialogue_edit",
                "Đề xuất sửa lời thoại một cảnh: thay, chèn hoặc xoá từng dòng. Người dùng sẽ xem trước/sau rồi mới quyết định áp dụng. Index theo đúng số thứ tự get_scene trả về (trước khi sửa).",
                Obj(new JsonObject
                {
                    ["scene_id"] = Str("id cảnh"),
                    ["summary"] = Str("một câu tóm tắt vì sao sửa, hiện cho người dùng"),
                    ["edits"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = Obj(new JsonObject
                        {
                            ["op"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("replace", "insert_after", "delete") },
                            ["index"] = Int("dòng bị thay/xoá, hoặc dòng đứng trước chỗ chèn (-1 để chèn lên đầu)"),
                            ["speaker"] = Str("id nhân vật hoặc \"\" cho lời dẫn; bỏ trống khi replace để giữ người nói cũ"),
                            ["expression"] = Str("id biểu cảm (tuỳ chọn)"),
                            ["text"] = Str("nội dung mới (bắt buộc với replace và insert_after)"),
                        }, "op", "index"),
                    },
                }, "scene_id", "summary", "edits")),
            new("propose_choice_edit",
                "Đề xuất sửa câu hỏi và chữ của các phương án trong một node Lựa chọn có sẵn, hoặc thêm phương án mới (phương án mới chưa nối tới đâu).",
                Obj(new JsonObject
                {
                    ["chapter_id"] = Str("id chương"),
                    ["node_id"] = Str("id node lựa chọn"),
                    ["summary"] = Str("một câu tóm tắt vì sao sửa"),
                    ["prompt"] = Str("câu hỏi mới (bỏ trống để giữ nguyên)"),
                    ["options"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = Obj(new JsonObject
                        {
                            ["option_id"] = Str("id phương án có sẵn cần sửa; bỏ trống để thêm phương án mới"),
                            ["text"] = Str("chữ của phương án"),
                        }, "text"),
                    },
                }, "chapter_id", "node_id", "summary")),
            new("propose_branch",
                "Đề xuất rẽ nhánh: chèn một node Lựa chọn ngay sau một node có sẵn; mỗi phương án dẫn tới một cảnh mới (kèm lời thoại mở đầu) rồi nhập lại vào mạch truyện. Dùng khi người dùng muốn thêm lựa chọn cho người chơi.",
                Obj(new JsonObject
                {
                    ["chapter_id"] = Str("id chương"),
                    ["from_node_id"] = Str("node mà sau nó sẽ rẽ nhánh"),
                    ["from_option_id"] = Str("nếu from_node là Lựa chọn: id phương án sẽ dẫn vào nhánh mới"),
                    ["summary"] = Str("một câu tóm tắt ý tưởng nhánh"),
                    ["prompt"] = Str("câu hỏi hiện cho người chơi"),
                    ["options"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 2,
                        ["items"] = Obj(new JsonObject
                        {
                            ["text"] = Str("chữ phương án"),
                            ["effects"] = new JsonObject { ["type"] = "array", ["items"] = effectSchema },
                            ["lines"] = new JsonObject { ["type"] = "array", ["items"] = lineSchema, ["description"] = "lời thoại của cảnh mới cho nhánh này" },
                        }, "text"),
                    },
                    ["rejoin_node_id"] = Str("node mà các nhánh nhập lại; bỏ trống để nhập vào đúng node vốn đứng sau from_node"),
                }, "chapter_id", "from_node_id", "summary", "prompt", "options")),
            new("propose_variable",
                "Đề xuất khai báo một biến mới (điểm thiện cảm, cờ sự kiện…) để dùng trong lựa chọn và điều kiện.",
                Obj(new JsonObject
                {
                    ["id"] = Str("id biến, chữ thường không dấu, gạch dưới"),
                    ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("number", "boolean", "text") },
                    ["initial"] = new JsonObject { ["description"] = "giá trị ban đầu" },
                    ["description"] = Str("biến này dùng để làm gì"),
                }, "id", "type")),
            new("propose_character_voice",
                "Đề xuất viết hoặc sửa hồ sơ giọng của nhân vật: tính cách, cách xưng hô với từng người, từ cửa miệng, nhịp câu. Hồ sơ này được đọc mỗi khi viết thoại cho nhân vật.",
                Obj(new JsonObject
                {
                    ["character_id"] = Str("id nhân vật"),
                    ["voice_profile"] = Str("hồ sơ giọng mới, đầy đủ"),
                }, "character_id", "voice_profile")),
            new("propose_story_premise",
                "Đề xuất viết hoặc sửa tiền đề câu chuyện của dự án (bối cảnh, xung đột chính, giọng văn chung).",
                Obj(new JsonObject { ["premise"] = Str("tiền đề mới, đầy đủ") }, "premise")),
        };
    }

    // ================= Chạy =================

    public ToolOutcome Execute(ToolCall call)
    {
        var a = call.Arguments;
        if (a.ContainsKey("__invalid_json"))
        {
            return Error("Tham số không phải JSON hợp lệ (có thể bị cắt giữa chừng). Gửi lại lời gọi với tham số đầy đủ.", $"⚠ Lời gọi {call.Name} bị lỗi tham số");
        }

        try
        {
            return call.Name switch
            {
                "get_project_overview" => Ok(Overview(), "📖 Đọc tổng quan dự án"),
                "get_chapter" => ReadChapter(Arg(a, "chapter_id")),
                "get_scene" => ReadScene(Arg(a, "scene_id")),
                "get_character" => ReadCharacter(Arg(a, "character_id")),
                "propose_dialogue_edit" => ProposeDialogue(a),
                "propose_choice_edit" => ProposeChoice(a),
                "propose_branch" => ProposeBranch(a),
                "propose_variable" => ProposeVariable(a),
                "propose_character_voice" => ProposeVoice(a),
                "propose_story_premise" => ProposePremise(a),
                _ when ExecuteAgent(call.Name, a) is { } agent => agent,
                _ => Error($"Không có công cụ \"{call.Name}\".", $"⚠ Công cụ lạ: {call.Name}"),
            };
        }
        catch (ToolInputException ex)
        {
            return Error(ex.Message, $"⚠ {call.Name}: {ex.Message}");
        }
    }

    private static ToolOutcome Ok(string result, string activity) => new(result, false, null, activity);
    private static ToolOutcome Error(string message, string activity) => new(message, true, null, activity);
    private static ToolOutcome Propose(Proposal p) => new(null, false, p, $"✎ Đề xuất: {p.Title}");

    private sealed class ToolInputException : Exception
    {
        public ToolInputException(string message) : base(message) { }
    }

    private static string Arg(JsonObject a, string name)
        => a[name]?.GetValue<string>() is { Length: > 0 } v ? v : throw new ToolInputException($"Thiếu tham số \"{name}\".");

    private static string? OptArg(JsonObject a, string name)
        => a[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private string Json(object value) => JsonSerializer.Serialize(value, Compact);

    // ================= Đọc =================

    private string Overview()
    {
        var p = L.Project;
        var sceneUse = SceneUsage();
        return Json(new
        {
            title = p.Title[Locale],
            premise = p.Description?[Locale],
            author = p.Author,
            language = Locale,
            chapters = p.Graphs.Select((id, i) =>
            {
                var g = L.Graphs[id];
                var issues = Validate(g);
                return new
                {
                    id,
                    title = g.Title[Locale],
                    order = i + 1,
                    nodes = g.Nodes.Count,
                    entry = g.Entry,
                    errors = issues.Count(x => x.Severity == IssueSeverity.Error),
                    warnings = issues.Count(x => x.Severity == IssueSeverity.Warning),
                };
            }),
            characters = L.Characters.Values.Select(c => new
            {
                id = c.Id,
                name = c.DisplayName[Locale],
                hasVoiceProfile = !string.IsNullOrWhiteSpace(c.VoiceProfile),
            }),
            variables = p.Variables.Select(v => new { id = v.Id, type = v.Type.ToString().ToLowerInvariant(), initial = v.Initial.ToString(), description = v.Description }),
            scenes = L.Scenes.Values.Select(s => new { id = s.Id, lines = s.Lines.Count, usedIn = sceneUse.GetValueOrDefault(s.Id) }),
            note = "Chương đầu tiên là nơi game bắt đầu. Mỗi chương bắt đầu ở node 'entry'.",
        });
    }

    private Dictionary<string, List<string>> SceneUsage()
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var g in L.Graphs.Values)
        {
            foreach (var n in g.Nodes.OfType<SceneNode>())
            {
                if (!map.TryGetValue(n.Scene, out var list)) map[n.Scene] = list = new();
                list.Add($"{g.Id}/{n.Id}");
            }
        }
        return map;
    }

    private List<ValidationIssue> Validate(StoryGraph g)
        => GraphValidator.Validate(g, L.Project.Variables, L.Scenes, L.Characters);

    private ToolOutcome ReadChapter(string id)
    {
        var g = FindGraph(id);
        var nodes = g.Nodes.Select(n => (object)(n switch
        {
            SceneNode s => new { id = n.Id, type = "scene", label = n.Label, scene = s.Scene, next = s.Next },
            ChoiceNode c => new
            {
                id = n.Id, type = "choice", label = n.Label, prompt = c.Prompt?[Locale],
                options = c.Options.Select(o => new
                {
                    id = o.Id, text = o.Text[Locale], next = o.Next,
                    effects = o.Effects.Count == 0 ? null : o.Effects.Select(EffectText).ToList(),
                    showIf = o.ShowIf is null ? null : ConditionText(o.ShowIf),
                }),
            },
            ConditionNode c => new { id = n.Id, type = "condition", label = n.Label, condition = ConditionText(c.Condition), whenTrue = c.WhenTrue, whenFalse = c.WhenFalse },
            VariableNode v => new { id = n.Id, type = "variable", label = n.Label, effects = v.Effects.Select(EffectText), next = v.Next },
            JumpNode j => new { id = n.Id, type = "jump", label = n.Label, target = j.Target },
            EndingNode e => new { id = n.Id, type = "ending", label = n.Label, name = e.Name[Locale] },
            CommentNode c => new { id = n.Id, type = "comment", text = c.Text },
            _ => new { id = n.Id, type = "unknown" },
        })).ToList();

        return Ok(Json(new
        {
            id = g.Id,
            title = g.Title[Locale],
            entry = g.Entry,
            nodes,
            issues = Validate(g).Select(i => $"[{i.Severity}] {i.Message}"),
        }), $"📖 Đọc chương \"{g.Title[Locale] ?? g.Id}\"");
    }

    private ToolOutcome ReadScene(string id)
    {
        var s = FindScene(id);
        return Ok(Json(new
        {
            id = s.Id,
            background = s.Background,
            onStageAtStart = s.Stage.Select(a => $"{a.Character} ({a.Expression})"),
            usedIn = SceneUsage().GetValueOrDefault(s.Id),
            lines = s.Lines.Select((l, i) => new
            {
                index = i,
                speaker = l.Speaker is null ? "(dẫn truyện)" : $"{l.Speaker} — {SpeakerName(l.Speaker)}",
                expression = l.Expression,
                text = l.Text[Locale],
            }),
        }), $"📖 Đọc cảnh \"{s.Id}\" ({s.Lines.Count} dòng)");
    }

    private ToolOutcome ReadCharacter(string id)
    {
        var c = FindCharacter(id);
        return Ok(Json(new
        {
            id = c.Id,
            name = c.DisplayName[Locale],
            expressions = c.Expressions.Select(e => e.Id),
            defaultExpression = c.DefaultExpression,
            voiceProfile = c.VoiceProfile,
        }), $"📖 Đọc nhân vật \"{c.DisplayName[Locale] ?? c.Id}\"");
    }

    private string SpeakerName(string? id)
        => id is null ? "Dẫn truyện" : L.Characters.TryGetValue(id, out var c) ? c.DisplayName[Locale] ?? id : id;

    private static string EffectText(Effect e) => e.Op switch
    {
        AssignOp.Set => $"{e.Variable} = {e.Value}",
        AssignOp.Add => $"{e.Variable} += {e.Value}",
        AssignOp.Sub => $"{e.Variable} -= {e.Value}",
        AssignOp.Mul => $"{e.Variable} *= {e.Value}",
        AssignOp.Div => $"{e.Variable} /= {e.Value}",
        AssignOp.Toggle => $"đảo {e.Variable}",
        _ => e.Variable,
    };

    private static string ConditionText(Condition c) => c switch
    {
        CompareCondition cmp => $"{cmp.Variable} {cmp.Op switch { ComparisonOp.Eq => "==", ComparisonOp.Neq => "!=", ComparisonOp.Gt => ">", ComparisonOp.Gte => ">=", ComparisonOp.Lt => "<", _ => "<=" }} {cmp.Value}",
        AndCondition and => "(" + string.Join(" và ", and.Operands.Select(ConditionText)) + ")",
        OrCondition or => "(" + string.Join(" hoặc ", or.Operands.Select(ConditionText)) + ")",
        NotCondition not => "không " + ConditionText(not.Operand),
        _ => "?",
    };

    private StoryGraph FindGraph(string id)
        => L.Graphs.TryGetValue(id, out var g) ? g : throw new ToolInputException($"Không có chương \"{id}\". Các chương: {string.Join(", ", L.Project.Graphs)}.");

    private SceneData FindScene(string id)
        => L.Scenes.TryGetValue(id, out var s) ? s : throw new ToolInputException($"Không có cảnh \"{id}\". Các cảnh: {string.Join(", ", L.Scenes.Keys)}.");

    private CharacterData FindCharacter(string id)
        => L.Characters.TryGetValue(id, out var c) ? c : throw new ToolInputException($"Không có nhân vật \"{id}\". Các nhân vật: {string.Join(", ", L.Characters.Keys)}.");

    private string? CheckSpeaker(string? speaker, string? expression)
    {
        if (string.IsNullOrEmpty(speaker)) return null;
        var c = FindCharacter(speaker);
        if (!string.IsNullOrEmpty(expression) && c.FindExpression(expression) is null)
        {
            throw new ToolInputException($"Nhân vật \"{speaker}\" không có biểu cảm \"{expression}\". Có: {string.Join(", ", c.Expressions.Select(e => e.Id))}.");
        }
        return speaker;
    }

    private string LineText(string? speaker, string? text) => $"[{SpeakerName(speaker)}] {text}";

    // ================= Đề xuất: lời thoại =================

    private ToolOutcome ProposeDialogue(JsonObject a)
    {
        var sceneId = Arg(a, "scene_id");
        var scene = FindScene(sceneId);
        var edits = (a["edits"] as JsonArray ?? throw new ToolInputException("Thiếu danh sách \"edits\"."))
            .Select(e => e as JsonObject ?? throw new ToolInputException("Mỗi phần tử của edits phải là object."))
            .Select(e => (
                Op: e["op"]?.GetValue<string>() ?? throw new ToolInputException("Thiếu \"op\"."),
                Index: e["index"]?.GetValue<int>() ?? throw new ToolInputException("Thiếu \"index\"."),
                Speaker: e.ContainsKey("speaker") ? (e["speaker"]?.GetValue<string>() ?? "") : null,
                Expression: OptArg(e, "expression"),
                Text: OptArg(e, "text")))
            .ToList();
        if (edits.Count == 0) throw new ToolInputException("Danh sách edits rỗng.");

        // Chụp lại nội dung lúc đề xuất để lúc áp dụng biết người dùng đã sửa
        // cảnh này chưa — áp theo số dòng lên một cảnh đã đổi sẽ sửa nhầm câu.
        var snapshot = scene.Lines.Select(l => l.Text[Locale] ?? "").ToList();

        foreach (var e in edits)
        {
            var max = e.Op == "insert_after" ? scene.Lines.Count - 1 : scene.Lines.Count - 1;
            var min = e.Op == "insert_after" ? -1 : 0;
            if (e.Index < min || e.Index > max) throw new ToolInputException($"Dòng {e.Index} không có trong cảnh (cảnh có {scene.Lines.Count} dòng, index 0–{scene.Lines.Count - 1}).");
            if (e.Op is "replace" or "insert_after" && e.Text is null) throw new ToolInputException($"Thao tác {e.Op} ở dòng {e.Index} thiếu \"text\".");
            if (e.Op is not ("replace" or "insert_after" or "delete")) throw new ToolInputException($"Thao tác \"{e.Op}\" không hợp lệ.");
            CheckSpeaker(e.Speaker is { Length: > 0 } sp ? sp : (e.Op == "replace" ? scene.Lines[e.Index].Speaker : null), e.Expression);
        }

        var diff = new List<DiffLine>();
        var byIndex = edits.GroupBy(e => e.Index).ToDictionary(g => g.Key, g => g.ToList());
        void AddInserts(int index)
        {
            if (!byIndex.TryGetValue(index, out var list)) return;
            foreach (var e in list.Where(x => x.Op == "insert_after")) diff.Add(new(DiffKind.Added, LineText(NullIfEmpty(e.Speaker), e.Text)));
        }
        AddInserts(-1);
        for (var i = 0; i < scene.Lines.Count; i++)
        {
            var line = scene.Lines[i];
            var touched = byIndex.TryGetValue(i, out var list) && list.Any(x => x.Op != "insert_after");
            var near = byIndex.Keys.Any(k => Math.Abs(k - i) <= 1);
            if (touched)
            {
                foreach (var e in list!.Where(x => x.Op != "insert_after"))
                {
                    diff.Add(new(DiffKind.Removed, LineText(line.Speaker, line.Text[Locale])));
                    if (e.Op == "replace")
                    {
                        var speaker = e.Speaker is null ? line.Speaker : NullIfEmpty(e.Speaker);
                        diff.Add(new(DiffKind.Added, LineText(speaker, e.Text)));
                    }
                }
            }
            else if (near)
            {
                diff.Add(new(DiffKind.Context, LineText(line.Speaker, line.Text[Locale])));
            }
            AddInserts(i);
        }

        return Propose(new Proposal
        {
            Title = $"Sửa thoại cảnh \"{sceneId}\"",
            Summary = Arg(a, "summary"),
            Diff = diff,
            Apply = () =>
            {
                if (!L.Scenes.TryGetValue(sceneId, out var current)) return "Cảnh đã bị xoá.";
                if (!current.Lines.Select(l => l.Text[Locale] ?? "").SequenceEqual(snapshot))
                {
                    return "Cảnh đã thay đổi từ lúc đề xuất — nhờ trợ lý đọc lại cảnh rồi đề xuất lại.";
                }

                // Làm từ dưới lên để số dòng phía trên không bị lệch khi chèn/xoá.
                foreach (var e in edits.OrderByDescending(x => x.Index).ThenBy(x => x.Op == "insert_after" ? 0 : 1))
                {
                    switch (e.Op)
                    {
                        case "replace":
                            var line = current.Lines[e.Index];
                            line.Text[Locale] = e.Text!;
                            if (e.Speaker is not null) line.Speaker = NullIfEmpty(e.Speaker);
                            if (e.Expression is not null) line.Expression = NullIfEmpty(e.Expression);
                            break;
                        case "delete":
                            current.Lines.RemoveAt(e.Index);
                            break;
                        case "insert_after":
                            current.Lines.Insert(e.Index + 1, NewLine(current, NullIfEmpty(e.Speaker), NullIfEmpty(e.Expression), e.Text!));
                            break;
                    }
                }
                ProjectIo.SaveScene(_session.Fs, current);
                return null;
            },
        });
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private DialogueLine NewLine(SceneData scene, string? speaker, string? expression, string text)
    {
        var id = Ids.Make("line", $"dong {scene.Lines.Count + 1}", scene.Lines.Select(l => l.Id));
        return new DialogueLine { Id = id, Speaker = speaker, Expression = expression, Text = new Localized(Locale, text) };
    }

    // ================= Đề xuất: lựa chọn =================

    private ToolOutcome ProposeChoice(JsonObject a)
    {
        var graphId = Arg(a, "chapter_id");
        var nodeId = Arg(a, "node_id");
        var graph = FindGraph(graphId);
        var node = graph.Find(nodeId) as ChoiceNode ?? throw new ToolInputException($"\"{nodeId}\" không phải node Lựa chọn trong chương này.");
        var prompt = OptArg(a, "prompt");
        var options = (a["options"] as JsonArray ?? new JsonArray())
            .Select(o => (Id: OptArg((JsonObject)o!, "option_id"), Text: Arg((JsonObject)o!, "text")))
            .ToList();
        foreach (var o in options.Where(o => o.Id is { Length: > 0 }))
        {
            if (node.Options.All(x => x.Id != o.Id)) throw new ToolInputException($"Node không có phương án \"{o.Id}\". Có: {string.Join(", ", node.Options.Select(x => x.Id))}.");
        }

        var diff = new List<DiffLine>();
        if (prompt is not null)
        {
            diff.Add(new(DiffKind.Removed, $"Câu hỏi: {node.Prompt?[Locale]}"));
            diff.Add(new(DiffKind.Added, $"Câu hỏi: {prompt}"));
        }
        foreach (var existing in node.Options)
        {
            var change = options.FirstOrDefault(o => o.Id == existing.Id);
            if (change.Text is null) diff.Add(new(DiffKind.Context, $"• {existing.Text[Locale]}"));
            else
            {
                diff.Add(new(DiffKind.Removed, $"• {existing.Text[Locale]}"));
                diff.Add(new(DiffKind.Added, $"• {change.Text}"));
            }
        }
        foreach (var o in options.Where(o => string.IsNullOrEmpty(o.Id))) diff.Add(new(DiffKind.Added, $"• {o.Text}  (phương án mới, chưa nối)"));

        return Propose(new Proposal
        {
            Title = $"Sửa lựa chọn \"{node.DisplayName}\"",
            Summary = Arg(a, "summary"),
            Diff = diff,
            Apply = () =>
            {
                if (!L.Graphs.TryGetValue(graphId, out var g) || g.Find(nodeId) is not ChoiceNode current) return "Node lựa chọn đã bị xoá.";
                if (prompt is not null)
                {
                    current.Prompt ??= new Localized();
                    current.Prompt[Locale] = prompt;
                }
                foreach (var o in options)
                {
                    if (string.IsNullOrEmpty(o.Id))
                    {
                        var id = Ids.Make("opt", o.Text, current.Options.Select(x => x.Id));
                        current.Options.Add(new ChoiceOption { Id = id, Text = new Localized(Locale, o.Text) });
                    }
                    else if (current.Options.FirstOrDefault(x => x.Id == o.Id) is { } opt)
                    {
                        opt.Text[Locale] = o.Text;
                    }
                }
                ProjectIo.SaveGraph(_session.Fs, g);
                return null;
            },
        });
    }

    // ================= Đề xuất: rẽ nhánh =================

    private ToolOutcome ProposeBranch(JsonObject a)
    {
        var graphId = Arg(a, "chapter_id");
        var fromId = Arg(a, "from_node_id");
        var fromOption = OptArg(a, "from_option_id");
        var prompt = Arg(a, "prompt");
        var rejoin = OptArg(a, "rejoin_node_id");
        var graph = FindGraph(graphId);
        var from = graph.Find(fromId) ?? throw new ToolInputException($"Không có node \"{fromId}\" trong chương.");
        if (from is EndingNode or CommentNode) throw new ToolInputException("Không rẽ nhánh sau node Kết thúc hoặc Ghi chú được.");

        var handle = from is ChoiceNode choiceFrom
            ? (fromOption is { Length: > 0 } && choiceFrom.Options.Any(o => o.Id == fromOption)
                ? fromOption
                : throw new ToolInputException($"Node \"{fromId}\" là Lựa chọn — cần from_option_id là một trong: {string.Join(", ", choiceFrom.Options.Select(o => o.Id))}."))
            : from is ConditionNode
                ? (fromOption is "true" or "false" ? fromOption : throw new ToolInputException("Node Điều kiện — from_option_id phải là \"true\" hoặc \"false\"."))
                : "";
        var oldTarget = from.Outgoing().FirstOrDefault(o => (o.Handle ?? "") == handle).Target;
        var mergeTo = rejoin is { Length: > 0 } ? rejoin : oldTarget;
        if (mergeTo is not null && graph.Find(mergeTo) is null) throw new ToolInputException($"Không có node \"{mergeTo}\" để nhập nhánh.");

        var declared = L.Project.Variables.ToDictionary(v => v.Id);
        var options = (a["options"] as JsonArray ?? throw new ToolInputException("Thiếu \"options\"."))
            .Select(o => (JsonObject)o!)
            .Select(o => new
            {
                Text = Arg(o, "text"),
                Effects = (o["effects"] as JsonArray ?? new JsonArray()).Select(e => ParseEffect((JsonObject)e!, declared)).ToList(),
                Lines = (o["lines"] as JsonArray ?? new JsonArray()).Select(l => (JsonObject)l!)
                    .Select(l => (Speaker: CheckSpeaker(NullIfEmpty(OptArg(l, "speaker")), OptArg(l, "expression")), Expression: NullIfEmpty(OptArg(l, "expression")), Text: Arg(l, "text")))
                    .ToList(),
            })
            .ToList();
        if (options.Count < 2) throw new ToolInputException("Rẽ nhánh cần ít nhất 2 phương án.");

        var diff = new List<DiffLine>
        {
            new(DiffKind.Note, $"Sau \"{from.DisplayName}\"{(handle.Length > 0 && from is ChoiceNode ? $" (phương án {handle})" : "")} chèn node Lựa chọn:"),
            new(DiffKind.Added, $"◈ {prompt}"),
        };
        foreach (var o in options)
        {
            var fx = o.Effects.Count > 0 ? $"   [{string.Join(", ", o.Effects.Select(EffectText))}]" : "";
            diff.Add(new(DiffKind.Added, $"  • {o.Text}{fx}"));
            diff.Add(new(DiffKind.Added, $"      ▣ cảnh mới, {o.Lines.Count} dòng thoại"));
            foreach (var l in o.Lines.Take(3)) diff.Add(new(DiffKind.Added, $"        {LineText(l.Speaker, l.Text)}"));
            if (o.Lines.Count > 3) diff.Add(new(DiffKind.Added, $"        … thêm {o.Lines.Count - 3} dòng"));
        }
        diff.Add(new(DiffKind.Note, mergeTo is null
            ? "Các nhánh chưa nối tiếp đi đâu — bảng lỗi sẽ nhắc nối."
            : $"Các nhánh nhập lại vào \"{graph.Find(mergeTo)!.DisplayName}\"."));

        return Propose(new Proposal
        {
            Title = $"Rẽ nhánh sau \"{from.DisplayName}\"",
            Summary = Arg(a, "summary"),
            Diff = diff,
            Apply = () =>
            {
                if (!L.Graphs.TryGetValue(graphId, out var g) || g.Find(fromId) is not { } src) return "Node gốc đã bị xoá.";
                var taken = new HashSet<string>(g.Nodes.Select(n => n.Id));
                string NewId(string prefix, string label)
                {
                    var id = Ids.Make(prefix, label, taken);
                    taken.Add(id);
                    return id;
                }

                var choice = new ChoiceNode
                {
                    Id = NewId("choice", prompt),
                    Position = new GraphPosition(src.Position.X + 280, src.Position.Y),
                    Prompt = new Localized(Locale, prompt),
                    Label = prompt.Length > 28 ? prompt[..28] + "…" : prompt,
                };
                var added = new List<StoryNode> { choice };
                var sceneIds = new HashSet<string>(L.Scenes.Keys);

                for (var i = 0; i < options.Count; i++)
                {
                    var o = options[i];
                    var sceneId = Ids.Make("scene", o.Text, sceneIds);
                    sceneIds.Add(sceneId);
                    var scene = new SceneData { Id = sceneId };
                    foreach (var l in o.Lines) scene.Lines.Add(NewLine(scene, l.Speaker, l.Expression, l.Text));
                    L.Scenes[sceneId] = scene;
                    ProjectIo.SaveScene(_session.Fs, scene);

                    var sceneNode = new SceneNode
                    {
                        Id = NewId("scene", o.Text),
                        Scene = sceneId,
                        Label = o.Text.Length > 28 ? o.Text[..28] + "…" : o.Text,
                        Position = new GraphPosition(choice.Position.X + 300, choice.Position.Y + (i - (options.Count - 1) / 2f) * 150),
                        Next = mergeTo,
                    };
                    added.Add(sceneNode);

                    var optId = Ids.Make("opt", o.Text, choice.Options.Select(x => x.Id));
                    choice.Options.Add(new ChoiceOption { Id = optId, Text = new Localized(Locale, o.Text), Effects = o.Effects, Next = sceneNode.Id });
                }

                g.Nodes.AddRange(added);
                GraphSync.SetOutgoing(src, handle, choice.Id);
                ProjectIo.SaveGraph(_session.Fs, g);
                return null;
            },
        });
    }

    private static Effect ParseEffect(JsonObject e, Dictionary<string, VariableDef> declared)
    {
        var variable = Arg(e, "variable");
        if (!declared.TryGetValue(variable, out var def))
        {
            throw new ToolInputException($"Biến \"{variable}\" chưa khai báo. Dùng propose_variable trước, hoặc chọn trong: {string.Join(", ", declared.Keys)}.");
        }
        var op = (e["op"]?.GetValue<string>() ?? "add") switch
        {
            "set" => AssignOp.Set,
            "sub" => AssignOp.Sub,
            "toggle" => AssignOp.Toggle,
            _ => AssignOp.Add,
        };
        return new Effect { Variable = variable, Op = op, Value = op == AssignOp.Toggle ? null : ParseValue(e["value"], def.Type) };
    }

    private static VarValue ParseValue(JsonNode? node, VariableType type)
    {
        if (node is not JsonValue v) return VarValue.Default(type);
        if (v.TryGetValue<double>(out var d)) return VarValue.Of(d);
        if (v.TryGetValue<bool>(out var b)) return VarValue.Of(b);
        var s = v.ToString();
        return type switch
        {
            VariableType.Number => VarValue.Of(double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0),
            VariableType.Boolean => VarValue.Of(s is "true" or "1"),
            _ => VarValue.Of(s),
        };
    }

    // ================= Đề xuất: biến, nhân vật, tiền đề =================

    private ToolOutcome ProposeVariable(JsonObject a)
    {
        var id = Ids.Slugify(Arg(a, "id"));
        if (id.Length == 0) throw new ToolInputException("id biến không hợp lệ.");
        if (L.Project.Variables.Any(v => v.Id == id)) throw new ToolInputException($"Biến \"{id}\" đã có rồi.");
        var type = (OptArg(a, "type") ?? "number") switch { "boolean" => VariableType.Boolean, "text" => VariableType.Text, _ => VariableType.Number };
        var initial = ParseValue(a["initial"], type);
        var description = OptArg(a, "description");

        return Propose(new Proposal
        {
            Title = $"Khai báo biến \"{id}\"",
            Summary = description ?? "",
            Diff = new List<DiffLine> { new(DiffKind.Added, $"{id} : {type.ToString().ToLowerInvariant()} = {initial}{(description is null ? "" : $"   — {description}")}") },
            Apply = () =>
            {
                if (L.Project.Variables.Any(v => v.Id == id)) return $"Biến \"{id}\" đã có rồi.";
                L.Project.Variables.Add(new VariableDef { Id = id, Type = type, Initial = initial, Description = description });
                ProjectIo.SaveProject(_session.Fs, L.Project);
                return null;
            },
        });
    }

    private ToolOutcome ProposeVoice(JsonObject a)
    {
        var id = Arg(a, "character_id");
        var c = FindCharacter(id);
        var profile = Arg(a, "voice_profile");
        var diff = new List<DiffLine>();
        foreach (var line in (c.VoiceProfile ?? "").Split('\n').Where(l => l.Length > 0)) diff.Add(new(DiffKind.Removed, line));
        foreach (var line in profile.Split('\n').Where(l => l.Length > 0)) diff.Add(new(DiffKind.Added, line));

        return Propose(new Proposal
        {
            Title = $"Hồ sơ giọng của {c.DisplayName[Locale] ?? id}",
            Diff = diff,
            Apply = () =>
            {
                if (!L.Characters.TryGetValue(id, out var current)) return "Nhân vật đã bị xoá.";
                current.VoiceProfile = profile;
                ProjectIo.SaveCharacter(_session.Fs, current);
                return null;
            },
        });
    }

    private ToolOutcome ProposePremise(JsonObject a)
    {
        var premise = Arg(a, "premise");
        var diff = new List<DiffLine>();
        foreach (var line in (L.Project.Description?[Locale] ?? "").Split('\n').Where(l => l.Length > 0)) diff.Add(new(DiffKind.Removed, line));
        foreach (var line in premise.Split('\n').Where(l => l.Length > 0)) diff.Add(new(DiffKind.Added, line));

        return Propose(new Proposal
        {
            Title = "Tiền đề câu chuyện",
            Diff = diff,
            Apply = () =>
            {
                L.Project.Description ??= new Localized();
                L.Project.Description[Locale] = premise;
                ProjectIo.SaveProject(_session.Fs, L.Project);
                return null;
            },
        });
    }
}
