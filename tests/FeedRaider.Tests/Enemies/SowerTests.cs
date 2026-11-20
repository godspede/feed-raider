using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Sower (a stage 3 unlock): a small orchid crossing that sows a curtain of slow seeds across the field with one gap in it,
/// a gap that is marked on the field with a dotted line before the seeds reach it. These tests give the gap (one, wide, never at an edge),
/// the machine (a steady crossing and a steady drop), the safety (a ship in the gap lives, one in a seeded column does not), the pixels,
/// the fit on the four fields and the end of the curtain when the sower is killed.
/// </summary>
public sealed class SowerTests
{
    private const uint Orchid = 0xc77dff, Shade = 0x6a3f9a;

    /// <summary>The stage whose waves bring sowers.</summary>
    private const int SowerStage = 3;

    /// <summary>The sower's score, its hit points before the stage's ramp, and its body's width and height.</summary>
    private const int SowerScore = 450, SowerHp = 8, BodyW = 11, BodyH = 6;

    /// <summary>Decimal places a place or a gap must agree to, and the finer places hit points and a seed's speed agree to.</summary>
    private const int Places = 6, ExactPlaces = 9;

    /// <summary>Where across the field a test asks for a sower: nearer the left edge, and nearer the right.</summary>
    private const double LeftSide = 0.2, RightSide = 0.8;

    /// <summary>The share of the field's height the sower crosses at, and its speed across in pixels a frame.</summary>
    private const double CrossRowShare = 0.1, CrossSpeed = 0.8;

    /// <summary>The seed columns: the first one's distance from the edge it starts at, the distance between two, the gap's width,
    /// the least distance of any seed from an edge, and of the gap's middle from an edge.</summary>
    private const int FirstColumn = 5, SeedSpacing = 10, GapWidth = 30, SeedEdgeMargin = 3, GapEdgeMargin = 20;

    /// <summary>A seed's speed down the field, in pixels a frame.</summary>
    private const double SeedSpeed = 0.7;

    /// <summary>The least distance between two seed columns that counts as the gap: more than the spacing of two seeds.</summary>
    private const double GapMin = SeedSpacing + 1;

    /// <summary>The most frames a sower is given to cross the field and go.</summary>
    private const int CrossingFrameCap = 700;

    /// <summary>The most frames the ship is given to walk to a column, and how near the column counts as there.</summary>
    private const int WalkFrameCap = 400;
    private const double ArrivedWithin = 0.75;

    private static EnemyBehaviour Sower => RaiderGame.TableBehaviour(EnemyKind.Sower)!;

    /// <summary>The sowers a test made itself: stage 3's own waves bring sowers too, and these tests count only the ones they made.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RaiderGame, List<Enemy>> Mine = new();

    private static Enemy Spawn(RaiderGame g, double x)
    {
        var e = g.SpawnKind(EnemyKind.Sower, x);
        Mine.GetOrCreateValue(g).Add(e);
        return e;
    }

    /// <summary>Steps, sweeping away every enemy the test did not make (the stage's waves) after each frame.</summary>
    private static void StepOwnOnly(RaiderGame g, int frames = 1)
    {
        var mine = Mine.GetOrCreateValue(g);
        for (int i = 0; i < frames; i++)
        {
            g.Step();
            g.Enemies.RemoveAll(e => !mine.Contains(e));
        }
    }

    private static RaiderGame Fresh(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows, ulong seed = EnemyKit.PlainSeed)
    {
        var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtStage(SowerStage);
        EnemyKit.Clear(g);
        return g;
    }

    private static List<Bullet> Hostile(RaiderGame g) => g.Bullets.Where(b => !b.Friendly).ToList();

    /// <summary>Makes a sower arrive and steps until it has crossed and gone, noting every seed the frame it is made: (frame, x, bolt).</summary>
    private static List<(int Frame, double X, Bullet B)> Sow(RaiderGame g, double side)
    {
        var seen = new HashSet<Bullet>(ReferenceEqualityComparer.Instance);
        var seeds = new List<(int, double, Bullet)>();
        var s = Spawn(g, side);
        for (int f = 1; f <= CrossingFrameCap; f++)
        {
            StepOwnOnly(g);
            foreach (var b in Hostile(g)) if (seen.Add(b)) seeds.Add((f, b.X, b));
            if (!g.Enemies.Contains(s)) break;
        }
        return seeds;
    }

    /// <summary>The seed columns of one crossing, left to right.</summary>
    private static List<double> Columns(int cols, int rows, ulong seed, double side) =>
        Sow(Fresh(cols, rows, seed), side).Select(t => t.X).Order().ToList();

