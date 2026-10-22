namespace FeedRaider.Engine.Raider;

/// <summary>A small opaque-or-clear pixel image. A pixel of 0 is clear; anything else carries its colour in the low 24 bits
/// with bit 24 set, so black can be drawn.</summary>
public sealed class Sprite
{
    public int W { get; }
    public int H { get; }
    public readonly uint[] Px;

    public Sprite(int w, int h) { W = w; H = h; Px = new uint[w * h]; }

    public const uint Opaque = 0x1000000;

    public static Sprite FromArt(string[] rows, Dictionary<char, uint> pal)
    {
        var s = new Sprite(rows.Max(r => r.Length), rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (pal.TryGetValue(rows[y][x], out uint c)) s.Px[y * s.W + x] = c | Opaque;
        return s;
    }

    public Sprite Mirrored()
    {
        var s = new Sprite(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++) s.Px[y * W + x] = Px[y * W + W - 1 - x];
        return s;
    }

    public Sprite FlippedV()
    {
        var s = new Sprite(W, H);
        for (int y = 0; y < H; y++) Array.Copy(Px, y * W, s.Px, (H - 1 - y) * W, W);
        return s;
    }
}

public static class SurfaceSprites
{
    /// <summary>Draws a sprite with its top-left at (x, y); <paramref name="white"/> blends opaque pixels toward white.</summary>
    public static void Draw(this Surface s, Sprite sp, int x, int y, double white = 0)
    {
        for (int sy = 0; sy < sp.H; sy++)
        {
            int dy = y + sy;
            if ((uint)dy >= (uint)s.H) continue;
            for (int sx = 0; sx < sp.W; sx++)
            {
                uint v = sp.Px[sy * sp.W + sx];
                if (v == 0) continue;
                int dx = x + sx;
                if ((uint)dx >= (uint)s.W) continue;
                uint c = v & 0xFFFFFF;
                s.P[dy * s.W + dx] = white > 0 ? Surface.Mix(c, 0xFFFFFF, white) : c;
            }
        }
    }
}

/// <summary>The art. Everything is hand-drawn pixel strings except the planes, which are generated.</summary>
public static class Art
{
    private static readonly Dictionary<char, uint> DarkPal = new()
    {
        ['#'] = 0x2a1740,
        ['o'] = 0x4b2a78,
        ['+'] = 0x7a3fb0,
        ['r'] = 0xff3d6e,
        ['y'] = 0xffd166,
        ['.'] = 0,
    };

    /// <summary>A small grunt: a dark husk with one angry eye.</summary>
    public static readonly Sprite Grunt = Sprite.FromArt(
    [
        "#.....#",
        "##o.o##",
        "#ooroo#",
        ".#ooo#.",
        "..#o#..",
        "...#...",
    ], DarkPal);

    /// <summary>The grunt that carries the text: larger, with a bright core.</summary>
    public static readonly Sprite Carrier = Sprite.FromArt(
    [
        "#...#...#",
        "##.#o#.##",
        "#o+ooo+o#",
        "#ooyryoo#",
        ".#ooyoo#.",
        "..#ooo#..",
        "...#o#...",
        "....#....",
    ], DarkPal);

    /// <summary>The rude heavy gunship.</summary>
    public static readonly Sprite Gunship = Sprite.FromArt(
    [
        "..#.......................#..",
        ".##...#...........#.......##.",
        "##o...##.........##...o...o##",
        "#oo#..#o#.......#o#..#oo..oo#",
        "#o+o###oo#######oo###o+o..o+#",
        "#o++ooooooooooooooooooo++o++#",
        ".#o+ooo+++oooooooo+++ooo+o+#.",
        "..#oo+oo++ooryyroo++oo+oo#...",
        "...#oooo+ooorrrrooo+oooo#....",
        "....##ooo++oorrroo++ooo##....",
        "......#oooo+ooooo+oooo#......",
        ".......##oo#+++#+oo##........",
        ".........##..#o#..##.........",
        "..............#..............",
    ], DarkPal);

    public static readonly Sprite Heart = Sprite.FromArt(
    [
        ".#.#.",
        "#####",
        "#####",
        ".###.",
        "..#..",
    ], new() { ['#'] = 0xff5a8a });
}

/// <summary>
/// The plane generator. <see cref="Make"/> is the only function the game calls: swap its body for something fancier
/// without touching anything else. The placeholder builds a symmetric little fighter from the DID's hash.
/// </summary>
public static class PlaneFactory
{
    public const int W = 11, H = 12;

    /// <summary>The ship's body colour: the accent its comm and chip wear.</summary>
    public static uint Accent(string did) => Hsv(Hashing.Unit(Hashing.Fnv64(did), 1), 0.55, 0.95);

    public static Sprite Make(string did, bool wingman = false)
    {
        ulong h = Hashing.Fnv64(did);
        double hue = Hashing.Unit(h, 1);
        uint body = Hsv(hue, 0.55, 0.95);
        uint wing = Hsv((hue + 0.08 + Hashing.Unit(h, 2) * 0.1) % 1, 0.7, 0.75);
        uint trim = Hsv((hue + 0.5) % 1, 0.25, 1.0);
        uint glass = 0xbfe9ff;
        uint dark = Surface.Mix(wing, 0, 0.5);
        int wingStart = 4 + (int)(Hashing.Unit(h, 3) * 3);          // row where the wings begin
        int span = 3 + (int)(Hashing.Unit(h, 4) * 3);               // half-width of the wings, past the fuselage
        int sweep = (int)(Hashing.Unit(h, 5) * 3);                  // how far the wing tips trail
        bool twinTail = Hashing.Unit(h, 6) < 0.5;
        var sp = new Sprite(W, H);
        int c = W / 2;
        void P(int x, int y, uint col) { if ((uint)x < W && (uint)y < H) sp.Px[y * W + x] = col | Sprite.Opaque; }
        for (int y = 0; y < H; y++)
        {
            int fh = y < 2 ? 0 : y < 6 ? 1 : 1;
            for (int x = -fh; x <= fh; x++) P(c + x, y, x == 0 && y > 1 && y < 5 ? (y == 3 ? glass : trim) : body);
        }
        for (int i = 1; i <= span; i++)
        {
            int top = wingStart + (i * sweep) / Math.Max(1, span);
            int len = Math.Max(2, 5 - i / 2);
            for (int y = top; y < Math.Min(H, top + len); y++)
            {
                P(c - 1 - i, y, y == top ? trim : wing);
                P(c + 1 + i, y, y == top ? trim : wing);
            }
        }
        for (int i = 0; i < 2; i++)
        {
            P(c - 1 - i, H - 1, twinTail ? dark : body); P(c + 1 + i, H - 1, twinTail ? dark : body);
        }
        if (twinTail) { P(c - 2, H - 3, wing); P(c + 2, H - 3, wing); P(c - 2, H - 2, wing); P(c + 2, H - 2, wing); }
        // a lit nose and engine
        P(c, 0, trim);
        P(c, H - 1, 0xffd166);
        return wingman ? sp : sp;
    }

    private static uint Hsv(double h, double s, double v)
    {
        double r, g, b;
        int i = (int)(h * 6); double f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        switch (i % 6) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
        return Surface.Pack((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}
