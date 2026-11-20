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
    /// <summary>The low 24 bits of a pixel, where its colour sits under <see cref="Opaque"/>.</summary>
    public const uint ColourMask = Opaque - 1;

    public static Sprite FromArt(string[] rows, Dictionary<char, uint> pal)
    {
        var s = new Sprite(rows.Max(r => r.Length), rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (pal.TryGetValue(rows[y][x], out uint c)) s.Px[y * s.W + x] = c | Opaque;
        return s;
    }

    /// <summary>A copy of the rectangle at (x, y), w by h pixels; whatever lies outside this sprite is clear.</summary>
    public Sprite Cut(int x, int y, int w, int h)
    {
        var s = new Sprite(w, h);
        for (int sy = 0; sy < h; sy++)
            for (int sx = 0; sx < w; sx++)
                if ((uint)(x + sx) < (uint)W && (uint)(y + sy) < (uint)H) s.Px[sy * w + sx] = Px[(y + sy) * W + x + sx];
        return s;
    }

    /// <summary>A copy with the rectangle at (x, y), w by h pixels, cleared.</summary>
    public Sprite Without(int x, int y, int w, int h)
    {
        var s = Cut(0, 0, W, H);
        for (int sy = y; sy < y + h; sy++)
            for (int sx = x; sx < x + w; sx++)
                if ((uint)sx < (uint)W && (uint)sy < (uint)H) s.Px[sy * W + sx] = 0;
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
            if (dy < s.ClipTop || dy >= s.ClipBottom) continue;
            for (int sx = 0; sx < sp.W; sx++)
            {
                uint v = sp.Px[sy * sp.W + sx];
                if (v == 0) continue;
                int dx = x + sx;
                if (dx < s.ClipLeft || dx >= s.ClipRight) continue;
                uint c = v & Sprite.ColourMask;
                s.P[dy * s.W + dx] = white > 0 ? Surface.Mix(c, Palette.White, white) : c;
            }
        }
    }
}

/// <summary>The art. Everything is hand-drawn pixel strings except the ships, which are generated.</summary>
public static class Art
{
    internal static readonly Dictionary<char, uint> DarkPal = new()
    {
        ['#'] = Palette.Plum,
        ['o'] = HuskFill,
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

    // each of these kinds keeps the dark husk outline and wears one colour of its own, so they read apart at a glance
    public const uint MineLit = 0xffb347, DiverLime = 0x9dff5a, SplitterPink = 0xff6be0, TurretGreen = 0x4fd65a, SplitterMagenta = 0xc23aa8, DarkViolet = 0x7a3fb0;
    /// <summary>The violet that fills a dark husk inside its plum outline.</summary>
    private const uint HuskFill = 0x4b2a78;
    private const uint MineColdSpike = 0x6b4f3a, MineColdCore = 0x8a6a4a, DiverGlint = 0xe4ffc8, TurretShade = 0x1f8f36, TurretCore = 0xc4ffcc;

    internal static Dictionary<char, uint> With(params (char C, uint V)[] extra)
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
    public static readonly Sprite MineCold = Sprite.FromArt(MineRows, With(('a', MineColdSpike), ('y', MineColdCore)));

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
    ], With(('g', DiverLime), ('w', DiverGlint)));

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

    /// <summary>A turret: a green hex pod with its barrel down.</summary>
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
    ], With(('t', TurretShade), ('T', TurretGreen), ('c', TurretCore)));

    /// <summary>The dark husk colour a destroyed part wears under its outline.</summary>
    public const uint Husk = 0x3a2c4f;

    /// <summary>The sprite as a wreck: the outline colour stays, every other colour turns to the husk colour. A boss is drawn once whole and its parts are cut from that drawing.</summary>
    public static Sprite Wrecked(Sprite sprite)
    {
        var s = new Sprite(sprite.W, sprite.H);
        for (int i = 0; i < sprite.Px.Length; i++)
        {
            uint v = sprite.Px[i];
            if (v != 0) s.Px[i] = (v & Sprite.ColourMask) == Palette.Plum ? v : Husk | Sprite.Opaque;
        }
        return s;
    }

    /// <summary>The ring a warden draws round an enemy it is shielding: a pale ring with the corners cut.</summary>
    public static readonly Sprite Shield = Sprite.FromArt(
    [
        "..sssss..",
        ".s.....s.",
        "s.......s",
        "s.......s",
        "s.......s",
        "s.......s",
        "s.......s",
        ".s.....s.",
        "..sssss..",
    ], new() { ['s'] = Palette.ShieldGlow });
}

