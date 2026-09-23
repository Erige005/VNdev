using System.Text.Json.Serialization;

namespace VNdev.Core.Model;

/// <summary>
/// Chuỗi hiển thị cho người chơi, lưu theo từng ngôn ngữ.
/// </summary>
/// <remarks>
/// Mọi văn bản người chơi nhìn thấy đều mang kiểu này, kể cả khi dự án chỉ có
/// một ngôn ngữ. Nếu ban đầu để <c>string</c> rồi sau mới thêm đa ngôn ngữ, ta
/// sẽ phải migrate toàn bộ dự án của người dùng. Tốn thêm vài ký tự ngay từ
/// đầu rẻ hơn nhiều so với một lần phá vỡ tương thích.
///
/// Cú pháp định dạng inline trong chuỗi, do engine phân tích khi hiển thị:
/// <code>
///   **đậm**  *nghiêng*  {color:#ff0000|chữ đỏ}
///   {ruby:漢字|かんじ}        furigana cho tiếng Nhật, pinyin cho tiếng Trung
///   {shake:2|chữ rung}
///   {speed:0.5|chữ chậm}
///   {var:player_name}         chèn giá trị biến
/// </code>
/// Lưu dạng chuỗi thô thay vì cây cú pháp để file vẫn diff được trên Git.
/// </remarks>
public sealed class Localized
{
    [JsonExtensionData]
    public Dictionary<string, object?> Raw { get; set; } = new();

    public Localized() { }

    public Localized(string locale, string value)
    {
        Raw[locale] = value;
    }

    [JsonIgnore]
    public IEnumerable<string> Locales => Raw.Keys;

    public string? this[string locale]
    {
        get => Raw.TryGetValue(locale, out var v) ? v?.ToString() : null;
        set => Raw[locale] = value;
    }

    /// <summary>
    /// Lấy chuỗi theo ngôn ngữ, có đường lùi.
    /// </summary>
    /// <remarks>
    /// Thứ tự ưu tiên: ngôn ngữ yêu cầu, rồi ngôn ngữ dự phòng, rồi bất kỳ bản
    /// dịch nào có nội dung. Không bao giờ trả về rỗng khi vẫn còn thứ để hiện,
    /// vì với người chơi thì một dòng thoại sai ngôn ngữ vẫn hơn hộp thoại trống.
    /// </remarks>
    public string Resolve(string locale, string? fallback = null)
    {
        var primary = this[locale];
        if (!string.IsNullOrEmpty(primary)) return primary!;

        if (fallback is not null)
        {
            var secondary = this[fallback];
            if (!string.IsNullOrEmpty(secondary)) return secondary!;
        }

        foreach (var value in Raw.Values)
        {
            var text = value?.ToString();
            if (!string.IsNullOrEmpty(text)) return text!;
        }
        return string.Empty;
    }

    /// <summary>Đã có bản dịch cho ngôn ngữ này chưa, và không phải chuỗi trắng.</summary>
    public bool Has(string locale) => !string.IsNullOrWhiteSpace(this[locale]);

    public Localized Set(string locale, string value)
    {
        this[locale] = value;
        return this;
    }

    public override string ToString() => Resolve(Raw.Keys.FirstOrDefault() ?? "");
}
