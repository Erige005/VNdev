using System.Text.Json.Serialization;

namespace VNdev.Core.Model;

public struct GraphPosition
{
    public float X { get; set; }
    public float Y { get; set; }

    public GraphPosition(float x, float y) { X = x; Y = y; }
}

/// <summary>
/// Một node trên đồ thị cốt truyện.
/// </summary>
/// <remarks>
/// Thuộc tính <c>type</c> trong JSON do bộ tuần tự hoá tự sinh ra từ các khai
/// báo <c>JsonDerivedType</c> bên dưới, nên định dạng file khớp đúng bản thiết
/// kế mà không phải viết tay lớp chuyển đổi nào.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SceneNode), "scene")]
[JsonDerivedType(typeof(ChoiceNode), "choice")]
[JsonDerivedType(typeof(ConditionNode), "condition")]
[JsonDerivedType(typeof(VariableNode), "variable")]
[JsonDerivedType(typeof(JumpNode), "jump")]
[JsonDerivedType(typeof(EndingNode), "ending")]
[JsonDerivedType(typeof(CommentNode), "comment")]
public abstract class StoryNode
{
    public string Id { get; set; } = string.Empty;
    public GraphPosition Position { get; set; }

    /// <summary>Nhãn tác giả tự đặt, hiện trên node. Không ảnh hưởng game.</summary>
    public string? Label { get; set; }

    /// <summary>Node thuộc nhóm nào — dùng để gập mở khối trên canvas.</summary>
    public string? Group { get; set; }

    /// <summary>Màu tuyến truyện, giúp phân biệt các route bằng mắt.</summary>
    public string? RouteColor { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrEmpty(Label) ? Id : Label!;

    /// <summary>
    /// Các node mà node này trỏ tới, kèm id cổng ra tương ứng.
    /// </summary>
    /// <remarks>
    /// Gom vào một chỗ vì mỗi loại node lưu liên kết theo cách riêng. Bộ kiểm
    /// tra, minimap, tự sắp xếp và engine chạy game đều cần đúng thông tin này.
    /// </remarks>
    public abstract IEnumerable<(string? Handle, string? Target)> Outgoing();

    /// <summary>Gỡ mọi liên kết trỏ tới node đã bị xoá.</summary>
    public abstract void ClearLinksTo(string nodeId);

    /// <summary>Mọi biến node này đụng tới, dù để đọc hay để ghi.</summary>
    public virtual void CollectVariables(ISet<string> into) { }
}

/// <summary>Trỏ tới một cảnh rồi đi tiếp.</summary>
public sealed class SceneNode : StoryNode
{
    public string Scene { get; set; } = string.Empty;
    public string? Next { get; set; }

    public override IEnumerable<(string?, string?)> Outgoing() => new (string?, string?)[] { (null, Next) };

    public override void ClearLinksTo(string nodeId)
    {
        if (Next == nodeId) Next = null;
    }
}

public sealed class ChoiceOption
{
    public string Id { get; set; } = string.Empty;
    public Localized Text { get; set; } = new();
    public List<Effect> Effects { get; set; } = new();

    /// <summary>Ẩn phương án khi điều kiện không thoả.</summary>
    public Condition? ShowIf { get; set; }

    public string? Next { get; set; }
}

/// <summary>Người chơi chọn một trong nhiều hướng.</summary>
public sealed class ChoiceNode : StoryNode
{
    public Localized? Prompt { get; set; }
    public List<ChoiceOption> Options { get; set; } = new();

    /// <summary>Giới hạn thời gian chọn, tính bằng giây. Bỏ trống là không giới hạn.</summary>
    public float? TimeLimit { get; set; }

    /// <summary>Phương án tự chọn khi hết giờ.</summary>
    public string? TimeoutOption { get; set; }

    public override IEnumerable<(string?, string?)> Outgoing()
        => Options.Select(o => ((string?)o.Id, o.Next));

    public override void ClearLinksTo(string nodeId)
    {
        foreach (var option in Options)
        {
            if (option.Next == nodeId) option.Next = null;
        }
    }

    public override void CollectVariables(ISet<string> into)
    {
        foreach (var option in Options)
        {
            foreach (var effect in option.Effects) into.Add(effect.Variable);
            option.ShowIf?.CollectVariables(into);
        }
    }
}

/// <summary>Rẽ nhánh tự động theo biến, người chơi không thấy gì.</summary>
public sealed class ConditionNode : StoryNode
{
    public Condition Condition { get; set; } = new CompareCondition();
    public string? WhenTrue { get; set; }
    public string? WhenFalse { get; set; }

    public override IEnumerable<(string?, string?)> Outgoing()
        => new (string?, string?)[] { ("true", WhenTrue), ("false", WhenFalse) };

    public override void ClearLinksTo(string nodeId)
    {
        if (WhenTrue == nodeId) WhenTrue = null;
        if (WhenFalse == nodeId) WhenFalse = null;
    }

    public override void CollectVariables(ISet<string> into) => Condition.CollectVariables(into);
}

/// <summary>Thay đổi biến rồi đi tiếp.</summary>
public sealed class VariableNode : StoryNode
{
    public List<Effect> Effects { get; set; } = new();
    public string? Next { get; set; }

    public override IEnumerable<(string?, string?)> Outgoing() => new (string?, string?)[] { (null, Next) };

    public override void ClearLinksTo(string nodeId)
    {
        if (Next == nodeId) Next = null;
    }

    public override void CollectVariables(ISet<string> into)
    {
        foreach (var effect in Effects) into.Add(effect.Variable);
    }
}

/// <summary>Nhảy tới node khác — dùng để tránh dây nối rối khi truyện dài.</summary>
public sealed class JumpNode : StoryNode
{
    public string? Target { get; set; }

    public override IEnumerable<(string?, string?)> Outgoing() => new (string?, string?)[] { (null, Target) };

    public override void ClearLinksTo(string nodeId)
    {
        if (Target == nodeId) Target = null;
    }
}

/// <summary>Điểm kết thúc một tuyến truyện.</summary>
public sealed class EndingNode : StoryNode
{
    public Localized Name { get; set; } = new();

    /// <summary>Ảnh CG mở khoá khi đạt ending này.</summary>
    public string? Cg { get; set; }

    public override IEnumerable<(string?, string?)> Outgoing() => Array.Empty<(string?, string?)>();

    public override void ClearLinksTo(string nodeId) { }
}

/// <summary>Ghi chú cho tác giả và đồng đội. Engine bỏ qua hoàn toàn.</summary>
public sealed class CommentNode : StoryNode
{
    public string Text { get; set; } = string.Empty;

    public override IEnumerable<(string?, string?)> Outgoing() => Array.Empty<(string?, string?)>();

    public override void ClearLinksTo(string nodeId) { }
}

public sealed class NodeGroup
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool Collapsed { get; set; }
}

/// <summary>
/// Một đồ thị cốt truyện, thường tương ứng một chương.
/// </summary>
/// <remarks>
/// Tách theo chương để file không phình to và để hai người sửa hai chương khác
/// nhau thì Git merge không xung đột.
/// </remarks>
public sealed class StoryGraph
{
    public string Id { get; set; } = string.Empty;
    public Localized Title { get; set; } = new();

    /// <summary>Node bắt đầu. Đồ thị của chương đầu tiên là điểm vào của cả game.</summary>
    public string Entry { get; set; } = string.Empty;

    public List<StoryNode> Nodes { get; set; } = new();
    public List<NodeGroup>? Groups { get; set; }

    public StoryNode? Find(string id) => Nodes.FirstOrDefault(n => n.Id == id);
}
