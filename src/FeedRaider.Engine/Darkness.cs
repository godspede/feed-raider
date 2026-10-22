using TUImation;

namespace FeedRaider.Engine;

/// <summary>A darkness monster. It knows its kind, a seed and a text fragment, and nothing about the author.</summary>
public sealed class Monster
{
    public required DarknessKind Kind { get; init; }
    /// <summary>Per-monster seed; motes of one swarm share <see cref="GroupSeed"/> (and so an edge to come from).</summary>
    public required ulong Seed { get; init; }
    public required ulong GroupSeed { get; init; }
    public required long SpawnMs { get; init; }
    public required string Text { get; init; }
    public required string TargetDid { get; set; }
    public DarkSource Source { get; init; } = DarkSource.Label;

    public double X { get; internal set; }
    public double Y { get; internal set; }
    internal long NearSinceMs = -1;
    public long DissolveStartMs { get; internal set; } = -1;
    public bool Dissolving => DissolveStartMs >= 0;

    public int Width => Kind == DarknessKind.Rude ? MonsterSprites.BruteW : MonsterSprites.MoteW;
    public int Height => Kind == DarknessKind.Rude ? MonsterSprites.BruteH : MonsterSprites.MoteH;
}

/// <summary>Monster movement as a pure function of (seed, time, target position): no state, no randomness.</summary>
public static class MonsterMotion
{
    public static (double X, double Y) Start(Monster m, int cols, int pixelHeight)
    {
        int edge = (int)(Hashing.Mix(m.GroupSeed) % 4);
        double u = Hashing.Unit(m.GroupSeed, 1);
        var (x, y) = edge switch
        {
            0 => (-(double)m.Width, u * pixelHeight),
            1 => ((double)cols, u * pixelHeight),
            2 => (u * cols, -(double)m.Height),
            _ => (u * cols, (double)pixelHeight),
        };
        if (m.Kind == DarknessKind.Spam)
        {
            x += (Hashing.Unit(m.Seed, 2) - 0.5) * 10;
            y += (Hashing.Unit(m.Seed, 3) - 0.5) * 10;
        }
        return (x, y);
    }

    public static long TravelMs(Monster m) => m.Kind == DarknessKind.Rude
        ? 5500 + (long)(Hashing.Unit(m.Seed, 4) * 1500)
        : 2200 + (long)(Hashing.Unit(m.Seed, 4) * 500);

    /// <summary>Top-left of the monster. <paramref name="target"/> is the top-left of the guy it is after.</summary>
    public static (double X, double Y) Position(Monster m, long ms, (double X, double Y) target, int cols, int pixelHeight)
    {
        var (sx, sy) = Start(m, cols, pixelHeight);
        double side = Hashing.Unit(m.Seed, 5) < 0.5 ? -1 : 1;
        double dx = target.X + side * (m.Kind == DarknessKind.Rude ? 11 : 7);
        double dy = target.Y + (m.Kind == DarknessKind.Rude ? 3 : Hashing.Unit(m.Seed, 6) * 8 - 2);
        long t = Math.Max(0, ms - m.SpawnMs);
        long travel = TravelMs(m);
        double p = Math.Min(1.0, (double)t / travel);
        if (p >= 1.0)
        {
            // arrived: menacing shake
            long tick = ms / 110;
            double jx = (Hashing.Unit(m.Seed, 100 + (int)(tick & 15)) - 0.5) * 2;
            double jy = m.Kind == DarknessKind.Rude ? 0 : (Hashing.Unit(m.Seed, 120 + (int)(tick & 15)) - 0.5) * 3;
            return (dx + Math.Round(jx), dy + Math.Round(jy));
        }
        double e = m.Kind == DarknessKind.Rude ? Math.Pow(p, 1.6) : p;   // the brute accelerates into the charge
        double x = sx + (dx - sx) * e, y = sy + (dy - sy) * e;
        if (m.Kind == DarknessKind.Spam)
        {
            double ph = Hashing.Unit(m.Seed, 7) * Math.PI * 2;
            x += Math.Sin(ms / 140.0 + ph) * 2 * (1 - p);
            y += Math.Cos(ms / 110.0 + ph) * 3 * (1 - p);
        }
        return (x, y);
    }
}

/// <summary>Spawning, throttling, charging and dispelling. Time is passed in; nothing here reads a clock.</summary>
public sealed class Darkness
{
    public const int MaxMonsters = 6;
    public const int SpawnGapMs = 2000;
    public const int MaxPending = 8;
    public const int DissolveMs = 800;
    public const int DispelGuys = 3;
    public const int DispelAfterMs = 900;

