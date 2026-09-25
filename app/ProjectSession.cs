using System;
using System.Collections.Generic;
using System.Linq;
using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Bọc file system thật để biết mỗi lần app ghi xuống đĩa. Lịch sử hoàn tác
/// và dòng "Đã lưu lúc…" ở thanh trạng thái đều dựa vào tín hiệu này.
/// </summary>
/// <remarks>
/// Móc vào tầng ghi file thay vì bắt từng màn hình tự báo "tôi vừa sửa": có
/// bốn màn hình, mỗi cái lưu theo cách riêng, và chỉ cần một màn hình mới
/// quên báo là thao tác đó không hoàn tác được mà không ai hay.
/// </remarks>
public sealed class NotifyingFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;

    public event Action? Written;

    /// <summary>Tắt tín hiệu trong lúc chính lịch sử hoàn tác đang ghi lại trạng thái cũ.</summary>
    public bool Muted { get; set; }

    public NotifyingFileSystem(IFileSystem inner) { _inner = inner; }

    public string ReadText(string path) => _inner.ReadText(path);
    public bool Exists(string path) => _inner.Exists(path);
    public void CreateDirectory(string path) => _inner.CreateDirectory(path);
    public IReadOnlyList<string> ListFiles(string path) => _inner.ListFiles(path);

    public void WriteText(string path, string content)
    {
        _inner.WriteText(path, content);
        if (!Muted) Written?.Invoke();
    }

    public void DeleteFile(string path)
    {
        _inner.DeleteFile(path);
        if (!Muted) Written?.Invoke();
    }
}

/// <summary>
/// Trạng thái toàn dự án tại một thời điểm, dạng chuỗi JSON đúng như trên đĩa.
/// </summary>
/// <remarks>
/// Chụp cả dự án thay vì ghi từng lệnh "đổi trường X từ a sang b": dự án chỉ
/// vài trăm KB, còn cách ghi từng lệnh bắt mọi chỗ sửa dữ liệu trong app phải
/// viết thêm hàm đảo ngược — quên một chỗ là hoàn tác sai mà không ai biết.
/// </remarks>
public sealed class ProjectSnapshot
{
    public required Dictionary<string, string> Files { get; init; }

    public static ProjectSnapshot Capture(LoadedProject loaded)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ProjectPaths.ProjectFile] = ProjectIo.Serialize(loaded.Project),
        };
        foreach (var g in loaded.Graphs.Values) files[ProjectPaths.Graph(g.Id)] = ProjectIo.Serialize(g);
        foreach (var s in loaded.Scenes.Values) files[ProjectPaths.Scene(s.Id)] = ProjectIo.Serialize(s);
        foreach (var c in loaded.Characters.Values) files[ProjectPaths.Character(c.Id)] = ProjectIo.Serialize(c);
        return new ProjectSnapshot { Files = files };
    }

    public bool SameAs(ProjectSnapshot other)
        => Files.Count == other.Files.Count
           && Files.All(kv => other.Files.TryGetValue(kv.Key, out var v) && v == kv.Value);

    /// <summary>Dựng lại dữ liệu trong bộ nhớ từ bản chụp.</summary>
    /// <remarks>
    /// Không dùng <see cref="ProjectIo.Load"/> vì hàm đó chỉ nạp những cảnh có
    /// node trỏ tới, trong khi cảnh vừa tạo ở tab Cảnh chưa chắc đã được nối.
    /// </remarks>
    public LoadedProject ToLoaded()
    {
        var loaded = new LoadedProject
        {
            Project = ProjectIo.Deserialize<ProjectData>(Files[ProjectPaths.ProjectFile], ProjectPaths.ProjectFile),
        };
        foreach (var (path, json) in Files)
        {
            if (path.EndsWith(".graph.json", StringComparison.Ordinal))
            {
                var g = ProjectIo.Deserialize<StoryGraph>(json, path);
                loaded.Graphs[g.Id] = g;
            }
            else if (path.EndsWith(".scene.json", StringComparison.Ordinal))
            {
                var s = ProjectIo.Deserialize<SceneData>(json, path);
                loaded.Scenes[s.Id] = s;
            }
            else if (path.EndsWith(".character.json", StringComparison.Ordinal))
            {
                var c = ProjectIo.Deserialize<CharacterData>(json, path);
                loaded.Characters[c.Id] = c;
            }
        }
        return loaded;
    }
}

/// <summary>
/// Một dự án đang mở: dữ liệu, nơi lưu và lịch sử hoàn tác. Sống lâu hơn
/// <see cref="EditorScreen"/> — hoàn tác xong thì màn hình được dựng lại từ
/// dữ liệu mới, còn lịch sử phải giữ nguyên để bấm tiếp.
/// </summary>
public sealed class ProjectSession
{
    private const int MaxHistory = 200;

