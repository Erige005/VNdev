using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using VNdev.Core;
using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.App.Ai;

/// <summary>
/// Phần công cụ cho trợ lý làm được mọi việc trong app — tạo nhân vật, chương,
/// cảnh, dựng và nối node, đặt nền, nhạc, sân khấu, quản lý biến — thay vì
/// chỉ sửa những thứ đã có rồi bắt người dùng tự tạo phần còn lại.
/// </summary>
/// <remarks>
/// Mọi công cụ ghi vẫn trả về <see cref="Proposal"/>: người dùng duyệt từng
/// cái, hoặc bật "tự áp dụng" trong Cài đặt. Kiểm tra tham số ngay lúc đề xuất
/// (AI sửa được ngay trong cùng lượt), và kiểm tra lại lúc áp dụng vì dự án có
/// thể đã đổi trong lúc người dùng đọc thẻ.
/// </remarks>
public sealed partial class ProjectTools
{
    private static readonly string[] AgentWriteTools =
    {
        "create_character", "update_character", "create_chapter", "update_chapter",
        "create_scene", "update_scene", "edit_graph", "manage_variable",
    };

    private static List<ToolSpec> AgentSpecs()
    {
        static JsonObject Obj(JsonObject props, params string[] required) => new()
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
        };
        static JsonObject Str(string d) => new() { ["type"] = "string", ["description"] = d };
        static JsonObject Num(string d) => new() { ["type"] = "number", ["description"] = d };
        static JsonObject Bool(string d) => new() { ["type"] = "boolean", ["description"] = d };
        // Sao ra bản mới mỗi lần: cùng một mẫu (biểu cảm, hiệu ứng biến…) dùng ở
        // nhiều công cụ, mà một JsonNode chỉ được có một cha.
        static JsonObject Arr(JsonObject items, string d) => new() { ["type"] = "array", ["items"] = items.DeepClone(), ["description"] = d };

