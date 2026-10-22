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
    public const int TextW = 16, TextLines = 3, CommW = TextW + 7, CommH = 6, ChipW = 14, ChipH = 4, ChipHandle = 8;

    private int _seq;

    public Wingman? Speaker { get; private set; }

    /// <summary>Fills in the wingman's cached comm text; called once at spawn.</summary>
    public void Arrive(Wingman w)
    {
        w.CommSeq = _seq++;
        w.CommLines = BubbleText.Format(w.Text, TextW, TextLines);
        int n = 0;
        foreach (var l in w.CommLines) n += l.Length;
        w.CommChars = n;
        w.HandleTag = Trim("@" + w.Handle, TextW);
        w.LikesTag = $"\u2665 {w.Post.LikeCount:N0}";   // \u2665 heart suit
        w.ChipTag = Trim(w.Handle, ChipHandle);
        w.Accent = PlaneFactory.Accent(w.Post.AuthorDid);
    }

    public static int RevealFrames(int chars) => (chars * Tuning.Fps + Tuning.CommCharsPerSecond - 1) / Tuning.CommCharsPerSecond;

    public static int TotalFrames(Wingman w) => RevealFrames(w.CommChars) + Tuning.CommHoldFrames + Tuning.CommCollapseFrames;

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
