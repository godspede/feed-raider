using TUImation;

namespace FeedRaider.Engine;

/// <summary>A darkness enemy. It knows its kind, a seed and a text fragment, and nothing about the author.</summary>
public sealed class DarknessEnemy
{
    public required DarknessKind Kind { get; init; }
    /// <summary>Per-enemy seed; motes of one swarm share <see cref="GroupSeed"/> (and so an edge to come from).</summary>
    public required ulong Seed { get; init; }
    public required ulong GroupSeed { get; init; }
    public required long SpawnMs { get; init; }
    public required string Text { get; init; }
    public required string TargetDid { get; set; }
    public DarknessSource Source { get; init; } = DarknessSource.Label;

    public double X { get; internal set; }
    public double Y { get; internal set; }
    internal long NearSinceMs = -1;
    public long DissolveStartMs { get; internal set; } = -1;
    public bool Dissolving => DissolveStartMs >= 0;

    public int Width => Kind == DarknessKind.Rude ? DarknessEnemySprites.BruteW : DarknessEnemySprites.MoteW;
    public int Height => Kind == DarknessKind.Rude ? DarknessEnemySprites.BruteH : DarknessEnemySprites.MoteH;
}

/// <summary>DarknessEnemy movement as a pure function of (seed, time, target position): no state, no randomness.</summary>
public static class DarknessEnemyMotion
{
    private const long BruteTravelBaseMs = 5500, BruteTravelSpreadMs = 1500;
    private const long MoteTravelBaseMs = 2200, MoteTravelSpreadMs = 500;
    private const int BruteSideOffset = 11, MoteSideOffset = 7;   // pixels left or right of the figure it stops at
    private const int ShakeTickMs = 110;                          // how long an arrived enemy holds one shake offset
    private const double MoteBobMs = 110.0;                       // the mote's vertical wobble, in ms per radian
    private const double SpamStartJitterPx = 10;
    private const double BruteLandingDropPx = 3;                  // the brute stops this far below the top of the figure
    private const double MoteLandingSpreadPx = 8, MoteLandingRisePx = 2;   // a mote stops between 2 px above and 6 px below that
    private const int ShakeVariants = 16;                         // distinct shake offsets before the pattern repeats
    private const int ShakeXSalt = 100, ShakeYSalt = 120;         // hash channels of the sideways and vertical shake
    private const double ShakeSpreadX = 2, ShakeSpreadY = 3;      // peak-to-peak shake in pixels
    private const double BruteChargeExponent = 1.6;               // above 1 so the brute starts slow and speeds up
    private const double MoteSwayMs = 140.0;                      // the mote's sideways wobble, in ms per radian
    private const double MoteSwayPx = 2, MoteBobPx = 3;           // wobble sizes while it closes in
    private const int FieldEdges = 4;                             // left, right, top, bottom: the edges an enemy can come in from
    private const int AlongEdgeSalt = 1;                          // hash channel of where along its edge a group comes in
    private const int StartJitterXSalt = 2, StartJitterYSalt = 3; // hash channels of a mote's scatter about its group's entry point
    private const int TravelSalt = 4;                             // hash channel of the travel time
    private const int SideSalt = 5;                               // hash channel of which side of the figure it stops at
    private const int MoteLandingSalt = 6;                        // hash channel of how high on the figure a mote stops
    private const int MoteWobblePhaseSalt = 7;                    // hash channel of where in its wobble a mote starts
    public static (double X, double Y) Start(DarknessEnemy m, int cols, int pixelHeight)
    {
        int edge = (int)(Hashing.Mix(m.GroupSeed) % FieldEdges);
        double u = Hashing.Unit(m.GroupSeed, AlongEdgeSalt);
        var (x, y) = edge switch
        {
            0 => (-(double)m.Width, u * pixelHeight),
            1 => ((double)cols, u * pixelHeight),
            2 => (u * cols, -(double)m.Height),
            _ => (u * cols, (double)pixelHeight),
        };
        if (m.Kind == DarknessKind.Spam)
        {
            x += (Hashing.Unit(m.Seed, StartJitterXSalt) - 0.5) * SpamStartJitterPx;
            y += (Hashing.Unit(m.Seed, StartJitterYSalt) - 0.5) * SpamStartJitterPx;
        }
        return (x, y);
    }

