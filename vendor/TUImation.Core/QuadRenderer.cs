// Vendored from TUImation, Core/QuadRenderer.cs @ d2f6ae2. This copy differs from that file: it has Block(), a public
// per-cell entry point for the diff renderer, and it has no whole-canvas Render, no Sgr helper for it and no choice in
// Reduce of which group is the foreground, since nothing here draws a whole canvas.

namespace TUImation;

/// <summary>
/// Reduces 2x2 pixel blocks for the quadrant-block technique:
/// one terminal cell carries a 2x2 pixel block, drawn with one of the 16
/// quadrant glyphs (space, ▘▝▀▖▌▞▛▗▚▐▜▄▙▟█). A cell has only two colours
/// (foreground = "on" pixels, background = "off" pixels), so a block with more
/// than two distinct colours is reduced: the two farthest-apart pixels seed two
/// groups, the rest join the nearer seed, and each group is averaged.
/// Blocks with at most two colours are exact.
/// </summary>
public static class QuadRenderer
{
    // Index = bitmask, bit0 TL, bit1 TR, bit2 BL, bit3 BR; a set bit is "foreground".
    private static readonly char[] Glyphs =
        { ' ', '▘', '▝', '▀', '▖', '▌', '▞', '▛', '▗', '▚', '▐', '▜', '▄', '▙', '▟', '█' };

    private const int BlockPixels = 4;                  // pixels in one 2x2 block
    private const int FullMask = (1 << BlockPixels) - 1;  // every pixel of a block set (15)
    private const double Unmatched = 1e9;               // distance between a transparent pixel and a coloured one

    /// <summary>One 2x2 block (TL, TR, BL, BR) as a glyph plus its two colours, reduced as
    /// described above. A fully transparent block comes back as a space.</summary>
    public static (char Glyph, Rgb? Fg, Rgb? Bg) Block(Rgb?[] px)
    {
        var (fg, bg, mask) = Reduce(px);
        return (Glyphs[mask], fg, bg);
    }

    /// <summary>The glyph for a quadrant mask (bit0 TL, bit1 TR, bit2 BL, bit3 BR).</summary>
    public static char GlyphFor(int mask) => Glyphs[mask & FullMask];

    internal static (Rgb? Fg, Rgb? Bg, int Mask) Reduce(Rgb?[] px)
    {
        Rgb? a = px[0];
        Rgb? b = null;
        bool haveB = false, many = false;
        for (int i = 1; i < BlockPixels; i++)
        {
            if (px[i] == a) continue;
            if (!haveB) { b = px[i]; haveB = true; }
            else if (px[i] != b) many = true;
        }
        if (!many)
        {
            if (!haveB) return (a, a, 0);
            return (a, b, MaskOf(px, a));
        }

        int si = 0, sj = 1;
        double best = -1;
        for (int i = 0; i < BlockPixels; i++)
            for (int j = i + 1; j < BlockPixels; j++)
            {
                double d = Dist(px[i], px[j]);
                if (d > best) { best = d; si = i; sj = j; }
            }
        int mask = 0;
        for (int i = 0; i < BlockPixels; i++)
            if (Dist(px[i], px[si]) <= Dist(px[i], px[sj])) mask |= 1 << i;
        Rgb? ga = Average(px, mask), gb = Average(px, ~mask & FullMask);
        return (ga, gb, mask);
    }

    private static int MaskOf(Rgb?[] px, Rgb? c)
    {
        int m = 0;
        for (int i = 0; i < BlockPixels; i++) if (px[i] == c) m |= 1 << i;
        return m;
    }

    private static double Dist(Rgb? p, Rgb? q)
    {
        if (p is null || q is null) return p is null && q is null ? 0 : Unmatched;
        double dr = p.Value.R - q.Value.R, dg = p.Value.G - q.Value.G, db = p.Value.B - q.Value.B;
        return dr * dr + dg * dg + db * db;
    }

    private static Rgb? Average(Rgb?[] px, int mask)
    {
        int n = 0, r = 0, g = 0, b = 0;
        for (int i = 0; i < BlockPixels; i++)
            if ((mask >> i & 1) != 0 && px[i] is { } c) { n++; r += c.R; g += c.G; b += c.B; }
        return n == 0 ? null : new Rgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
