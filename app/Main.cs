using Godot;
using VNdev.Core.Io;

namespace VNdev.App;

/// <summary>
/// Nút gốc của app: chỉ lo chuyển giữa màn hình chào và màn hình editor,
/// không giữ logic dự án. Đúng nguyên tắc "một cửa sổ duy nhất" trong
/// docs/design/ui-ux.html.
/// </summary>
public partial class Main : Control
{
    private Control? _current;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = AppTheme.Build();

        var bg = new ColorRect { Color = Palette.Bg };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(bg);

        ShowWelcome();
    }

    private void ShowWelcome()
    {
        var welcome = new WelcomeScreen();
        welcome.ProjectReady += OnProjectReady;
        SwitchTo(welcome);
    }

    private void OnProjectReady(LoadedProject project, DiskFileSystem fs, string projectDir)
    {
        var editor = new EditorScreen(project, fs, projectDir);
        editor.BackToWelcome += ShowWelcome;
        SwitchTo(editor);
    }

    private void SwitchTo(Control screen)
    {
        if (_current is not null)
        {
            _current.QueueFree();
        }

        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
        _current = screen;
    }
}
