using System.Reflection;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;
using Xunit.Abstractions;

namespace FeedRaider.Tests;

/// <summary>
/// A run from stage 1 past boss 10 meets every kind and every boss in the order the stage
/// table gives, the kinds wear colours of their own, the colour atlas holds across every boss scene, and no enemy goes over its caps
/// on parts and bullets. The lesser gunship's rising visits in the last stages are checked here too.
/// </summary>
public class RosterTests(ITestOutputHelper output)
{
    // ---------------------------------------------------------------- a ship that cannot die, and a bot that shoots what is lowest

    private const int MaxRunFrames = 400_000;   // a whole run past boss 10 must end well inside this
    private const int MaxBossFightFrames = 12_000;   // one boss, from its warning, must end inside this
    private const int FramesAfterBoss10 = 900;   // the run goes on this long after the last boss falls
    private const int BossEndLingerFrames = 120;   // the parts run goes on this long after its boss is beaten
    private const int StageBuckets = 12;   // one per stage, with every stage past the last counted in the last bucket
    private const int KeepAliveInvuln = 6;   // frames of invulnerability the bot grants itself each step
    private const int MaxWholeRunPartsFrames = 60_000;   // the parts-cap run stops here if boss 10 is never beaten
    private const double BotBlindRows = 4;   // the bot ignores enemies this close above its ship or lower, since it cannot shoot them
    private const double BotDeadband = 1;   // the bot stops steering once it is this close to its target column
    private const double ShareTolerance = 0.02;   // how much earlier in a stage than its listed share a kind may first appear

    private static readonly PropertyInfo InvulnProp = typeof(RaiderGame).GetProperty(nameof(RaiderGame.Invuln))!;

    private static void KeepAlive(RaiderGame g) => InvulnProp.SetValue(g, KeepAliveInvuln);

    private static void Bot(RaiderGame g)
    {
        KeepAlive(g);
        g.Input.Key(GameKey.Fire, true);
        double target = g.PX;
        double best = double.NegativeInfinity;
        foreach (var e in g.Enemies)
        {
            if (e.Y > g.PY - BotBlindRows || e.Leaving) continue;
            foreach (var p in e.Parts ?? [])
                if (p.State == PartState.Open && e.Y + p.OY > best) { best = e.Y + p.OY; target = e.X + p.OX; }
            if (e.Parts is null && e.Y > best) { best = e.Y; target = e.X; }
        }
        g.Input.Key(GameKey.Left, g.PX > target + BotDeadband);
        g.Input.Key(GameKey.Right, g.PX < target - BotDeadband);
    }

