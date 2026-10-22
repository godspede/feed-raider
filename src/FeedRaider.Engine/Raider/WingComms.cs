namespace FeedRaider.Engine.Raider;

/// <summary>A rectangle of terminal cells.</summary>
public readonly record struct CellRect(int C, int R, int W, int H)
{
    public int Right => C + W;
    public int Bottom => R + H;
    public bool Overlaps(CellRect o) => C < o.Right && o.C < Right && R < o.Bottom && o.R < Bottom;
}

/// <summary>Star Fox style comms: every wingman carries its post in a comm under its ship, and one at a time, in arrival
/// order, reads it out. This is the sim half (who speaks, for how long); <see cref="CommPainter"/> draws it.</summary>
public sealed class WingComms
{
    public const int MaxTextW = 30, MinTextW = 16, TextLines = 11, MaxPages = 6, PrevLinesMax = 3, MaxStubs = 2, MinPageLines = 4, CommSide = 7, CommChrome = 3, ChipHandle = 8;

    private int _seq;

    public Wingman? Speaker { get; private set; }

    /// <summary>Text columns for a field <paramref name="fieldCols"/> wide: as wide as <see cref="MaxTextW"/> where there is room, never so wide the comm covers the field.</summary>
    public static int TextWidth(int fieldCols) => Math.Clamp(fieldCols - 2 * CommSide, MinTextW, MaxTextW);

    public static int CommW(Wingman w) => w.CommTextW + CommSide;

    /// <summary>Header, borders and one page of text; a short post gets a short comm.</summary>
    public static int CommH(Wingman w) => CommChrome + StubRows(w) + w.PrevLines.Length + Math.Min(w.CommLines.Length, w.PageLines);

    public static int Pages(Wingman w) => (w.CommLines.Length + w.PageLines - 1) / w.PageLines;

    /// <summary>One-line stubs for the posts opened before the previous one: at most <see cref="MaxStubs"/>, the first standing for any further back.</summary>
    public static int StubRows(Wingman w) => Math.Min(MaxStubs, Math.Max(0, w.Earlier.Count - 1));

    /// <summary>The stub lines, oldest first, each cut to <paramref name="width"/>. When posts are hidden above the stubs the first reads "+N earlier".</summary>
    public static string[] Stubs(Wingman w, int width)
    {
        int older = Math.Max(0, w.Earlier.Count - 1), rows = StubRows(w);
        var lines = new string[rows];
        for (int i = 0; i < rows; i++)
        {
            int idx = older - rows + i;
            lines[i] = i == 0 && older > rows ? $"+{older - rows + 1} earlier" : CommPainter.Snippet(w.Earlier[idx], width - 2);
            if (lines[i].Length > width) lines[i] = lines[i][..width];
        }
        return lines;
    }

    /// <summary>Is a thread being read down: more than one post open and the newest not yet acted on. Its comm stays open until then.</summary>
    public static bool HeldOpen(Wingman w) => w.Earlier.Count > 0 && !w.Followed && w.Phase != WingPhase.Leave;

    /// <summary>Opens <paramref name="next"/> under the posts already open: the newest becomes the previous (shortened), those before
    /// it collapse to stubs, and the wingman takes the mic to read the new post out. <paramref name="fieldRows"/> bounds the bubble to
    /// about half the field, so it stays readable on a phone and leaves the player's ship room.</summary>
    public void OpenNext(Wingman w, FeedPost next, int fieldRows)
    {
        w.Earlier.Add(w.Text);
        w.PrevLines = BubbleText.Format(w.Text, w.CommTextW, PrevLinesMax);
        w.Post = next;
        w.Text = BubbleText.Sanitize(next.Text);
        w.CommLines = BubbleText.Format(w.Text, w.CommTextW, TextLines * MaxPages);
        int n = 0;
        foreach (var l in w.CommLines) n += l.Length;
        w.CommChars = n;
        w.LikesTag = $"\u2665 {next.LikeCount:N0}";   // U+2665, the heart suit
        w.PostAge = 0;
        w.PageLines = Math.Clamp(fieldRows / 2 - CommChrome - StubRows(w) - w.PrevLines.Length, MinPageLines, TextLines);
        if (Speaker is { } s && s != w) s.Comm = CommState.Spoken;
        Speaker = w; w.Comm = CommState.Speaking; w.CommAge = 0;
    }

