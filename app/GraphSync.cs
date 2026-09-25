using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using VNdev.Core;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Lớp dịch giữa <see cref="StoryGraph"/> (nói bằng id trong JSON) và
/// <see cref="GraphEdit"/> của Godot (nói bằng tên node của nó).
/// </summary>
/// <remarks>
/// Đây là "chỗ khó" mà docs/LO-TRINH.md nhắc tới ở Chặng 2. Quy ước chốt:
/// <c>GraphNode.Name</c> LUÔN bằng <c>StoryNode.Id</c> — nhờ vậy không cần
/// bảng tra hai chiều cho node, chỉ còn phải dịch CỔNG (port index của Godot)
/// sang HANDLE (id lựa chọn, "true"/"false", hoặc rỗng cho cổng "tiếp theo"
/// duy nhất). Bảng tra <see cref="_portHandles"/> giữ đúng việc dịch đó.
/// </remarks>
public sealed class GraphSync
{
    private readonly GraphEdit _graphEdit;
    private StoryGraph _graph;

    /// <summary>node id → (port index → handle). Handle "" nghĩa là cổng "tiếp theo" duy nhất.</summary>
    private readonly Dictionary<string, List<string>> _portHandles = new();

    public event Action? Changed;
    public event Action<StoryNode?>? SelectionChanged;

    /// <summary>
    /// Người dùng muốn xoá các node này (phím Delete trên canvas). Không xoá
    /// ngay mà để màn hình quyết định có hỏi xác nhận hay không, rồi gọi
    /// <see cref="DeleteNodes"/>.
    /// </summary>
    public event Action<IReadOnlyList<string>>? DeleteRequested;

    /// <summary>Ctrl+D / Ctrl+C / Ctrl+V bấm ngay trên canvas — GraphEdit tự bắt các phím này.</summary>
    public event Action? DuplicateRequested;
    public event Action? CopyRequested;
    public event Action? PasteRequested;

    public StoryGraph Graph => _graph;

    public GraphSync(GraphEdit graphEdit, StoryGraph graph)
    {
        _graphEdit = graphEdit;
        _graph = graph;

        _graphEdit.ConnectionRequest += OnConnectionRequest;
        _graphEdit.DisconnectionRequest += OnDisconnectionRequest;
        _graphEdit.DeleteNodesRequest += OnDeleteNodesRequest;
        _graphEdit.NodeSelected += node => SelectionChanged?.Invoke(_graph.Find(node.Name.ToString()));
        _graphEdit.NodeDeselected += _ => SelectionChanged?.Invoke(null);
        _graphEdit.EndNodeMove += OnEndNodeMove;
        _graphEdit.DuplicateNodesRequest += () => DuplicateRequested?.Invoke();
        _graphEdit.CopyNodesRequest += () => CopyRequested?.Invoke();
        _graphEdit.PasteNodesRequest += () => PasteRequested?.Invoke();

        Rebuild(graph);
    }

    public void Rebuild(StoryGraph graph)
    {
        _graph = graph;
        _portHandles.Clear();
        // GraphEdit giữ danh sách dây riêng, không tự xoá khi node bị gỡ — không
        // xoá ở đây thì đổi chương xong vẫn còn dây ma trỏ tới node của chương cũ.
        _graphEdit.ClearConnections();
        foreach (var child in _graphEdit.GetChildren().OfType<GraphNode>().ToList())
        {
            _graphEdit.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var node in graph.Nodes)
        {
            _graphEdit.AddChild(BuildGraphNode(node));
        }

        // Nối lại dây sau khi mọi GraphNode đã có mặt — không thể nối dây tới
        // một node chưa được thêm vào cây.
        foreach (var node in graph.Nodes)
        {
            var handles = _portHandles[node.Id];
            foreach (var (handle, target) in node.Outgoing())
            {
                if (string.IsNullOrEmpty(target)) continue;
                var port = handles.IndexOf(handle ?? "");
                if (port < 0) continue;
                _graphEdit.ConnectNode(node.Id, port, target, 0);
            }
        }
    }

    public void AddNode(StoryNode node)
    {
        _graph.Nodes.Add(node);
        _graphEdit.AddChild(BuildGraphNode(node));
        Changed?.Invoke();
    }

    public void RefreshNode(StoryNode node)
    {
        // Số cổng ra có thể đổi (ví dụ thêm lựa chọn) — cách chắc chắn nhất là
        // dựng lại đúng node đó và nối lại các dây đang có của riêng nó.
        var existing = _graphEdit.GetChildren().OfType<GraphNode>()
            .FirstOrDefault(n => n.Name == node.Id);
        if (existing is not null)
        {
            _graphEdit.RemoveChild(existing);
            existing.QueueFree();
        }

        _graphEdit.AddChild(BuildGraphNode(node));

        var handles = _portHandles[node.Id];
        foreach (var (handle, target) in node.Outgoing())
        {
            if (string.IsNullOrEmpty(target)) continue;
            var port = handles.IndexOf(handle ?? "");
            if (port < 0) continue;
            _graphEdit.ConnectNode(node.Id, port, target, 0);
        }
    }

