using Godot;

namespace VNdev.App;

/// <summary>
/// Theme dùng chung toàn app. Dựng bằng code thay vì file .tres để không phải
/// mở editor Godot mới sửa được màu — sửa số trong <see cref="Palette"/> là đủ.
/// </summary>
public static class AppTheme
{
    public static Theme Build()
    {
        var theme = new Theme();

        theme.SetColor("font_color", "Label", Palette.Text2);
        theme.SetColor("font_color", "Button", Palette.Text);
        theme.SetColor("font_color", "LineEdit", Palette.Text);
        theme.SetColor("font_color", "GraphNode", Palette.Text);
        theme.SetColor("font_color", "OptionButton", Palette.Text);

        theme.SetStylebox("panel", "Panel", Flat(Palette.Panel));
        theme.SetStylebox("panel", "PanelContainer", Flat(Palette.Panel));

        var btnNormal = Flat(Palette.Panel3, 6);
        var btnHover = Flat(Palette.Line2, 6);
        var btnPressed = Flat(Palette.Accent, 6);
        theme.SetStylebox("normal", "Button", btnNormal);
        theme.SetStylebox("hover", "Button", btnHover);
        theme.SetStylebox("pressed", "Button", btnPressed);
        theme.SetStylebox("focus", "Button", btnNormal);

        var editBox = Flat(Palette.Panel2, 6);
        editBox.BorderColor = Palette.Line;
        editBox.SetBorderWidthAll(1);
        theme.SetStylebox("normal", "LineEdit", editBox);
        theme.SetStylebox("normal", "TextEdit", editBox);
        theme.SetStylebox("normal", "OptionButton", Flat(Palette.Panel3, 6));

        theme.SetStylebox("panel", "GraphNode", Flat(Palette.Panel3, 8));
        theme.SetStylebox("titlebar", "GraphNode", Flat(Palette.Line2, 8));
        theme.SetStylebox("titlebar_selected", "GraphNode", Flat(Palette.Accent, 8));

        theme.SetStylebox("panel", "Tree", Flat(Palette.Panel));
        theme.SetColor("font_color", "Tree", Palette.Text2);
        theme.SetColor("font_selected_color", "Tree", Palette.Text);
        theme.SetStylebox("selected", "Tree", Flat(new Color(Palette.Accent, 0.16f), 4));
        theme.SetStylebox("selected_focus", "Tree", Flat(new Color(Palette.Accent, 0.16f), 4));

        theme.SetStylebox("panel", "ItemList", Flat(Palette.Panel));
        theme.SetColor("font_color", "ItemList", Palette.Text2);
        theme.SetColor("font_selected_color", "ItemList", Palette.Text);
        theme.SetStylebox("selected", "ItemList", Flat(new Color(Palette.Accent, 0.16f), 4));
        theme.SetStylebox("selected_focus", "ItemList", Flat(new Color(Palette.Accent, 0.16f), 4));

        return theme;
    }

    private static StyleBoxFlat Flat(Color color, float radius = 0)
    {
        var sb = new StyleBoxFlat { BgColor = color };
        sb.SetCornerRadiusAll((int)radius);
        sb.SetContentMarginAll(8);
        return sb;
    }
}
