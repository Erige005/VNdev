using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNdev.Core.Model;

public enum VariableType
{
    Number,
    Boolean,
    Text,
}

/// <summary>
/// Giá trị một biến có thể mang: số, đúng/sai, hoặc chuỗi.
/// </summary>
/// <remarks>
/// Dùng struct thay vì <c>object</c> để tránh boxing và để so sánh có ngữ
/// nghĩa rõ ràng. Bộ chuyển JSON đi kèm ghi thẳng ra số, true/false hoặc chuỗi
/// chứ không bọc thêm lớp nào — file dự án phải đọc được bằng mắt.
/// </remarks>
[JsonConverter(typeof(VarValueConverter))]
public readonly struct VarValue : IEquatable<VarValue>
{
    public VariableType Kind { get; }
    public double Number { get; }
    public bool Boolean { get; }
    public string Text { get; }

    private VarValue(VariableType kind, double number, bool boolean, string text)
    {
        Kind = kind;
        Number = number;
        Boolean = boolean;
        Text = text;
    }

    public static VarValue Of(double value) => new(VariableType.Number, value, false, string.Empty);
    public static VarValue Of(bool value) => new(VariableType.Boolean, 0, value, string.Empty);
    public static VarValue Of(string value) => new(VariableType.Text, 0, false, value);

    public static VarValue Default(VariableType type) => type switch
    {
        VariableType.Number => Of(0d),
        VariableType.Boolean => Of(false),
        _ => Of(string.Empty),
    };

    /// <summary>Đọc như số. Đúng/sai quy ước thành 1 và 0.</summary>
    public double AsNumber() => Kind switch
    {
        VariableType.Number => Number,
        VariableType.Boolean => Boolean ? 1 : 0,
        _ => double.TryParse(Text, out var parsed) ? parsed : 0,
    };

    public bool AsBoolean() => Kind switch
    {
        VariableType.Boolean => Boolean,
        VariableType.Number => Number != 0,
        _ => !string.IsNullOrEmpty(Text),
    };

    public bool Equals(VarValue other) => Kind == other.Kind && Kind switch
    {
        VariableType.Number => Math.Abs(Number - other.Number) < 1e-9,
        VariableType.Boolean => Boolean == other.Boolean,
        _ => Text == other.Text,
    };

    public override bool Equals(object? obj) => obj is VarValue other && Equals(other);

    public override int GetHashCode() => Kind switch
    {
        VariableType.Number => Number.GetHashCode(),
        VariableType.Boolean => Boolean.GetHashCode(),
        _ => Text.GetHashCode(StringComparison.Ordinal),
    };

    public override string ToString() => Kind switch
    {
        VariableType.Number => Number % 1 == 0
            ? ((long)Number).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        VariableType.Boolean => Boolean ? "true" : "false",
        _ => Text,
    };
}

internal sealed class VarValueConverter : JsonConverter<VarValue>
{
    public override VarValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => VarValue.Of(reader.GetDouble()),
            JsonTokenType.True => VarValue.Of(true),
            JsonTokenType.False => VarValue.Of(false),
            JsonTokenType.String => VarValue.Of(reader.GetString() ?? string.Empty),
            _ => throw new JsonException("Giá trị biến phải là số, true/false hoặc chuỗi."),
        };

    public override void Write(Utf8JsonWriter writer, VarValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case VariableType.Number:
                writer.WriteNumberValue(value.Number);
                break;
            case VariableType.Boolean:
                writer.WriteBooleanValue(value.Boolean);
                break;
            default:
                writer.WriteStringValue(value.Text);
                break;
        }
    }
}

/// <summary>Khai báo một biến của dự án.</summary>
/// <remarks>
/// Biến phải khai báo trước khi dùng — bộ kiểm tra báo lỗi nếu một node tham
/// chiếu tới biến không tồn tại. Ràng buộc này bắt lỗi gõ sai tên ngay trong
/// editor thay vì để người chơi phát hiện.
/// </remarks>
public sealed class VariableDef
{
    public string Id { get; set; } = string.Empty;
    public VariableType Type { get; set; } = VariableType.Number;
    public VarValue Initial { get; set; } = VarValue.Of(0d);
    public string? Description { get; set; }

    /// <summary>Giữ giá trị qua nhiều lần chơi — dùng cho New Game+ và nội dung ẩn.</summary>
    public bool Persistent { get; set; }
}

public enum ComparisonOp { Eq, Neq, Gt, Gte, Lt, Lte }

public enum AssignOp { Set, Add, Sub, Mul, Div, Toggle }

/// <summary>Một tác động lên biến, phát sinh khi người chơi chọn hoặc khi đi qua node.</summary>
public sealed class Effect
{
    public string Variable { get; set; } = string.Empty;
    public AssignOp Op { get; set; } = AssignOp.Add;
    public VarValue? Value { get; set; }
}

/// <summary>
/// Biểu thức điều kiện rẽ nhánh.
/// </summary>
/// <remarks>
/// Cấu trúc cây cho phép ghép AND/OR nhiều tầng, nhưng giao diện chỉ phơi ra
/// một tầng phẳng với nút "và / hoặc" — đủ cho gần như mọi nhu cầu thực tế mà
/// không bắt người viết truyện phải nghĩ như lập trình viên.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CompareCondition), "compare")]
[JsonDerivedType(typeof(AndCondition), "and")]
[JsonDerivedType(typeof(OrCondition), "or")]
[JsonDerivedType(typeof(NotCondition), "not")]
public abstract class Condition
{
    /// <summary>Mọi tên biến mà điều kiện này tham chiếu tới.</summary>
    public abstract void CollectVariables(ISet<string> into);
}

public sealed class CompareCondition : Condition
{
    public string Variable { get; set; } = string.Empty;
    public ComparisonOp Op { get; set; } = ComparisonOp.Gte;
    public VarValue Value { get; set; } = VarValue.Of(0d);

    public override void CollectVariables(ISet<string> into) => into.Add(Variable);
}

public sealed class AndCondition : Condition
{
    public List<Condition> Operands { get; set; } = new();

    public override void CollectVariables(ISet<string> into)
    {
        foreach (var operand in Operands) operand.CollectVariables(into);
    }
}

public sealed class OrCondition : Condition
{
    public List<Condition> Operands { get; set; } = new();

    public override void CollectVariables(ISet<string> into)
    {
        foreach (var operand in Operands) operand.CollectVariables(into);
    }
}

public sealed class NotCondition : Condition
{
    public Condition Operand { get; set; } = new CompareCondition();

    public override void CollectVariables(ISet<string> into) => Operand.CollectVariables(into);
}