    /// <summary>The middle of the gap between the seed columns <paramref name="xs"/>, left to right; throws when the curtain has no gap.</summary>
    private static double GapMidOf(List<double> xs)
    {
        int right = Enumerable.Range(1, xs.Count - 1).First(i => xs[i] - xs[i - 1] > GapMin);
        return (xs[right] + xs[right - 1]) / 2;
    }

    private static void Walk(RaiderGame g, double x)
    {
        for (int f = 0; f < WalkFrameCap && Math.Abs(g.PX - x) > ArrivedWithin; f++)
        {
            g.Input.Key(GameKey.Left, g.PX > x);
            g.Input.Key(GameKey.Right, g.PX < x);
            StepOwnOnly(g);
        }
        g.Input.Key(GameKey.Left, false);
        g.Input.Key(GameKey.Right, false);
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_is_small_and_not_a_boss_and_stage_three_brings_it()
    {
        Assert.Equal(EnemyKind.Sower, Sower.Kind);
        Assert.Equal(SowerScore, Sower.Score);
        Assert.Equal(SowerHp, Sower.Hp);
        Assert.False(Sower.Boss);
        Assert.False(Sower.Speaks);
        Assert.Equal(BodyW, Sower.Body.W);
        Assert.Equal(BodyH, Sower.Body.H);
        Assert.Contains(EnemyKind.Sower, Ramp.Unlocks(SowerStage));
        var g = Fresh();
        var e = Spawn(g, 0.5);
        Assert.Equal(SowerHp * Ramp.HpMul(g.Stage), e.MaxHp, ExactPlaces);
        Assert.Null(e.Parts);
    }

    [Fact]
    public void It_dies_to_a_shot_that_is_enough_and_scores_four_fifty()
    {
        const int onFieldAfter = 30;
        var g = Fresh();
        var e = Spawn(g, LeftSide);
        StepOwnOnly(g, onFieldAfter);
        int before = g.Score;
        EnemyKit.Shot(g, e.X, e.Y, e.Hp + 1);
        StepOwnOnly(g);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(before + SowerScore, g.Score);
    }

    // ---------------------------------------------------------------- the machine

    [Theory]
    [InlineData(LeftSide, 1)]
    [InlineData(RightSide, -1)]
    public void It_enters_from_the_side_it_is_nearer_just_off_the_field_and_crosses_at_four_fifths_of_a_pixel_a_frame(double side, int dir)
    {
        const int watchFrames = 40;
        const double offFieldSlack = 1e-9;
        var g = Fresh();
        var e = Spawn(g, side);
        double half = e.Sprite.W / 2.0;
        if (dir > 0) Assert.True(e.X <= -half + offFieldSlack, $"enters left, off the field: {e.X}");
        else Assert.True(e.X >= g.PW + half - offFieldSlack, $"enters right, off the field: {e.X}");
        Assert.Equal(CrossRowShare * g.PH, e.Y, Places);
        double x0 = e.X;
        for (int f = 1; f <= watchFrames; f++)
        {
            StepOwnOnly(g);
            Assert.Equal(x0 + dir * CrossSpeed * f, e.X, Places);
            Assert.Equal(CrossRowShare * g.PH, e.Y, Places);
        }
    }

    [Fact]
    public void It_leaves_the_far_edge_and_is_gone()
    {
        // it travels the field's width and about its own width more, half of it off each edge
        const int watchFrames = 400;
        const double leastPastField = 10, mostPastField = 30;
        var g = Fresh();
        var e = Spawn(g, LeftSide);
        int gone = 0;
        for (int f = 1; f <= watchFrames && gone == 0; f++)
        {
            StepOwnOnly(g);
            if (!g.Enemies.Contains(e)) gone = f;
        }
        Assert.InRange(gone, (g.PW + leastPastField) / CrossSpeed, (g.PW + mostPastField) / CrossSpeed);
    }

    [Theory]
    [InlineData(LeftSide)]
    [InlineData(RightSide)]
    public void Its_seeds_are_straight_and_slow_and_dropped_as_it_passes_each_column_ten_pixels_apart(double side)
    {
        const int columnsInGap = GapWidth / SeedSpacing - 1;
        const double columnWithin = 1e-6;
        var g = Fresh();
        var seeds = Sow(g, side);
        var expect = new List<double>();
        for (double x = FirstColumn; x <= g.PW - SeedEdgeMargin; x += SeedSpacing) expect.Add(side < 0.5 ? x : g.PW - x);
        Assert.Equal(expect.Count - columnsInGap, seeds.Count);
        foreach (var (_, x, b) in seeds)
        {
            Assert.Contains(expect, c => Math.Abs(c - x) < columnWithin);
            Assert.Equal(0, b.VX);
            Assert.Equal(SeedSpeed, b.VY, ExactPlaces);
            Assert.False(b.Friendly);
        }
        // a seed for each column the sower passes, so the longest wait is across the gap's missing columns
        const double framesPerColumn = SeedSpacing / CrossSpeed;
        var frames = seeds.Select(t => t.Frame).ToList();
        for (int i = 1; i < frames.Count; i++)
            Assert.InRange(frames[i] - frames[i - 1], (int)Math.Floor(framesPerColumn), (int)Math.Ceiling((columnsInGap + 1) * framesPerColumn));
    }

    [Fact]
    public void Killing_it_stops_the_curtain()
    {
        const int seedsBeforeShot = 2, watchFrames = 200;
        const double killingDamage = 1000;
        var g = Fresh();
        var e = Spawn(g, LeftSide);
        var seen = new HashSet<Bullet>(ReferenceEqualityComparer.Instance);
        while (seen.Count < seedsBeforeShot && g.Enemies.Contains(e))
        {
            StepOwnOnly(g);
            foreach (var b in Hostile(g)) seen.Add(b);
        }
        Assert.True(seen.Count >= seedsBeforeShot);
        EnemyKit.Shot(g, e.X, e.Y, killingDamage);
        StepOwnOnly(g);
        Assert.DoesNotContain(e, g.Enemies);
        for (int f = 0; f < watchFrames; f++)
        {
            StepOwnOnly(g);
            foreach (var b in Hostile(g)) Assert.Contains(b, seen);
        }
    }

    // ---------------------------------------------------------------- the gap

    [Theory, MemberData(nameof(PileOnTests.Fields), MemberType = typeof(PileOnTests))]
    public void The_curtain_has_one_gap_thirty_wide_whose_middle_is_twenty_from_either_edge_and_every_seed_is_inside_the_field(int cols, int rows)
    {
        foreach (double side in new[] { LeftSide, RightSide })
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                var g = Fresh(cols, rows, seed);
                var xs = Sow(g, side).Select(t => t.X).Order().ToList();
                var deltas = Enumerable.Range(1, xs.Count - 1).Select(i => xs[i] - xs[i - 1]).ToList();
                Assert.Single(deltas, d => d > GapMin);
                Assert.Equal(GapWidth, deltas.Single(d => d > GapMin), Places);
                Assert.All(deltas.Where(d => d <= GapMin), d => Assert.Equal(SeedSpacing, d, Places));
                double mid = GapMidOf(xs);
                Assert.InRange(mid, GapEdgeMargin, g.PW - GapEdgeMargin);
                Assert.All(xs, x => Assert.InRange(x, SeedEdgeMargin, g.PW - SeedEdgeMargin));
            }
    }

