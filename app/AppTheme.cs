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
        var settings = AppSettings.Current;
        Palette.Accent = Color.FromHtml(settings.AccentColor);

        var theme = new Theme { DefaultFontSize = settings.FontSize };

        theme.SetColor("font_color", "Label", Palette.Text2);
        theme.SetColor("font_color", "Button", Palette.Text);
        theme.SetColor("font_hover_color", "Button", Palette.Text);
        theme.SetColor("font_pressed_color", "Button", Palette.Text);
        theme.SetColor("font_focus_color", "Button", Palette.Text);
        theme.SetColor("font_disabled_color", "Button", Palette.Text3);
        theme.SetColor("font_color", "LineEdit", Palette.Text);
        theme.SetColor("font_placeholder_color", "LineEdit", Palette.Text3);
        theme.SetColor("font_color", "TextEdit", Palette.Text);
        theme.SetColor("font_color", "GraphNode", Palette.Text);
        theme.SetColor("font_color", "OptionButton", Palette.Text);
        theme.SetColor("caret_color", "LineEdit", Palette.Accent);
        theme.SetColor("selection_color", "LineEdit", new Color(Palette.Accent, 0.35f));
        theme.SetColor("selection_color", "TextEdit", new Color(Palette.Accent, 0.35f));

        theme.SetStylebox("panel", "Panel", Flat(Palette.Panel));
        theme.SetStylebox("panel", "PanelContainer", Flat(Palette.Panel));

        var btnNormal = Flat(Palette.Panel3, 6);
        var btnHover = Flat(Palette.Line2, 6);
        var btnPressed = Flat(Palette.Accent, 6);
        var btnDisabled = Flat(Palette.Panel2, 6);
        theme.SetStylebox("normal", "Button", btnNormal);
        theme.SetStylebox("hover", "Button", btnHover);
        theme.SetStylebox("pressed", "Button", btnPressed);
        theme.SetStylebox("hover_pressed", "Button", btnPressed);
        theme.SetStylebox("disabled", "Button", btnDisabled);
        theme.SetStylebox("focus", "Button", FocusRing());

        var editBox = Flat(Palette.Panel2, 6);
        editBox.BorderColor = Palette.Line;
        editBox.SetBorderWidthAll(1);
        var editFocus = Flat(Palette.Panel2, 6);
        editFocus.BorderColor = Palette.Accent;
        editFocus.SetBorderWidthAll(1);
        theme.SetStylebox("normal", "LineEdit", editBox);
        theme.SetStylebox("focus", "LineEdit", editFocus);
        theme.SetStylebox("normal", "TextEdit", editBox);
        theme.SetStylebox("focus", "TextEdit", editFocus);
        theme.SetStylebox("normal", "OptionButton", Flat(Palette.Panel3, 6));
        theme.SetStylebox("hover", "OptionButton", Flat(Palette.Line2, 6));

        theme.SetStylebox("panel", "GraphNode", Flat(Palette.Panel3, 8));
        theme.SetStylebox("titlebar", "GraphNode", Flat(Palette.Line2, 8));
        theme.SetStylebox("titlebar_selected", "GraphNode", Flat(Palette.Accent, 8));

        theme.SetStylebox("panel", "Tree", Flat(Palette.Panel));
        theme.SetColor("font_color", "Tree", Palette.Text2);
        theme.SetColor("font_selected_color", "Tree", Palette.Text);
        theme.SetStylebox("selected", "Tree", Flat(new Color(Palette.Accent, 0.16f), 4));
        theme.SetStylebox("selected_focus", "Tree", Flat(new Color(Palette.Accent, 0.16f), 4));

        theme.SetStylebox("panel", "ItemList", Flat(Palette.Panel));
        theme.SetStylebox("focus", "ItemList", new StyleBoxEmpty());
        theme.SetColor("font_color", "ItemList", Palette.Text2);
        theme.SetColor("font_selected_color", "ItemList", Palette.Text);
        theme.SetStylebox("selected", "ItemList", Flat(new Color(Palette.Accent, 0.16f), 4));
        theme.SetStylebox("selected_focus", "ItemList", Flat(new Color(Palette.Accent, 0.16f), 4));
        theme.SetStylebox("hovered", "ItemList", Flat(new Color(Palette.Text, 0.05f), 4));

        // Thanh menu và menu thả xuống — trước đây app không có menu nên chưa cần.
        theme.SetStylebox("normal", "MenuBar", Flat(Colors.Transparent, 4));
        theme.SetStylebox("hover", "MenuBar", Flat(Palette.Line2, 4));
        theme.SetStylebox("pressed", "MenuBar", Flat(Palette.Line2, 4));
        theme.SetColor("font_color", "MenuBar", Palette.Text2);
        theme.SetColor("font_hover_color", "MenuBar", Palette.Text);

        var popup = Flat(Palette.Panel3, 6);
        popup.BorderColor = Palette.Line2;
        popup.SetBorderWidthAll(1);
        popup.SetContentMarginAll(4);
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("hover", "PopupMenu", Flat(new Color(Palette.Accent, 0.28f), 4));
        theme.SetColor("font_color", "PopupMenu", Palette.Text);
        theme.SetColor("font_hover_color", "PopupMenu", Palette.Text);
        theme.SetColor("font_disabled_color", "PopupMenu", Palette.Text3);
        theme.SetColor("font_accelerator_color", "PopupMenu", Palette.Text3);
        theme.SetColor("font_separator_color", "PopupMenu", Palette.Text3);
        theme.SetConstant("v_separation", "PopupMenu", 8);
        theme.SetConstant("item_start_padding", "PopupMenu", 10);
        theme.SetConstant("item_end_padding", "PopupMenu", 10);
        theme.SetStylebox("separator", "PopupMenu", Line());

        // Hộp thoại và cửa sổ con (Cài đặt, Giới thiệu, xác nhận xoá…).
        var window = Flat(Palette.Panel, 8);
        window.BorderColor = Palette.Line2;
        window.SetBorderWidthAll(1);
        window.ExpandMarginTop = 30;
        window.ExpandMarginLeft = 1;
        window.ExpandMarginRight = 1;
        window.ExpandMarginBottom = 1;
        theme.SetStylebox("embedded_border", "Window", window);
        theme.SetStylebox("embedded_unfocused_border", "Window", window);
        theme.SetColor("title_color", "Window", Palette.Text);
        theme.SetStylebox("panel", "AcceptDialog", Flat(Palette.Panel, 0));

        // Tab trong cửa sổ Cài đặt.
        theme.SetStylebox("panel", "TabContainer", Flat(Palette.Panel2, 6));
        theme.SetStylebox("tab_selected", "TabContainer", Flat(Palette.Panel2, 6));
        theme.SetStylebox("tab_unselected", "TabContainer", Flat(Palette.Panel, 6));
        theme.SetStylebox("tab_hovered", "TabContainer", Flat(Palette.Panel3, 6));
        theme.SetColor("font_selected_color", "TabContainer", Palette.Text);
        theme.SetColor("font_unselected_color", "TabContainer", Palette.Text3);
        theme.SetColor("font_hovered_color", "TabContainer", Palette.Text2);

        theme.SetColor("font_color", "CheckBox", Palette.Text2);
        theme.SetColor("font_hover_color", "CheckBox", Palette.Text);
        theme.SetColor("font_pressed_color", "CheckBox", Palette.Text);
        theme.SetStylebox("normal", "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("hover", "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("pressed", "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("hover_pressed", "CheckBox", new StyleBoxEmpty());
        theme.SetStylebox("focus", "CheckBox", new StyleBoxEmpty());
        // Ô tick mặc định của Godot màu xám đậm, gần như biến mất trên nền tối
        // của app — vẽ lại để ô chưa tick vẫn nhìn ra là một ô bấm được.
        theme.SetIcon("unchecked", "CheckBox", CheckIcon(false));
        theme.SetIcon("checked", "CheckBox", CheckIcon(true));

        var tooltip = Flat(Palette.Panel3, 4);
        tooltip.BorderColor = Palette.Line2;
        tooltip.SetBorderWidthAll(1);
        tooltip.SetContentMarginAll(6);
        theme.SetStylebox("panel", "TooltipPanel", tooltip);
        theme.SetColor("font_color", "TooltipLabel", Palette.Text);

        theme.SetStylebox("separator", "HSeparator", Line());
        theme.SetStylebox("separator", "VSeparator", VLine());

        theme.SetStylebox("slider", "HSlider", Flat(Palette.Panel3, 3));
        theme.SetStylebox("grabber_area", "HSlider", Flat(Palette.Accent, 3));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Flat(Palette.Accent, 3));

        return theme;
    }

    private static StyleBoxFlat Flat(Color color, float radius = 0)
    {
        var sb = new StyleBoxFlat { BgColor = color };
        sb.SetCornerRadiusAll((int)radius);
        sb.SetContentMarginAll(8);
        return sb;
    }

    private static StyleBoxFlat FocusRing()
    {
        var sb = new StyleBoxFlat { DrawCenter = false, BorderColor = new Color(Palette.Accent, 0.7f) };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(6);
        return sb;
    }

    private static ImageTexture CheckIcon(bool on)
    {
        const int size = 16;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);
        var border = on ? Palette.Accent : Palette.Text3;
        for (var x = 1; x < size - 1; x++)
        {
            for (var y = 1; y < size - 1; y++)
            {
                var edge = x == 1 || y == 1 || x == size - 2 || y == size - 2;
                if (edge) img.SetPixel(x, y, border);
                else if (on) img.SetPixel(x, y, Palette.Accent);
            }
        }
        if (on)
        {
            // Dấu tick hai nét, vẽ tay từng điểm để không phải kèm file ảnh.
            for (var i = 0; i < 3; i++) { img.SetPixel(4 + i, 8 + i, Colors.White); img.SetPixel(4 + i, 7 + i, Colors.White); }
            for (var i = 0; i < 6; i++) { img.SetPixel(7 + i, 10 - i, Colors.White); img.SetPixel(7 + i, 9 - i, Colors.White); }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static StyleBoxLine Line() => new() { Color = Palette.Line, Thickness = 1 };

    private static StyleBoxLine VLine() => new() { Color = Palette.Line, Thickness = 1, Vertical = true };
}
