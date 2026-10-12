using System;
using System.Collections.Generic;
using Godot;
using VNdev.Core.Model;

namespace VNdev.App;

/// <summary>
/// Năm kiểu particle dựng sẵn: mưa, tuyết, hoa anh đào, bụi, bong bóng.
/// </summary>
/// <remarks>
/// Hạt vẽ bằng code chứ không lấy từ asset: người viết truyện chỉ cần chọn
/// "Mưa" là có mưa, không phải đi tìm ảnh giọt nước. Dùng CPUParticles2D
/// thay vì GPUParticles2D vì chạy được trên mọi renderer, kể cả máy yếu và
/// chế độ không cửa sổ lúc kiểm thử. Mọi tốc độ và cỡ hạt nhân theo chiều cao
/// khung (chuẩn 720) để khung nhỏ trong editor trông y như toàn màn hình.
/// </remarks>
public static class ParticlePresets
{
    private static readonly Dictionary<ParticlePreset, Texture2D> Textures = new();

    public static Node2D Build(ParticleEffect fx, Vector2 size)
    {
        var k = size.Y / 720f;
        var density = Math.Clamp(fx.Density ?? 1f, 0.1f, 3f);
        var p = new CpuParticles2D
        {
            Texture = TextureFor(fx.Preset),
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            LocalCoords = false,
            Emitting = true,
        };

        switch (fx.Preset)
        {
            case ParticlePreset.Rain:
                Setup(p, amount: 260, lifetime: 0.9f, density);
                Top(p, size, widen: 0.35f);
                p.Direction = new Vector2(0.18f, 1);
                p.Spread = 2;
                p.Gravity = Vector2.Zero;
                Speed(p, 1000 * k, 1300 * k);
                // Giọt mưa nghiêng theo hướng rơi.
                p.AngleMin = p.AngleMax = -10;
                Scale(p, 0.7f * k, 1.2f * k);
                p.ColorRamp = Fade(new Color(0.75f, 0.85f, 1f, 0.55f));
                break;
            case ParticlePreset.Snow:
                Setup(p, amount: 150, lifetime: 9f, density);
                Top(p, size, widen: 0.2f);
                p.Direction = new Vector2(0.15f, 1);
                p.Spread = 25;
                p.Gravity = new Vector2(0, 8 * k);
                Speed(p, 35 * k, 80 * k);
                Scale(p, 0.35f * k, 1.1f * k);
                p.ColorRamp = Fade(new Color(1, 1, 1, 0.9f));
                break;
            case ParticlePreset.Petals:
                Setup(p, amount: 70, lifetime: 10f, density);
                Top(p, size, widen: 0.5f);
                p.Direction = new Vector2(0.45f, 1);
                p.Spread = 20;
                p.Gravity = new Vector2(6 * k, 10 * k);
                Speed(p, 50 * k, 110 * k);
                p.AngleMin = 0;
                p.AngleMax = 360;
                p.AngularVelocityMin = -120;
                p.AngularVelocityMax = 120;
                Scale(p, 0.6f * k, 1.3f * k);
                p.HueVariationMin = -0.03f;
                p.HueVariationMax = 0.03f;
                p.ColorRamp = Fade(new Color(1f, 0.78f, 0.86f, 0.95f));
                break;
            case ParticlePreset.Dust:
                Setup(p, amount: 80, lifetime: 7f, density);
                p.Position = size / 2f;
                p.EmissionRectExtents = size / 2f;
                p.Direction = new Vector2(0.3f, -1);
                p.Spread = 180;
                p.Gravity = Vector2.Zero;
                Speed(p, 4 * k, 18 * k);
                Scale(p, 0.3f * k, 0.9f * k);
                p.ColorRamp = Fade(new Color(1f, 0.95f, 0.8f, 0.45f));
                break;
            default: // Bubbles
                Setup(p, amount: 45, lifetime: 8f, density);
                p.Position = new Vector2(size.X / 2f, size.Y + 20 * k);
                p.EmissionRectExtents = new Vector2(size.X / 2f, 10 * k);
                p.Direction = new Vector2(0, -1);
                p.Spread = 12;
                p.Gravity = new Vector2(0, -6 * k);
                Speed(p, 45 * k, 110 * k);
                Scale(p, 0.5f * k, 1.4f * k);
                p.ColorRamp = Fade(new Color(0.8f, 0.95f, 1f, 0.7f));
                break;
        }

        // Chạy trước trọn một vòng đời: mở cảnh là trời đang mưa, không phải
        // vài giây đầu chỉ lác đác mấy giọt từ mép trên.
        p.Preprocess = p.Lifetime;
        return p;
    }