    [Fact]
    public void The_gap_is_not_always_in_the_same_place()
    {
        const ulong seeds = 24;
        const int fewestPlaces = 3;
        var mids = new HashSet<double>();
        for (ulong seed = 1; seed <= seeds; seed++) mids.Add(GapMidOf(Columns(EnemyKit.BigCols, EnemyKit.BigRows, seed, LeftSide)));
        Assert.True(mids.Count >= fewestPlaces, $"{mids.Count} different gaps in {seeds} seeds");
    }

    [Fact]
    public void The_same_seed_gives_the_same_gap()
    {
        const ulong seed = 9;
        Assert.Equal(Columns(EnemyKit.BigCols, EnemyKit.BigRows, seed, LeftSide), Columns(EnemyKit.BigCols, EnemyKit.BigRows, seed, LeftSide));
    }

    [Fact]
    public void A_ship_that_does_not_fire_and_waits_in_the_gap_lives()
    {
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
            foreach (ulong seed in new ulong[] { 1, 2, 3 })
            {
                double mid = GapMidOf(Columns(cols, rows, seed, LeftSide));
                var g = Fresh(cols, rows, seed);
                var s = Spawn(g, LeftSide);
                Walk(g, mid);
                StepOwnOnly(g, CrossingFrameCap);
                Assert.Equal(0, g.Deaths);
                Assert.DoesNotContain(s, g.Enemies);
            }
    }

    [Fact]
    public void A_ship_in_a_seeded_column_that_waits_is_hit()
    {
        var xs = Columns(EnemyKit.BigCols, EnemyKit.BigRows, EnemyKit.PlainSeed, LeftSide);
        double column = xs[xs.Count / 2];
        var g = Fresh();
        Spawn(g, LeftSide);
        Walk(g, column);
        for (int f = 0; f < CrossingFrameCap && g.Deaths == 0; f++) StepOwnOnly(g);
        Assert.True(g.Deaths > 0 || g.Hp + g.Shield < Tuning.ShipHp + Tuning.ShieldHits);   // the shield takes the first hit
    }

    // ---------------------------------------------------------------- pixels

