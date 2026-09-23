using VNdev.Core.Model;

namespace VNdev.Core.Validation;

public enum IssueSeverity { Error, Warning, Info }

public enum IssueCode
{
    /// <summary>Node không ai trỏ tới và cũng không phải điểm vào.</summary>
    OrphanNode,
    /// <summary>Từ node này không có đường nào dẫn tới kết thúc.</summary>
    DeadEnd,
    /// <summary>Cổng ra chưa nối dây.</summary>
    UnconnectedPort,
    /// <summary>Trỏ tới một node không tồn tại.</summary>
    MissingTarget,
    /// <summary>Dùng biến chưa khai báo.</summary>
    UndeclaredVariable,
    /// <summary>Trỏ tới cảnh không tồn tại.</summary>
    MissingScene,
    /// <summary>Trỏ tới nhân vật không tồn tại.</summary>
    MissingCharacter,
    /// <summary>Trỏ tới biểu cảm nhân vật không có.</summary>
    MissingExpression,
    /// <summary>Điểm vào của chương không tồn tại.</summary>
    MissingEntry,
    /// <summary>Trùng id trong cùng một chương.</summary>
    DuplicateId,
    /// <summary>Chương không có node kết thúc nào.</summary>
    NoEnding,
}

public sealed class ValidationIssue
{
    public required IssueCode Code { get; init; }
    public required IssueSeverity Severity { get; init; }

    /// <summary>Thông điệp tiếng Việt, hiện thẳng cho người dùng.</summary>
    public required string Message { get; init; }

    /// <summary>Node liên quan — dùng để nhảy tới khi người dùng bấm vào cảnh báo.</summary>
    public string? NodeId { get; init; }
    public string? GraphId { get; init; }
    public string? SceneId { get; init; }
    public string? LineId { get; init; }
    public string? Field { get; init; }
}

public static class GraphValidator
{
    /// <summary>
    /// Kiểm tra một chương và trả về mọi vấn đề tìm thấy.
    /// </summary>
    /// <remarks>
    /// Hàm này chạy mỗi khi đồ thị đổi nên phải nhanh và không được ném lỗi:
    /// một dự án đang dở dang vốn dĩ đầy lỗi, đó là chuyện bình thường. Nhiệm
    /// vụ của nó là liệt kê, không phải chặn.
    /// </remarks>
    public static List<ValidationIssue> Validate(
        StoryGraph graph,
        IReadOnlyCollection<VariableDef> variables,
        IReadOnlyDictionary<string, SceneData>? scenes = null,
        IReadOnlyDictionary<string, CharacterData>? characters = null)
    {
        var issues = new List<ValidationIssue>();

        /* --- id trùng --- */
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (!seen.Add(node.Id))
            {
                issues.Add(new ValidationIssue
                {
                    Code = IssueCode.DuplicateId,
                    Severity = IssueSeverity.Error,
                    Message = $"Có hai node cùng mang id \"{node.Id}\".",
                    GraphId = graph.Id,
                    NodeId = node.Id,
                });
            }
        }

        var byId = graph.Nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var declared = new HashSet<string>(variables.Select(v => v.Id), StringComparer.Ordinal);

        /* --- điểm vào --- */
        if (!byId.ContainsKey(graph.Entry))
        {
            issues.Add(new ValidationIssue
            {
                Code = IssueCode.MissingEntry,
                Severity = IssueSeverity.Error,
                Message = $"Điểm bắt đầu \"{graph.Entry}\" không tồn tại trong chương này.",
                GraphId = graph.Id,
                Field = nameof(graph.Entry),
            });
        }

        /* --- liên kết, cổng trống, tham chiếu --- */
        var incoming = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            if (node is CommentNode) continue;

            foreach (var (_, target) in node.Outgoing())
            {
                if (string.IsNullOrEmpty(target))
                {
                    issues.Add(new ValidationIssue
                    {
                        Code = IssueCode.UnconnectedPort,
                        Severity = IssueSeverity.Warning,
                        Message = $"Node \"{node.DisplayName}\" còn một cổng ra chưa nối.",
                        GraphId = graph.Id,
                        NodeId = node.Id,
                    });
                    continue;
                }

                if (!byId.ContainsKey(target!))
                {
                    issues.Add(new ValidationIssue
                    {
                        Code = IssueCode.MissingTarget,
                        Severity = IssueSeverity.Error,
                        Message = $"Node \"{node.DisplayName}\" trỏ tới \"{target}\" — node này không tồn tại.",
                        GraphId = graph.Id,
                        NodeId = node.Id,
                    });
                    continue;
                }

                incoming.Add(target!);
            }

            /* biến chưa khai báo */
            var used = new HashSet<string>(StringComparer.Ordinal);
            node.CollectVariables(used);
            foreach (var variable in used)
            {
                if (declared.Contains(variable)) continue;
                issues.Add(new ValidationIssue
                {
                    Code = IssueCode.UndeclaredVariable,
                    Severity = IssueSeverity.Error,
                    Message = string.IsNullOrEmpty(variable)
                        ? $"Node \"{node.DisplayName}\" có một ô chưa chọn biến."
                        : $"Node \"{node.DisplayName}\" dùng biến \"{variable}\" nhưng biến này chưa được khai báo.",
                    GraphId = graph.Id,
                    NodeId = node.Id,
                    Field = variable,
                });
            }