/// <summary>
/// The ship generator: <see cref="Make"/> builds a symmetric little ship from the DID's hash.
/// </summary>
public static class ShipFactory
{
    public const int W = 11, H = 12;
    private const double BodySaturation = 0.55, BodyValue = 0.95;
    private const double WingHueShift = 0.08, WingHueSpread = 0.1, WingSaturation = 0.7, WingValue = 0.75;
    private const double TrimHueOffset = 0.5, TrimSaturation = 0.25, TrimValue = 1.0;
    private const double DarkTailMix = 0.5;
    private const double TwinTailChance = 0.5;
    private const int WingStartMin = 4, WingStartRange = 3;
    private const int WingSpanMin = 3, WingSpanRange = 3;
    private const int WingSweepRange = 3;
    private const int NoseRows = 2;
    private const int CockpitFirstRow = 2, CockpitEndRow = 5, CockpitGlassRow = 3;
    private const int WingLengthBase = 5, WingLengthMin = 2;
    private const int TailWidth = 2;
    /// <summary>A twin tail's fins stand over the tail's outer ends, on the rows just above it.</summary>
    private const int TwinFinX = TailWidth, TwinFinRows = 2;
    /// <summary>The lanes of the DID's hash each choice is drawn from, so every choice is independent of the others.</summary>
    private const int HueLane = 1, WingHueLane = 2, WingStartLane = 3, SpanLane = 4, SweepLane = 5, TwinTailLane = 6;
    private const int HueSectors = 6;
    private const int ChannelMax = 255;

    public static Sprite Make(string did)
    {
        ulong h = Hashing.MixedFnv64(did);
        double hue = Hashing.Unit(h, HueLane);
        uint body = Hsv(hue, BodySaturation, BodyValue);
        uint wing = Hsv((hue + WingHueShift + Hashing.Unit(h, WingHueLane) * WingHueSpread) % 1, WingSaturation, WingValue);
        uint trim = Hsv((hue + TrimHueOffset) % 1, TrimSaturation, TrimValue);
        uint glass = Palette.Ice;
        uint dark = Surface.Mix(wing, 0, DarkTailMix);
        int wingStart = WingStartMin + (int)(Hashing.Unit(h, WingStartLane) * WingStartRange); // row where the wings begin
        int span = WingSpanMin + (int)(Hashing.Unit(h, SpanLane) * WingSpanRange); // half-width of the wings, past the fuselage
        int sweep = (int)(Hashing.Unit(h, SweepLane) * WingSweepRange); // how far the wing tips trail
        bool twinTail = Hashing.Unit(h, TwinTailLane) < TwinTailChance;
        var sp = new Sprite(W, H);
        int c = W / 2;
        void P(int x, int y, uint col) { if ((uint)x < W && (uint)y < H) sp.Px[y * W + x] = col | Sprite.Opaque; }
        for (int y = 0; y < H; y++)
        {
            int fh = y < NoseRows ? 0 : 1;
            for (int x = -fh; x <= fh; x++) P(c + x, y, x == 0 && y >= CockpitFirstRow && y < CockpitEndRow ? (y == CockpitGlassRow ? glass : trim) : body);
        }
        for (int i = 1; i <= span; i++)
        {
            int top = wingStart + (i * sweep) / span;
            int len = Math.Max(WingLengthMin, WingLengthBase - i / 2);
            for (int y = top; y < Math.Min(H, top + len); y++)
            {
                P(c - 1 - i, y, y == top ? trim : wing);
                P(c + 1 + i, y, y == top ? trim : wing);
            }
        }
        for (int i = 0; i < TailWidth; i++)
        {
            P(c - 1 - i, H - 1, twinTail ? dark : body); P(c + 1 + i, H - 1, twinTail ? dark : body);
        }
        if (twinTail)
            for (int y = H - 1 - TwinFinRows; y < H - 1; y++) { P(c - TwinFinX, y, wing); P(c + TwinFinX, y, wing); }
        P(c, 0, trim);
        P(c, H - 1, Palette.Gold);
        return sp;
    }

    private static uint Hsv(double h, double s, double v)
    {
        double r, g, b;
        int i = (int)(h * HueSectors); double f = h * HueSectors - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        switch (i % HueSectors) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
        return Surface.Pack((int)(r * ChannelMax), (int)(g * ChannelMax), (int)(b * ChannelMax));
    }
}