    public static long TravelMs(DarknessEnemy m) => m.Kind == DarknessKind.Rude
        ? BruteTravelBaseMs + (long)(Hashing.Unit(m.Seed, TravelSalt) * BruteTravelSpreadMs)
        : MoteTravelBaseMs + (long)(Hashing.Unit(m.Seed, TravelSalt) * MoteTravelSpreadMs);

    /// <summary>Top-left of the enemy. <paramref name="target"/> is the top-left of the figure it is after.</summary>
    public static (double X, double Y) Position(DarknessEnemy m, long ms, (double X, double Y) target, int cols, int pixelHeight)
    {
        var (sx, sy) = Start(m, cols, pixelHeight);
        double side = Hashing.Unit(m.Seed, SideSalt) < 0.5 ? -1 : 1;
        double dx = target.X + side * (m.Kind == DarknessKind.Rude ? BruteSideOffset : MoteSideOffset);
        double dy = target.Y + (m.Kind == DarknessKind.Rude ? BruteLandingDropPx : Hashing.Unit(m.Seed, MoteLandingSalt) * MoteLandingSpreadPx - MoteLandingRisePx);
        long t = Math.Max(0, ms - m.SpawnMs);
        long travel = TravelMs(m);
        double p = Math.Min(1.0, (double)t / travel);
        if (p >= 1.0)
        {
            long tick = ms / ShakeTickMs;
            double jx = (Hashing.Unit(m.Seed, ShakeXSalt + (int)(tick % ShakeVariants)) - 0.5) * ShakeSpreadX;
            double jy = m.Kind == DarknessKind.Rude ? 0 : (Hashing.Unit(m.Seed, ShakeYSalt + (int)(tick % ShakeVariants)) - 0.5) * ShakeSpreadY;
            return (dx + Math.Round(jx), dy + Math.Round(jy));
        }
        double e = m.Kind == DarknessKind.Rude ? Math.Pow(p, BruteChargeExponent) : p;
        double x = sx + (dx - sx) * e, y = sy + (dy - sy) * e;
        if (m.Kind == DarknessKind.Spam)
        {
            double ph = Hashing.Unit(m.Seed, MoteWobblePhaseSalt) * Math.Tau;
            x += Math.Sin(ms / MoteSwayMs + ph) * MoteSwayPx * (1 - p);
            y += Math.Cos(ms / MoteBobMs + ph) * MoteBobPx * (1 - p);
        }
        return (x, y);
    }
}

/// <summary>Arrival, throttling, charging and dispelling. Time is passed in; nothing here reads a clock.</summary>
public sealed class Darkness
{
    public const int MaxEnemies = 6;
    public const int SpawnGapMs = 2000;
    public const int MaxPending = 8;
    public const int DissolveMs = 800;
    public const int DispelFigures = 3;
    public const int DispelAfterMs = 900;

    private const int MaxMotesPerPost = 3;
    private const int FlinchForceBrute = 3, FlinchForceMote = 1;
    private const int FlinchShakeMs = 90;
    private const long BruteLifeMs = 16000, MoteLifeMs = 9000;
    /// <summary>A figure counts as "near" an enemy (for dispelling it) within this many pixels either side on x, and on y.</summary>
    private const double DispelReachX = 20, DispelReachY = 14;
    /// <summary>An enemy pushes a figure within this many pixels either side on x, and on y.</summary>
    private const double FlinchReachX = 16, FlinchReachY = 12;

    private readonly List<DarknessEnemy> _enemies = [];
    private readonly Queue<DarknessPost> _pending = new();
    /// <summary>Far enough back that the first arrival is due at once; half of the minimum so that ms minus it cannot overflow.</summary>
    private const long NeverSpawnedMs = long.MinValue / 2;

    private long _lastSpawnMs = NeverSpawnedMs;
    private int _serial;

    public IReadOnlyList<DarknessEnemy> Enemies => _enemies;
    public int Pending => _pending.Count;
    public int Spawned { get; private set; }
    public int SpawnedFromLabels { get; private set; }
    public int SpawnedFromHidden { get; private set; }
    public int Dispelled { get; private set; }

    public void Offer(DarknessPost post)
    {
        _pending.Enqueue(post);
        while (_pending.Count > MaxPending) _pending.Dequeue();
    }

    private static long LifeMs(DarknessEnemy m) => m.Kind == DarknessKind.Rude ? BruteLifeMs : MoteLifeMs;

