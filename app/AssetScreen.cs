using System;
using System.IO;
using Godot;
using VNdev.Core.Io;

namespace VNdev.App;

/// <summary>
/// Chặng 4: thư viện asset. Chỉ đọc lại những gì người dùng đã tự chép vào
/// assets/ bằng Explorer — không có nút import, không có kho nội bộ phải
/// đồng bộ (ràng buộc số 5 trong CLAUDE.md).
/// </summary>
public partial class AssetScreen : Control
{
    private readonly LoadedProject _loaded;
    private readonly DiskFileSystem _fs;
    private readonly string _projectDir;

    private VBoxContainer _content = null!;

    public AssetScreen(LoadedProject loaded, DiskFileSystem fs, string projectDir)
    {
        _loaded = loaded;
        _fs = fs;
        _projectDir = projectDir;
    }

    public override void _Ready()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel2 });
        AddChild(panel);

        var scroll = new ScrollContainer();
        panel.AddChild(scroll);

        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(_content);

        Rescan();
    }

    private void Rescan()
    {
        foreach (var child in _content.GetChildren()) child.QueueFree();

        BuildSection("Nền (assets/backgrounds)", AssetIndex.Backgrounds(_fs), image: true);
        BuildSection("Nhân vật (assets/sprites)", AssetIndex.Sprites(_fs), image: true);
        BuildSection("Nhạc nền (assets/bgm)", AssetIndex.Bgm(_fs), image: false);
        BuildSection("Hiệu ứng âm thanh (assets/sfx)", AssetIndex.Sfx(_fs), image: false);
    }

    private void BuildSection(string title, System.Collections.Generic.List<string> paths, bool image)
    {
        var header = new HBoxContainer();
        var label = new Label { Text = $"{title.ToUpperInvariant()} ({paths.Count})" };
        label.AddThemeColorOverride("font_color", Palette.Text3);
        header.AddChild(label);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(spacer);

        var rescan = new Button { Text = "⊞ Quét lại" };
        rescan.Pressed += Rescan;
        header.AddChild(rescan);

        _content.AddChild(header);

        if (paths.Count == 0)
        {
            var hint = new Label { Text = "Chưa có file nào — chép vào thư mục rồi bấm Quét lại." };
            hint.AddThemeColorOverride("font_color", Palette.Text3);
            _content.AddChild(hint);
            return;
        }

        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (var relPath in paths)
        {
            grid.AddChild(BuildCard(relPath, image));
        }
        _content.AddChild(grid);
    }

    private Control BuildCard(string relPath, bool image)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(96, 0) };
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Panel3 });
        panel.AddChild(box);

        if (image)
        {
            var texture = LoadTexture(Path.Combine(_projectDir, relPath));
            var rect = new TextureRect
            {
                CustomMinimumSize = new Vector2(96, 96),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Texture = texture,
            };
            box.AddChild(rect);
        }
        else
        {
            var placeholder = new Label { Text = "♪", HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(96, 60) };
            placeholder.AddThemeColorOverride("font_color", Palette.Text3);
            placeholder.AddThemeFontSizeOverride("font_size", 28);
            box.AddChild(placeholder);
        }

        var caption = new Label
        {
            Text = Path.GetFileName(relPath),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(96, 0),
        };
        caption.AddThemeColorOverride("font_color", Palette.Text2);
        caption.AddThemeFontSizeOverride("font_size", 10);
        box.AddChild(caption);

        return panel;
    }

    private static ImageTexture? LoadTexture(string absPath)
    {
        var image = new Image();
        var err = image.Load(absPath);
        return err == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
    }
}