    private void OnEndNodeMove()
    {
        foreach (var gn in _graphEdit.GetChildren().OfType<GraphNode>())
        {
            var node = _graph.Find(gn.Name.ToString());
            if (node is null) continue;
            node.Position = new GraphPosition(gn.PositionOffset.X, gn.PositionOffset.Y);
        }
        Changed?.Invoke();
    }

    private void OnConnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        var node = _graph.Find(fromNode.ToString());
        if (node is null) return;

        // Một cổng ra chỉ dẫn tới một nơi — nối dây mới thì bỏ dây cũ của
        // đúng cổng đó trước, không thì GraphEdit sẽ vẽ hai đường chồng nhau.
        var handles = _portHandles[node.Id];
        var handle = fromPort >= 0 && fromPort < handles.Count ? handles[(int)fromPort] : "";
        foreach (var (h, target) in node.Outgoing().ToList())
        {
            if (h == handle && !string.IsNullOrEmpty(target))
            {
                _graphEdit.DisconnectNode(fromNode, (int)fromPort, target, 0);
            }
        }

        SetOutgoing(node, handle, toNode.ToString());
        _graphEdit.ConnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
        Changed?.Invoke();
    }

    private void OnDisconnectionRequest(StringName fromNode, long fromPort, StringName toNode, long toPort)
    {
        var node = _graph.Find(fromNode.ToString());
        if (node is null) return;

        var handles = _portHandles[node.Id];
        var handle = fromPort >= 0 && fromPort < handles.Count ? handles[(int)fromPort] : "";
        SetOutgoing(node, handle, null);
        _graphEdit.DisconnectNode(fromNode, (int)fromPort, toNode, (int)toPort);
        Changed?.Invoke();
    }

    private void OnDeleteNodesRequest(Godot.Collections.Array nodes)
    {
        var ids = nodes.Select(n => n.AsString()).ToList();
        if (ids.Count > 0) DeleteRequested?.Invoke(ids);
    }

    public IReadOnlyList<string> SelectedIds()
        => _graphEdit.GetChildren().OfType<GraphNode>().Where(g => g.Selected).Select(g => g.Name.ToString()).ToList();

    public void SelectOnly(ICollection<string> ids)
    {
        foreach (var gn in _graphEdit.GetChildren().OfType<GraphNode>()) gn.Selected = ids.Contains(gn.Name.ToString());
    }

    /// <summary>
    /// Thêm một loạt node cùng lúc (dán, nhân bản) rồi dựng lại dây một lần —
    /// thêm từng cái qua <see cref="AddNode"/> thì dây giữa các node mới với
    /// nhau không được vẽ, vì lúc thêm node đầu thì node đích chưa có mặt.
    /// </summary>
    public void AddNodes(IReadOnlyCollection<StoryNode> nodes)
    {
        _graph.Nodes.AddRange(nodes);
        Rebuild(_graph);
        SelectOnly(nodes.Select(n => n.Id).ToList());
        Changed?.Invoke();
    }

    public void DeleteNodes(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            _graph.Nodes.RemoveAll(x => x.Id == id);
            foreach (var other in _graph.Nodes) other.ClearLinksTo(id);

            var gn = _graphEdit.GetChildren().OfType<GraphNode>().FirstOrDefault(x => x.Name == id);
            if (gn is not null)
            {
                _graphEdit.RemoveChild(gn);
                gn.QueueFree();
            }
            _portHandles.Remove(id);
        }

        // Các dây trỏ tới node vừa xoá đã null ở data model, nhưng GraphEdit
        // không tự biết — dựng lại toàn bộ để đường nối trên canvas khớp lại.
        Rebuild(_graph);
        Changed?.Invoke();
    }

    public static void SetOutgoing(StoryNode node, string handle, string? target)
    {
        switch (node)
        {
            case SceneNode s: s.Next = target; break;
            case VariableNode v: v.Next = target; break;
            case JumpNode j: j.Target = target; break;
            case ConditionNode c:
                if (handle == "true") c.WhenTrue = target;
                else if (handle == "false") c.WhenFalse = target;
                break;
            case ChoiceNode ch:
                var opt = ch.Options.FirstOrDefault(o => o.Id == handle);
                if (opt is not null) opt.Next = target;
                break;
        }
    }

    private GraphNode BuildGraphNode(StoryNode node)
    {
        var gn = new GraphNode
        {
            Name = node.Id,
            Title = TitleFor(node),
            PositionOffset = new Vector2(node.Position.X, node.Position.Y),
        };

        var titleBar = new StyleBoxFlat { BgColor = TypeColor(node) };
        titleBar.SetCornerRadiusAll(6);
        titleBar.SetContentMarginAll(6);
        gn.AddThemeStyleboxOverride("titlebar", titleBar);
        gn.AddThemeStyleboxOverride("titlebar_selected", titleBar);

        var handles = new List<string>();
        _portHandles[node.Id] = handles;

        var summary = new Label
        {
            Text = Summary(node),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(160, 0),
        };
        summary.AddThemeColorOverride("font_color", Palette.Text2);
        gn.AddChild(summary);
        gn.SetSlot(0, node is not CommentNode, 0, Palette.Text3, false, 0, Palette.Text3);
        handles.Add(""); // hàng 0 là cổng vào, không có handle cổng ra

        switch (node)
        {
            case ChoiceNode choice:
                foreach (var option in choice.Options)
                {
                    AddOutputRow(gn, handles, option.Text[option.Text.Locales.FirstOrDefault() ?? "vi"] ?? option.Id, Palette.NodeChoice, option.Id);
                }
                break;
            case ConditionNode:
                AddOutputRow(gn, handles, "Đúng", Palette.Ok, "true");
                AddOutputRow(gn, handles, "Sai", Palette.Err, "false");
                break;
            case SceneNode:
            case VariableNode:
            case JumpNode:
                AddOutputRow(gn, handles, "→ tiếp theo", Palette.Text3, "");
                break;
            // EndingNode và CommentNode không có cổng ra.
        }

        return gn;
    }

    /// <summary>
    /// Đổi điểm bắt đầu của chương và cập nhật dấu ▶ trên canvas.
    /// </summary>
    /// <remarks>
    /// Chỉ sửa tiêu đề của hai node liên quan chứ không gọi <see cref="RefreshNode"/>,
    /// vì dựng lại GraphNode làm nó mất trạng thái đang chọn và panel thuộc tính
    /// sẽ trống ngay sau khi người dùng vừa bấm nút trong đó.
    /// </remarks>
    public void SetEntry(string nodeId)
    {
        var previous = _graph.Entry;
        _graph.Entry = nodeId;
        foreach (var gn in _graphEdit.GetChildren().OfType<GraphNode>())
        {
            var id = gn.Name.ToString();
            if (id != previous && id != nodeId) continue;
            var node = _graph.Find(id);
            if (node is not null) gn.Title = TitleFor(node);
        }
        Changed?.Invoke();
    }

    private string TitleFor(StoryNode node)
    {
        var title = $"{TypeIcon(node)}  {node.DisplayName}";
        return node.Id == _graph.Entry ? $"▶ {title}" : title;
    }

    private static void AddOutputRow(GraphNode gn, List<string> handles, string label, Color color, string handle)
    {
        var row = new Label { Text = label };
        row.AddThemeColorOverride("font_color", Palette.Text2);
        gn.AddChild(row);
        gn.SetSlot(handles.Count, false, 0, color, true, 0, color);
        handles.Add(handle);
    }

    public static string TypeIcon(StoryNode node) => node switch
    {
        SceneNode => "▣",
        ChoiceNode => "◈",
        ConditionNode => "◇",
        VariableNode => "≡",
        JumpNode => "→",
        EndingNode => "★",
        CommentNode => "✎",
        _ => "?",
    };

    public static Color TypeColor(StoryNode node) => node switch
    {
        SceneNode => Palette.NodeScene,
        ChoiceNode => Palette.NodeChoice,
        ConditionNode => Palette.NodeCondition,
        VariableNode => Palette.NodeVariable,
        EndingNode => Palette.NodeEnding,
        JumpNode => Palette.NodeJump,
        CommentNode => Palette.NodeComment,
        _ => Palette.Text3,
    };

    private static string Summary(StoryNode node) => node switch
    {
        SceneNode s => string.IsNullOrEmpty(s.Scene) ? "(chưa chọn cảnh)" : $"Cảnh: {s.Scene}",
        ChoiceNode c => c.Prompt?[c.Prompt.Locales.FirstOrDefault() ?? "vi"] ?? "(chưa có câu hỏi)",
        ConditionNode cond when cond.Condition is CompareCondition cmp =>
            string.IsNullOrEmpty(cmp.Variable) ? "(chưa chọn biến)" : $"{cmp.Variable} {OpText(cmp.Op)} {cmp.Value}",
        ConditionNode => "Điều kiện ghép",
        VariableNode v => v.Effects.Count == 0
            ? "(chưa có tác động)"
            : string.Join(", ", v.Effects.Select(e => $"{e.Variable} {AssignText(e.Op)}")),
        JumpNode j => string.IsNullOrEmpty(j.Target) ? "(chưa nối)" : $"→ {j.Target}",
        EndingNode e => e.Name[e.Name.Locales.FirstOrDefault() ?? "vi"] ?? "(chưa đặt tên)",
        CommentNode c => c.Text,
        _ => "",
    };

    private static string OpText(ComparisonOp op) => op switch
    {
        ComparisonOp.Eq => "=",
        ComparisonOp.Neq => "≠",
        ComparisonOp.Gt => ">",
        ComparisonOp.Gte => "≥",
        ComparisonOp.Lt => "<",
        ComparisonOp.Lte => "≤",
        _ => "?",
    };

    private static string AssignText(AssignOp op) => op switch
    {
        AssignOp.Set => "=",
        AssignOp.Add => "+=",
        AssignOp.Sub => "-=",
        AssignOp.Mul => "*=",
        AssignOp.Div => "/=",
        AssignOp.Toggle => "đảo",
        _ => "?",
    };
}
