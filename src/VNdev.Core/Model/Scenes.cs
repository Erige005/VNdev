namespace VNdev.Core.Model;

public enum TransitionType
{
    Cut, Fade, Dissolve,
    WipeLeft, WipeRight, WipeUp, WipeDown,
    Pixelate, Blur,
}

public sealed class Transition
{
    public TransitionType Type { get; set; } = TransitionType.Fade;

    /// <summary>Thời lượng tính bằng giây.</summary>
    public float Duration { get; set; } = 0.5f;

    /// <summary>Ảnh mask cho transition tuỳ chỉnh. Chỉ dùng với Dissolve.</summary>
    public string? Mask { get; set; }
}

public enum MotionType { None, Fade, SlideLeft, SlideRight, SlideUp, SlideDown, Bounce }

public sealed class Motion
{
    public MotionType Type { get; set; } = MotionType.Fade;
    public float Duration { get; set; } = 0.4f;
}

/// <summary>Năm vị trí đứng chuẩn, tính theo tỷ lệ chiều ngang màn hình.</summary>
public enum StageAnchor { FarLeft, Left, Center, Right, FarRight }

public static class StageAnchors
{
    public static float Ratio(StageAnchor anchor) => anchor switch
    {
        StageAnchor.FarLeft => 0.15f,
        StageAnchor.Left => 0.30f,
        StageAnchor.Center => 0.50f,
        StageAnchor.Right => 0.70f,
        _ => 0.85f,
    };

    /// <summary>Độ sáng áp cho nhân vật không phải người đang nói.</summary>
    public const float DimInactive = 0.62f;
}

/// <summary>Một nhân vật đang đứng trên sân khấu tại một thời điểm.</summary>
public sealed class StageActor
{
    public string Character { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;

    /// <summary>Vị trí chuẩn. Bỏ qua nếu dùng <see cref="X"/> để đặt tự do.</summary>
    public StageAnchor? Anchor { get; set; }

    /// <summary>Toạ độ tự do theo hệ thiết kế. Ưu tiên hơn Anchor nếu có cả hai.</summary>
    public float? X { get; set; }
    public float? Y { get; set; }

    public float? Scale { get; set; }
    public bool Flip { get; set; }

    /// <summary>Độ sáng 0–1. Engine tự hạ xuống DimInactive cho nhân vật không nói.</summary>
    public float? Dim { get; set; }

    public Motion? Enter { get; set; }

    /// <summary>
    /// Có giá trị nghĩa là nhân vật <b>rời sân khấu</b> tại dòng thoại chứa thay
    /// đổi này, theo hiệu ứng đó.
    /// </summary>
    /// <remarks>
    /// Dùng lại trường sẵn có thay vì thêm cờ <c>leave</c> riêng: cờ bool không
    /// null sẽ bị ghi <c>"leave": false</c> vào mọi diễn viên của mọi dự án cũ,
    /// lần lưu đầu tiên Git báo cả loạt file thay đổi dù người dùng không sửa gì.
    /// </remarks>
    public Motion? Exit { get; set; }

    public StageActor Clone() => (StageActor)MemberwiseClone();
}

public enum ScreenEffectType { Shake, Flash, Tint, BlurBackground, KenBurns }

public sealed class ScreenEffect
{
    public ScreenEffectType Type { get; set; }
    public float? Intensity { get; set; }
    public float? Duration { get; set; }
    public string? Color { get; set; }
}

public enum ParticlePreset { Rain, Snow, Petals, Dust, Bubbles }

public sealed class ParticleEffect
{
    public ParticlePreset Preset { get; set; }
    public float? Density { get; set; }
}

public sealed class AudioCue
{
    public string Src { get; set; } = string.Empty;
    public float? Volume { get; set; }
    public float? FadeIn { get; set; }
    public bool Loop { get; set; } = true;

    /// <summary>Điểm bắt đầu lặp tính bằng giây — cho nhạc có đoạn intro.</summary>
    public float? LoopStart { get; set; }
}

/// <summary>
/// Một dòng thoại — đơn vị nhỏ nhất người chơi tương tác.
/// </summary>
/// <remarks><see cref="Speaker"/> bằng null nghĩa là lời dẫn truyện, hiển thị không kèm tên.</remarks>
public sealed class DialogueLine
{
    public string Id { get; set; } = string.Empty;
    public string? Speaker { get; set; }

    /// <summary>Đổi biểu cảm ngay tại dòng này.</summary>
    public string? Expression { get; set; }

    public Localized Text { get; set; } = new();

    public string? Sfx { get; set; }

    /// <summary>File lồng tiếng, tự dừng khi sang dòng khác.</summary>
    public string? Voice { get; set; }

    /// <summary>Thay đổi sân khấu áp dụng từ dòng này trở đi.</summary>
    public List<StageActor>? StageChanges { get; set; }

    public ScreenEffect? ScreenEffect { get; set; }

