using VNdev.Core.Io;

namespace VNdev.Core.Model;

/// <summary>
/// Một bản lưu của người chơi: đang đứng ở đâu trong truyện và biến mang giá trị gì.
/// </summary>
/// <remarks>
/// Chỉ lưu vị trí (id node + số dòng) chứ không chụp lại cả sân khấu: sân khấu
/// tính lại được bằng <see cref="SceneData.StageAt"/>. Nhờ vậy bản lưu vài trăm
/// byte, và khi tác giả sửa lời thoại thì tải lại thấy ngay chữ mới thay vì chữ cũ
/// đóng băng trong file lưu.
/// </remarks>
public sealed class SaveGame
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>Chương đang chơi.</summary>
    public string Graph { get; set; } = string.Empty;

    /// <summary>Node để chạy tiếp: node cảnh, node lựa chọn hoặc node kết thúc.</summary>
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// Node cảnh đang hiện trên sân khấu. Trùng <see cref="Node"/> khi lưu giữa
    /// cảnh; khác khi lưu lúc đang chọn phương án — lúc đó nó là cảnh làm nền
    /// phía sau các nút, không có thì tải lên chỉ thấy nút trên nền đen.
    /// </summary>
    public string? Scene { get; set; }

    /// <summary>Dòng thoại đang hiện trong <see cref="Scene"/>.</summary>
    public int Line { get; set; }

    public Dictionary<string, VarValue> Variables { get; set; } = new();

    /// <summary>Câu thoại lúc lưu, chỉ để hiện trên ô lưu cho người chơi nhận ra.</summary>
    public string? Preview { get; set; }

    /// <summary>
    /// Đối chiếu bản lưu với dự án hiện tại để chạy tiếp.
    /// </summary>
    /// <remarks>
    /// Tác giả sửa truyện liên tục giữa các lần chơi thử, nên bản lưu cũ hay trỏ
    /// vào thứ đã đổi. Cái gì sửa được thì sửa êm (dòng bị xoá bớt → lùi về dòng
    /// cuối, biến mới khai báo → lấy giá trị đầu, biến đã xoá → bỏ); chỉ báo lỗi
    /// khi node để chạy tiếp không còn, vì khi đó không có chỗ nào đúng để đứng.
    /// </remarks>
    public SaveResume Resolve(LoadedProject loaded)
    {
        if (FormatVersion > CurrentFormatVersion)
        {
            return SaveResume.Fail("Bản lưu được tạo bởi phiên bản VNdev mới hơn, bản này không đọc được.");
        }
        if (!loaded.Graphs.TryGetValue(Graph, out var graph))
        {
            return SaveResume.Fail($"Chương \"{Graph}\" của bản lưu không còn trong dự án.");
        }

        var node = graph.Find(Node);
        if (node is not (SceneNode or ChoiceNode or EndingNode))
        {
            return SaveResume.Fail($"Chỗ đã lưu (node \"{Node}\") đã bị xoá hoặc đổi loại — không chạy tiếp được.");
        }
        if (node is SceneNode sn && !loaded.Scenes.ContainsKey(sn.Scene))
        {
            return SaveResume.Fail($"Cảnh \"{sn.Scene}\" của bản lưu không còn file.");
        }

        // Cảnh nền chỉ để trang trí — mất thì thôi, không chặn việc tải.
        SceneNode? backdrop = null;
        SceneData? backdropScene = null;
        if (Scene is not null
            && graph.Find(Scene) is SceneNode b
            && loaded.Scenes.TryGetValue(b.Scene, out var bs)
            && bs.Lines.Count > 0)
        {
            backdrop = b;
            backdropScene = bs;
        }
        if (node is SceneNode && backdrop != node)
        {
            backdrop = (SceneNode)node;
            backdropScene = loaded.Scenes[backdrop.Scene];
        }

        var line = backdropScene is null ? 0 : Math.Clamp(Line, 0, Math.Max(0, backdropScene.Lines.Count - 1));

        var vars = new Dictionary<string, VarValue>();
        foreach (var def in loaded.Project.Variables)
        {
            vars[def.Id] = Variables.TryGetValue(def.Id, out var saved) && saved.Kind == def.Type ? saved : def.Initial;
        }

        return new SaveResume(null, graph, node, backdrop, line, vars);
    }
}

/// <summary>Kết quả đối chiếu bản lưu: hoặc đủ thứ để chạy tiếp, hoặc lý do không chạy được.</summary>
public sealed record SaveResume(
    string? Error,
    StoryGraph? Graph,
    StoryNode? Node,
    SceneNode? Backdrop,
    int Line,
    Dictionary<string, VarValue> Variables)
{
    public bool Ok => Error is null;

    public static SaveResume Fail(string error) => new(error, null, null, null, 0, new());
}
