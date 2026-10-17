using System.Text.RegularExpressions;
using VNdev.Core.Model;

namespace VNdev.Core.Io;

/// <summary>
/// Đọc ghi các ô lưu của người chơi.
/// </summary>
/// <remarks>
/// Nhận <see cref="IFileSystem"/> gắn vào thư mục lưu của máy người chơi, KHÔNG
/// phải thư mục dự án: bản lưu là của người chơi chứ không phải của truyện, để
/// trong dự án thì lên Git, và trong editor còn bị bộ hoàn tác chụp thành một
/// bước Ctrl+Z.
/// </remarks>
public sealed partial class SaveStore
{
    /// <summary>Ô lưu nhanh (F5/F9), tách khỏi các ô đánh số để lưu nhanh không đè nhầm ô người chơi chọn.</summary>
    public const string QuickSlot = "quick";

    public const int SlotCount = 9;

    private const string Suffix = ".save.json";

    private readonly IFileSystem _fs;

    public SaveStore(IFileSystem fs)
    {
        _fs = fs;
    }

    public static IEnumerable<string> NumberedSlots => Enumerable.Range(1, SlotCount).Select(i => i.ToString());

    // Tên ô thành tên file, nên chỉ nhận chữ thường, số, gạch dưới — không để
    // một tên ô lạ biến thành đường dẫn.
    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex SlotPattern();

    private static string PathOf(string slot)
    {
        if (!SlotPattern().IsMatch(slot)) throw new ArgumentException($"Tên ô lưu không hợp lệ: \"{slot}\".", nameof(slot));
        return slot + Suffix;
    }

    public void Write(string slot, SaveGame save) => _fs.WriteText(PathOf(slot), ProjectIo.Serialize(save));

    /// <summary>Đọc một ô. Trống hoặc hỏng thì trả null.</summary>
    /// <remarks>
    /// Không ném lỗi với file hỏng: một ô lưu bị cắt dở (tắt máy giữa lúc ghi)
    /// không được làm hỏng cả màn tải — người chơi vẫn phải dùng được các ô khác.
    /// </remarks>
    public SaveGame? Read(string slot)
    {
        var path = PathOf(slot);
        if (!_fs.Exists(path)) return null;
        try
        {
            return ProjectIo.Deserialize<SaveGame>(_fs.ReadText(path), path);
        }
        catch (Exception ex) when (ex is ProjectLoadException or IOException)
        {
            return null;
        }
    }

    public void Delete(string slot) => _fs.DeleteFile(PathOf(slot));
}
