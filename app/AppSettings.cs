using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using VNdev.Core.Io;

namespace VNdev.App;

public sealed class RecentProject
{
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }
}

public sealed class WindowState
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>
/// Cài đặt của app trên máy này — khác với dữ liệu dự án.
/// </summary>
/// <remarks>
/// Lưu ở thư mục người dùng (%APPDATA%\VNdev) chứ không nằm trong thư mục dự
/// án: cỡ chữ hay danh sách dự án gần đây là chuyện của từng người, đưa lên
/// Git thì hai người trong nhóm sẽ ghi đè cài đặt của nhau.
/// </remarks>
public sealed class AppSettings
{
    public const int MaxRecent = 10;

    // --- Chung ---
    public bool ReopenLastProject { get; set; }
    public bool ConfirmDeleteNodes { get; set; } = true;
    public string UiLanguage { get; set; } = "vi";

    // --- Giao diện ---
    public float UiScale { get; set; } = 1f;
    public int FontSize { get; set; } = 15;
    public string AccentColor { get; set; } = "#7c6cff";
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; }
    public bool ShowMinimap { get; set; } = true;

    // --- Phím tắt: id hành động → chuỗi phím, chỉ lưu những cái người dùng đã đổi ---
    public Dictionary<string, string> Shortcuts { get; set; } = new();

    public List<RecentProject> RecentProjects { get; set; } = new();
    public WindowState? Window { get; set; }

    public static AppSettings Current { get; private set; } = new();

    /// <summary>Có sự thay đổi cần áp dụng lại lên giao diện đang mở.</summary>
    public static event Action? Changed;

    private static string FilePath => System.IO.Path.Combine(OS.GetUserDataDir(), "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new(ProjectIo.Options);

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            // File cài đặt hỏng không được phép chặn app mở lên — dùng mặc định
            // và ghi đè ở lần lưu kế tiếp.
            GD.PushWarning($"Không đọc được cài đặt, dùng mặc định: {ex.Message}");
            Current = new AppSettings();
        }
        Current.Clamp();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Không lưu được cài đặt: {ex.Message}");
        }
    }

    /// <summary>Lưu và báo cho mọi màn hình áp dụng lại.</summary>
    public static void Apply()
    {
        Current.Clamp();
        Save();
        Changed?.Invoke();
    }

    public void AddRecent(string path, string title)
    {
        var full = System.IO.Path.GetFullPath(path);
        RecentProjects.RemoveAll(r => string.Equals(System.IO.Path.GetFullPath(r.Path), full, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, new RecentProject { Path = full, Title = title, LastOpened = DateTime.Now });
        if (RecentProjects.Count > MaxRecent) RecentProjects.RemoveRange(MaxRecent, RecentProjects.Count - MaxRecent);
        Save();
    }

    public void RemoveRecent(string path)
    {
        RecentProjects.RemoveAll(r => r.Path == path);
        Save();
    }

    private void Clamp()
    {
        UiScale = Math.Clamp(UiScale, 0.75f, 2f);
        FontSize = Math.Clamp(FontSize, 11, 24);
        if (!AccentColor.StartsWith('#') || AccentColor.Length != 7) AccentColor = "#7c6cff";
        RecentProjects = RecentProjects.Where(r => !string.IsNullOrWhiteSpace(r.Path)).ToList();
    }
}
