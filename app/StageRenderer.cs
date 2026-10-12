using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using VNdev.Core.Io;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Vẽ sân khấu (nền + nhân vật) dùng chung cho Scene Canvas (Chặng 5) và
/// Player (Chặng 6) — cả hai phải hiện đúng y hệt nhau, đúng nguyên tắc
/// "Canvas là game thật" trong docs/design/ui-ux.html.
/// </summary>
public static class StageRenderer
{
    /// <summary>Tên node ảnh nền trong khung — để lớp hiệu ứng tìm lại nó (zoom chậm, nhoè).</summary>
    public const string BackgroundName = "Bg";

    /// <summary>Những gì vừa vẽ, để lớp hiệu ứng diễn chuyển động trên đúng node.</summary>
    public sealed record Drawn(TextureRect? Background, Dictionary<string, Control> Actors);

    /// <summary>
    /// Xoá mọi con của <paramref name="stage"/> từ vị trí <paramref name="keepFirst"/> trở đi rồi vẽ lại.
    /// </summary>
    /// <remarks>
    /// Ảnh nền giữ lại nếu vẫn là file cũ: sang dòng mới mà dựng lại nền thì zoom
    /// chậm đang chạy giật về đầu, và mỗi dòng lại đọc một ảnh vài MB từ đĩa.
    /// </remarks>
    public static Drawn Draw(Control stage, LoadedProject loaded, string projectDir, SceneData? scene, IReadOnlyList<StageActor> actors, int keepFirst = 0, float? frameHeight = null)
    {
        var wantBg = scene?.Background;
        var oldBg = stage.GetNodeOrNull<TextureRect>(BackgroundName);
        var keepBg = oldBg is not null && !oldBg.IsQueuedForDeletion() && !string.IsNullOrEmpty(wantBg)
            && oldBg.HasMeta("src") && oldBg.GetMeta("src").AsString() == wantBg;

        foreach (var child in stage.GetChildren().Skip(keepFirst))
        {
            if (keepBg && child == oldBg) continue;
            // Gỡ ngay chứ không chỉ QueueFree: node chờ xoá vẫn giữ tên "Bg" tới
            // cuối khung hình, nền mới thêm vào sẽ bị Godot đổi tên và không tìm lại được.
            stage.RemoveChild(child);
            child.QueueFree();
        }

        TextureRect? bgRect = keepBg ? oldBg : null;
        if (!keepBg && !string.IsNullOrEmpty(wantBg))
        {
            var texture = LoadTexture(Path.Combine(projectDir, wantBg));
            if (texture is not null)
            {
                bgRect = new TextureRect
                {
                    Name = BackgroundName,
                    Texture = texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    ClipContents = true,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                bgRect.SetMeta("src", wantBg);
                bgRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                stage.AddChild(bgRect);
                stage.MoveChild(bgRect, keepFirst);
            }
        }

        var nodes = new Dictionary<string, Control>();
        foreach (var actor in actors)
        {
            var node = BuildActor(actor, loaded, projectDir, frameHeight ?? stage.Size.Y);
            stage.AddChild(node);
            nodes[actor.Character] = node;
        }
        return new Drawn(bgRect, nodes);
    }

    public static Control BuildActor(StageActor actor, LoadedProject loaded, string projectDir, float frameHeight)
    {
        var ratio = actor.X ?? (actor.Anchor is { } a ? StageAnchors.Ratio(a) : 0.5f);
        var dim = actor.Dim ?? 1f;

        loaded.Characters.TryGetValue(actor.Character, out var character);
        var expr = character?.FindExpression(actor.Expression);

        Control node;
        // Khung mặc định khi chưa có sprite: thon, cao, đúng dáng đứng full-body.
        var size = new Vector2(frameHeight * 0.3f, frameHeight * 0.78f);

        if (expr?.Sprite is not null)
        {
            var texture = LoadTexture(Path.Combine(projectDir, expr.Sprite));

            if (texture is not null && texture.GetHeight() > 0)
            {
                // Tính khung theo đúng tỉ lệ khung/cao của ảnh gốc thay vì ép
                // cứng theo dáng full-body: sprite "halfbody" (nửa người) có tỉ
                // lệ rộng hơn nhiều so với sprite đứng cả người, ép vào khung cũ
                // sẽ bị co bề ngang trước, kéo cả người bé tí theo.
                var aspect = texture.GetWidth() / (float)texture.GetHeight();
                var targetHeight = frameHeight * 0.78f;
                size = new Vector2(targetHeight * aspect, targetHeight);
            }

            node = new TextureRect
            {
                Texture = texture,
                // IgnoreSize thay vì KeepSize: nếu không, ảnh sprite người dùng
                // chép vào có độ phân giải gốc lớn (vd xuất từ GIMP/Photoshop)
                // sẽ ép kích thước tối thiểu của node bằng đúng ảnh gốc, tràn ra
                // ngoài khung sân khấu thay vì co theo CustomMinimumSize bên dưới.
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = size,
            };
        }
        else
        {
            // Chưa gán sprite — vẫn vẽ khối màu để biết nhân vật đang đứng
            // đâu, đúng nguyên tắc "thấy trước, sửa sau" dù thiếu asset.
            node = new ColorRect
            {
                Color = character is not null ? Color.FromHtml(character.Color) : Palette.Text3,
                CustomMinimumSize = size,
            };
        }

        node.MouseFilter = Control.MouseFilterEnum.Ignore;
        node.Modulate = new Color(dim, dim, dim, 1);
        node.AnchorLeft = node.AnchorRight = ratio;
        node.AnchorTop = 1f;
        node.AnchorBottom = 1f;
        node.OffsetLeft = -size.X / 2f;
        node.OffsetRight = size.X / 2f;
        node.OffsetTop = -size.Y;
        node.OffsetBottom = 0;
        return node;
    }

    public static ImageTexture? LoadTexture(string absPath)
    {
        var image = new Image();
        if (image.Load(absPath) != Error.Ok) return null;
        // Mipmap để thu nhỏ ảnh gốc to không răng cưa, và để hiệu ứng nhoè có
        // mức mờ sẵn mà lấy mẫu.
        if (!image.IsCompressed()) image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Nạp âm thanh từ đường dẫn tuyệt đối. Chỉ hỗ trợ ogg/mp3 vì Godot 4 chỉ
    /// cho hai định dạng này API nạp thẳng từ đĩa lúc chạy không qua import —
    /// wav cần file .import mà asset chép tay của người dùng không có.
    /// </summary>
    public static AudioStream? LoadAudio(string absPath)
    {
        var ext = Path.GetExtension(absPath).ToLowerInvariant();
        if (!File.Exists(absPath)) return null;

        try
        {
            return ext switch
            {
                ".ogg" => AudioStreamOggVorbis.LoadFromFile(absPath),
                ".mp3" => new AudioStreamMP3 { Data = File.ReadAllBytes(absPath) },
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }
}
