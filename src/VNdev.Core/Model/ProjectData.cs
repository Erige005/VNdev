namespace VNdev.Core.Model;

/// <summary>Cấu hình hiển thị chữ trong hộp thoại.</summary>
public sealed class TextSettings
{
    /// <summary>Số ký tự hiện mỗi giây. 0 nghĩa là hiện tức thì.</summary>
    public float TypewriterSpeed { get; set; } = 40f;

    public string FontFamily { get; set; } = "Noto Sans";
    public float FontSize { get; set; } = 34f;
    public float LineHeight { get; set; } = 1.5f;

    /// <summary>
    /// Font thay thế cho từng ngôn ngữ.
    /// </summary>
    /// <remarks>
    /// Bắt buộc phải có: font Latin thường không chứa chữ Hán hay chữ Thái, và
    /// luật ngắt dòng của ba hệ chữ này khác nhau hoàn toàn.
    /// </remarks>
    public Dictionary<string, string> LocaleFonts { get; set; } = new();
}

/// <summary>Giao diện game mà người chơi thấy.</summary>
public sealed class ThemeSettings
{
    public string Id { get; set; } = "default_dark";
    public string TextboxColor { get; set; } = "#0a0c16";
    public float TextboxOpacity { get; set; } = 0.78f;
    public string TextColor { get; set; } = "#f2f4fa";
    public string AccentColor { get; set; } = "#7c6cff";
    public float CornerRadius { get; set; } = 12f;
    public string? TitleBackground { get; set; }
}

public sealed class AudioSettings
{
    public float MasterVolume { get; set; } = 1f;
    public float BgmVolume { get; set; } = 0.7f;
    public float SfxVolume { get; set; } = 0.9f;
    public float VoiceVolume { get; set; } = 1f;
    public float AmbientVolume { get; set; } = 0.5f;
}

public struct Resolution
{
    public int Width { get; set; }
    public int Height { get; set; }

    public Resolution(int width, int height) { Width = width; Height = height; }
}

/// <summary>
/// Metadata và cấu hình của cả dự án — nội dung file project.json.
/// </summary>
/// <remarks>
/// Không chứa nội dung truyện: cảnh nằm trong scenes/, đồ thị nằm trong story/,
/// nhân vật nằm trong characters/. File này chỉ nói dự án gồm những gì và chạy
/// theo cấu hình nào.
/// </remarks>
public sealed class ProjectData
{
    /// <summary>Phiên bản định dạng. Tăng khi cấu trúc file đổi không tương thích.</summary>
    public int FormatVersion { get; set; } = Constants.ProjectFormatVersion;

    public string Id { get; set; } = string.Empty;
    public Localized Title { get; set; } = new();
    public string? Author { get; set; }
    public string Version { get; set; } = "0.1.0";
    public Localized? Description { get; set; }

    /// <summary>Ngôn ngữ game hỗ trợ. Phần tử đầu là ngôn ngữ chính.</summary>
    public List<string> Locales { get; set; } = new() { Constants.DefaultLocale };

    /// <summary>Id các đồ thị theo đúng thứ tự chương. Phần tử đầu là điểm vào game.</summary>
    public List<string> Graphs { get; set; } = new();

    public List<string> Characters { get; set; } = new();
    public List<VariableDef> Variables { get; set; } = new();

    public TextSettings Text { get; set; } = new();
    public ThemeSettings Theme { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();

    /// <summary>Độ phân giải thiết kế. Mọi toạ độ trong cảnh tính theo hệ này.</summary>
    public Resolution Resolution { get; set; } = new(Constants.DesignWidth, Constants.DesignHeight);

    public string PrimaryLocale => Locales.Count > 0 ? Locales[0] : Constants.DefaultLocale;
}

public static class Constants
{
    public const int ProjectFormatVersion = 1;
    public const string DefaultLocale = "vi";
    public const int DesignWidth = 1920;
    public const int DesignHeight = 1080;

    /// <summary>Ngôn ngữ giao diện editor được hỗ trợ.</summary>
    public static readonly string[] UiLocales = { "en", "vi", "ja", "zh", "th" };
}