    private readonly List<Monster> _monsters = [];
    private readonly Queue<DarkPost> _pending = new();
    private long _lastSpawnMs = long.MinValue / 2;
    private int _serial;

    public IReadOnlyList<Monster> Monsters => _monsters;
    public int Pending => _pending.Count;
    public int Spawned { get; private set; }
    public int SpawnedFromLabels { get; private set; }
    public int SpawnedFromHidden { get; private set; }
    public int Dispelled { get; private set; }

    public void Offer(DarkPost post)
    {
        _pending.Enqueue(post);
        while (_pending.Count > MaxPending) _pending.Dequeue();
    }

    private static long LifeMs(Monster m) => m.Kind == DarknessKind.Rude ? 16000 : 9000;

    /// <summary>Advance the monsters. <paramref name="guys"/> are the guys' top-left positions this frame.</summary>
    public void Update(long ms, IReadOnlyList<(string Did, (double X, double Y) Pos)> guys, int cols, int pixelHeight)
    {
        _monsters.RemoveAll(m => m.Dissolving && ms - m.DissolveStartMs >= DissolveMs);

        foreach (var m in _monsters)
        {
            if (m.Dissolving) continue;
            if (guys.Count == 0) { m.DissolveStartMs = ms; continue; }
            var target = guys.FirstOrDefault(g => g.Did == m.TargetDid);
            if (target.Did is null)
            {
                target = Nearest(guys, m.X, m.Y);
                m.TargetDid = target.Did;
            }
            (m.X, m.Y) = MonsterMotion.Position(m, ms, target.Pos, cols, pixelHeight);
            if (ms - m.SpawnMs > LifeMs(m)) { m.DissolveStartMs = ms; continue; }

            double mx = m.X + m.Width / 2.0, my = m.Y + m.Height / 2.0;
            int near = guys.Count(g => Math.Abs(g.Pos.X + GuyBuilder.Width / 2.0 - mx) <= 20 && Math.Abs(g.Pos.Y + GuyBuilder.Height / 2.0 - my) <= 14);
            if (near >= DispelGuys)
            {
                if (m.NearSinceMs < 0) m.NearSinceMs = ms;
                if (ms - m.NearSinceMs >= DispelAfterMs) { m.DissolveStartMs = ms; Dispelled++; }
            }
            else m.NearSinceMs = -1;
        }

        if (_pending.Count > 0 && guys.Count > 0 && ms - _lastSpawnMs >= SpawnGapMs && _monsters.Count < MaxMonsters)
            Spawn(_pending.Dequeue(), ms, guys, cols, pixelHeight);
    }

    private void Spawn(DarkPost post, long ms, IReadOnlyList<(string Did, (double X, double Y) Pos)> guys, int cols, int pixelHeight)
    {
        _lastSpawnMs = ms;
        var texts = new List<string>();
        if (post.Kind == DarknessKind.Rude) texts.Add(post.Text);
        else texts.AddRange(DarknessText.Fragments(post.Text).Take(Math.Min(3, MaxMonsters - _monsters.Count)));
        if (texts.Count == 0) return;

        ulong group = Hashing.Mix(post.Seed ^ (ulong)(++_serial));
        // aim at whoever is nearest to where the group comes in from
        var probe = new Monster { Kind = post.Kind, Seed = group, GroupSeed = group, SpawnMs = ms, Text = "", TargetDid = "" };
        var (sx, sy) = MonsterMotion.Start(probe, cols, pixelHeight);
        var target = Nearest(guys, sx, sy);
        for (int i = 0; i < texts.Count; i++)
        {
            var m = new Monster { Kind = post.Kind, Seed = Hashing.Mix(group + (ulong)i), GroupSeed = group, SpawnMs = ms, Text = texts[i], TargetDid = target.Did, Source = post.Source };
            (m.X, m.Y) = MonsterMotion.Start(m, cols, pixelHeight);
            _monsters.Add(m);
            Spawned++;
            if (post.Source == DarkSource.Label) SpawnedFromLabels++;
            else if (post.Source == DarkSource.Hidden) SpawnedFromHidden++;
        }
    }

