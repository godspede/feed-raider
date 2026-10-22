namespace FeedRaider.Engine.Raider;

/// <summary>A rectangle of terminal cells.</summary>
public readonly record struct CellRect(int C, int R, int W, int H)
{
    public int Right => C + W;
    public int Bottom => R + H;
    public bool Overlaps(CellRect o) => C < o.Right && o.C < Right && R < o.Bottom && o.R < Bottom;
}

/// <summary>Star Fox style comms: one wingman at a time holds the full comm under its ship, in arrival order;
/// the rest wear a chip. This is the sim half (who speaks, for how long); <see cref="CommPlacement"/> is the layout half.</summary>
public sealed class WingComms
{
    public const int MaxTextW = 30, MinTextW = 16, TextLines = 11, MaxPages = 6, PrevLinesMax = 3, MaxStubs = 2, MinPageLines = 4, CommSide = 7, CommChrome = 3, ChipW = 14, ChipH = 4, ChipHandle = 8;

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

    /// <summary>1 while open, falling to 0 over the collapse; the fraction of the comm's height to draw.</summary>
    public static double Openness(Wingman w)
    {
        int left = TotalFrames(w) - w.CommAge, k = Tuning.CommCollapseFrames;
        return Math.Clamp(Math.Min(w.CommAge + 1, left) / (double)k, 0, 1);
    }

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

/// <summary>Where a comm or chip goes: under its ship, flipped above near the bottom, clamped to the field, and clear of
/// the cells it is told to avoid (the player's ship, the bomb button, speech bubbles, badges).</summary>
public static class CommPlacement
{
    /// <param name="shipCol">cell column of the ship centre.</param>
    /// <param name="shipTop">first cell row of the ship; <paramref name="shipBottom"/> the row after its last.</param>
    /// <param name="field">the playfield in cells; rows above <paramref name="minRow"/> belong to the HUD.</param>
    public static CellRect Place(int shipCol, int shipTop, int shipBottom, int w, int h, CellRect field, int minRow, ReadOnlySpan<CellRect> avoid, out bool above)
    {
        int maxRow = Math.Max(minRow, field.Bottom - h);
        int belowRow = Math.Clamp(shipBottom + 1, minRow, maxRow);
        int aboveRow = Math.Clamp(shipTop - h - 1, minRow, maxRow);
        bool fitsBelow = shipBottom + 1 <= field.Bottom - h, fitsAbove = shipTop - h - 1 >= minRow;
        int col = Math.Clamp(shipCol - w / 2, field.C, Math.Max(field.C, field.Right - w));
        bool belowFirst = fitsBelow || !fitsAbove;
        int r0 = belowFirst ? belowRow : aboveRow, r1 = belowFirst ? aboveRow : belowRow;
        bool f0 = !belowFirst, f1 = belowFirst;

        var a = new CellRect(col, r0, w, h);
        if (!Hits(a, avoid)) { above = f0; return a; }
        a = new CellRect(col, r1, w, h);
        if (!Hits(a, avoid)) { above = f1; return a; }
        // slide sideways past whatever is in the way
        int maxCol = Math.Max(field.C, field.Right - w);
        for (int pass = 0; pass < 2; pass++)
            foreach (var o in avoid)
            {
                int row = pass == 0 ? r0 : r1;
                a = new CellRect(Math.Clamp(o.C - w, field.C, maxCol), row, w, h);
                if (!Hits(a, avoid)) { above = pass == 0 ? f0 : f1; return a; }
                a = new CellRect(Math.Clamp(o.Right, field.C, maxCol), row, w, h);
                if (!Hits(a, avoid)) { above = pass == 0 ? f0 : f1; return a; }
            }
        above = f0;
        return new CellRect(col, r0, w, h);
    }

    private static bool Hits(CellRect r, ReadOnlySpan<CellRect> avoid)
    {
        foreach (var a in avoid) if (r.Overlaps(a)) return true;
        return false;
    }
}
