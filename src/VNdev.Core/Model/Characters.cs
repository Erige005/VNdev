namespace VNdev.Core.Model;

/// <summary>Một biểu cảm của nhân vật.</summary>
/// <remarks>
/// <see cref="Layers"/> cho phép sprite phân lớp: thân giữ nguyên, chỉ đổi
/// phần mặt. Nhờ vậy hoạ sĩ không phải vẽ lại toàn thân cho mỗi cảm xúc —
/// một trong những chi phí lớn nhất khi làm visual novel.
/// </remarks>
public sealed class Expression
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Ảnh dùng khi nhân vật không bật chế độ phân lớp.</summary>
    public string? Sprite { get; set; }

    /// <summary>Các lớp ghép lại, vẽ theo đúng thứ tự trong danh sách.</summary>
    public List<string>? Layers { get; set; }
}

public sealed class CharacterData
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Tên hiện trong hộp thoại, theo từng ngôn ngữ.</summary>
    public Localized DisplayName { get; set; } = new();

    /// <summary>Màu tên trong hộp thoại, dạng #RRGGBB. Giúp nhận ra ai đang nói.</summary>
    public string Color { get; set; } = "#ffffff";

    public List<Expression> Expressions { get; set; } = new();

    /// <summary>Biểu cảm dùng khi lời thoại không chỉ định rõ.</summary>
    public string DefaultExpression { get; set; } = "normal";

    /// <summary>Bật khi các biểu cảm dùng <see cref="Expression.Layers"/> thay vì một ảnh.</summary>
    public bool Layered { get; set; }

    /// <summary>
    /// Mô tả tính cách và cách nói bằng ngôn ngữ tự nhiên.
    /// </summary>
    /// <remarks>
    /// Đây là ngữ cảnh AI đọc trước khi viết bất kỳ lời thoại nào cho nhân vật
    /// này. Trường này là lý do AI giữ được giọng nhân vật nhất quán thay vì
    /// viết ra thứ văn trung tính vô hồn.
    /// </remarks>
    public string? VoiceProfile { get; set; }

    /// <summary>Thư mục chứa file lồng tiếng của nhân vật, nếu có.</summary>
    public string? VoiceDir { get; set; }

    public Expression? FindExpression(string id) => Expressions.FirstOrDefault(e => e.Id == id);
}
