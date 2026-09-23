using System.Globalization;
using System.Text;

namespace VNdev.Core;

/// <summary>
/// Sinh id cho các thực thể trong dự án.
/// </summary>
/// <remarks>
/// Id đi thẳng vào file JSON mà con người sẽ đọc và diff trên Git, nên chúng
/// ưu tiên dễ đọc hơn là ngắn: <c>scene_san_truong_a3f2</c> nói lên điều gì đó,
/// còn <c>c7f3a91b</c> thì không.
/// </remarks>
public static class Ids
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Bỏ dấu, hạ chữ thường, thay mọi ký tự không an toàn bằng gạch dưới.
    /// </summary>
    /// <remarks>
    /// Chuẩn hoá Unicode về dạng tách dấu rồi loại các ký tự dấu, nên xử lý
    /// được cả tiếng Việt lẫn các ngôn ngữ Latin có dấu khác. Riêng chữ đ phải
    /// thay tay vì nó là một ký tự riêng chứ không phải d cộng dấu.
    /// </remarks>
    public static string Slugify(string input)
    {
        var replaced = input.Replace('đ', 'd').Replace('Đ', 'D');
        var decomposed = replaced.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = true; // chặn gạch dưới ở đầu chuỗi

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            var lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z' || lower is >= '0' and <= '9')
            {
                builder.Append(lower);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('_');
                lastWasSeparator = true;
            }
        }

        var slug = builder.ToString().TrimEnd('_');
        return slug.Length > 48 ? slug[..48].TrimEnd('_') : slug;
    }

    public static string RandomSuffix(int length = 4)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];
        }
        return new string(chars);
    }

    /// <summary>
    /// Tạo id duy nhất trong một tập id đã có.
    /// </summary>
    /// <remarks>
    /// Thêm hậu tố ngẫu nhiên thay vì đếm tăng dần, vì hai người làm việc song
    /// song trên hai nhánh Git đều đếm từ 1 và sẽ đụng nhau khi merge.
    /// </remarks>
    public static string Make(string prefix, string label, IEnumerable<string>? taken = null)
    {
        var used = taken as ISet<string> ?? new HashSet<string>(taken ?? Array.Empty<string>());

        var slug = Slugify(label);
        if (slug.Length == 0) slug = "untitled";

        var plain = $"{prefix}_{slug}";
        if (!used.Contains(plain)) return plain;

        for (var attempt = 0; attempt < 32; attempt++)
        {
            var candidate = $"{plain}_{RandomSuffix()}";
            if (!used.Contains(candidate)) return candidate;
        }

        return $"{plain}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }
}