    [Fact]
    public void It_is_drawn_in_orchid_with_a_deeper_underside_and_a_gold_seed_pod()
    {
        // a middle a half pixel off the whole so the sprite's top left falls on whole pixels
        const double bodyX = 60.5, bodyY = 20;
        // art points: a horn's tip and the clear pixel beside it on the top row, the shade and the orchid on the upper row,
        // the gold pod, the orchid under it and the outline under that on the bottom row
        const int hornX = 0, besideHornX = 1, topRow = 0, shadeX = 1, orchidX = 4, upperRow = 2, podX = 5, podRow = 3, underPodRow = 4;
        const int bottomRow = BodyH - 1;
        var g = Fresh();
        var e = Spawn(g, 0.5);
        e.X = bodyX; e.Y = bodyY;
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int left = (int)Math.Round(bodyX - BodyW / 2.0), top = (int)Math.Round(bodyY - BodyH / 2.0);
        uint At(int x, int y) => f.Get(left + x, top + y) & EnemyKit.RgbMask;
        Assert.Equal(Palette.Plum, At(hornX, topRow));
        Assert.Equal(Shade, At(shadeX, upperRow));
        Assert.Equal(Orchid, At(orchidX, upperRow));
        Assert.Equal(Palette.Gold, At(podX, podRow));
        Assert.Equal(Orchid, At(podX, underPodRow));
        Assert.Equal(Palette.Plum, At(podX, bottomRow));
        Assert.NotEqual(Palette.Plum, At(besideHornX, topRow));   // the horns stand apart: clear pixels between them
    }

    [Fact]
    public void A_dotted_orchid_line_marks_the_gap_below_the_sower_down_to_the_bottom_of_the_field()
    {
        // the line has a dot on about every other row, a dot or two short of half; it reaches within a few rows of the bottom,
        // and the columns either side of it stay clear
        const int markedAfter = 25, missingDots = 2, bottomRows = 3, clearBeside = 2;
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
        {
            double mid = GapMidOf(Columns(cols, rows, EnemyKit.PlainSeed, LeftSide));
            var g = Fresh(cols, rows);
            var e = Spawn(g, LeftSide);
            StepOwnOnly(g, markedAfter);
            var f = EnemyKit.ClearPickupsAndDraw(g);
            int x = (int)Math.Round(mid), below = (int)Math.Ceiling(e.Y + e.Sprite.H / 2.0);
            var dots = Enumerable.Range(below, g.PH - below).Where(y => (f.Get(x, y) & EnemyKit.RgbMask) == Orchid).ToList();
            Assert.True(dots.Count >= (g.PH - below) / 2 - missingDots, $"{cols}x{rows}: {dots.Count} dots of {g.PH - below} rows");
            Assert.True(dots.Max() >= g.PH - bottomRows, "the line runs to the bottom of the field");
            Assert.DoesNotContain(dots, y => dots.Contains(y + 1));   // dotted, never a solid line
            for (int dx = -clearBeside; dx <= clearBeside; dx++)
                if (dx != 0) Assert.DoesNotContain(Enumerable.Range(below, g.PH - below), y => (f.Get(x + dx, y) & EnemyKit.RgbMask) == Orchid);
        }
    }

    // ---------------------------------------------------------------- fit and determinism

    [Theory, MemberData(nameof(PileOnTests.Fields), MemberType = typeof(PileOnTests))]
    public void It_crosses_in_the_top_part_of_the_field_above_the_floor_on_every_field(int cols, int rows)
    {
        var g = Fresh(cols, rows);
        var e = Spawn(g, LeftSide);
        double floor = EnemyKit.BossFloor(g.PH);
        Assert.True(e.Y + e.Sprite.H / 2.0 <= floor, $"the sower's lowest pixel {e.Y + e.Sprite.H / 2.0} is above the floor {floor}");
        Assert.True(e.Y - e.Sprite.H / 2.0 >= 0);
    }

    [Fact]
    public void The_same_seed_gives_the_same_run_with_a_sower_in_it()
    {
        const int runCols = 80, runRows = 24, runFrames = 600, spawnEvery = 200;
        const ulong runSeed = 7;
        string Run(ulong seed)
        {
            var g = Fresh(runCols, runRows, seed);
            for (int f = 0; f < runFrames; f++)
            {
                if (f % spawnEvery == 0) Spawn(g, f % (2 * spawnEvery) == 0 ? LeftSide : RightSide);
                EnemyKit.BotFrame(g);
                StepOwnOnly(g);
            }
            return string.Join("|", g.Bullets.Select(b => $"{b.X:0.000}:{b.Y:0.000}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}";
        }
        Assert.Equal(Run(runSeed), Run(runSeed));
    }
}