    /// <summary>Advance the enemies. <paramref name="figures"/> are the figures' top-left positions this frame.</summary>
    public void Update(long ms, IReadOnlyList<(string Did, (double X, double Y) Pos)> figures, int cols, int pixelHeight)
    {
        _enemies.RemoveAll(m => m.Dissolving && ms - m.DissolveStartMs >= DissolveMs);

        foreach (var m in _enemies)
        {
            if (m.Dissolving) continue;
            if (figures.Count == 0) { m.DissolveStartMs = ms; continue; }
            var target = figures.FirstOrDefault(g => g.Did == m.TargetDid);
            if (target.Did is null)
            {
                target = Nearest(figures, m.X, m.Y);
                m.TargetDid = target.Did;
            }
            (m.X, m.Y) = DarknessEnemyMotion.Position(m, ms, target.Pos, cols, pixelHeight);
            if (ms - m.SpawnMs > LifeMs(m)) { m.DissolveStartMs = ms; continue; }

            double mx = m.X + m.Width / 2.0, my = m.Y + m.Height / 2.0;
            int near = figures.Count(g => Math.Abs(g.Pos.X + FigureBuilder.Width / 2.0 - mx) <= DispelReachX && Math.Abs(g.Pos.Y + FigureBuilder.Height / 2.0 - my) <= DispelReachY);
            if (near >= DispelFigures)
            {
                if (m.NearSinceMs < 0) m.NearSinceMs = ms;
                if (ms - m.NearSinceMs >= DispelAfterMs) { m.DissolveStartMs = ms; Dispelled++; }
            }
            else m.NearSinceMs = -1;
        }

        if (_pending.Count > 0 && figures.Count > 0 && ms - _lastSpawnMs >= SpawnGapMs && _enemies.Count < MaxEnemies)
            Spawn(_pending.Dequeue(), ms, figures, cols, pixelHeight);
    }

    private void Spawn(DarknessPost post, long ms, IReadOnlyList<(string Did, (double X, double Y) Pos)> figures, int cols, int pixelHeight)
    {
        _lastSpawnMs = ms;
        var texts = new List<string>();
        if (post.Kind == DarknessKind.Rude) texts.Add(post.Text);
        else texts.AddRange(DarknessText.Fragments(post.Text).Take(Math.Min(MaxMotesPerPost, MaxEnemies - _enemies.Count)));
        if (texts.Count == 0) return;

        ulong group = Hashing.Mix(post.Seed ^ (ulong)(++_serial));
        var probe = new DarknessEnemy { Kind = post.Kind, Seed = group, GroupSeed = group, SpawnMs = ms, Text = "", TargetDid = "" };
        var (sx, sy) = DarknessEnemyMotion.Start(probe, cols, pixelHeight);
        var target = Nearest(figures, sx, sy);
        for (int i = 0; i < texts.Count; i++)
        {
            var m = new DarknessEnemy { Kind = post.Kind, Seed = Hashing.Mix(group + (ulong)i), GroupSeed = group, SpawnMs = ms, Text = texts[i], TargetDid = target.Did, Source = post.Source };
            (m.X, m.Y) = DarknessEnemyMotion.Start(m, cols, pixelHeight);
            _enemies.Add(m);
            Spawned++;
            if (post.Source == DarknessSource.Label) SpawnedFromLabels++;
            else if (post.Source == DarknessSource.Hidden) SpawnedFromHidden++;
        }
    }

    private static (string Did, (double X, double Y) Pos) Nearest(IReadOnlyList<(string Did, (double X, double Y) Pos)> figures, double x, double y) =>
        figures.OrderBy(g => (g.Pos.X - x) * (g.Pos.X - x) + (g.Pos.Y - y) * (g.Pos.Y - y)).ThenBy(g => g.Did, StringComparer.Ordinal).First();

    /// <summary>How far a figure is pushed (away, with a shake) by enemies on top of him. Zero when none is near.</summary>
    public (double X, double Y) Flinch(double figureX, double figureY, long ms)
    {
        double ox = 0, oy = 0;
        foreach (var m in _enemies)
        {
            if (m.Dissolving) continue;
            double cx = m.X + m.Width / 2.0 - (figureX + FigureBuilder.Width / 2.0);
            double cy = m.Y + m.Height / 2.0 - (figureY + FigureBuilder.Height / 2.0);
            if (Math.Abs(cx) > FlinchReachX || Math.Abs(cy) > FlinchReachY) continue;
            double force = m.Kind == DarknessKind.Rude ? FlinchForceBrute : FlinchForceMote;
            ox -= Math.Sign(cx) * force;
            oy += ((ms / FlinchShakeMs) & 1) == 0 ? 1 : 0;
        }
        return (ox, oy);
    }
}

