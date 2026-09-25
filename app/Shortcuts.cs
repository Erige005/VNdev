using System.Collections.Generic;
using System.Linq;
using Godot;

namespace VNdev.App;

public sealed record ShortcutAction(string Id, string Group, string Label, string DefaultKeys);

/// <summary>
/// Bảng phím tắt duy nhất của app. Menu, tooltip, trang Cài đặt → Phím tắt và
/// hộp thoại danh sách phím tắt đều đọc từ đây, nên đổi một phím ở Cài đặt là
/// mọi nơi hiện đúng phím mới.
/// </summary>
/// <remarks>
/// Phím lưu dạng chuỗi như "Ctrl+Shift+Z" thay vì mã số của Godot, để file
/// cài đặt mở ra đọc được và không vỡ nếu Godot đổi giá trị enum.
/// </remarks>
public static class Shortcuts
{
    public const string NewProject = "file.new";
    public const string OpenProject = "file.open";
    public const string CloseProject = "file.close";
    public const string ShowInExplorer = "file.explorer";
    public const string Settings = "file.settings";
    public const string Quit = "file.quit";

    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string Copy = "edit.copy";
    public const string Paste = "edit.paste";
    public const string Duplicate = "edit.duplicate";
    public const string Delete = "edit.delete";
    public const string SelectAll = "edit.select_all";

    public const string TabStory = "view.tab_story";
    public const string TabScene = "view.tab_scene";
    public const string TabCharacter = "view.tab_character";
    public const string TabAsset = "view.tab_asset";
    public const string ZoomIn = "view.zoom_in";
    public const string ZoomOut = "view.zoom_out";
    public const string ZoomFit = "view.zoom_fit";
    public const string Fullscreen = "view.fullscreen";

    public const string Play = "run.play";
    public const string ShortcutList = "help.shortcuts";

    public static readonly IReadOnlyList<ShortcutAction> All = new[]
    {
        new ShortcutAction(NewProject, "Tệp", "Dự án mới", "Ctrl+N"),
        new ShortcutAction(OpenProject, "Tệp", "Mở dự án", "Ctrl+O"),
        new ShortcutAction(CloseProject, "Tệp", "Đóng dự án", "Ctrl+W"),
        new ShortcutAction(ShowInExplorer, "Tệp", "Mở thư mục dự án", ""),
        new ShortcutAction(Settings, "Tệp", "Cài đặt", "Ctrl+Comma"),
        new ShortcutAction(Quit, "Tệp", "Thoát", "Ctrl+Q"),

        new ShortcutAction(Undo, "Sửa", "Hoàn tác", "Ctrl+Z"),
        new ShortcutAction(Redo, "Sửa", "Làm lại", "Ctrl+Y"),
        new ShortcutAction(Copy, "Sửa", "Sao chép node", "Ctrl+C"),
        new ShortcutAction(Paste, "Sửa", "Dán node", "Ctrl+V"),
        new ShortcutAction(Duplicate, "Sửa", "Nhân bản node", "Ctrl+D"),
        new ShortcutAction(Delete, "Sửa", "Xoá node đang chọn", "Delete"),
        new ShortcutAction(SelectAll, "Sửa", "Chọn tất cả node", "Ctrl+A"),

        new ShortcutAction(TabStory, "Xem", "Tab Cốt truyện", "Ctrl+1"),
        new ShortcutAction(TabScene, "Xem", "Tab Cảnh", "Ctrl+2"),
        new ShortcutAction(TabCharacter, "Xem", "Tab Nhân vật", "Ctrl+3"),
        new ShortcutAction(TabAsset, "Xem", "Tab Asset", "Ctrl+4"),
        new ShortcutAction(ZoomIn, "Xem", "Phóng to đồ thị", "Ctrl+Equal"),
        new ShortcutAction(ZoomOut, "Xem", "Thu nhỏ đồ thị", "Ctrl+Minus"),
        new ShortcutAction(ZoomFit, "Xem", "Vừa khung đồ thị", "Ctrl+0"),
        new ShortcutAction(Fullscreen, "Xem", "Toàn màn hình", "F11"),

        new ShortcutAction(Play, "Chạy", "Chơi thử", "F5"),
        new ShortcutAction(ShortcutList, "Trợ giúp", "Danh sách phím tắt", "F1"),
    };

    public static ShortcutAction Find(string id) => All.First(a => a.Id == id);

    /// <summary>Phím đang dùng: phím người dùng tự đặt nếu có, không thì mặc định.</summary>
    public static string KeysFor(string id)
        => AppSettings.Current.Shortcuts.TryGetValue(id, out var custom) ? custom : Find(id).DefaultKeys;

    /// <summary>Chuỗi hiện cho người dùng, ví dụ "Ctrl+," thay vì "Ctrl+Comma".</summary>
    public static string Display(string id)
    {
        var keys = KeysFor(id);
        return keys.Replace("Comma", ",").Replace("Equal", "=").Replace("Minus", "-");
    }

    /// <summary>Dựng <see cref="Shortcut"/> của Godot cho menu. Không gán phím thì trả null.</summary>
    public static Shortcut? Make(string id)
    {
        var ev = Parse(KeysFor(id));
        if (ev is null) return null;
        var shortcut = new Shortcut();
        shortcut.Events.Add(ev);
        return shortcut;
    }

    public static bool Matches(string id, InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return false;
        var bound = Parse(KeysFor(id));
        return bound is not null && bound.IsMatch(key, exactMatch: true);
    }

    /// <summary>Biến một lần bấm phím thành chuỗi lưu được, dùng ở trang đổi phím tắt.</summary>
    public static string FromEvent(InputEventKey key)
    {
        var parts = new List<string>();
        if (key.CtrlPressed) parts.Add("Ctrl");
        if (key.ShiftPressed) parts.Add("Shift");
        if (key.AltPressed) parts.Add("Alt");
        if (key.MetaPressed) parts.Add("Meta");
        parts.Add(OS.GetKeycodeString(key.Keycode));
        return string.Join("+", parts);
    }

    /// <summary>Phím bổ trợ đứng một mình không phải một phím tắt hoàn chỉnh.</summary>
    public static bool IsModifierOnly(InputEventKey key)
        => key.Keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta or Key.None;

    public static InputEventKey? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var ev = new InputEventKey();
        foreach (var part in text.Split('+').Select(p => p.Trim()))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ev.CtrlPressed = true; break;
                case "shift": ev.ShiftPressed = true; break;
                case "alt": ev.AltPressed = true; break;
                case "meta": ev.MetaPressed = true; break;
                default:
                    var code = OS.FindKeycodeFromString(part);
                    if (code == Key.None) return null;
                    ev.Keycode = code;
                    break;
            }
        }
        return ev.Keycode == Key.None ? null : ev;
    }

    /// <summary>Hành động khác đang dùng cùng tổ hợp phím, để trang cài đặt cảnh báo trùng.</summary>
    public static ShortcutAction? ConflictWith(string id, string keys)
    {
        if (keys.Length == 0) return null;
        return All.FirstOrDefault(a => a.Id != id && string.Equals(KeysFor(a.Id), keys, System.StringComparison.OrdinalIgnoreCase));
    }
}