            /* cảnh và nhân vật */
            if (node is SceneNode sceneNode && scenes is not null)
            {
                if (!scenes.TryGetValue(sceneNode.Scene, out var scene))
                {
                    issues.Add(new ValidationIssue
                    {
                        Code = IssueCode.MissingScene,
                        Severity = IssueSeverity.Error,
                        Message = $"Node \"{node.DisplayName}\" trỏ tới cảnh \"{sceneNode.Scene}\" — cảnh này không tồn tại.",
                        GraphId = graph.Id,
                        NodeId = node.Id,
                    });
                }
                else if (characters is not null)
                {
                    issues.AddRange(ValidateSceneRefs(scene, characters, graph.Id, node.Id));
                }
            }
        }

        /* --- node mồ côi --- */
        foreach (var node in graph.Nodes)
        {
            if (node is CommentNode) continue;
            if (node.Id == graph.Entry) continue;
            if (incoming.Contains(node.Id)) continue;

            issues.Add(new ValidationIssue
            {
                Code = IssueCode.OrphanNode,
                Severity = IssueSeverity.Warning,
                Message = $"Node \"{node.DisplayName}\" không có node nào trỏ tới — người chơi sẽ không bao giờ tới đây.",
                GraphId = graph.Id,
                NodeId = node.Id,
            });
        }

        issues.AddRange(FindDeadEnds(graph, byId));

        if (!graph.Nodes.OfType<EndingNode>().Any())
        {
            issues.Add(new ValidationIssue
            {
                Code = IssueCode.NoEnding,
                Severity = IssueSeverity.Warning,
                Message = "Chương này chưa có node Kết thúc nào.",
                GraphId = graph.Id,
            });
        }

        return issues;
    }

    /// <summary>
    /// Tìm những node mà từ đó không có đường nào dẫn tới kết thúc.
    /// </summary>
    /// <remarks>
    /// Làm ngược: bắt đầu từ mọi node Kết thúc rồi lan ngược theo chiều dây.
    /// Node nào không bị chạm tới là nhánh cụt. Cách này duyệt đồ thị đúng một
    /// lần, thay vì thử tìm đường riêng từ từng node.
    /// </remarks>
    private static IEnumerable<ValidationIssue> FindDeadEnds(
        StoryGraph graph,
        IReadOnlyDictionary<string, StoryNode> byId)
    {
        var reverse = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            foreach (var (_, target) in node.Outgoing())
            {
                if (string.IsNullOrEmpty(target) || !byId.ContainsKey(target!)) continue;
                if (!reverse.TryGetValue(target!, out var list))
                {
                    list = new List<string>();
                    reverse[target!] = list;
                }
                list.Add(node.Id);
            }
        }

        var canReachEnding = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Stack<string>();
        foreach (var ending in graph.Nodes.OfType<EndingNode>())
        {
            if (canReachEnding.Add(ending.Id)) queue.Push(ending.Id);
        }

        while (queue.Count > 0)
        {
            var current = queue.Pop();
            if (!reverse.TryGetValue(current, out var previous)) continue;
            foreach (var id in previous)
            {
                if (canReachEnding.Add(id)) queue.Push(id);
            }
        }

        foreach (var node in graph.Nodes)
        {
            if (node is CommentNode or EndingNode) continue;
            if (canReachEnding.Contains(node.Id)) continue;

            yield return new ValidationIssue
            {
                Code = IssueCode.DeadEnd,
                Severity = IssueSeverity.Warning,
                Message = $"Từ node \"{node.DisplayName}\" không có đường nào dẫn tới một kết thúc.",
                GraphId = graph.Id,
                NodeId = node.Id,
            };
        }
    }

    /// <summary>Kiểm tra cảnh có tham chiếu tới nhân vật và biểu cảm có thật không.</summary>
    public static List<ValidationIssue> ValidateSceneRefs(
        SceneData scene,
        IReadOnlyDictionary<string, CharacterData> characters,
        string? graphId = null,
        string? nodeId = null)
    {
        var issues = new List<ValidationIssue>();

        void Check(string characterId, string? expressionId, string? lineId)
        {
            if (!characters.TryGetValue(characterId, out var character))
            {
                issues.Add(new ValidationIssue
                {
                    Code = IssueCode.MissingCharacter,
                    Severity = IssueSeverity.Error,
                    Message = $"Cảnh \"{scene.Id}\" dùng nhân vật \"{characterId}\" — nhân vật này không tồn tại.",
                    SceneId = scene.Id,
                    GraphId = graphId,
                    NodeId = nodeId,
                    LineId = lineId,
                });
                return;
            }

            if (!string.IsNullOrEmpty(expressionId) && character.FindExpression(expressionId!) is null)
            {
                issues.Add(new ValidationIssue
                {
                    Code = IssueCode.MissingExpression,
                    Severity = IssueSeverity.Error,
                    Message = $"Nhân vật \"{characterId}\" không có biểu cảm \"{expressionId}\".",
                    SceneId = scene.Id,
                    GraphId = graphId,
                    NodeId = nodeId,
                    LineId = lineId,
                });
            }
        }

        foreach (var actor in scene.Stage) Check(actor.Character, actor.Expression, null);

        foreach (var line in scene.Lines)
        {
            if (line.Speaker is not null) Check(line.Speaker, line.Expression, line.Id);
            foreach (var change in line.StageChanges ?? new List<StageActor>())
            {
                Check(change.Character, change.Expression, line.Id);
            }
        }

        return issues;
    }
}
