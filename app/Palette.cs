using Godot;

namespace VNdev.App;

/// <summary>
/// Bảng màu lấy đúng từ docs/design/ui-ux.html. Giữ một bản duy nhất ở đây để
/// mọi màn hình dùng chung — đổi màu chủ đề thì chỉ sửa một chỗ.
/// </summary>
public static class Palette
{
    public static readonly Color Bg = Color.FromHtml("#0b0c10");
    public static readonly Color Panel = Color.FromHtml("#13151c");
    public static readonly Color Panel2 = Color.FromHtml("#181b24");
    public static readonly Color Panel3 = Color.FromHtml("#1e222d");
    public static readonly Color Line = Color.FromHtml("#272c38");
    public static readonly Color Line2 = Color.FromHtml("#343a49");

    public static readonly Color Text = Color.FromHtml("#e8eaf0");
    public static readonly Color Text2 = Color.FromHtml("#a0a6b8");
    public static readonly Color Text3 = Color.FromHtml("#6b7386");

    public static readonly Color Accent = Color.FromHtml("#7c6cff");
    public static readonly Color Accent2 = Color.FromHtml("#ff6ba8");
    public static readonly Color Ok = Color.FromHtml("#3ecf8e");
    public static readonly Color Warn = Color.FromHtml("#ffb648");
    public static readonly Color Err = Color.FromHtml("#ff5f6d");

    // Màu theo loại node — khớp bảng trong ui-ux.html để dùng nhất quán ở
    // graph, minimap và cây chương.
    public static readonly Color NodeScene = Color.FromHtml("#5f6fe8");
    public static readonly Color NodeChoice = Color.FromHtml("#e08b2a");
    public static readonly Color NodeCondition = Color.FromHtml("#25a893");
    public static readonly Color NodeVariable = Color.FromHtml("#9260d1");
    public static readonly Color NodeEnding = Color.FromHtml("#d1445c");
    public static readonly Color NodeJump = Color.FromHtml("#8b90a0");
    public static readonly Color NodeComment = Color.FromHtml("#5a6376");
}