public static class DarknessEnemySprites
{
    public const int BruteW = 14, BruteH = 12, MoteW = 9, MoteH = 6;
    private const int FlickerMs = 90;   // how long a mote stays on or off in its flicker
    private const ulong FlickerOffMask = 7;   // a mote is off for one flicker step in eight
    private const int StompMs = 200;    // how long the brute holds one foot down
    private const int DissolveSalt = 1000, DissolveRowStride = 64;   // hash channel per sprite pixel; the stride exceeds any sprite width

    private static readonly Rgb Void = new(0x14, 0x0c, 0x1e);
    private static readonly Rgb Plum = new(0x2e, 0x1a, 0x45);
    private static readonly Rgb Violet = new(0x4a, 0x2a, 0x70);
    private static readonly Rgb Eye = new(0xdd, 0xe6, 0xa8);
    private static readonly Rgb Fang = new(0xef, 0xe7, 0xcf);
    private static readonly Rgb SickGreen = new(0x7c, 0x9a, 0x1c);
    private static readonly Rgb DeepGreen = new(0x3d, 0x4f, 0x12);

    /// <summary>The character a drawing leaves clear.</summary>
    private const char Clear = '.';

    private static readonly Dictionary<char, Rgb> Ink = new()
    {
        ['v'] = Void,
        ['p'] = Plum,
        ['i'] = Violet,
        ['e'] = Eye,
        ['f'] = Fang,
        ['g'] = SickGreen,
        ['d'] = DeepGreen,
    };

    /// <summary>The brute down to its knees: horns, brow, eyes, fists at its sides, snarl, fangs and the tops of both legs.</summary>
    private static readonly string[] BruteBody =
    [
        "..i........i..",
        "..ip......pi..",
        "..vvvvvvvvvv..",
        "..vppppppppv..",
        ".vvppppppppvv.",
        "pvvveevveevvvp",
        "pvvvvvvvvvvvvp",
        "pvvvvvvvvvvvvp",
        "p.vviiiiiivv.p",
        "..vvfvfvfvvv..",
        "...ppp..ppp...",
    ];

    /// <summary>The brute's bottom row, one foot down at a time.</summary>
    private const string BruteLeftFootDown = "...ppp........", BruteRightFootDown = "........ppp...";

    private static readonly string[] Mote =
    [
        "...ddd...",
        ".dgggggd.",
        "dggegeggd",
        "dgggggggd",
        ".dgggggd.",
        "...ddd...",
    ];

    /// <summary>The sprite at this moment, or null for a flickering mote that is "off" this frame.</summary>
    public static Canvas? Build(DarknessEnemy m, long ms)
    {
        Canvas c;
        if (m.Kind == DarknessKind.Rude)
        {
            bool leftFootDown = ((ms / StompMs) & 1) == 0;
            c = Paint([.. BruteBody, leftFootDown ? BruteLeftFootDown : BruteRightFootDown]);
        }
        else
        {
            if ((Hashing.Mix(m.Seed + (ulong)(ms / FlickerMs)) & FlickerOffMask) == 0) return null;
            c = Paint(Mote);
        }
        if (m.Dissolving) Dissolve(c, m, ms);
        return c;
    }

    private static Canvas Paint(IReadOnlyList<string> drawing)
    {
        var c = new Canvas(drawing[0].Length, drawing.Count);
        for (int y = 0; y < drawing.Count; y++)
            for (int x = 0; x < drawing[y].Length; x++)
                if (drawing[y][x] != Clear) c[x, y] = Ink[drawing[y][x]];
        return c;
    }

    private static void Dissolve(Canvas c, DarknessEnemy m, long ms)
    {
        double p = Math.Clamp((double)(ms - m.DissolveStartMs) / Darkness.DissolveMs, 0, 1);
        for (int y = 0; y < c.Height; y++)
            for (int x = 0; x < c.Width; x++)
                if (c[x, y] is not null && Hashing.Unit(m.Seed, DissolveSalt + y * DissolveRowStride + x) < p)
                    c[x, y] = null;
    }
}