    private static RaiderGame Fresh(int cols, int rows, ulong seed = 5)
    {
        var g = new RaiderGame(seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        return g;
    }

    private static void ArmTo(RaiderGame g, int level)
    {
        while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); EnemyKit.Clear(g); }
    }

    // ---------------------------------------------------------------- the roster, in order

    /// <summary>What stage each kind first unlocks in, and the share of that stage it waits for.</summary>
    private static readonly (EnemyKind Kind, int Stage, double Share)[] Unlocks =
    [
        (EnemyKind.Mine, 1, 0.5),
        (EnemyKind.Diver, 2, 0), (EnemyKind.Warden, 2, 0),
        (EnemyKind.Splitter, 3, 0), (EnemyKind.Sower, 3, 0),
        (EnemyKind.Turret, 4, 0), (EnemyKind.Flanker, 4, 0),
        (EnemyKind.Lobber, 5, 0),
        (EnemyKind.Nest, 6, 0),
        (EnemyKind.Yoke, 7, 0), (EnemyKind.Lurk, 7, 0),
        (EnemyKind.Chorus, 8, 0),
        (EnemyKind.Glint, 9, 0),
        (EnemyKind.Fuse, 10, 0),
    ];

    private static int BossCount => BossOrder.Length;

    private static readonly EnemyKind[] BossOrder =
    [
        EnemyKind.Gunship, EnemyKind.Bulwark, EnemyKind.Shutter, EnemyKind.Thread, EnemyKind.Spotlight,
        EnemyKind.Brood, EnemyKind.Schism, EnemyKind.PileOn, EnemyKind.Mirror, EnemyKind.Doomscroll,
    ];

    private sealed record Run(Dictionary<EnemyKind, (int Stage, double Share)> First, List<EnemyKind> Bosses, int[] LessersByStage, int Frames);

    private static Run Play(int cols, int rows, ulong seed)
    {
        var g = Fresh(cols, rows, seed);
        ArmTo(g, 3);
        var seen = new HashSet<Enemy>(ReferenceEqualityComparer.Instance);
        var first = new Dictionary<EnemyKind, (int, double)>();
        var bosses = new List<EnemyKind>();
        var lessers = new int[StageBuckets];
        Enemy? lastBoss = null;
        int f = 0, after = 0;
        for (; f < MaxRunFrames && after < FramesAfterBoss10; f++)
        {
            Bot(g);
            g.Step();
            int stage = Math.Min(StageBuckets - 1, g.Stage);
            foreach (var e in g.Enemies)
            {
                if (!seen.Add(e)) continue;
                if (e.Lesser) lessers[stage]++;
                if (!first.ContainsKey(e.Kind)) first[e.Kind] = (stage, Ramp.StageShare(g.Progress));
            }
            if (g.Boss is { } b && !ReferenceEquals(b, lastBoss)) { bosses.Add(b.Kind); lastBoss = b; }
            if (g.BossesBeaten >= BossCount) after++;
        }
        return new Run(first, bosses, lessers, f);
    }

    [Theory]
    [InlineData(120, 46)]
    [InlineData(56, 58)]
    public void A_run_from_stage_one_past_boss_ten_meets_every_kind_in_its_stage_and_every_boss_once_in_order(int cols, int rows)
    {
        var run = Play(cols, rows, 5);
        output.WriteLine($"{cols}x{rows}: {run.Frames} frames; bosses {string.Join(", ", run.Bosses)}");
        output.WriteLine("first seen: " + string.Join(", ", run.First.OrderBy(p => p.Value.Stage).Select(p => $"{p.Key}@{p.Value.Stage}")));
        output.WriteLine("lessers by stage: " + string.Join(" ", run.LessersByStage.Select((n, i) => $"{i}:{n}")));
        Assert.True(run.Frames < MaxRunFrames, "the run never got past boss 10");
        Assert.Equal(BossOrder, run.Bosses.Take(BossCount).ToArray());
        foreach (var (kind, stage, share) in Unlocks)
        {
            Assert.True(run.First.TryGetValue(kind, out var at), $"{kind} was never met");
            Assert.True(at.Stage == stage, $"{kind} was first met in stage {at.Stage}, expected stage {stage}");
            Assert.True(at.Share >= share - ShareTolerance, $"{kind} came at {at.Share:0.00} of stage {stage}, expected {share}");
        }
        Assert.Contains(EnemyKind.Gnat, run.First.Keys);
    }

    [Fact]
    public void The_first_boss_returns_as_a_lesser_gunship_from_stage_four_and_stage_ten_sees_it_more_than_stage_nine()
    {
        var run = Play(120, 46, 5);
        var l = run.LessersByStage;
        output.WriteLine("lessers by stage: " + string.Join(" ", l.Select((n, i) => $"{i}:{n}")));
        for (int s = 1; s < Tuning.LesserFirstStage; s++) Assert.Equal(0, l[s]);
        for (int s = Tuning.LesserFirstStage; s < BossCount; s++) Assert.True(l[s] >= 1, $"stage {s} has no lesser gunship");
        Assert.True(l[BossCount] > l[BossCount - 1], $"stage 10 has {l[BossCount]} visits, stage {BossCount - 1} has {l[BossCount - 1]}");
    }

    // ---------------------------------------------------------------- body colours

    private static readonly EnemyKind[][] Families =
    [
        [EnemyKind.Nest, EnemyKind.Gnat, EnemyKind.Brood],
        [EnemyKind.Glint, EnemyKind.Mirror],
        [EnemyKind.Splitter, EnemyKind.Shard],   // a shard is a piece of its splitter
        [EnemyKind.PileOn, EnemyKind.Brick],     // a brick is a piece of its wall
        [EnemyKind.Grunt, EnemyKind.Carrier, EnemyKind.Gunship],   // the first game's violet ships, which keep their look
    ];

    private static bool Close(uint a, uint b)
    {
        int d = Math.Max(Math.Abs((int)(a >> 16 & 0xff) - (int)(b >> 16 & 0xff)),
            Math.Max(Math.Abs((int)(a >> 8 & 0xff) - (int)(b >> 8 & 0xff)), Math.Abs((int)(a & 0xff) - (int)(b & 0xff))));
        return d <= ColourSnap.DefaultTolerance;   // near enough for the snap to fold the two into one colour
    }

    [Fact]
    public void No_two_kinds_share_a_body_colour_except_within_a_family()
    {
        var kinds = Enum.GetValues<EnemyKind>();
        var bad = new List<string>();
        for (int i = 0; i < kinds.Length; i++)
            for (int j = i + 1; j < kinds.Length; j++)
            {
                if (Families.Any(f => f.Contains(kinds[i]) && f.Contains(kinds[j]))) continue;
                uint a = RaiderGame.TableBehaviour(kinds[i]).Colour, b = RaiderGame.TableBehaviour(kinds[j]).Colour;
                if (Close(a, b)) bad.Add($"{kinds[i]} {a:x6} / {kinds[j]} {b:x6}");
            }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    // ---------------------------------------------------------------- caps on parts and bullets

    private static int HostileBolts(RaiderGame g) => g.Bullets.Count(b => !b.Friendly);

    /// <summary>One boss fought to its end (or to a limit) from its warning, with the screen drawn each frame when an encoder is given;
    /// reports the most of its own bullets alive and the most parts on one enemy.</summary>
    private static (int MaxHostileBolts, int MaxParts, int Frames) Fight(int boss, int cols, int rows, DiffEncoder? enc = null)
    {
        var g = Fresh(cols, rows, 7);
        g.StartAtBoss(boss);
        EnemyKit.Clear(g);
        ArmTo(g, 3);
        var scr = new Screen(cols, rows);
        var view = new RaiderView(7);
        int bullets = 0, parts = 0, f = 0, end = -1;
        for (; f < MaxBossFightFrames; f++)
        {
            Bot(g);
            g.Step();
            if (g.Gate == GateState.Fighting)
            {
                bullets = Math.Max(bullets, HostileBolts(g));
                foreach (var e in g.Enemies) parts = Math.Max(parts, e.Parts?.Count ?? 0);
            }
            if (enc is not null) { view.FitAndRender(g, scr); enc.Encode(scr); }
            if (end < 0 && g.BossesBeaten >= boss) end = f;
            if (end >= 0 && f - end > BossEndLingerFrames) break;
        }
        return (bullets, parts, f);
    }

    [Theory]
    [InlineData(1, 24)]
    [InlineData(2, 24)]
    [InlineData(3, 24)]
    [InlineData(4, 24)]
    [InlineData(5, 24)]
    [InlineData(6, 24)]
    [InlineData(7, 24)]
    [InlineData(8, 48)]
    [InlineData(9, 48)]
    [InlineData(10, 48)]
    public void No_enemy_has_more_than_the_tuned_parts_and_no_boss_more_of_its_own_bolts_alive_than_the_rules_allow(int boss, int boltCap)
    {
        foreach (var (cols, rows) in new[] { (56, 58), (120, 46), (80, 24) })
        {
            var (bullets, parts, frames) = Fight(boss, cols, rows);
            output.WriteLine($"boss {boss} on {cols}x{rows}: {bullets} bullets at most, {parts} parts at most, {frames} frames");
            Assert.True(parts <= Tuning.MaxParts, $"boss {boss} on {cols}x{rows} has {parts} parts");
            Assert.True(bullets <= boltCap, $"boss {boss} on {cols}x{rows} had {bullets} bullets alive, the cap is {boltCap}");
        }
    }

    [Fact]
    public void Every_kind_stays_inside_the_parts_cap_in_a_whole_run()
    {
        var g = Fresh(120, 46, 9);
        ArmTo(g, 3);
        int parts = 0;
        for (int f = 0; f < MaxWholeRunPartsFrames && g.BossesBeaten < BossCount; f++)
        {
            Bot(g);
            g.Step();
            foreach (var e in g.Enemies) parts = Math.Max(parts, e.Parts?.Count ?? 0);
        }
        output.WriteLine($"most parts on one enemy in a whole run: {parts}");
        Assert.True(parts <= Tuning.MaxParts);
    }

    // ---------------------------------------------------------------- the colour atlas

    [Theory]
    [InlineData(56, 58)]
    [InlineData(120, 46)]
    public void Across_every_boss_scene_the_colour_atlas_stays_under_its_limit(int cols, int rows)
    {
        var enc = DiffEncoder.Snapped();
        for (int boss = 1; boss <= BossCount; boss++)
        {
            Fight(boss, cols, rows, enc);
            output.WriteLine($"after boss {boss} on {cols}x{rows}: {enc.Snap!.Count} colours");
        }
        Assert.True(enc.Snap!.Count < ColourSnap.DefaultCapacity, $"{enc.Snap.Count} colours reached the atlas, the limit is {ColourSnap.DefaultCapacity}");
    }
}