    private static void Setup(CpuParticles2D p, int amount, float lifetime, float density)
    {
        p.Amount = Math.Max(1, (int)(amount * density));
        p.Lifetime = lifetime;
    }

    /// <summary>Phát từ dải trên mép khung, rộng hơn khung để hạt rơi xiên vẫn phủ kín góc.</summary>
    private static void Top(CpuParticles2D p, Vector2 size, float widen)
    {
        p.Position = new Vector2(size.X / 2f - size.X * widen / 2f, -10);
        p.EmissionRectExtents = new Vector2(size.X * (1 + widen) / 2f, 4);
    }

    private static void Speed(CpuParticles2D p, float min, float max)
    {
        p.InitialVelocityMin = min;
        p.InitialVelocityMax = max;
    }

    private static void Scale(CpuParticles2D p, float min, float max)
    {
        p.ScaleAmountMin = min;
        p.ScaleAmountMax = max;
    }

    /// <summary>Hiện dần lúc sinh, mờ dần lúc tắt — hạt không bật tắt cái phụt giữa màn hình.</summary>
    private static Gradient Fade(Color c) => new()
    {
        Offsets = new[] { 0f, 0.12f, 0.85f, 1f },
        Colors = new[] { new Color(c, 0), c, c, new Color(c, 0) },
    };

    private static Texture2D TextureFor(ParticlePreset preset)
    {
        if (Textures.TryGetValue(preset, out var cached)) return cached;
        var tex = preset switch
        {
            ParticlePreset.Rain => Paint(3, 34, (x, y, w, h) => 1f - Math.Abs(x - (w - 1) / 2f) / (w / 2f)),
            ParticlePreset.Petals => Paint(18, 12, (x, y, w, h) => Soft(Ellipse(x, y, w, h), 0.2f)),
            ParticlePreset.Bubbles => Paint(20, 20, (x, y, w, h) =>
            {
                // Vòng sáng ở viền, ruột trong suốt nhạt — đọc ra là bong bóng chứ không phải chấm tròn.
                var r = Ellipse(x, y, w, h);
                return r > 1 ? 0 : 0.18f + 0.82f * MathF.Pow(r, 6);
            }),
            ParticlePreset.Dust => Paint(10, 10, (x, y, w, h) => Soft(Ellipse(x, y, w, h), 1f)),
            _ => Paint(12, 12, (x, y, w, h) => Soft(Ellipse(x, y, w, h), 0.5f)),
        };
        Textures[preset] = tex;
        return tex;
    }

    /// <summary>Khoảng cách chuẩn hoá tới tâm: 0 ở giữa, 1 ở viền elip.</summary>
    private static float Ellipse(int x, int y, int w, int h)
    {
        var dx = (x + 0.5f - w / 2f) / (w / 2f);
        var dy = (y + 0.5f - h / 2f) / (h / 2f);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float Soft(float r, float softness) => r >= 1 ? 0 : Math.Clamp((1 - r) / Math.Max(0.01f, softness), 0, 1);

    private static ImageTexture Paint(int w, int h, Func<int, int, int, int, float> alpha)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            img.SetPixel(x, y, new Color(1, 1, 1, Math.Clamp(alpha(x, y, w, h), 0, 1)));
        }
        return ImageTexture.CreateFromImage(img);
    }
}
