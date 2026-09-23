namespace VNdev.Core.Io;

/// <summary>
/// Bố cục thư mục của một dự án VNdev.
/// </summary>
/// <remarks>
/// Một dự án là một THƯ MỤC, không phải file nhị phân. Mỗi cảnh, mỗi chương,
/// mỗi nhân vật nằm trong file riêng — nhờ vậy hai người sửa hai cảnh khác
/// nhau thì Git merge không xung đột. Đây là ràng buộc thiết kế có chủ đích,
/// không phải hệ quả tình cờ.
/// </remarks>
public static class ProjectPaths
{
    public const string ProjectFile = "project.json";
    public const string StoryDir = "story";
    public const string ScenesDir = "scenes";
    public const string CharactersDir = "characters";
    public const string I18nDir = "i18n";
    public const string InternalDir = ".vndev";

    public const string BackgroundsDir = "assets/backgrounds";
    public const string SpritesDir = "assets/sprites";
    public const string BgmDir = "assets/bgm";
    public const string SfxDir = "assets/sfx";
    public const string VoiceDir = "assets/voice";
    public const string UiAssetsDir = "assets/ui";

    public static string Graph(string id) => $"{StoryDir}/{id}.graph.json";
    public static string Scene(string id) => $"{ScenesDir}/{id}.scene.json";
    public static string Character(string id) => $"{CharactersDir}/{id}.character.json";
    public static string Locale(string locale) => $"{I18nDir}/{locale}.json";

    /// <summary>Mọi thư mục cần tạo khi khởi tạo dự án mới.</summary>
    public static readonly string[] RequiredDirs =
    {
        StoryDir, ScenesDir, CharactersDir, I18nDir,
        BackgroundsDir, SpritesDir, BgmDir, SfxDir, VoiceDir, UiAssetsDir,
    };

    /// <summary>Đuôi file ảnh mà thư viện asset nhận.</summary>
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    /// <summary>Đuôi file âm thanh mà thư viện asset nhận.</summary>
    public static readonly string[] AudioExtensions = { ".ogg", ".mp3", ".wav" };
}