    private static (string Did, (double X, double Y) Pos) Nearest(IReadOnlyList<(string Did, (double X, double Y) Pos)> guys, double x, double y) =>
        guys.OrderBy(g => (g.Pos.X - x) * (g.Pos.X - x) + (g.Pos.Y - y) * (g.Pos.Y - y)).ThenBy(g => g.Did, StringComparer.Ordinal).First();

    /// <summary>How far a guy is pushed (away, with a shake) by monsters on top of him. Zero when none is near.</summary>
    public (double X, double Y) Flinch(double guyX, double guyY, long ms)
    {
        double ox = 0, oy = 0;
        foreach (var m in _monsters)
        {
            if (m.Dissolving) continue;
            double cx = m.X + m.Width / 2.0 - (guyX + GuyBuilder.Width / 2.0);
            double cy = m.Y + m.Height / 2.0 - (guyY + GuyBuilder.Height / 2.0);
            if (Math.Abs(cx) > 16 || Math.Abs(cy) > 12) continue;
            double force = m.Kind == DarknessKind.Rude ? 3 : 1;
            ox -= Math.Sign(cx) * force;
            oy += ((ms / 90) & 1) == 0 ? 1 : 0;
        }
        return (ox, oy);
    }
}

public static class MonsterSprites
{
    public const int BruteW = 14, BruteH = 12, MoteW = 9, MoteH = 6;

    private static readonly Rgb Void = new(0x14, 0x0c, 0x1e);
    private static readonly Rgb Plum = new(0x2e, 0x1a, 0x45);
    private static readonly Rgb Violet = new(0x4a, 0x2a, 0x70);
    private static readonly Rgb Eye = new(0xdd, 0xe6, 0xa8);
    private static readonly Rgb Fang = new(0xef, 0xe7, 0xcf);
    private static readonly Rgb SickGreen = new(0x7c, 0x9a, 0x1c);
    private static readonly Rgb DeepGreen = new(0x3d, 0x4f, 0x12);

    /// <summary>The sprite at this moment, or null for a flickering mote that is "off" this frame.</summary>
    public static Canvas? Build(Monster m, long ms)
    {
        Canvas c;
        if (m.Kind == DarknessKind.Rude)
        {
            c = new Canvas(BruteW, BruteH);
            bool stomp = ((ms / 200) & 1) == 0;
            c.FillRect(2, 2, 10, 8, Void);
            c.FillRect(1, 4, 12, 4, Void);
            c.FillRect(3, 3, 8, 2, Plum);                 // brow
            c.FillRect(0, 5, 1, 4, Plum); c.FillRect(13, 5, 1, 4, Plum);   // fists
            c.FillRect(3, 10, 3, stomp ? 2 : 1, Plum); c.FillRect(8, 10, 3, stomp ? 1 : 2, Plum);
            c[2, 0] = Violet; c[2, 1] = Violet; c[3, 1] = Plum;   // horns
            c[11, 0] = Violet; c[11, 1] = Violet; c[10, 1] = Plum;
            c.FillRect(4, 5, 2, 1, Eye); c.FillRect(8, 5, 2, 1, Eye);
            c.FillRect(4, 8, 6, 1, Violet);               // snarl
            c[4, 9] = Fang; c[6, 9] = Fang; c[8, 9] = Fang;
        }
        else
        {
            // flicker: each mote blinks off for a few frames at its own rhythm
            if ((Hashing.Mix(m.Seed + (ulong)(ms / 90)) & 7) == 0) return null;
            c = new Canvas(MoteW, MoteH);
            c.FillRect(1, 1, 7, 4, SickGreen);
            c.FillRect(3, 0, 3, 1, DeepGreen); c.FillRect(3, 5, 3, 1, DeepGreen);
            c.FillRect(0, 2, 1, 2, DeepGreen); c.FillRect(8, 2, 1, 2, DeepGreen);
            c[1, 1] = DeepGreen; c[7, 1] = DeepGreen; c[1, 4] = DeepGreen; c[7, 4] = DeepGreen;
            c[3, 2] = Eye; c[5, 2] = Eye;
        }
        if (m.Dissolving) Dissolve(c, m, ms);
        return c;
    }

    private static void Dissolve(Canvas c, Monster m, long ms)
    {
        double p = Math.Clamp((double)(ms - m.DissolveStartMs) / Darkness.DissolveMs, 0, 1);
        for (int y = 0; y < c.Height; y++)
            for (int x = 0; x < c.Width; x++)
                if (c[x, y] is not null && Hashing.Unit(m.Seed, 1000 + y * 64 + x) < p)
                    c[x, y] = null;
    }
}
