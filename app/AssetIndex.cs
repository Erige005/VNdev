using System.Collections.Generic;
using System.IO;
using System.Linq;
using VNdev.Core.Io;

namespace VNdev.App;

/// <summary>
/// Quét thư mục asset của dự án. Không có nút import, không có thư viện nội
/// bộ phải đồng bộ — người dùng tự chép file bằng Explorer, app chỉ đọc lại
/// (xem ràng buộc số 5 trong CLAUDE.md).
/// </summary>
public static class AssetIndex
{
    public static List<string> Backgrounds(IFileSystem fs) => Filter(fs, ProjectPaths.BackgroundsDir, ProjectPaths.ImageExtensions);
    public static List<string> Sprites(IFileSystem fs) => Filter(fs, ProjectPaths.SpritesDir, ProjectPaths.ImageExtensions);
    public static List<string> Bgm(IFileSystem fs) => Filter(fs, ProjectPaths.BgmDir, ProjectPaths.AudioExtensions);
    public static List<string> Sfx(IFileSystem fs) => Filter(fs, ProjectPaths.SfxDir, ProjectPaths.AudioExtensions);

    private static List<string> Filter(IFileSystem fs, string dir, string[] extensions)
    {
        return fs.ListFiles(dir)
            .Where(name => extensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
            .Select(name => $"{dir}/{name}")
            .ToList();
    }
}
