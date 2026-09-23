using System.Text.Json;
using System.Text.Json.Serialization;
using VNdev.Core.Model;

namespace VNdev.Core.Io;

/// <summary>Toàn bộ nội dung một dự án sau khi nạp xong.</summary>
public sealed class LoadedProject
{
    public required ProjectData Project { get; init; }
    public Dictionary<string, StoryGraph> Graphs { get; init; } = new();
    public Dictionary<string, SceneData> Scenes { get; init; } = new();
    public Dictionary<string, CharacterData> Characters { get; init; } = new();
}

public sealed class ProjectLoadException : Exception
{
    public string Path { get; }

    public ProjectLoadException(string message, string path, Exception? inner = null)
        : base(message, inner)
    {
        Path = path;
    }
}

public static class ProjectIo
{
    /// <summary>
    /// Cấu hình JSON dùng chung cho mọi thao tác đọc ghi.
    /// </summary>
    /// <remarks>
    /// Thụt lề và tên trường viết thường kiểu camelCase là một phần của định
    /// dạng file. Nếu chúng dao động giữa hai lần lưu, Git sẽ báo cả file thay
    /// đổi dù người dùng chỉ sửa một chữ, và lời hứa "diff được từng dòng
    /// thoại" mất sạch ý nghĩa.
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // Giữ nguyên chữ có dấu và chữ Nhật/Trung/Thái thay vì escape thành \uXXXX,
        // để file mở ra bằng mắt vẫn đọc được.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    public static T Deserialize<T>(string json, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new ProjectLoadException($"File rỗng: {path}", path);
        }
        catch (JsonException ex)
        {
            throw new ProjectLoadException($"Dữ liệu trong file sai cấu trúc: {path}", path, ex);
        }
    }

    private static T ReadJson<T>(IFileSystem fs, string path)
    {
        string raw;
        try
        {
            raw = fs.ReadText(path);
        }
        catch (Exception ex)
        {
            throw new ProjectLoadException($"Không đọc được file: {path}", path, ex);
        }
        return Deserialize<T>(raw, path);
    }

    /// <summary>
    /// Nạp toàn bộ dự án.
    /// </summary>
    /// <remarks>
    /// Nạp hết một lần thay vì nạp lười từng phần: một dự án visual novel dù
    /// lớn cũng chỉ vài megabyte JSON, trong khi việc luôn có đủ dữ liệu trong
    /// bộ nhớ giúp bộ kiểm tra, tìm kiếm toàn văn và AI hoạt động tức thì.
    /// </remarks>
    public static LoadedProject Load(IFileSystem fs)
    {
        var project = ReadJson<ProjectData>(fs, ProjectPaths.ProjectFile);

        var graphs = new Dictionary<string, StoryGraph>();
        foreach (var id in project.Graphs)
        {
            graphs[id] = ReadJson<StoryGraph>(fs, ProjectPaths.Graph(id));
        }

        var characters = new Dictionary<string, CharacterData>();
        foreach (var id in project.Characters)
        {
            characters[id] = ReadJson<CharacterData>(fs, ProjectPaths.Character(id));
        }

        // Cảnh không liệt kê trong project.json — lấy từ các node scene trong đồ
        // thị, để thêm một cảnh không phải sửa hai file.
        var sceneIds = graphs.Values
            .SelectMany(g => g.Nodes)
            .OfType<SceneNode>()
            .Select(n => n.Scene)
            .Distinct();

        var scenes = new Dictionary<string, SceneData>();
        foreach (var id in sceneIds)
        {
            var path = ProjectPaths.Scene(id);
            if (!fs.Exists(path)) continue; // bộ kiểm tra sẽ báo thiếu
            scenes[id] = ReadJson<SceneData>(fs, path);
        }

        return new LoadedProject
        {
            Project = project,
            Graphs = graphs,
            Scenes = scenes,
            Characters = characters,
        };
    }

    public static void SaveProject(IFileSystem fs, ProjectData project)
        => fs.WriteText(ProjectPaths.ProjectFile, Serialize(project));

    public static void SaveGraph(IFileSystem fs, StoryGraph graph)
        => fs.WriteText(ProjectPaths.Graph(graph.Id), Serialize(graph));

    public static void SaveScene(IFileSystem fs, SceneData scene)
        => fs.WriteText(ProjectPaths.Scene(scene.Id), Serialize(scene));

    public static void SaveCharacter(IFileSystem fs, CharacterData character)
        => fs.WriteText(ProjectPaths.Character(character.Id), Serialize(character));

    public static void SaveAll(IFileSystem fs, LoadedProject loaded)
    {
        SaveProject(fs, loaded.Project);
        foreach (var graph in loaded.Graphs.Values) SaveGraph(fs, graph);
        foreach (var scene in loaded.Scenes.Values) SaveScene(fs, scene);
        foreach (var character in loaded.Characters.Values) SaveCharacter(fs, character);
    }

    /// <summary>
    /// Tạo dự án mới với cấu hình mặc định hợp lý.
    /// </summary>
    /// <remarks>
    /// Dự án mới đã có sẵn một chương và một node kết thúc, không phải màn hình
    /// trắng hoàn toàn. Người dùng mở lên là có thứ để bấm ngay.
    /// </remarks>
    public static LoadedProject CreateNew(string title, string? author, IList<string> locales)
    {
        var primary = locales.Count > 0 ? locales[0] : Constants.DefaultLocale;

        var ending = new EndingNode
        {
            Id = "ending_ket_thuc",
            Position = new GraphPosition(240, 160),
            Label = "Kết thúc",
            Name = new Localized(primary, "Kết thúc"),
        };

        var graph = new StoryGraph
        {
            Id = "chapter_1",
            Title = new Localized(primary, "Chương 1"),
            Entry = ending.Id,
            Nodes = { ending },
        };

        var slug = Ids.Slugify(title);
        var project = new ProjectData
        {
            Id = slug.Length > 0 ? slug : "untitled_project",
            Title = new Localized(primary, title),
            Author = string.IsNullOrWhiteSpace(author) ? null : author,
            Locales = locales.ToList(),
            Graphs = { graph.Id },
        };

        project.Text.LocaleFonts["ja"] = "Noto Sans JP";
        project.Text.LocaleFonts["zh"] = "Noto Sans SC";
        project.Text.LocaleFonts["th"] = "Noto Sans Thai";

        return new LoadedProject
        {
            Project = project,
            Graphs = { [graph.Id] = graph },
        };
    }

    /// <summary>Tạo dự án mới rồi ghi toàn bộ cấu trúc thư mục xuống đĩa.</summary>
    public static LoadedProject Scaffold(IFileSystem fs, string title, string? author, IList<string> locales)
    {
        var loaded = CreateNew(title, author, locales);
        foreach (var dir in ProjectPaths.RequiredDirs) fs.CreateDirectory(dir);
        SaveAll(fs, loaded);
        return loaded;
    }
}
