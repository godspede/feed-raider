using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>A flat opaque pixel buffer (0xRRGGBB). The renderer's working canvas: cheap to copy and compare, which
/// matters under the browser's interpreter. Small transparent sprites stay <see cref="Canvas"/>es and are blitted in.</summary>
public sealed class Surface
{
    public int W { get; }
    public int H { get; }
    public readonly uint[] P;

    public Surface(int w, int h) { W = w; H = h; P = new uint[w * h]; }

    public static uint Pack(Rgb c) => (uint)(c.R << 16 | c.G << 8 | c.B);
    public static uint Pack(int r, int g, int b) => (uint)(Math.Clamp(r, 0, 255) << 16 | Math.Clamp(g, 0, 255) << 8 | Math.Clamp(b, 0, 255));
    public static Rgb Unpack(uint c) => new((byte)(c >> 16), (byte)(c >> 8), (byte)c);

    public static uint Mix(uint a, uint b, double t)
    {
        int ar = (int)(a >> 16 & 255), ag = (int)(a >> 8 & 255), ab = (int)(a & 255);
        int br = (int)(b >> 16 & 255), bg = (int)(b >> 8 & 255), bb = (int)(b & 255);
        return Pack((int)(ar + (br - ar) * t), (int)(ag + (bg - ag) * t), (int)(ab + (bb - ab) * t));
    }

    /// <summary>The WCAG contrast ratio for body text.</summary>
    public const double BodyTextContrast = 4.5;

    /// <summary><paramref name="fg"/>, moved toward white (or black, on a light <paramref name="bg"/>) only as far as it takes
    /// to read at <see cref="BodyTextContrast"/>:1 against <paramref name="bg"/>.</summary>
    public static uint Legible(uint fg, uint bg)
    {
        // asked every frame for a handful of pairs, so remembered: the interpreter in the browser is slow at Math.Pow
        ulong key = (ulong)fg << 32 | bg;
        if (LegibleSeen.TryGetValue(key, out uint seen)) return seen;
        uint pole = Luminance(bg) > 0.18 ? 0u : 0xffffffu;
        uint c = fg;
        for (int i = 1; i <= 20 && Contrast(c, bg) < BodyTextContrast; i++) c = Mix(fg, pole, i / 20.0);
        if (LegibleSeen.Count > 4096) LegibleSeen.Clear();
        LegibleSeen[key] = c;
        return c;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, uint> LegibleSeen = new();

    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint c)
    {
        static double Lin(uint v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c >> 16 & 255) + 0.7152 * Lin(c >> 8 & 255) + 0.0722 * Lin(c & 255);
    }

    public void Fill(uint c) => Array.Fill(P, c);

    public void Set(int x, int y, uint c)
    {
        if ((uint)x < (uint)W && (uint)y < (uint)H) P[y * W + x] = c;
    }

    public uint Get(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H ? P[y * W + x] : 0;

    public void Blend(int x, int y, uint c, double a)
    {
        if ((uint)x < (uint)W && (uint)y < (uint)H) P[y * W + x] = Mix(P[y * W + x], c, a);
    }

    public void Rect(int x, int y, int w, int h, uint c)
    {
        int x0 = Math.Max(0, x), x1 = Math.Min(W, x + w);
        if (x1 <= x0) return;
        for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++)
            Array.Fill(P, c, yy * W + x0, x1 - x0);
    }

    /// <summary>Copies a rectangle from another surface, clipped to both.</summary>
    public void CopyFrom(Surface src, int sx, int sy, int w, int h, int dx, int dy)
    {
        if (sx < 0) { w += sx; dx -= sx; sx = 0; }
        if (sy < 0) { h += sy; dy -= sy; sy = 0; }
        if (dx < 0) { w += dx; sx -= dx; dx = 0; }
        if (dy < 0) { h += dy; sy -= dy; dy = 0; }
        w = Math.Min(w, Math.Min(src.W - sx, W - dx));
        h = Math.Min(h, Math.Min(src.H - sy, H - dy));
        if (w <= 0 || h <= 0) return;
        for (int y = 0; y < h; y++)
            Array.Copy(src.P, (sy + y) * src.W + sx, P, (dy + y) * W + dx, w);
    }

    /// <summary>Draws a sprite with its top-left at (x, y). <paramref name="whiteness"/> blends every opaque pixel toward white.</summary>
    public void Blit(Canvas s, int x, int y, double whiteness = 0)
    {
        for (int sy = 0; sy < s.Height; sy++)
            for (int sx = 0; sx < s.Width; sx++)
                if (s[sx, sy] is { } c)
                {
                    uint v = Pack(c);
                    Set(x + sx, y + sy, whiteness > 0 ? Mix(v, 0xFFFFFF, whiteness) : v);
                }
    }

    public void Disc(int cx, int cy, int r, uint c)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r + r / 2) Set(cx + x, cy + y, c);
    }

    /// <summary>One-pixel circle outline; <paramref name="a"/> blends it over what is there.</summary>
    public void Ring(double cx, double cy, double r, uint c, double a)
    {
        int steps = Math.Max(8, (int)(r * 6.3));
        for (int i = 0; i < steps; i++)
        {
            double t = i * Math.PI * 2 / steps;
            Blend((int)Math.Round(cx + Math.Cos(t) * r), (int)Math.Round(cy + Math.Sin(t) * r), c, a);
        }
    }
}
