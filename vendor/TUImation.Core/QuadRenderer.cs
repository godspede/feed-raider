// Vendored from TUImation (vaettir) Core/QuadRenderer.cs @ d2f6ae2. Local addition: Block(), a public per-cell entry
// point for the diff renderer; everything else is unchanged.
using System.Text;

namespace TUImation;

/// <summary>
/// Renders a Canvas to truecolor ANSI with the 2x2 quadrant-block technique:
/// one terminal cell carries a 2x2 pixel block, drawn with one of the 16
/// quadrant glyphs (space, ▘▝▀▖▌▞▛▗▚▐▜▄▙▟█). A cell has only two colours
/// (foreground = "on" pixels, background = "off" pixels), so a block with more
/// than two distinct colours is reduced: the two farthest-apart pixels seed two
/// groups, the rest join the nearer seed, and each group is averaged.
/// Blocks with at most two colours are exact. Width and Height should be even
/// (a stray odd edge reads as transparent). Output is W/2 columns by H/2 rows.
///
/// Frame hygiene matches <see cref="Renderer"/>: rows joined by '\n' (none
/// trailing), each row ends with reset + \x1b[K, and SGR codes are skipped
/// when the colours match the previous cell.
/// </summary>
public static class QuadRenderer
{
    // Index = bitmask, bit0 TL, bit1 TR, bit2 BL, bit3 BR; a set bit is "foreground".
    private static readonly char[] Glyphs =
        { ' ', '▘', '▝', '▀', '▖', '▌', '▞', '▛', '▗', '▚', '▐', '▜', '▄', '▙', '▟', '█' };

    public static string Render(Canvas canvas)
    {
        var sb = new StringBuilder();
        var px = new Rgb?[4];
        for (int y = 0; y < canvas.Height; y += 2)
        {
            if (y > 0) sb.Append('\n');
            Rgb? lastFg = null, lastBg = null;
            for (int x = 0; x < canvas.Width; x += 2)
            {
                px[0] = canvas[x, y];
                px[1] = canvas[x + 1, y];
                px[2] = canvas[x, y + 1];
                px[3] = canvas[x + 1, y + 1];
                var (fg, bg, mask) = Reduce(px, lastFg, lastBg);
                if (mask == 0) fg = lastFg;          // all background: foreground is irrelevant
                else if (mask == 15) bg = lastBg;    // all foreground: background is irrelevant
                if (fg != lastFg) sb.Append(Sgr(38, fg));
                if (bg != lastBg) sb.Append(Sgr(48, bg));
                sb.Append(Glyphs[mask]);
                lastFg = fg;
                lastBg = bg;
            }
            sb.Append("\x1b[0m\x1b[K");
        }
        return sb.ToString();
    }

    /// <summary>The image the terminal will show: each 2x2 block reduced to its (at most) two
    /// colours. For judging, offline, how much the 2-colours-per-cell limit costs.</summary>
    public static Canvas Preview(Canvas canvas)
    {
        var o = new Canvas(canvas.Width, canvas.Height);
        var px = new Rgb?[4];
        for (int y = 0; y + 1 < canvas.Height; y += 2)
            for (int x = 0; x + 1 < canvas.Width; x += 2)
            {
                px[0] = canvas[x, y]; px[1] = canvas[x + 1, y]; px[2] = canvas[x, y + 1]; px[3] = canvas[x + 1, y + 1];
                var (fg, bg, mask) = Reduce(px, null, null);
                for (int i = 0; i < 4; i++) o[x + (i & 1), y + (i >> 1)] = (mask >> i & 1) != 0 ? fg : bg;
            }
        return o;
    }

    /// <summary>One 2x2 block (TL, TR, BL, BR) as a glyph plus its two colours, with the same
    /// reduction <see cref="Render"/> uses. A fully transparent block comes back as a space.</summary>
    public static (char Glyph, Rgb? Fg, Rgb? Bg) Block(Rgb?[] px)
    {
        var (fg, bg, mask) = Reduce(px, null, null);
        return (Glyphs[mask], fg, bg);
    }

    /// <summary>The glyph for a quadrant mask (bit0 TL, bit1 TR, bit2 BL, bit3 BR).</summary>
    public static char GlyphFor(int mask) => Glyphs[mask & 15];

    private static string Sgr(int baseCode, Rgb? c) =>
        c is { } v ? $"\x1b[{baseCode};2;{v.R};{v.G};{v.B}m" : $"\x1b[{baseCode + 1}m";

    // Splits four pixels into foreground/background colours plus the foreground mask.
    internal static (Rgb? Fg, Rgb? Bg, int Mask) Reduce(Rgb?[] px, Rgb? lastFg, Rgb? lastBg)
    {
        Rgb? a = px[0];
        Rgb? b = null;
        bool haveB = false, many = false;
        for (int i = 1; i < 4; i++)
        {
            if (px[i] == a) continue;
            if (!haveB) { b = px[i]; haveB = true; }
            else if (px[i] != b) many = true;
        }
        if (!many)
        {
            // <= 2 distinct colours: exact.
            if (!haveB) return (a, a, 0);
            return Pick(a, b, MaskOf(px, a), lastFg, lastBg);
        }

        // Farthest pair as seeds.
        int si = 0, sj = 1;
        double best = -1;
        for (int i = 0; i < 4; i++)
            for (int j = i + 1; j < 4; j++)
            {
                double d = Dist(px[i], px[j]);
                if (d > best) { best = d; si = i; sj = j; }
            }
        int mask = 0;
        for (int i = 0; i < 4; i++)
            if (Dist(px[i], px[si]) <= Dist(px[i], px[sj])) mask |= 1 << i;
        Rgb? ga = Average(px, mask), gb = Average(px, ~mask & 15);
        return Pick(ga, gb, mask, lastFg, lastBg);
    }

    private static int MaskOf(Rgb?[] px, Rgb? c)
    {
        int m = 0;
        for (int i = 0; i < 4; i++) if (px[i] == c) m |= 1 << i;
        return m;
    }

    // Choose which of the two groups is the foreground so fewer SGR codes change.
    private static (Rgb? Fg, Rgb? Bg, int Mask) Pick(Rgb? ga, Rgb? gb, int maskA, Rgb? lastFg, Rgb? lastBg)
    {
        int costAFg = (ga != lastFg ? 1 : 0) + (gb != lastBg ? 1 : 0);
        int costBFg = (gb != lastFg ? 1 : 0) + (ga != lastBg ? 1 : 0);
        return costBFg < costAFg ? (gb, ga, ~maskA & 15) : (ga, gb, maskA);
    }

    private static double Dist(Rgb? p, Rgb? q)
    {
        if (p is null || q is null) return p is null && q is null ? 0 : 1e9;
        double dr = p.Value.R - q.Value.R, dg = p.Value.G - q.Value.G, db = p.Value.B - q.Value.B;
        return dr * dr + dg * dg + db * db;
    }

    private static Rgb? Average(Rgb?[] px, int mask)
    {
        int n = 0, r = 0, g = 0, b = 0;
        for (int i = 0; i < 4; i++)
            if ((mask >> i & 1) != 0 && px[i] is { } c) { n++; r += c.R; g += c.G; b += c.B; }
        return n == 0 ? null : new Rgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