    private readonly IFileSystem _disk;
    private readonly List<ProjectSnapshot> _undo = new();
    private readonly List<ProjectSnapshot> _redo = new();
    private ProjectSnapshot _current;

    public LoadedProject Loaded { get; private set; }
    public NotifyingFileSystem Fs { get; }
    public string Dir { get; }
    public DateTime? LastSaved { get; private set; }

    /// <summary>Có thay đổi đã ghi xuống đĩa nhưng chưa gom thành một bước hoàn tác.</summary>
    public bool HasPendingChange { get; private set; }

    public bool CanUndo => _undo.Count > 0 || HasPendingChange;
    public bool CanRedo => _redo.Count > 0 && !HasPendingChange;

    public event Action? Saved;

    public string Title => Loaded.Project.Title[Loaded.Project.PrimaryLocale] ?? Loaded.Project.Id;

    public ProjectSession(LoadedProject loaded, IFileSystem disk, string dir)
    {
        Loaded = loaded;
        _disk = disk;
        Dir = dir;
        Fs = new NotifyingFileSystem(disk);
        Fs.Written += () =>
        {
            HasPendingChange = true;
            LastSaved = DateTime.Now;
            Saved?.Invoke();
        };
        _current = ProjectSnapshot.Capture(loaded);
    }

    /// <summary>
    /// Gom mọi thay đổi từ lần gom trước thành một bước hoàn tác.
    /// </summary>
    /// <remarks>
    /// Được gọi sau khi người dùng ngừng tay một chút (xem EditorScreen), để gõ
    /// một câu thoại 40 chữ là một bước hoàn tác chứ không phải 40 bước.
    /// </remarks>
    public void Commit()
    {
        HasPendingChange = false;
        var now = ProjectSnapshot.Capture(Loaded);
        if (now.SameAs(_current)) return;

        _undo.Add(_current);
        if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        _redo.Clear();
        _current = now;
    }

    public bool Undo()
    {
        Commit();
        if (_undo.Count == 0) return false;
        _redo.Add(_current);
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        Commit();
        if (_redo.Count == 0) return false;
        _undo.Add(_current);
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    private void Restore(ProjectSnapshot target)
    {
        // Chỉ ghi file thực sự khác và chỉ xoá file mà chính app đã tạo ra
        // giữa hai bản chụp, để không đụng vào file người dùng tự đặt vào.
        Fs.Muted = true;
        try
        {
            foreach (var (path, json) in target.Files)
            {
                if (!_current.Files.TryGetValue(path, out var old) || old != json) _disk.WriteText(path, json);
            }
            foreach (var path in _current.Files.Keys.Where(p => !target.Files.ContainsKey(p)))
            {
                _disk.DeleteFile(path);
            }
        }
        finally
        {
            Fs.Muted = false;
        }

        _current = target;
        Loaded = target.ToLoaded();
        LastSaved = DateTime.Now;
        Saved?.Invoke();
    }
}

/// <summary>
/// Mở và tạo dự án — dùng chung cho màn hình chào, menu Tệp và danh sách dự
/// án gần đây, để ba đường vào cùng kiểm tra và cùng báo lỗi một kiểu.
/// </summary>
public static class ProjectOpener
{
    public static ProjectSession? Open(string dir, out string? error)
    {
        error = null;
        if (!System.IO.Directory.Exists(dir))
        {
            error = $"Không còn thư mục \"{dir}\" — có thể đã bị đổi tên, chuyển chỗ hoặc xoá.";
            return null;
        }

        var fs = new DiskFileSystem(dir);
        if (!fs.Exists(ProjectPaths.ProjectFile))
        {
            error = "Không thấy project.json trong thư mục này — đây chưa phải thư mục dự án VNdev.";
            return null;
        }

        try
        {
            var loaded = ProjectIo.Load(fs);
            return Register(new ProjectSession(loaded, fs, dir));
        }
        catch (Exception ex)
        {
            error = $"Không mở được dự án: {ex.Message}";
            return null;
        }
    }

    public static ProjectSession? Create(string dir, string title, string? author, out string? error)
    {
        error = null;
        var fs = new DiskFileSystem(dir);
        if (fs.Exists(ProjectPaths.ProjectFile))
        {
            error = "Thư mục này đã có dự án rồi. Chọn thư mục khác hoặc dùng Mở dự án.";
            return null;
        }

        try
        {
            var loaded = ProjectIo.Scaffold(fs, title, string.IsNullOrWhiteSpace(author) ? null : author, new List<string> { "vi" });
            return Register(new ProjectSession(loaded, fs, dir));
        }
        catch (Exception ex)
        {
            error = $"Không tạo được dự án: {ex.Message}";
            return null;
        }
    }

    private static ProjectSession Register(ProjectSession session)
    {
        AppSettings.Current.AddRecent(session.Dir, session.Title);
        return session;
    }
}