    /// <summary>Chờ bao nhiêu giây trước khi cho phép bấm tiếp.</summary>
    public float? WaitBefore { get; set; }
}

/// <summary>
/// Nội dung một cảnh.
/// </summary>
/// <remarks>
/// Tách khỏi node đồ thị: node giữ vị trí và liên kết, cảnh giữ nội dung. Nhờ
/// vậy hai người sửa hai cảnh khác nhau thì Git merge không đụng cùng file.
/// </remarks>
public sealed class SceneData
{
    public string Id { get; set; } = string.Empty;
    public string? Background { get; set; }
    public Transition? BackgroundTransition { get; set; }
    public AudioCue? Bgm { get; set; }
    public AudioCue? Ambient { get; set; }
    public ParticleEffect? Particles { get; set; }

    /// <summary>Trạng thái sân khấu lúc bắt đầu cảnh.</summary>
    public List<StageActor> Stage { get; set; } = new();

    public List<DialogueLine> Lines { get; set; } = new();

    /// <summary>
    /// Trạng thái sân khấu tại dòng thứ <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// Gộp trạng thái đầu cảnh với mọi thay đổi tính tới dòng đó, và tự làm mờ
    /// những nhân vật không phải người đang nói. Đây là hàm cho phép editor
    /// hiện đúng sân khấu tại dòng người dùng đang chọn mà không cần chạy game.
    /// </remarks>
    public List<StageActor> StageAt(int index)
    {
        var actors = new Dictionary<string, StageActor>();
        foreach (var actor in Stage) actors[actor.Character] = actor.Clone();

        for (var i = 0; i <= index && i < Lines.Count; i++)
        {
            var line = Lines[i];

            foreach (var change in line.StageChanges ?? new List<StageActor>())
            {
                if (change.Exit is not null)
                {
                    actors.Remove(change.Character);
                    continue;
                }

                if (actors.TryGetValue(change.Character, out var existing))
                {
                    var merged = existing.Clone();
                    // Biểu cảm rỗng nghĩa là "giữ nguyên" — để một thay đổi chỉ dời
                    // vị trí không vô tình xoá mất biểu cảm đang có.
                    if (!string.IsNullOrEmpty(change.Expression)) merged.Expression = change.Expression;
                    if (change.Anchor is not null) merged.Anchor = change.Anchor;
                    if (change.X is not null) merged.X = change.X;
                    if (change.Y is not null) merged.Y = change.Y;
                    if (change.Scale is not null) merged.Scale = change.Scale;
                    if (change.Dim is not null) merged.Dim = change.Dim;
                    actors[change.Character] = merged;
                }
                else
                {
                    actors[change.Character] = change.Clone();
                }
            }

            if (line.Speaker is not null)
            {
                foreach (var key in actors.Keys.ToList())
                {
                    actors[key].Dim = key == line.Speaker ? 1f : StageAnchors.DimInactive;
                }
                if (line.Expression is not null && actors.TryGetValue(line.Speaker, out var speaking))
                {
                    speaking.Expression = line.Expression;
                }
            }
        }

        return actors.Values.ToList();
    }

    /// <summary>
    /// Sân khấu thay đổi những gì khi chuyển sang dòng <paramref name="index"/>:
    /// mỗi nhân vật có mặt trước hoặc sau dòng đó một mục, kèm hiệu ứng vào/ra.
    /// </summary>
    /// <remarks>
    /// Tính ở lõi thay vì ở tầng vẽ để trình soạn cảnh và màn chơi thử diễn
    /// cùng một chuyển động, và để kiểm được bằng test không cần mở engine.
    /// Ở dòng đầu, nhân vật đứng sẵn có <see cref="StageActor.Enter"/> được coi
    /// là chưa có mặt — họ bước ra khi cảnh vừa mở chứ không đứng chờ sẵn.
    /// </remarks>
    public List<StageStep> StepsAt(int index)
    {
        var before = index <= 0
            ? Stage.Where(a => a.Enter is null).Select(a => a.Clone()).ToList()
            : StageAt(index - 1);
        var after = StageAt(index);
        var changes = index >= 0 && index < Lines.Count ? Lines[index].StageChanges ?? new List<StageActor>() : new List<StageActor>();

        var steps = new List<StageStep>();
        foreach (var now in after)
        {
            var was = before.FirstOrDefault(b => b.Character == now.Character);
            Motion? enter = null;
            if (was is null)
            {
                enter = changes.FirstOrDefault(c => c.Character == now.Character)?.Enter
                    ?? (index <= 0 ? Stage.FirstOrDefault(s => s.Character == now.Character)?.Enter : null);
            }
            steps.Add(new StageStep(now.Character, was, now, enter, null));
        }
        foreach (var was in before.Where(b => after.All(a => a.Character != b.Character)))
        {
            steps.Add(new StageStep(was.Character, was, null, null, changes.FirstOrDefault(c => c.Character == was.Character)?.Exit));
        }
        return steps;
    }
}

/// <summary>
/// Một nhân vật khi sang dòng mới. <see cref="Before"/> null là vừa bước ra,
/// <see cref="After"/> null là vừa rời đi; có cả hai thì có thể đã dời chỗ,
/// đổi biểu cảm hoặc sáng/tối theo người nói.
/// </summary>
public sealed record StageStep(string Character, StageActor? Before, StageActor? After, Motion? Enter, Motion? Exit);