        var expression = Obj(new JsonObject
        {
            ["id"] = Str("id biểu cảm, chữ thường không dấu (vd binh_thuong, cuoi, buon)"),
            ["sprite"] = Str("đường dẫn ảnh trong assets/sprites (lấy từ list_assets); bỏ trống nếu chưa có ảnh"),
        }, "id");
        var line = Obj(new JsonObject
        {
            ["speaker"] = Str("id nhân vật; bỏ trống là lời dẫn truyện"),
            ["expression"] = Str("id biểu cảm (tuỳ chọn)"),
            ["text"] = Str("nội dung"),
        }, "text");
        var actor = Obj(new JsonObject
        {
            ["character"] = Str("id nhân vật"),
            ["expression"] = Str("id biểu cảm"),
            ["anchor"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("farLeft", "left", "center", "right", "farRight") },
        }, "character");
        var effect = Obj(new JsonObject
        {
            ["variable"] = Str("id biến đã khai báo"),
            ["op"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("set", "add", "sub", "toggle") },
            ["value"] = new JsonObject { ["description"] = "số, true/false hoặc chuỗi; bỏ trống với toggle" },
        }, "variable", "op");
        var condition = Obj(new JsonObject
        {
            ["variable"] = Str("id biến"),
            ["op"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("==", "!=", ">", ">=", "<", "<=") },
            ["value"] = new JsonObject { ["description"] = "giá trị so sánh" },
        }, "variable", "op", "value");
        var option = Obj(new JsonObject
        {
            ["ref"] = Str("tên tạm để nối dây từ phương án này trong links (vd \"o1\")"),
            ["text"] = Str("chữ phương án"),
            ["effects"] = Arr(effect, "đổi biến khi chọn phương án này"),
        }, "text");
        var nodeFields = new JsonObject
        {
            ["label"] = Str("nhãn hiện trên node (tuỳ chọn)"),
            ["scene"] = Str("scene: id cảnh mà node trỏ tới (tạo bằng create_scene trước)"),
            ["prompt"] = Str("choice: câu hỏi hiện cho người chơi"),
            ["options"] = Arr(option, "choice: các phương án"),
            ["condition"] = condition.DeepClone(),
            ["effects"] = Arr(effect, "variable: các thay đổi biến"),
            ["name"] = Str("ending: tên kết thúc"),
            ["text"] = Str("comment: nội dung ghi chú"),
        };
        var addNode = new JsonObject(nodeFields.Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())))
        {
            ["ref"] = Str("tên tạm để nhắc tới node này trong links (vd \"n1\")"),
            ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("scene", "choice", "condition", "variable", "jump", "ending", "comment") },
        };
        var updateNode = new JsonObject(nodeFields.Select(kv => KeyValuePair.Create(kv.Key, kv.Value?.DeepClone())))
        {
            ["node_id"] = Str("id node có sẵn"),
        };

        return new List<ToolSpec>
        {
            new("list_assets",
                "Liệt kê file ảnh nền, sprite, nhạc nền, hiệu ứng âm thanh người dùng đã chép vào thư mục assets. Dùng trước khi gán ảnh cho biểu cảm, đặt nền hay nhạc cho cảnh — chỉ dùng được file có trong danh sách.",
                Obj(new JsonObject())),
            new("create_character",
                "Tạo nhân vật mới, kèm biểu cảm và hồ sơ giọng.",
                Obj(new JsonObject
                {
                    ["id"] = Str("id nhân vật, chữ thường không dấu (vd linh); bỏ trống để tự sinh từ tên"),
                    ["name"] = Str("tên hiển thị"),
                    ["color"] = Str("màu tên dạng #RRGGBB (tuỳ chọn)"),
                    ["expressions"] = Arr(expression, "biểu cảm; bỏ trống sẽ có sẵn một biểu cảm 'normal'"),
                    ["voice_profile"] = Str("hồ sơ giọng: tính cách, cách xưng hô với từng người, từ cửa miệng, nhịp câu"),
                }, "name")),
            new("update_character",
                "Sửa nhân vật có sẵn: tên, màu, hồ sơ giọng, thêm/bớt biểu cảm, gán ảnh cho biểu cảm, hoặc xoá hẳn nhân vật.",
                Obj(new JsonObject
                {
                    ["character_id"] = Str("id nhân vật"),
                    ["name"] = Str("tên mới"),
                    ["color"] = Str("màu mới #RRGGBB"),
                    ["voice_profile"] = Str("hồ sơ giọng mới, đầy đủ (thay hẳn bản cũ)"),
                    ["set_expressions"] = Arr(expression, "thêm biểu cảm mới, hoặc gán ảnh cho biểu cảm đã có cùng id"),
                    ["remove_expressions"] = Arr(Str("id biểu cảm"), "biểu cảm cần xoá"),
                    ["default_expression"] = Str("biểu cảm mặc định"),
                    ["delete"] = Bool("true để xoá hẳn nhân vật"),
                }, "character_id")),
            new("create_chapter",
                "Tạo chương mới (có sẵn một node Kết thúc). Sau đó dùng edit_graph để dựng nội dung.",
                Obj(new JsonObject { ["title"] = Str("tên chương") }, "title")),
            new("update_chapter",
                "Đổi tên, đổi thứ tự, hoặc xoá một chương. Chương đứng đầu là nơi game bắt đầu.",
                Obj(new JsonObject
                {
                    ["chapter_id"] = Str("id chương"),
                    ["title"] = Str("tên mới"),
                    ["position"] = Num("vị trí mới, bắt đầu từ 1"),
                    ["delete"] = Bool("true để xoá chương (cảnh không bị xoá)"),
                }, "chapter_id")),
            new("create_scene",
                "Tạo cảnh mới: nền, nhạc, nhân vật đứng sẵn trên sân khấu, và lời thoại. Cảnh chỉ xuất hiện trong truyện khi có node scene trỏ tới — dùng edit_graph để thêm node đó.",
                Obj(new JsonObject
                {
                    ["scene_id"] = Str("id cảnh, chữ thường không dấu (vd canh_toa_tau); bỏ trống để tự sinh"),
                    ["title"] = Str("vài chữ mô tả cảnh, dùng để sinh id nếu không đặt"),
                    ["background"] = Str("ảnh nền, đường dẫn từ list_assets"),
                    ["bgm"] = Str("nhạc nền, đường dẫn từ list_assets"),
                    ["stage"] = Arr(actor, "nhân vật có mặt từ đầu cảnh"),
                    ["lines"] = Arr(line, "lời thoại"),
                })),
            new("update_scene",
                "Đổi nền, nhạc nền hoặc nhân vật đứng sẵn trên sân khấu của một cảnh có sẵn. Sửa lời thoại thì dùng propose_dialogue_edit.",
                Obj(new JsonObject
                {
                    ["scene_id"] = Str("id cảnh"),
                    ["background"] = Str("ảnh nền mới; chuỗi rỗng để bỏ nền"),
                    ["bgm"] = Str("nhạc nền mới; chuỗi rỗng để tắt nhạc"),
                    ["stage"] = Arr(actor, "thay toàn bộ danh sách nhân vật đứng sẵn"),
                }, "scene_id")),
            new("edit_graph",
                "Dựng và sửa đồ thị cốt truyện của một chương trong một lần: thêm node, sửa node, xoá node, nối dây, đặt điểm bắt đầu. Node mới đặt 'ref' tạm để nối dây; trong links, from/to là ref hoặc id node có sẵn. Cổng ra (port): với node choice là ref hoặc id phương án; với node condition là \"true\"/\"false\"; node khác bỏ trống. to = null để gỡ dây.",
                Obj(new JsonObject
                {
                    ["chapter_id"] = Str("id chương"),
                    ["summary"] = Str("một câu tóm tắt thay đổi"),
                    ["add_nodes"] = Arr(Obj(addNode, "ref", "type"), "node mới"),
                    ["update_nodes"] = Arr(Obj(updateNode, "node_id"), "sửa node có sẵn (chỉ trường nào gửi mới đổi; options thay toàn bộ danh sách phương án)"),
                    ["delete_nodes"] = Arr(Str("id node"), "node cần xoá"),
                    ["links"] = Arr(Obj(new JsonObject
                    {
                        ["from"] = Str("ref hoặc id node nguồn"),
                        ["port"] = Str("cổng ra, xem mô tả công cụ"),
                        ["to"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["description"] = "ref hoặc id node đích; null để gỡ dây" },
                    }, "from"), "dây nối"),
                    ["entry"] = Str("ref hoặc id node làm điểm bắt đầu của chương"),
                }, "chapter_id", "summary")),
            new("manage_variable",
                "Khai báo, sửa hoặc xoá một biến (điểm thiện cảm, cờ sự kiện…).",
                Obj(new JsonObject
                {
                    ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("create", "update", "delete") },
                    ["id"] = Str("id biến, chữ thường không dấu"),
                    ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("number", "boolean", "text") },
                    ["initial"] = new JsonObject { ["description"] = "giá trị ban đầu" },
                    ["description"] = Str("biến dùng để làm gì"),
                }, "action", "id")),
        };
    }

    public static bool IsWriteTool(string name) => WriteTools.Contains(name) || AgentWriteTools.Contains(name);

    private ToolOutcome? ExecuteAgent(string name, JsonObject a) => name switch
    {
        "list_assets" => ListAssets(),
        "create_character" => CreateCharacter(a),
        "update_character" => UpdateCharacter(a),
        "create_chapter" => CreateChapter(a),
        "update_chapter" => UpdateChapter(a),
        "create_scene" => CreateScene(a),
        "update_scene" => UpdateScene(a),
        "edit_graph" => EditGraph(a),
        "manage_variable" => ManageVariable(a),
        _ => null,
    };

    // ================= Asset =================

    private ToolOutcome ListAssets()
    {
        var fs = _session.Fs;
        return Ok(Json(new
        {
            backgrounds = AssetIndex.Backgrounds(fs),
            sprites = AssetIndex.Sprites(fs),
            bgm = AssetIndex.Bgm(fs),
            sfx = AssetIndex.Sfx(fs),
            note = "Chỉ dùng đường dẫn trong danh sách này. Chưa có file nào thì người dùng phải tự chép ảnh/nhạc vào thư mục assets của dự án.",
        }), "📖 Xem thư viện asset");
    }

    private void CheckAsset(string? path, List<string> allowed, string kind)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!allowed.Contains(path))
        {
            throw new ToolInputException(allowed.Count == 0
                ? $"Dự án chưa có {kind} nào — người dùng cần chép file vào thư mục assets trước."
                : $"Không có {kind} \"{path}\". Có: {string.Join(", ", allowed.Take(30))}.");
        }
    }

    // ================= Nhân vật =================

    private static string Slug(string s) => Ids.Slugify(s);

    private List<VNdev.Core.Model.Expression> ParseExpressions(JsonArray? arr, List<string> sprites)
    {
        var list = new List<VNdev.Core.Model.Expression>();
        foreach (var node in arr ?? new JsonArray())
        {
            if (node is not JsonObject e) continue;
            var id = Slug(Arg(e, "id"));
            if (id.Length == 0) throw new ToolInputException("id biểu cảm không hợp lệ.");
            var sprite = NullIfEmpty(OptArg(e, "sprite"));
            CheckAsset(sprite, sprites, "sprite");
            if (list.All(x => x.Id != id)) list.Add(new VNdev.Core.Model.Expression { Id = id, Sprite = sprite });
        }
        return list;
    }

    private ToolOutcome CreateCharacter(JsonObject a)
    {
        var name = Arg(a, "name");
        var wanted = Slug(OptArg(a, "id") ?? name);
        if (wanted.Length == 0) wanted = "nhan_vat";
        if (L.Characters.ContainsKey(wanted)) throw new ToolInputException($"Đã có nhân vật id \"{wanted}\". Dùng update_character để sửa.");
        var color = OptArg(a, "color") is { } c && c.StartsWith('#') && c.Length == 7 ? c : "#ffffff";
        var expressions = ParseExpressions(a["expressions"] as JsonArray, AssetIndex.Sprites(_session.Fs));
        if (expressions.Count == 0) expressions.Add(new VNdev.Core.Model.Expression { Id = "normal" });
        var voice = NullIfEmpty(OptArg(a, "voice_profile"));

        var diff = new List<DiffLine>
        {
            new(DiffKind.Added, $"☺ {name}   (id: {wanted}, màu {color})"),
            new(DiffKind.Added, $"   Biểu cảm: {string.Join(", ", expressions.Select(e => e.Sprite is null ? e.Id : $"{e.Id} → {System.IO.Path.GetFileName(e.Sprite)}"))}"),
        };
        if (voice is not null) foreach (var l in voice.Split('\n').Where(l => l.Length > 0)) diff.Add(new(DiffKind.Added, $"   {l}"));

        return Propose(new Proposal
        {
            Title = $"Tạo nhân vật {name}",
            Diff = diff,
            Apply = () =>
            {
                if (L.Characters.ContainsKey(wanted)) return $"Đã có nhân vật \"{wanted}\".";
                var character = new CharacterData
                {
                    Id = wanted,
                    DisplayName = new Localized(Locale, name),
                    Color = color,
                    Expressions = expressions,
                    DefaultExpression = expressions[0].Id,
                    VoiceProfile = voice,
                };
                L.Project.Characters.Add(wanted);
                L.Characters[wanted] = character;
                ProjectIo.SaveProject(_session.Fs, L.Project);
                ProjectIo.SaveCharacter(_session.Fs, character);
                return null;
            },
        });
    }

    private ToolOutcome UpdateCharacter(JsonObject a)
    {
        var id = Arg(a, "character_id");
        var c = FindCharacter(id);
        var displayName = c.DisplayName[Locale] ?? id;

        if (a["delete"]?.GetValue<bool>() == true)
        {
            var uses = L.Scenes.Values.Count(s => s.Lines.Any(l => l.Speaker == id) || s.Stage.Any(x => x.Character == id));
            return Propose(new Proposal
            {
                Title = $"Xoá nhân vật {displayName}",
                Summary = uses > 0 ? $"Nhân vật đang được dùng trong {uses} cảnh — các dòng thoại đó sẽ báo lỗi thiếu nhân vật." : "",
                Diff = new List<DiffLine> { new(DiffKind.Removed, $"☺ {displayName} (id: {id})") },
                Apply = () =>
                {
                    if (!L.Characters.Remove(id)) return "Nhân vật đã bị xoá.";
                    L.Project.Characters.Remove(id);
                    ProjectIo.SaveProject(_session.Fs, L.Project);
                    _session.Fs.DeleteFile(ProjectPaths.Character(id));
                    return null;
                },
            });
        }

        var name = OptArg(a, "name");
        var color = OptArg(a, "color");
        if (color is not null && (!color.StartsWith('#') || color.Length != 7)) throw new ToolInputException("Màu phải có dạng #RRGGBB.");
        var voice = a.ContainsKey("voice_profile") ? OptArg(a, "voice_profile") ?? "" : null;
        var setExpr = ParseExpressions(a["set_expressions"] as JsonArray, AssetIndex.Sprites(_session.Fs));
        var removeExpr = (a["remove_expressions"] as JsonArray ?? new JsonArray()).Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList();
        var defaultExpr = OptArg(a, "default_expression");

        var diff = new List<DiffLine>();
        if (name is not null) { diff.Add(new(DiffKind.Removed, $"Tên: {displayName}")); diff.Add(new(DiffKind.Added, $"Tên: {name}")); }
        if (color is not null) { diff.Add(new(DiffKind.Removed, $"Màu: {c.Color}")); diff.Add(new(DiffKind.Added, $"Màu: {color}")); }
        foreach (var e in setExpr)
        {
            var existing = c.FindExpression(e.Id);
            if (existing is not null) diff.Add(new(DiffKind.Removed, $"Biểu cảm {e.Id}: {existing.Sprite ?? "(chưa có ảnh)"}"));
            diff.Add(new(DiffKind.Added, $"Biểu cảm {e.Id}: {e.Sprite ?? "(chưa có ảnh)"}"));
        }
        foreach (var r in removeExpr) diff.Add(new(DiffKind.Removed, $"Biểu cảm {r}"));
        if (defaultExpr is not null) diff.Add(new(DiffKind.Added, $"Biểu cảm mặc định: {defaultExpr}"));
        if (voice is not null)
        {
            foreach (var l in (c.VoiceProfile ?? "").Split('\n').Where(l => l.Length > 0)) diff.Add(new(DiffKind.Removed, l));
            foreach (var l in voice.Split('\n').Where(l => l.Length > 0)) diff.Add(new(DiffKind.Added, l));
        }
        if (diff.Count == 0) throw new ToolInputException("Không có gì để sửa — gửi ít nhất một trường.");

        return Propose(new Proposal
        {
            Title = $"Sửa nhân vật {displayName}",
            Diff = diff,
            Apply = () =>
            {
                if (!L.Characters.TryGetValue(id, out var cur)) return "Nhân vật đã bị xoá.";
                if (name is not null) cur.DisplayName[Locale] = name;
                if (color is not null) cur.Color = color;
                if (voice is not null) cur.VoiceProfile = NullIfEmpty(voice);
                foreach (var e in setExpr)
                {
                    if (cur.FindExpression(e.Id) is { } ex) ex.Sprite = e.Sprite ?? ex.Sprite;
                    else cur.Expressions.Add(e);
                }
                cur.Expressions.RemoveAll(x => removeExpr.Contains(x.Id));
                if (defaultExpr is not null && cur.FindExpression(defaultExpr) is not null) cur.DefaultExpression = defaultExpr;
                ProjectIo.SaveCharacter(_session.Fs, cur);
                return null;
            },
        });
    }

    // ================= Chương =================

    private ToolOutcome CreateChapter(JsonObject a)
    {
        var title = Arg(a, "title");
        return Propose(new Proposal
        {
            Title = $"Tạo chương \"{title}\"",
            Diff = new List<DiffLine> { new(DiffKind.Added, $"⬡ Chương {L.Project.Graphs.Count + 1}: {title}  (có sẵn một node Kết thúc)") },
            Apply = () =>
            {
                var id = Ids.Make("chapter", title, L.Project.Graphs);
                var ending = new EndingNode
                {
                    Id = "ending_ket_thuc",
                    Position = new GraphPosition(900, 200),
                    Name = new Localized(Locale, "Kết thúc"),
                };
                var graph = new StoryGraph { Id = id, Title = new Localized(Locale, title), Entry = ending.Id, Nodes = { ending } };
                L.Project.Graphs.Add(id);
                L.Graphs[id] = graph;
                ProjectIo.SaveProject(_session.Fs, L.Project);
                ProjectIo.SaveGraph(_session.Fs, graph);
                LastCreated = $"Chương mới có id \"{id}\", node kết thúc \"{ending.Id}\".";
                return null;
            },
        });
    }

    /// <summary>
    /// Id sinh ra lúc áp dụng (chương, cảnh, node mới) — gửi lại cho AI trong kết
    /// quả công cụ để nó dùng tiếp ở bước sau thay vì đoán.
    /// </summary>
    public string? LastCreated { get; set; }

    private ToolOutcome UpdateChapter(JsonObject a)
    {
        var id = Arg(a, "chapter_id");
        var g = FindGraph(id);
        var oldTitle = g.Title[Locale] ?? id;

        if (a["delete"]?.GetValue<bool>() == true)
        {
            if (L.Project.Graphs.Count <= 1) throw new ToolInputException("Dự án cần ít nhất một chương, không xoá được chương cuối cùng.");
            return Propose(new Proposal
            {
                Title = $"Xoá chương \"{oldTitle}\"",
                Summary = "Các cảnh (lời thoại) không bị xoá.",
                Diff = new List<DiffLine> { new(DiffKind.Removed, $"⬡ {oldTitle} — {g.Nodes.Count} node") },
                Apply = () =>
                {
                    if (!L.Graphs.Remove(id)) return "Chương đã bị xoá.";
                    L.Project.Graphs.Remove(id);
                    ProjectIo.SaveProject(_session.Fs, L.Project);
                    _session.Fs.DeleteFile(ProjectPaths.Graph(id));
                    return null;
                },
            });
        }

        var title = OptArg(a, "title");
        int? position = a["position"] is JsonValue pv && pv.TryGetValue<double>(out var pd) ? (int)pd : null;
        if (title is null && position is null) throw new ToolInputException("Gửi title, position hoặc delete.");
        var diff = new List<DiffLine>();
        if (title is not null) { diff.Add(new(DiffKind.Removed, oldTitle)); diff.Add(new(DiffKind.Added, title)); }
        if (position is not null) diff.Add(new(DiffKind.Note, $"Chuyển từ vị trí {L.Project.Graphs.IndexOf(id) + 1} sang {position}"));

        return Propose(new Proposal
        {
            Title = $"Sửa chương \"{oldTitle}\"",
            Diff = diff,
            Apply = () =>
            {
                if (!L.Graphs.TryGetValue(id, out var cur)) return "Chương đã bị xoá.";
                if (title is not null)
                {
                    cur.Title[Locale] = title;
                    ProjectIo.SaveGraph(_session.Fs, cur);
                }
                if (position is not null)
                {
                    L.Project.Graphs.Remove(id);
                    L.Project.Graphs.Insert(Math.Clamp(position.Value - 1, 0, L.Project.Graphs.Count), id);
                    ProjectIo.SaveProject(_session.Fs, L.Project);
                }
                return null;
            },
        });
    }

    // ================= Cảnh =================

    private List<StageActor> ParseStage(JsonArray? arr)
    {
        var list = new List<StageActor>();
        foreach (var node in arr ?? new JsonArray())
        {
            if (node is not JsonObject o) continue;
            var who = Arg(o, "character");
            var ch = FindCharacter(who);
            var expr = NullIfEmpty(OptArg(o, "expression")) ?? ch.DefaultExpression;
            CheckSpeaker(who, expr);
            var anchor = (OptArg(o, "anchor") ?? "center") switch
            {
                "farLeft" => StageAnchor.FarLeft,
                "left" => StageAnchor.Left,
                "right" => StageAnchor.Right,
                "farRight" => StageAnchor.FarRight,
                _ => StageAnchor.Center,
            };
            list.Add(new StageActor { Character = who, Expression = expr, Anchor = anchor });
        }
        return list;
    }

    private string StageText(IEnumerable<StageActor> stage)
        => string.Join(", ", stage.Select(s => $"{SpeakerName(s.Character)} ({s.Expression}, {s.Anchor})"));

    private ToolOutcome CreateScene(JsonObject a)
    {
        var fs = _session.Fs;
        var background = NullIfEmpty(OptArg(a, "background"));
        CheckAsset(background, AssetIndex.Backgrounds(fs), "ảnh nền");
        var bgm = NullIfEmpty(OptArg(a, "bgm"));
        CheckAsset(bgm, AssetIndex.Bgm(fs), "nhạc nền");
        var stage = ParseStage(a["stage"] as JsonArray);
        var lines = (a["lines"] as JsonArray ?? new JsonArray()).Select(l => (JsonObject)l!)
            .Select(l => (Speaker: CheckSpeaker(NullIfEmpty(OptArg(l, "speaker")), OptArg(l, "expression")), Expression: NullIfEmpty(OptArg(l, "expression")), Text: Arg(l, "text")))
            .ToList();
        var wanted = Slug(OptArg(a, "scene_id") ?? OptArg(a, "title") ?? "canh moi");
        if (!wanted.StartsWith("scene", StringComparison.Ordinal) && OptArg(a, "scene_id") is null) wanted = $"scene_{wanted}";
        if (L.Scenes.ContainsKey(wanted) && OptArg(a, "scene_id") is not null) throw new ToolInputException($"Đã có cảnh \"{wanted}\".");

        var diff = new List<DiffLine> { new(DiffKind.Added, $"▣ Cảnh {wanted}") };
        if (background is not null) diff.Add(new(DiffKind.Added, $"   Nền: {background}"));
        if (bgm is not null) diff.Add(new(DiffKind.Added, $"   Nhạc: {bgm}"));
        if (stage.Count > 0) diff.Add(new(DiffKind.Added, $"   Trên sân khấu: {StageText(stage)}"));
        foreach (var l in lines) diff.Add(new(DiffKind.Added, $"   {LineText(l.Speaker, l.Text)}"));

        return Propose(new Proposal
        {
            Title = $"Tạo cảnh {wanted}",
            Summary = lines.Count == 0 ? "" : $"{lines.Count} dòng thoại",
            Diff = diff,
            Apply = () =>
            {
                var id = L.Scenes.ContainsKey(wanted) ? Ids.Make("scene", wanted, L.Scenes.Keys) : wanted;
                var scene = new SceneData
                {
                    Id = id,
                    Background = background,
                    Bgm = bgm is null ? null : new AudioCue { Src = bgm, Volume = 0.6f, Loop = true },
                    Stage = stage,
                };
                foreach (var l in lines) scene.Lines.Add(NewLine(scene, l.Speaker, l.Expression, l.Text));
                L.Scenes[id] = scene;
                ProjectIo.SaveScene(_session.Fs, scene);
                LastCreated = $"Cảnh mới có id \"{id}\". Nhớ thêm node scene trỏ tới nó bằng edit_graph để cảnh xuất hiện trong truyện.";
                return null;
            },
        });
    }

    private ToolOutcome UpdateScene(JsonObject a)
    {
        var id = Arg(a, "scene_id");
        var s = FindScene(id);
        var fs = _session.Fs;
        var setBg = a.ContainsKey("background");
        var background = NullIfEmpty(OptArg(a, "background"));
        if (setBg) CheckAsset(background, AssetIndex.Backgrounds(fs), "ảnh nền");
        var setBgm = a.ContainsKey("bgm");
        var bgm = NullIfEmpty(OptArg(a, "bgm"));
        if (setBgm) CheckAsset(bgm, AssetIndex.Bgm(fs), "nhạc nền");
        var setStage = a["stage"] is JsonArray;
        var stage = ParseStage(a["stage"] as JsonArray);
        if (!setBg && !setBgm && !setStage) throw new ToolInputException("Gửi background, bgm hoặc stage.");

        var diff = new List<DiffLine>();
        if (setBg) { diff.Add(new(DiffKind.Removed, $"Nền: {s.Background ?? "(không)"}")); diff.Add(new(DiffKind.Added, $"Nền: {background ?? "(không)"}")); }
        if (setBgm) { diff.Add(new(DiffKind.Removed, $"Nhạc: {s.Bgm?.Src ?? "(không)"}")); diff.Add(new(DiffKind.Added, $"Nhạc: {bgm ?? "(không)"}")); }
        if (setStage) { diff.Add(new(DiffKind.Removed, $"Sân khấu: {StageText(s.Stage)}")); diff.Add(new(DiffKind.Added, $"Sân khấu: {StageText(stage)}")); }

        return Propose(new Proposal
        {
            Title = $"Sửa cảnh {id}",
            Diff = diff,
            Apply = () =>
            {
                if (!L.Scenes.TryGetValue(id, out var cur)) return "Cảnh đã bị xoá.";
                if (setBg) cur.Background = background;
                if (setBgm) cur.Bgm = bgm is null ? null : new AudioCue { Src = bgm, Volume = cur.Bgm?.Volume ?? 0.6f, Loop = true };
                if (setStage) cur.Stage = stage;
                ProjectIo.SaveScene(_session.Fs, cur);
                return null;
            },
        });
    }

    // ================= Đồ thị =================

    private static string NodePrefix(string type) => type switch
    {
        "scene" => "scene", "choice" => "choice", "condition" => "cond", "variable" => "var",
        "jump" => "jump", "ending" => "ending", _ => "note",
    };

    private Condition ParseCondition(JsonObject c, Dictionary<string, VariableDef> declared)
    {
        var variable = Arg(c, "variable");
        if (!declared.TryGetValue(variable, out var def)) throw new ToolInputException($"Biến \"{variable}\" chưa khai báo — dùng manage_variable trước.");
        var op = (OptArg(c, "op") ?? ">=") switch
        {
            "==" or "eq" => ComparisonOp.Eq,
            "!=" or "neq" => ComparisonOp.Neq,
            ">" or "gt" => ComparisonOp.Gt,
            "<" or "lt" => ComparisonOp.Lt,
            "<=" or "lte" => ComparisonOp.Lte,
            _ => ComparisonOp.Gte,
        };
        return new CompareCondition { Variable = variable, Op = op, Value = ParseValue(c["value"], def.Type) };
    }

    /// <summary>Dữ liệu một node đã kiểm tra xong, chờ áp dụng.</summary>
    private sealed class NodeSpec
    {
        public string Ref = "";
        public string Type = "";
        public JsonObject Raw = new();
        public List<(string? Ref, string Text, List<Effect> Effects)>? Options;
        public Condition? Condition;
        public List<Effect>? Effects;
    }

    private NodeSpec CheckNode(JsonObject n, string type, Dictionary<string, VariableDef> declared)
    {
        var spec = new NodeSpec { Type = type, Raw = n };
        if (OptArg(n, "scene") is { Length: > 0 } scene && !L.Scenes.ContainsKey(scene))
        {
            throw new ToolInputException($"Không có cảnh \"{scene}\" — tạo bằng create_scene trước.");
        }
        if (n["options"] is JsonArray opts)
        {
            spec.Options = opts.Select(o => (JsonObject)o!)
                .Select(o => (NullIfEmpty(OptArg(o, "ref")), Arg(o, "text"), (o["effects"] as JsonArray ?? new JsonArray()).Select(e => ParseEffect((JsonObject)e!, declared)).ToList()))
                .ToList();
        }
        if (n["condition"] is JsonObject cond) spec.Condition = ParseCondition(cond, declared);
        if (n["effects"] is JsonArray fx) spec.Effects = fx.Select(e => ParseEffect((JsonObject)e!, declared)).ToList();
        return spec;
    }

    private static StoryNode NewNode(string type) => type switch
    {
        "scene" => new SceneNode(),
        "choice" => new ChoiceNode(),
        "condition" => new ConditionNode(),
        "variable" => new VariableNode(),
        "jump" => new JumpNode(),
        "ending" => new EndingNode(),
        "comment" => new CommentNode(),
        _ => throw new ToolInputException($"Loại node \"{type}\" không hợp lệ."),
    };

    /// <summary>Ghi các trường của spec vào node. optionRefs nhận ánh xạ ref phương án → id thật.</summary>
    private void Fill(StoryNode node, NodeSpec spec, Dictionary<string, string> optionRefs)
    {
        var n = spec.Raw;
        if (OptArg(n, "label") is { } label) node.Label = NullIfEmpty(label);
        switch (node)
        {
            case SceneNode s when OptArg(n, "scene") is { } scene: s.Scene = scene; break;
            case ChoiceNode c:
                if (OptArg(n, "prompt") is { } prompt) { c.Prompt ??= new Localized(); c.Prompt[Locale] = prompt; }
                if (spec.Options is not null)
                {
                    var old = c.Options.ToList();
                    c.Options.Clear();
                    foreach (var (r, text, effects) in spec.Options)
                    {
                        // Giữ id và dây nối cũ nếu phương án trùng chữ, để sửa câu hỏi
                        // không làm đứt nhánh đang có.
                        var keep = old.FirstOrDefault(o => o.Text[Locale] == text);
                        var id = keep?.Id ?? Ids.Make("opt", text, c.Options.Select(o => o.Id).Concat(old.Select(o => o.Id)));
                        c.Options.Add(new ChoiceOption { Id = id, Text = new Localized(Locale, text), Effects = effects, Next = keep?.Next });
                        if (r is not null) optionRefs[$"{node.Id}/{r}"] = id;
                    }
                }
                break;
            case ConditionNode cn when spec.Condition is not null: cn.Condition = spec.Condition; break;
            case VariableNode v when spec.Effects is not null: v.Effects = spec.Effects; break;
            case EndingNode e when OptArg(n, "name") is { } name: e.Name[Locale] = name; break;
            case CommentNode cm when OptArg(n, "text") is { } text: cm.Text = text; break;
        }
    }

    private ToolOutcome EditGraph(JsonObject a)
    {
        var graphId = Arg(a, "chapter_id");
        var graph = FindGraph(graphId);
        var declared = L.Project.Variables.ToDictionary(v => v.Id);

        var adds = new List<NodeSpec>();
        foreach (var n in (a["add_nodes"] as JsonArray ?? new JsonArray()).Select(x => (JsonObject)x!))
        {
            var type = Arg(n, "type");
            NewNode(type);
            var spec = CheckNode(n, type, declared);
            spec.Ref = Arg(n, "ref");
            if (adds.Any(x => x.Ref == spec.Ref) || graph.Find(spec.Ref) is not null) throw new ToolInputException($"ref \"{spec.Ref}\" bị trùng.");
            adds.Add(spec);
        }

        var updates = new List<(string Id, NodeSpec Spec)>();
        foreach (var n in (a["update_nodes"] as JsonArray ?? new JsonArray()).Select(x => (JsonObject)x!))
        {
            var id = Arg(n, "node_id");
            var existing = graph.Find(id) ?? throw new ToolInputException($"Không có node \"{id}\" trong chương.");
            updates.Add((id, CheckNode(n, existing.GetType().Name, declared)));
        }

        var deletes = (a["delete_nodes"] as JsonArray ?? new JsonArray()).Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList();
        foreach (var d in deletes) if (graph.Find(d) is null) throw new ToolInputException($"Không có node \"{d}\" để xoá.");

        bool Known(string r) => adds.Any(x => x.Ref == r) || (graph.Find(r) is not null && !deletes.Contains(r));
        var links = (a["links"] as JsonArray ?? new JsonArray()).Select(x => (JsonObject)x!)
            .Select(l => (From: Arg(l, "from"), Port: NullIfEmpty(OptArg(l, "port")), To: NullIfEmpty(OptArg(l, "to"))))
            .ToList();
        foreach (var l in links)
        {
            if (!Known(l.From)) throw new ToolInputException($"links: không có node nguồn \"{l.From}\".");
            if (l.To is not null && !Known(l.To)) throw new ToolInputException($"links: không có node đích \"{l.To}\".");
        }
        var entry = NullIfEmpty(OptArg(a, "entry"));
        if (entry is not null && !Known(entry)) throw new ToolInputException($"entry: không có node \"{entry}\".");
        if (adds.Count == 0 && updates.Count == 0 && deletes.Count == 0 && links.Count == 0 && entry is null)
        {
            throw new ToolInputException("Không có thay đổi nào.");
        }

        string Describe(string r) => adds.FirstOrDefault(x => x.Ref == r) is { } s
            ? $"[mới] {OptArg(s.Raw, "label") ?? OptArg(s.Raw, "prompt") ?? OptArg(s.Raw, "name") ?? s.Type}"
            : graph.Find(r)?.DisplayName ?? r;

        var diff = new List<DiffLine>();
        foreach (var s in adds)
        {
            var detail = s.Type switch
            {
                "scene" => $"cảnh {OptArg(s.Raw, "scene") ?? "(chưa gán)"}",
                "choice" => $"\"{OptArg(s.Raw, "prompt")}\" — {string.Join(" / ", s.Options?.Select(o => o.Text) ?? Array.Empty<string>())}",
                "condition" => s.Condition is CompareCondition cc ? $"{cc.Variable} {cc.Op} {cc.Value}" : "",
                "variable" => string.Join(", ", s.Effects?.Select(EffectText) ?? Array.Empty<string>()),
                "ending" => OptArg(s.Raw, "name") ?? "",
                _ => OptArg(s.Raw, "text") ?? "",
            };
            diff.Add(new(DiffKind.Added, $"+ node {s.Type}: {OptArg(s.Raw, "label") ?? ""} {detail}".Trim()));
        }
        foreach (var (id, _) in updates) diff.Add(new(DiffKind.Note, $"sửa node {graph.Find(id)!.DisplayName}"));
        foreach (var d in deletes) diff.Add(new(DiffKind.Removed, $"xoá node {graph.Find(d)!.DisplayName}"));
        foreach (var l in links)
        {
            var port = l.Port is null ? "" : $" [{l.Port}]";
            diff.Add(l.To is null
                ? new(DiffKind.Removed, $"gỡ dây từ {Describe(l.From)}{port}")
                : new(DiffKind.Added, $"{Describe(l.From)}{port} → {Describe(l.To)}"));
        }
        if (entry is not null) diff.Add(new(DiffKind.Added, $"▶ điểm bắt đầu: {Describe(entry)}"));

        return Propose(new Proposal
        {
            Title = $"Sửa đồ thị chương \"{graph.Title[Locale] ?? graphId}\"",
            Summary = Arg(a, "summary"),
            Diff = diff,
            Apply = () =>
            {
                if (!L.Graphs.TryGetValue(graphId, out var g)) return "Chương đã bị xoá.";
                var taken = new HashSet<string>(g.Nodes.Select(n => n.Id));
                var refs = new Dictionary<string, string>();
                var optionRefs = new Dictionary<string, string>();

                // Node mới xếp thành một cột bên phải những gì đang có, để không đè
                // lên node cũ; người dùng kéo lại chỗ khác tuỳ ý.
                var startX = g.Nodes.Count == 0 ? 80 : g.Nodes.Max(n => n.Position.X) + 300;
                var y = 80f;
                foreach (var s in adds)
                {
                    var node = NewNode(s.Type);
                    var label = OptArg(s.Raw, "label") ?? OptArg(s.Raw, "prompt") ?? OptArg(s.Raw, "name") ?? s.Type;
                    node.Id = Ids.Make(NodePrefix(s.Type), label, taken);
                    taken.Add(node.Id);
                    node.Position = new GraphPosition(startX, y);
                    y += 170;
                    if (node is EndingNode en) en.Name = new Localized(Locale, OptArg(s.Raw, "name") ?? "Kết thúc");
                    Fill(node, s, optionRefs);
                    if (s.Options is not null)
                    {
                        foreach (var (r, _, _) in s.Options.Where(o => o.Ref is not null)) optionRefs[$"{s.Ref}/{r}"] = optionRefs[$"{node.Id}/{r}"];
                    }
                    g.Nodes.Add(node);
                    refs[s.Ref] = node.Id;
                }

                foreach (var (id, s) in updates)
                {
                    if (g.Find(id) is { } node) Fill(node, s, optionRefs);
                }

                foreach (var d in deletes)
                {
                    g.Nodes.RemoveAll(n => n.Id == d);
                    foreach (var other in g.Nodes) other.ClearLinksTo(d);
                }

                string Resolve(string r) => refs.TryGetValue(r, out var id) ? id : r;
                foreach (var l in links)
                {
                    var from = g.Find(Resolve(l.From));
                    if (from is null) return $"Không còn node \"{l.From}\".";
                    var port = l.Port ?? "";
                    if (from is ChoiceNode ch)
                    {
                        if (optionRefs.TryGetValue($"{l.From}/{port}", out var viaRef)) port = viaRef;
                        else if (optionRefs.TryGetValue($"{from.Id}/{port}", out var viaId)) port = viaId;
                        if (ch.Options.All(o => o.Id != port))
                        {
                            return $"Node lựa chọn \"{from.DisplayName}\" không có phương án \"{l.Port}\" — cần port là ref hoặc id phương án.";
                        }
                    }
                    else if (from is ConditionNode && port is not ("true" or "false"))
                    {
                        return $"Node điều kiện \"{from.DisplayName}\" cần port \"true\" hoặc \"false\".";
                    }
                    GraphSync.SetOutgoing(from, port, l.To is null ? null : Resolve(l.To));
                }

                if (entry is not null) g.Entry = Resolve(entry);
                ProjectIo.SaveGraph(_session.Fs, g);
                LastCreated = refs.Count == 0 ? null : "Id node mới: " + string.Join(", ", refs.Select(kv => $"{kv.Key} = {kv.Value}"));
                return null;
            },
        });
    }

    // ================= Biến =================

    private ToolOutcome ManageVariable(JsonObject a)
    {
        var action = Arg(a, "action");
        var id = Slug(Arg(a, "id"));
        var existing = L.Project.Variables.FirstOrDefault(v => v.Id == id);
        var description = OptArg(a, "description");

        switch (action)
        {
            case "create":
                if (existing is not null) throw new ToolInputException($"Biến \"{id}\" đã có — dùng action update.");
                var type = (OptArg(a, "type") ?? "number") switch { "boolean" => VariableType.Boolean, "text" => VariableType.Text, _ => VariableType.Number };
                var initial = ParseValue(a["initial"], type);
                return Propose(new Proposal
                {
                    Title = $"Khai báo biến \"{id}\"",
                    Diff = new List<DiffLine> { new(DiffKind.Added, $"{id} : {type.ToString().ToLowerInvariant()} = {initial}{(description is null ? "" : $"   — {description}")}") },
                    Apply = () =>
                    {
                        if (L.Project.Variables.Any(v => v.Id == id)) return $"Biến \"{id}\" đã có.";
                        L.Project.Variables.Add(new VariableDef { Id = id, Type = type, Initial = initial, Description = description });
                        ProjectIo.SaveProject(_session.Fs, L.Project);
                        return null;
                    },
                });

            case "update":
                if (existing is null) throw new ToolInputException($"Không có biến \"{id}\".");
                var hasInitial = a.ContainsKey("initial");
                var newInitial = hasInitial ? ParseValue(a["initial"], existing.Type) : existing.Initial;
                return Propose(new Proposal
                {
                    Title = $"Sửa biến \"{id}\"",
                    Diff = new List<DiffLine>
                    {
                        new(DiffKind.Removed, $"{id} = {existing.Initial}   — {existing.Description}"),
                        new(DiffKind.Added, $"{id} = {newInitial}   — {description ?? existing.Description}"),
                    },
                    Apply = () =>
                    {
                        var v = L.Project.Variables.FirstOrDefault(x => x.Id == id);
                        if (v is null) return "Biến đã bị xoá.";
                        if (hasInitial) v.Initial = newInitial;
                        if (description is not null) v.Description = description;
                        ProjectIo.SaveProject(_session.Fs, L.Project);
                        return null;
                    },
                });

            case "delete":
                if (existing is null) throw new ToolInputException($"Không có biến \"{id}\".");
                return Propose(new Proposal
                {
                    Title = $"Xoá biến \"{id}\"",
                    Summary = "Lựa chọn và điều kiện đang dùng biến này sẽ báo lỗi biến chưa khai báo.",
                    Diff = new List<DiffLine> { new(DiffKind.Removed, $"{id} = {existing.Initial}") },
                    Apply = () =>
                    {
                        L.Project.Variables.RemoveAll(x => x.Id == id);
                        ProjectIo.SaveProject(_session.Fs, L.Project);
                        return null;
                    },
                });

            default:
                throw new ToolInputException("action phải là create, update hoặc delete.");
        }
    }
}
