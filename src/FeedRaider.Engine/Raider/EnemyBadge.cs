namespace FeedRaider.Engine.Raider;

/// <summary>The little label tag over a darkness enemy: " SPAM ", " RUDE ", or the labeler's own value, upper-cased and cut short.
/// Built once per wave (never per frame) and carried on the enemy.</summary>
public static class EnemyBadge
{
    public const int MaxLabel = 8;
    public const uint SpamBg = 0x9fbf2a, RudeBg = 0xff3d8e, Ink = 0x140a1c;

    /// <summary>The badge string for a label value (null or blank falls back to the kind's name).</summary>
    public static string Text(string? label, DarknessKind kind)
    {
        string s = string.IsNullOrWhiteSpace(label) ? kind.ToString() : label.Trim();
        s = s.ToUpperInvariant();
        if (s.Length > MaxLabel) s = s[..(MaxLabel - 1)] + "…";
        return " " + s + " ";
    }

    public static uint Background(DarknessKind kind) => kind == DarknessKind.Rude ? RudeBg : SpamBg;

    /// <summary>Left column and row for a badge: centred over the sprite's column, one row above its top (or above its text,
    /// when a carrier has some), clamped inside the play columns and below the HUD.</summary>
    public static (int Col, int Row) Place(Enemy e, int c0, int cols, int len)
    {
        int centre = c0 + (int)(e.X / 2);
        int col = Math.Clamp(centre - len / 2, c0, Math.Max(c0, c0 + cols - len));
        int row = (int)((e.Y - e.Sprite.H / 2.0) / 2) - 1;
        if (e.Kind == EnemyKind.Carrier && e.Text is { Length: > 0 }) row--;
        return (col, Math.Max(2, row));
    }
}
