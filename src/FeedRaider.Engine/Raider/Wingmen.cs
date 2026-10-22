namespace FeedRaider.Engine.Raider;

public enum WingPhase { Enter, Fight, Leave }

public enum CommState { Waiting, Speaking, Spoken }

/// <summary>A feed post flying beside the player. It speaks, fights for a while, then leaves.</summary>
public sealed class Wingman
{
    public required FeedPost Post { get; init; }
    public required Sprite Plane { get; init; }
    public required double HomeX { get; init; }
    public required double HomeY { get; init; }
    public required string Text { get; init; }
    public required string Handle { get; init; }
    /// <summary>Firepower multiplier, from log(likeCount).</summary>
    public required double Firepower { get; init; }
    public double X, Y;
    public WingPhase Phase = WingPhase.Enter;
    public int Age;
    public int FireCd;
    public bool Liked;
    public uint[]? Avatar;                    // 8x8, 0xRRGGBB, null until fetched (or when CORS blocks it)
    public int Slot;
    public int MuzzleFrames;

    // the comm (see WingComms): text wrapped once at spawn, strings cached for drawing
    public string[] CommLines = [];
    public int CommChars;                     // characters over all lines
    public string HandleTag = "";             // "@handle" for the comm header
    public string LikesTag = "";
    public string ChipTag = "";               // short handle for the chip
    public uint Accent;
    public int CommSeq;                       // arrival order
    public CommState Comm = CommState.Waiting;
    public int CommAge;                       // frames since this wingman took the mic

    public int Typed(int fps = Tuning.Fps) => Age * Tuning.TypeCharsPerSecond / fps;
}

/// <summary>Which feed posts may become wingmen, and the queue of those waiting. Text-only posts (a quote of another post is
/// fine) with at least <see cref="Tuning.WingMinTextChars"/> characters; each post at most once.</summary>
public sealed class WingQueue
{
    private readonly Queue<FeedPost> _q = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public int Count => _q.Count;

    public static bool Eligible(FeedPost p) => !p.HasMedia && BubbleText.Sanitize(p.Text).Length >= Tuning.WingMinTextChars;

    public bool Offer(FeedPost p)
    {
        if (!Eligible(p) || _seen.Contains(p.Uri) || _q.Count >= Tuning.WingQueueMax) return false;
        if (_seen.Count > 4000) _seen.Clear();
        _seen.Add(p.Uri);
        _q.Enqueue(p);
        return true;
    }

    public bool TryDequeue(out FeedPost post) => _q.TryDequeue(out post!);
}
