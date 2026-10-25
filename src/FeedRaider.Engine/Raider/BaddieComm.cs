namespace FeedRaider.Engine.Raider;

/// <summary>The comm box of a darkness enemy that carries text: the wingmen's popup turned around. It sits above the enemy with its
/// pointer facing down, and the label ("SPAM", "RUDE", the labeler's own value, "HIDDEN REPLY") stands where a wingman's name would.
/// The author is never known here, so there is nothing else to show. Only carriers and gunships carry text, so only they speak;
/// the little guys of a wave are bare sprites.</summary>
public static class BaddieComm
{
    public const int MinRow = 3, MaxLabel = 24, TextSide = 4, Chrome = 3;
    /// <summary>How far onto the field (in pixel rows) an enemy flies before its box starts typing, so the first words aren't lost above the top.</summary>
    public const double OpenY = 6;
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
    public static int Frames(Enemy e) => WingComms.RevealFrames(e.CommChars) + Tuning.CommHoldFrames;

    /// <summary>Caches the wrapped text (once) and ages the box while the enemy is on the field.</summary>
    public static void Update(Enemy e, int fieldCols)
    {
        if (!Speaks(e)) return;
        if (e.CommLines is null)
        {
            e.CommLines = BubbleText.Format(BubbleText.Sanitize(e.Text!), WingComms.TextWidth(fieldCols), 4);
            e.CommChars = e.CommLines.Sum(l => l.Length);
        }
        if (e.Y >= OpenY) e.CommAge++;
    }

    public static bool IsOpen(Enemy e) => Speaks(e) && e.CommLines is { Length: > 0 } && e.CommAge > 0 && e.CommAge < Frames(e);

    /// <summary>The cells of the box: centred over the enemy, its bottom edge on the row above the sprite, clamped inside the play
    /// columns and below the HUD (rows 0 and 1) and the phone's one-line feed strip on row 2.</summary>
    public static CellRect Box(Enemy e, FieldLayout lay)
    {
        int textW = Math.Max(Math.Max(e.Header!.Length, e.CommLines!.Max(l => l.Length)), 1);
        int w = Math.Min(textW + TextSide, lay.PCols), h = e.CommLines!.Length + Chrome;
        int col = Math.Clamp(lay.Col0 + (int)Math.Floor(e.X / 2) - w / 2, lay.Col0, Math.Max(lay.Col0, lay.Col0 + lay.PCols - w));
        int top = (int)Math.Floor((e.Y - e.Sprite.H / 2.0) / 2);
        return new CellRect(col, Math.Max(MinRow, top - h), w, h);
    }

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
        return list;
    }
}
