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
                s.P[dy * s.W + dx] = white > 0 ? Surface.Mix(c, Palette.White, white) : c;
            }
        }
    }
}

/// <summary>The art. Everything is hand-drawn pixel strings except the planes, which are generated.</summary>
public static class Art
{
    private static readonly Dictionary<char, uint> DarkPal = new()
    {
        ['#'] = Palette.Plum,
        ['o'] = 0x4b2a78,
        ['+'] = DarkViolet,
        ['r'] = Palette.Crimson,
        ['y'] = Palette.Gold,
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

    // the newer kinds keep the dark husk outline and each wears one colour of its own, so they read apart at a glance
    public const uint MineLit = 0xffb347, DiverLime = 0x9dff5a, SplitterPink = 0xff6be0, TurretTeal = 0x3fe0d0, SplitterMagenta = 0xc23aa8, DarkViolet = 0x7a3fb0;

    private static Dictionary<char, uint> With(params (char C, uint V)[] extra)
    {
        var pal = new Dictionary<char, uint>(DarkPal);
        foreach (var (c, v) in extra) pal[c] = v;
        return pal;
    }

    private static readonly string[] MineRows =
    [
        "a..a..a",
        ".#ooo#.",
        ".oayao.",
        "aoyyyoa",
        ".oayao.",
        ".#ooo#.",
        "a..a..a",
    ];

    /// <summary>A mine, armed: an amber spiked orb. Bullets ping off it.</summary>
    public static readonly Sprite Mine = Sprite.FromArt(MineRows, With(('a', MineLit), ('y', Palette.Gold)));

    /// <summary>A mine before it arms: the same orb, unlit.</summary>
    public static readonly Sprite MineCold = Sprite.FromArt(MineRows, With(('a', 0x6b4f3a), ('y', 0x8a6a4a)));

    /// <summary>A diver: a narrow lime dart, nose down.</summary>
    public static readonly Sprite Diver = Sprite.FromArt(
    [
        "#...#",
        "##.##",
        "#g#g#",
        ".#g#.",
        ".ggg.",
        ".gwg.",
        "..g..",
        "..g..",
        "..#..",
    ], With(('g', DiverLime), ('w', 0xe4ffc8)));

    /// <summary>A splitter: a pink blob with a seam down the middle, where it bursts.</summary>
    public static readonly Sprite Splitter = Sprite.FromArt(
    [
        "..##.##..",
        ".#mm#mm#.",
        "#mMm#mMm#",
        "#mmm.mmm#",
        "#mMm#mMm#",
        ".#mm#mm#.",
        "..##.##..",
    ], With(('m', SplitterMagenta), ('M', SplitterPink)));

    /// <summary>Half a splitter, flying off on its own.</summary>
    public static readonly Sprite Shard = Sprite.FromArt(
    [
        ".#m.",
        "#mM#",
        "#mm#",
        ".##.",
    ], With(('m', SplitterMagenta), ('M', SplitterPink)));

    /// <summary>A turret: a teal hex pod with its barrel down.</summary>
    public static readonly Sprite Turret = Sprite.FromArt(
    [
        "...#####...",
        "..#ttttt#..",
        ".#tTTtTTt#.",
        "#ttT#c#Ttt#",
        "#tttTcTttt#",
        ".#ttttttt#.",
        "..##t#t##..",
        "....#t#....",
        "....#t#....",
    ], With(('t', 0x1f8f86), ('T', TurretTeal), ('c', 0xb8fff6)));

    /// <summary>The colour a kind's debris and telegraphs wear.</summary>
    public static uint Body(EnemyKind kind) => kind switch
    {
        EnemyKind.Mine => MineLit,
        EnemyKind.Diver => DiverLime,
        EnemyKind.Splitter or EnemyKind.Shard => SplitterPink,
        EnemyKind.Turret => TurretTeal,
        _ => DarkViolet,
    };
}

/// <summary>
/// The plane generator: <see cref="Make"/> builds a symmetric little fighter from the DID's hash, and
/// <see cref="Accent"/> gives its accent colour.
/// </summary>
public static class PlaneFactory
{
    public const int W = 11, H = 12;

    /// <summary>The ship's body colour: the accent its comm and chip wear.</summary>
    public static uint Accent(string did) => Hsv(Hashing.Unit(Hashing.Fnv64(did), 1), 0.55, 0.95);

    public static Sprite Make(string did)
    {
        ulong h = Hashing.Fnv64(did);
        double hue = Hashing.Unit(h, 1);
        uint body = Hsv(hue, 0.55, 0.95);
        uint wing = Hsv((hue + 0.08 + Hashing.Unit(h, 2) * 0.1) % 1, 0.7, 0.75);
        uint trim = Hsv((hue + 0.5) % 1, 0.25, 1.0);
        uint glass = Palette.Ice;
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
            int fh = y < 2 ? 0 : 1;
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
        P(c, 0, trim);
        P(c, H - 1, Palette.Gold);
        return sp;
    }

    private static uint Hsv(double h, double s, double v)
    {
        double r, g, b;
        int i = (int)(h * 6); double f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        switch (i % 6) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
        return Surface.Pack((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}
