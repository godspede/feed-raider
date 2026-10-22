namespace FeedRaider.Engine.Raider;

public enum WingPhase { Enter, Fight, Leave }

public enum CommState { Waiting, Speaking, Spoken }

/// <summary>A feed post flying beside the player. It speaks, fights for a while, then leaves.</summary>
public sealed class Wingman
{
    /// <summary>The newest open post: the first post at spawn, then each reply the player opened. It is the one a like targets.</summary>
    public required FeedPost Post { get; set; }
    public required Sprite Plane { get; init; }
    public required double HomeX { get; init; }
    public required double HomeY { get; init; }
    public required string Text { get; set; }
    public required string Handle { get; init; }
    /// <summary>Firepower multiplier, from log(likeCount).</summary>
    public required double Firepower { get; init; }
    public double X, Y;
    public WingPhase Phase = WingPhase.Enter;
    public int Age;
    public int FireCd;
    public bool Liked;
    /// <summary>The like press on <see cref="Post"/> has been taken and its thread is being followed (or has ended): it is not a target again.</summary>
    public bool Followed;
    /// <summary>Posts opened before <see cref="Post"/>, oldest first, as sanitised text.</summary>
    public readonly List<string> Earlier = [];
    /// <summary>The previous post, wrapped short for the comm; empty when no thread is open.</summary>
    public string[] PrevLines = [];
    /// <summary>Frames added to the fight phase while a thread is open; see <see cref="Tuning.ThreadReadFrames"/>.</summary>
    public int FightExtra;
    /// <summary>Frames since <see cref="Post"/> opened.</summary>
    public int PostAge;
    public int TotalFrames => Tuning.WingEnterFrames + Tuning.WingFightFrames + FightExtra + Tuning.WingLeaveFrames;
    public uint[]? Avatar;                    // 8x8, 0xRRGGBB, null until fetched (or when CORS blocks it)
    public int Slot;
    public int MuzzleFrames;

    // the comm (see WingComms): text wrapped once at spawn, strings cached for drawing
    public string[] CommLines = [];
    public int CommTextW = WingComms.MinTextW;
    public int PageLines = WingComms.TextLines;   // lines of the newest post per page
    public int CommChars;                     // characters over all lines
    public string HandleTag = "";             // "@handle" for the comm header
    public string LikesTag = "";
    public string ChipTag = "";               // short handle for the phone panel line
    public uint Accent;
    public int CommSeq;                       // arrival order
    public CommState Comm = CommState.Waiting;
    public int CommAge;                       // frames since this wingman took the mic

    public int Typed(int fps = Tuning.Fps) => PostAge * Tuning.TypeCharsPerSecond / fps;
}

/// <summary>Which feed posts may become wingmen, and the queue of those waiting. Plain posts (no embed: no image, video, link card or quote) with at least <see cref="Tuning.WingMinTextChars"/> characters; each post at most once.</summary>
public sealed class WingQueue
{
    private readonly Queue<FeedPost> _q = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public int Count => _q.Count;
    /// <summary>True for a signed-out viewer: authors who opted out of logged-out viewers are skipped.</summary>
    public bool SignedOut { get; set; }

    /// <summary>Whether the post may be shown at all: never an adult or graphic post, and when the viewer is signed out never one by an
    /// author who opted out of logged-out viewers (a signed-in feed is governed by the AppView).</summary>
    public static bool Allowed(FeedPost p, bool signedOut) => !p.Adult && !(signedOut && p.AuthorOptOut);

    public static bool Eligible(FeedPost p) => Eligible(p, false);

    public static bool Eligible(FeedPost p, bool signedOut) =>
        Allowed(p, signedOut) && !p.HasEmbed && BubbleText.Sanitize(p.Text).Length >= Tuning.WingMinTextChars;

    public bool Offer(FeedPost p)
    {
        if (!Eligible(p, SignedOut) || _seen.Contains(p.Uri) || _q.Count >= Tuning.WingQueueMax) return false;
        if (_seen.Count > 4000) _seen.Clear();
        _seen.Add(p.Uri);
        _q.Enqueue(p);
        return true;
    }

    public bool TryDequeue(out FeedPost post) => _q.TryDequeue(out post!);
}
