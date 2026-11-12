namespace FeedRaider.Engine.Raider;

/// <summary>The comm box of a darkness enemy that carries text: the wingmen's popup turned around. It sits above the enemy with its
/// pointer facing down, and the label ("SPAM", "RUDE", the labeler's own value, "HIDDEN REPLY") stands where a wingman's name would.
/// The author is never known here, so there is nothing else to show. Only carriers and gunships carry text, so only they speak;
/// the little guys of a wave are bare sprites.</summary>
public static class EnemyComm
{
    public const int MinRow = 3, MaxLabel = 24, TextSide = 4, Chrome = 3;
    /// <summary>How far onto the field (in pixel rows) an enemy flies before its box starts typing, so the first words aren't lost above the top.</summary>
    public const double OpenY = 6;
    /// <summary>Pixel rows kept between a buried enemy pinned inside the field and the field's bottom edge, so its echo stays on screen.</summary>
    public const double BuryBottomMargin = 4;
    /// <summary>A few fixed solid colours, so the glyph atlas gains a handful of entries and no more.</summary>
    public const uint PanelBg = 0x140608, Edge = 0xff2a2a, HeaderFg = 0xff5050, TextFg = 0xffe4e4, UnreadFg = 0xc08a8a;

    /// <summary>The header for a label value (blank falls back to "HIDDEN REPLY" for a hidden reply, else the kind's name).</summary>
    public static string Header(string? label, DarknessKind kind, DarkSource source)
    {
        string s = string.IsNullOrWhiteSpace(label) ? (source == DarkSource.Hidden ? "HIDDEN REPLY" : kind.ToString()) : label.Trim();
        s = s.ToUpperInvariant();
        return s.Length > MaxLabel ? s[..(MaxLabel - 1)] + "\u2026" : s;
    }

    public static bool Speaks(Enemy e) => e.Header is not null && e.Text is { Length: > 0 };

    /// <summary>Frames the box stays open: the wingmen's typing speed, then their hold.</summary>
    public static int Frames(Enemy e) => WingComms.TotalFrames(e.CommLines ?? [], e.PageLines, 0);

    /// <summary>Lines of text on one page: the wingmen's rule (about half the field, less the chrome), so a rare overlong post pages.</summary>
    public static int PageLinesFor(int fieldRows) => Math.Clamp(fieldRows / 2 - Chrome, WingComms.MinPageLines, WingComms.TextLines);

    /// <summary>Caches the wrapped text (once) and ages the box while the enemy is on the field.</summary>
    public static void Update(Enemy e, int fieldCols, int fieldRows = 24)
    {
        if (!Speaks(e)) return;
        if (e.CommLines is null)
        {
            e.CommLines = BubbleText.Format(BubbleText.Sanitize(e.Text!), WingComms.TextWidth(fieldCols), int.MaxValue);   // no line cap: the box pages rather than cut a post
            e.PageLines = PageLinesFor(fieldRows);
        }
        if (e.Y >= OpenY) e.CommAge++;
    }

    /// <summary>An enemy leaving the field (shot down, flown off) with words still to read: a copy of it, pinned inside the field,
    /// goes to <see cref="RaiderGame.Echoes"/> and keeps the box up until every page has been read out.</summary>
    public static void Bury(RaiderGame g, Enemy e)
    {
        if (!Speaks(e)) return;
        Update(e, g.Layout.PCols, g.Layout.PRows);
        if (e.CommLines is not { Length: > 0 } || e.CommAge >= Frames(e)) return;
        e.CommAge = Math.Max(e.CommAge, 1);
        e.X = Math.Clamp(e.X, 0, g.Layout.PW);
        e.Y = Math.Clamp(e.Y, OpenY, g.Layout.PH - BuryBottomMargin);
        g.Echoes.Add(e);
    }

    public static bool IsOpen(Enemy e) => Speaks(e) && e.CommLines is { Length: > 0 } && e.CommAge > 0 && e.CommAge < Frames(e);

    /// <summary>The cells of the box: centred over the enemy, its bottom edge on the row above the sprite, clamped inside the play
    /// columns and below the HUD (rows 0 and 1) and the phone's one-line feed strip on row 2.</summary>
    public static CellRect Box(Enemy e, FieldLayout lay)
    {
        int textW = Math.Max(Math.Max(e.Header!.Length, e.CommLines!.Max(l => l.Length)), 1);
        int w = Math.Min(textW + TextSide, lay.PCols), h = Math.Min(e.CommLines!.Length, e.PageLines) + Chrome;
        int col = Math.Clamp(lay.Col0 + (int)Math.Floor(e.X / 2) - w / 2, lay.Col0, Math.Max(lay.Col0, lay.Col0 + lay.PCols - w));
        int top = (int)Math.Floor((e.Y - e.Sprite.H / 2.0) / 2);
        int row = Math.Max(MinRow, top - h);
        if (row + h > top)
        {
            // no room above: it would sit on the sprite, so it goes below when it fits there (else it stays pinned and the sprite draws over it)
            int below = (int)Math.Ceiling((e.Y + e.Sprite.H / 2.0) / 2) + 1;
            if (below + h <= lay.PRows) row = below;
        }
        return new CellRect(col, row, w, h);
    }

    /// <summary>True when <paramref name="box"/> sits below the enemy (pointer facing up), because there was no room above it.</summary>
    public static bool IsBelow(Enemy e, CellRect box) => box.R * 2 >= e.Y + e.Sprite.H / 2.0;

    /// <summary>The boxes on show: those open, at most <paramref name="max"/> of them (the wingmen's cap), none over the player's ship.</summary>
    public static List<Enemy> Visible(RaiderGame g, FieldLayout lay, int max)
    {
        var ship = new CellRect(lay.Col0 + (int)Math.Floor((g.PX - g.Plane.W / 2.0) / 2) - 1, (int)Math.Floor((g.PY - g.Plane.H / 2.0) / 2) - 1, g.Plane.W / 2 + 3, g.Plane.H / 2 + 3);
        var list = new List<Enemy>();
        foreach (var e in g.Enemies)
        {
            if (list.Count >= max) break;
            if (IsOpen(e) && !Box(e, lay).Overlaps(ship)) list.Add(e);
        }
        foreach (var e in g.Echoes)
        {
            if (list.Count >= max) break;
            if (IsOpen(e) && !Box(e, lay).Overlaps(ship)) list.Add(e);
        }
        return list;
    }
}