    /// <summary>Fills in the wingman's cached comm text; called once at spawn. A post too long for one page of
    /// <see cref="TextLines"/> lines is read page by page.</summary>
    public void Arrive(Wingman w, int fieldCols)
    {
        w.CommSeq = _seq++;
        w.CommTextW = TextWidth(fieldCols);
        w.CommLines = BubbleText.Format(w.Text, w.CommTextW, TextLines * MaxPages);
        int n = 0;
        foreach (var l in w.CommLines) n += l.Length;
        w.CommChars = n;
        w.HandleTag = Trim("@" + w.Handle, w.CommTextW);
        w.LikesTag = $"\u2665 {w.Post.LikeCount:N0}";   // \u2665 heart suit
        w.ChipTag = Trim(w.Handle, ChipHandle);
        w.Accent = PlaneFactory.Accent(w.Post.AuthorDid);
    }

    public static int RevealFrames(int chars) => (chars * Tuning.Fps + Tuning.CommCharsPerSecond - 1) / Tuning.CommCharsPerSecond;

    public static int PageChars(Wingman w, int page)
    {
        int n = 0;
        for (int i = page * w.PageLines; i < Math.Min(w.CommLines.Length, (page + 1) * w.PageLines); i++) n += w.CommLines[i].Length;
        return n;
    }

    public static int TotalFrames(Wingman w)
    {
        int pages = Pages(w), total = Tuning.CommCollapseFrames;
        for (int p = 0; p < pages; p++) total += RevealFrames(PageChars(w, p)) + (p == pages - 1 ? Tuning.CommHoldFrames : Tuning.CommPageHoldFrames);
        return total;
    }

    /// <summary>The page on show and the characters of it typed so far, <paramref name="age"/> frames into the comm.</summary>
    public static (int Page, int Shown) Progress(Wingman w, int age)
    {
        int pages = Pages(w);
        for (int p = 0; p < pages - 1; p++)
        {
            int chars = PageChars(w, p), span = RevealFrames(chars) + Tuning.CommPageHoldFrames;
            if (age < span) return (p, Revealed(age, chars));
            age -= span;
        }
        return (Math.Max(0, pages - 1), Revealed(age, PageChars(w, Math.Max(0, pages - 1))));
    }

    /// <summary>Characters shown after <paramref name="age"/> frames of speaking.</summary>
    public static int Revealed(int age, int chars) => Math.Min(chars, age * Tuning.CommCharsPerSecond / Tuning.Fps);

    /// <summary>Advances the mic one frame. Call after the wingmen have aged and been removed.</summary>
    public void Update(List<Wingman> wings)
    {
        if (Speaker is { } s)
        {
            if (!wings.Contains(s) || s.Phase == WingPhase.Leave) { s.Comm = CommState.Spoken; Speaker = null; }
            else if (HeldOpen(s) && s.CommAge + 1 >= TotalFrames(s) - Tuning.CommCollapseFrames) { }   // a thread being read stays open, just short of collapsing
            else if (++s.CommAge >= TotalFrames(s)) { s.Comm = CommState.Spoken; Speaker = null; }
        }
        if (Speaker is not null) return;
        Wingman? next = null;
        foreach (var w in wings)
        {
            if (w.Comm != CommState.Waiting) continue;
            if (w.Phase == WingPhase.Leave) { w.Comm = CommState.Spoken; continue; }
            if (w.Age < Tuning.WingEnterFrames / 2) continue;       // radios in once the ship is on screen
            if (next is null || w.CommSeq < next.CommSeq) next = w;
        }
        if (next is null) return;
        next.Comm = CommState.Speaking; next.CommAge = 0; Speaker = next;
    }

    private static string Trim(string s, int w) => s.Length <= w ? s : s[..Math.Max(1, w - 1)] + "…";
}
