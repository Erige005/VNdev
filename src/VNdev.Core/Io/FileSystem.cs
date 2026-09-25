namespace VNdev.Core.Io;

/// <summary>
/// Cổng truy cập file, do phía gọi cung cấp.
/// </summary>
/// <remarks>
/// Lõi cố tình không tự đọc đĩa. Cùng đoạn code này phải chạy được trong
/// editor (dùng System.IO), trong game xuất ra (có thể đọc từ gói .pck của
/// Godot), và trong bộ test (dùng bản giả lập trong bộ nhớ). Mỗi nơi cắm một
/// bản cài đặt riêng vào đây.
/// </remarks>
public interface IFileSystem
{
    string ReadText(string path);
    void WriteText(string path, string content);
    bool Exists(string path);
    void CreateDirectory(string path);
    IReadOnlyList<string> ListFiles(string path);

    /// <summary>Xoá một file. Không có file thì bỏ qua, không báo lỗi.</summary>
    void DeleteFile(string path);
}

/// <summary>Bản cài đặt dùng đĩa thật, gắn vào một thư mục gốc.</summary>
public sealed class DiskFileSystem : IFileSystem
{
    private readonly string _root;

    public DiskFileSystem(string root)
    {
        _root = Path.GetFullPath(root);
    }

    /// <summary>
    /// Ghép đường dẫn và chặn mọi lối đi ra ngoài thư mục dự án.
    /// </summary>
    /// <remarks>
    /// Không có lớp chặn này thì một file dự án bị sửa tay có thể khiến app ghi
    /// đè file bất kỳ trên máy người dùng.
    /// </remarks>
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Đường dẫn nằm ngoài thư mục dự án: {relative}");
        }
        return full;
    }

    public string ReadText(string path) => File.ReadAllText(Resolve(path));

    public void WriteText(string path, string content)
    {
        var full = Resolve(path);
        var dir = Path.GetDirectoryName(full);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(full, content);
    }

    public bool Exists(string path)
    {
        var full = Resolve(path);
        return File.Exists(full) || Directory.Exists(full);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(Resolve(path));

    public void DeleteFile(string path)
    {
        var full = Resolve(path);
        if (File.Exists(full)) File.Delete(full);
    }

    public IReadOnlyList<string> ListFiles(string path)
    {
        var full = Resolve(path);
        if (!Directory.Exists(full)) return Array.Empty<string>();

        return Directory.GetFiles(full)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>Bản giả lập trong bộ nhớ, dùng cho test.</summary>
public sealed class MemoryFileSystem : IFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

    public string ReadText(string path)
        => Files.TryGetValue(Normalize(path), out var content)
            ? content
            : throw new FileNotFoundException($"Không tìm thấy {path}");

    public void WriteText(string path, string content) => Files[Normalize(path)] = content;

    public bool Exists(string path)
    {
        var key = Normalize(path);
        return Files.ContainsKey(key) || Directories.Contains(key);
    }

    public void CreateDirectory(string path) => Directories.Add(Normalize(path));

    public void DeleteFile(string path) => Files.Remove(Normalize(path));

    public IReadOnlyList<string> ListFiles(string path)
    {
        var prefix = Normalize(path) + "/";
        return Files.Keys
            .Where(f => f.StartsWith(prefix, StringComparison.Ordinal))
            .Select(f => f[prefix.Length..])
            .Where(name => !name.Contains('/'))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}
