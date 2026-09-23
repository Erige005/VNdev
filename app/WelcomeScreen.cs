using System;
using System.Collections.Generic;
using Godot;
using VNdev.Core.Io;

namespace VNdev.App;

/// <summary>
/// Màn hình đầu tiên khi mở app: Tạo dự án mới / Mở dự án. Không có dự án
/// mẫu nào nạp sẵn — người dùng phải tự tạo hoặc tự chọn thư mục, giống
/// Ren'Py và VS Code (xem CLAUDE.md, ràng buộc số 5).
/// </summary>
public partial class WelcomeScreen : Control
{
    public event Action<LoadedProject, DiskFileSystem, string>? ProjectReady;

    private LineEdit _titleEdit = null!;
    private LineEdit _authorEdit = null!;
    private Label _errorLabel = null!;

    public override void _Ready()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddThemeConstantOverride("separation", 14);
        center.AddChild(box);

        var title = new Label { Text = "VNdev" };
        title.AddThemeColorOverride("font_color", Palette.Text);
        title.AddThemeFontSizeOverride("font_size", 40);
        box.AddChild(title);

        var subtitle = new Label { Text = "Công cụ tạo visual novel — không dự án mẫu, tự tạo hoặc mở của bạn." };
        subtitle.AddThemeColorOverride("font_color", Palette.Text2);
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(subtitle);

        box.AddChild(new HSeparator());

        _titleEdit = MakeField(box, "Tên dự án");
        _authorEdit = MakeField(box, "Tác giả (không bắt buộc)");

        var createBtn = new Button { Text = "+  Tạo dự án mới" };
        createBtn.Pressed += OnCreatePressed;
        box.AddChild(createBtn);

        var openBtn = new Button { Text = "Mở dự án" };
        openBtn.Pressed += OnOpenPressed;
        box.AddChild(openBtn);

        _errorLabel = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _errorLabel.AddThemeColorOverride("font_color", Palette.Err);
        box.AddChild(_errorLabel);
    }

    private static LineEdit MakeField(VBoxContainer parent, string placeholder)
    {
        var edit = new LineEdit { PlaceholderText = placeholder };
        parent.AddChild(edit);
        return edit;
    }

    private void OnCreatePressed()
    {
        var title = _titleEdit.Text.Trim();
        if (title.Length == 0)
        {
            _errorLabel.Text = "Nhập tên dự án trước khi chọn thư mục.";
            return;
        }
        _errorLabel.Text = "";

        ShowFolderDialog("Chọn thư mục trống để tạo dự án", CreateProjectAt);
    }

    private void OnOpenPressed()
    {
        _errorLabel.Text = "";
        ShowFolderDialog("Chọn thư mục dự án đã có", OpenProjectAt);
    }

    private void ShowFolderDialog(string title, Action<string> onSelected)
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = title,
        };
        AddChild(dialog);
        dialog.DirSelected += dir =>
        {
            onSelected(dir);
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        dialog.PopupCentered(new Vector2I(760, 500));
    }

    private void CreateProjectAt(string dir)
    {
        var fs = new DiskFileSystem(dir);
        if (fs.Exists(ProjectPaths.ProjectFile))
        {
            _errorLabel.Text = "Thư mục này đã có dự án rồi. Chọn thư mục khác hoặc dùng Mở dự án.";
            return;
        }

        var title = _titleEdit.Text.Trim();
        var author = _authorEdit.Text.Trim();
        var locales = new List<string> { "vi" };

        LoadedProject loaded;
        try
        {
            loaded = ProjectIo.Scaffold(fs, title, string.IsNullOrEmpty(author) ? null : author, locales);
        }
        catch (Exception ex)
        {
            _errorLabel.Text = $"Không tạo được dự án: {ex.Message}";
            return;
        }

        ProjectReady?.Invoke(loaded, fs, dir);
    }

    private void OpenProjectAt(string dir)
    {
        var fs = new DiskFileSystem(dir);
        if (!fs.Exists(ProjectPaths.ProjectFile))
        {
            _errorLabel.Text = "Không thấy project.json trong thư mục này.";
            return;
        }

        LoadedProject loaded;
        try
        {
            loaded = ProjectIo.Load(fs);
        }
        catch (Exception ex)
        {
            _errorLabel.Text = $"Không mở được dự án: {ex.Message}";
            return;
        }

        ProjectReady?.Invoke(loaded, fs, dir);
    }
}
