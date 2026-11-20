using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Lurk (stage 7): cannot be touched until it surfaces to shoot. These tests give its machine (enter, hidden, surfacing, solid,
/// hidden again, three strikes, gone), the pair it comes in, its safety (nothing hurts it while hidden, it can be killed while it
/// surfaces, nothing of it can kill during the surfacing), its pixels (four dots, the fill-in in three steps, white eyes for the last
/// twelve frames), its fit on the four fields, and its fan.
/// </summary>
public sealed class LurkTests
{
    private const uint Smoke = 0x8a94a8, Dark = 0x4f5360;
    private const int Entering = 0, Hidden = 1, Surfacing = 2, Solid = 3;

    /// <summary>Frames it hides, surfaces and stays solid for, the frames of each step of its surfacing, and the strikes before it leaves.</summary>
    private const int HiddenFrames = 90, SurfaceFrames = 36, StepFrames = 12, SolidFrames = 30, Strikes = 3;

    /// <summary>Frames the second of a pair surfaces after the first, and the bolts in each fan.</summary>
    private const int PartnerLag = 60, FanBolts = 3;

    /// <summary>The row it hides at as a share of the field's height, and how near the ship's column it drifts.</summary>
    private const double HoldRow = 0.2, Near = 20;

    /// <summary>The speed of its bolts, the angle between two in its fan, and the speed it leaves at.</summary>
    private const double BoltSpeed = 1.25, Spread = 0.25, LeaveSpeed = 0.8;

    /// <summary>The stage it first comes in.</summary>
    private const int ItsStage = 7;

    /// <summary>A shot heavier than its hit points.</summary>
    private const double KillingShot = 100;

    /// <summary>A shot of one hit point.</summary>
    private const double LightShot = 1;

    /// <summary>Its score, hit points and drawing size in the enemy table.</summary>
    private const int TableScore = 650, TableHp = 4, BodyW = 9, BodyH = 8;

    /// <summary>The lurks a pair is made of.</summary>
    private const int PairSize = 2;

    /// <summary>Where a lurk arrives, as shares of the field's width: the middle, left of it, near the left edge, the first of the pair test, and just either side of the middle.</summary>
    private const double MidWidth = 0.5, LeftOfMiddle = 0.3, FarLeft = 0.05, PairFirst = 0.4;
    private const double JustLeftOfMiddle = 0.48, JustRightOfMiddle = 0.52;

    /// <summary>Ages in a mode the tests wait for, in frames: settled in hiding, long in hiding, part way into surfacing, settled solid, just solid.</summary>
    private const int HiddenAWhile = 5, HiddenLonger = 10, SurfacingAWhile = 3, SolidAWhile = 5, SolidJustNow = 2;

    /// <summary>Decimal places a speed or hit points must agree to, and the distance two positions or speeds may differ by.</summary>
    private const int Places = 6, HpPlaces = 9;
    private const double Close = 0.01;

    /// <summary>The seed of the seeded run.</summary>
    private const ulong RunSeed = 11;

    /// <summary>Where the pixel tests hold it, clear of the field's edges.</summary>
    private const double DrawX = 60, DrawY = 30;

    /// <summary>Frames a wait may take before the test gives up on the mode it waits for.</summary>
    private const int MaxWaitFrames = 2000;

    public static IEnumerable<object[]> Fields() => EnemyKit.FieldSizes.Select(f => new object[] { f.Cols, f.Rows });

    private static EnemyBehaviour Lurk => RaiderGame.TableBehaviour(EnemyKind.Lurk)!;

    /// <summary>One frame with every other enemy swept away and every hostile bolt removed after the frame, the bolts it made added to <paramref name="seen"/>.</summary>
    private static void StepAloneCollectingBolts(RaiderGame g, Enemy l, List<Bullet>? seen = null)
    {
        g.Step();
        g.Enemies.RemoveAll(e => e != l);
        if (seen is not null) foreach (var b in g.Bullets) if (!b.Friendly && !seen.Contains(b)) seen.Add(b);
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    /// <summary>Steps until the lurk is in a mode at an age.</summary>
    private static void StepUntilMode(RaiderGame g, Enemy l, int mode, int age = 1)
    {
        for (int i = 0; i < MaxWaitFrames && !(l.Mode == mode && l.ModeAge >= age); i++) StepAloneCollectingBolts(g, l);
        Assert.Equal(mode, l.Mode);
    }

    /// <summary>Holds the lurk at a fixed spot clear of the edges and gives the top left pixel of its drawing.</summary>
    private static (int X0, int Y0) HoldForDrawing(Enemy l)
    {
        l.X = DrawX; l.BaseX = DrawX; l.Y = DrawY;
        return ((int)Math.Round(l.X - Lurk.Body.W / 2.0), (int)Math.Round(l.Y - Lurk.Body.H / 2.0));
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Lurk, Lurk.Kind);
        Assert.Equal("LURK", Lurk.Name);
        Assert.Equal(TableScore, Lurk.Score);
        Assert.False(Lurk.Boss);
        Assert.True(Lurk.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(BodyW, Lurk.Body.W);
        Assert.Equal(BodyH, Lurk.Body.H);
        Assert.Equal(TableHp, Lurk.Hp);
    }

    [Fact]
    public void Stage_seven_has_it_in_its_unlocks()
    {
        Assert.Contains(EnemyKind.Lurk, Ramp.Unlocks(ItsStage));
    }

    [Fact]
    public void Its_hit_points_follow_the_ramp_like_any_other_kind()
    {
        var g = EnemyKit.Game();
        g.StartAtStage(ItsStage);
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        Assert.Equal(Lurk.Hp * Ramp.HpMul(ItsStage), l.Hp, HpPlaces);
    }

    // ---------------------------------------------------------------- the machine

    [Theory, MemberData(nameof(Fields))]
    public void It_enters_hides_ninety_surfaces_thirty_six_stays_solid_thirty_and_goes_after_three_strikes(int cols, int rows)
    {
        const int RunFrames = 1500;
        const double RowSlack = 0.5;
        var g = EnemyKit.Game(cols, rows);
        var l = g.SpawnKind(EnemyKind.Lurk, LeftOfMiddle);
        Assert.Equal(Entering, l.Mode);
        Assert.True(l.Y < 0);
        var starts = new List<(int Mode, int Frame)>();
        int prev = Entering, fans = 0;
        var seen = new List<Bullet>();
        for (int f = 1; f <= RunFrames && g.Enemies.Contains(l); f++)
        {
            int n = seen.Count;
            StepAloneCollectingBolts(g, l, seen);
            if (!g.Enemies.Contains(l)) break;
            if (seen.Count - n == FanBolts) fans++;
            if (l.Mode != prev) { starts.Add((l.Mode, f)); prev = l.Mode; }
            if (l.Mode == Hidden && !l.Leaving) Assert.Equal(HoldRow * g.PH, l.Y, RowSlack);
            Assert.True(EnemyKit.InsideWidth(g, l), $"frame {f}: {EnemyKit.Extent(l)} outside {g.PW}");
        }
        Assert.Equal(Strikes, fans);
        Assert.DoesNotContain(l, g.Enemies);
        Assert.Equal(0, g.Kills);
        Assert.Equal(0, g.Score);
        int[] threeStrikes = [Hidden, Surfacing, Solid, Hidden, Surfacing, Solid, Hidden, Surfacing, Solid];
        Assert.Equal(threeStrikes, starts.Take(threeStrikes.Length).Select(s => s.Mode));
        Assert.Equal(HiddenFrames, starts[1].Frame - starts[0].Frame);
        Assert.Equal(SurfaceFrames, starts[2].Frame - starts[1].Frame);
        Assert.Equal(SolidFrames, starts[3].Frame - starts[2].Frame);
        Assert.Equal(HiddenFrames, starts[4].Frame - starts[3].Frame);
        Assert.Equal(HiddenFrames + SurfaceFrames + SolidFrames, starts[6].Frame - starts[3].Frame);
    }

    [Fact]
    public void After_the_third_strike_it_leaves_upward_and_cannot_be_touched()
    {
        var g = EnemyKit.Game();
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        for (int i = 0; i < Strikes; i++) { StepUntilMode(g, l, Surfacing); StepUntilMode(g, l, Solid); }
        for (int i = 0; i < SolidFrames + 1; i++) StepAloneCollectingBolts(g, l);
        Assert.True(l.Leaving);
        Assert.True(l.Untouchable);
        double y = l.Y;
        StepAloneCollectingBolts(g, l);
        Assert.Equal(LeaveSpeed, y - l.Y, Places);
    }

    [Fact]
    public void Hidden_it_drifts_to_within_twenty_pixels_of_the_ship_s_column_and_the_hold_row_is_a_fifth_of_the_field()
    {
        var g = EnemyKit.Game();
        const double Slack = 0.5;
        var l = g.SpawnKind(EnemyKind.Lurk, FarLeft);
        StepUntilMode(g, l, Hidden, HiddenFrames - 1);
        Assert.InRange(Math.Abs(l.X - g.PX), 0, Near + Slack);
        Assert.Equal(HoldRow * g.PH, l.Y, Slack);
    }

    [Fact]
    public void Hidden_it_will_not_cross_to_the_far_side_of_the_ship_s_column_on_purpose()
    {
        var g = EnemyKit.Game();
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);   // already above the ship
        StepUntilMode(g, l, Hidden);
        const int WatchFrames = 60;
        double x = l.X;
        for (int i = 0; i < WatchFrames; i++) StepAloneCollectingBolts(g, l);
        Assert.Equal(x, l.X, Close);   // within twenty already: it stays put
    }

    [Fact]
    public void It_comes_in_a_pair_whose_second_surfaces_sixty_frames_after_the_first_and_that_second_makes_no_more()
    {
        var g = EnemyKit.Game();
        var a = g.SpawnKind(EnemyKind.Lurk, PairFirst);
        g.Step();
        var lurks = g.Enemies.Where(e => e.Kind == EnemyKind.Lurk).ToList();
        Assert.Equal(PairSize, lurks.Count);
        var b = lurks.Single(e => e != a);
        Assert.Same(a, b.Parent);
        Assert.False(b.Lesser);
        const double ApartAtLeast = 16;
        const int MaxFrames = 700;
        Assert.True(Math.Abs(a.X - b.X) >= ApartAtLeast, "the pair stand apart");
        int aSurf = -1, bSurf = -1;
        for (int f = 1; f < MaxFrames && (aSurf < 0 || bSurf < 0); f++)
        {
            g.Step();
            g.Enemies.RemoveAll(e => e.Kind < EnemyKind.Gnat);
            g.Bullets.RemoveAll(x => !x.Friendly);
            Assert.Equal(PairSize, g.Enemies.Count(e => e.Kind == EnemyKind.Lurk));
            if (aSurf < 0 && a.Mode == Surfacing) aSurf = f;
            if (bSurf < 0 && b.Mode == Surfacing) bSurf = f;
        }
        Assert.Equal(PartnerLag, bSurf - aSurf);
    }

    [Theory]
    [InlineData(JustLeftOfMiddle)]
    [InlineData(JustRightOfMiddle)]
    public void When_the_first_stands_near_the_middle_the_second_stands_on_the_other_side_of_it(double across)
    {
        var g = EnemyKit.Game();
        var a = g.SpawnKind(EnemyKind.Lurk, across);
        g.Step();
        var b = g.Enemies.Single(e => e.Kind == EnemyKind.Lurk && e != a);
        bool firstIsLeft = a.X < g.PW / 2.0;
        Assert.Equal(firstIsLeft, b.X > a.X);
    }

    // ---------------------------------------------------------------- safe

    [Fact]
    public void Hidden_nothing_touches_it_not_a_shot_a_bomb_or_the_ship()
    {
        var g = EnemyKit.Game();
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        StepUntilMode(g, l, Hidden, HiddenLonger);
        Assert.True(l.Untouchable);
        double hp = l.Hp;
        var b = EnemyKit.Shot(g, l.X, l.Y, KillingShot);
        g.Step();
        g.Enemies.RemoveAll(e => e != l);
        Assert.Equal(hp, l.Hp);
        Assert.Contains(b, g.Bullets);   // the shot flew on
        g.Bullets.Clear();
        g.Input.BombPressed = true;
        g.Step();
        g.Enemies.RemoveAll(e => e != l);
        Assert.Contains(l, g.Enemies);
        Assert.Equal(hp, l.Hp);
        Assert.Equal(0, g.Kills);
        // and the ship may sit in it
        Assert.False(Lurk.Touches(g, l));
    }

    [Fact]
    public void Surfacing_it_can_be_hurt_and_a_shot_kills_it_for_six_hundred_fifty()
    {
        var g = EnemyKit.Game();
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        StepUntilMode(g, l, Surfacing, SurfacingAWhile);
        Assert.False(l.Untouchable);
        int before = g.Score;
        EnemyKit.Shot(g, l.X, l.Y, KillingShot);
        StepAloneCollectingBolts(g, l);
        Assert.DoesNotContain(l, g.Enemies);
        Assert.Equal(1, g.Kills);
        Assert.Equal(before + Lurk.Score, g.Score);
    }

    [Fact]
    public void Solid_after_the_strike_it_can_still_be_hurt()
    {
        var g = EnemyKit.Game();
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        StepUntilMode(g, l, Solid, SolidAWhile);
        Assert.False(l.Untouchable);
        double hp = l.Hp;
        EnemyKit.Shot(g, l.X, l.Y, LightShot);
        StepAloneCollectingBolts(g, l);
        Assert.Equal(hp - LightShot, l.Hp, Places);
    }

    [Fact]
    public void In_the_surfacing_nothing_of_its_own_exists_and_it_does_not_touch_then_the_fan_comes_at_the_end()
    {
        var g = EnemyKit.Game();
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        StepUntilMode(g, l, Hidden);
        const int MaxFrames = 300;
        var seen = new List<Bullet>();
        int surf = 0;
        for (int f = 0; f < MaxFrames && l.Mode != Solid; f++)
        {
            int n = seen.Count;
            StepAloneCollectingBolts(g, l, seen);
            if (l.Mode == Surfacing)
            {
                surf++;
                Assert.Equal(seen.Count, n);
                Assert.False(Lurk.Touches(g, l));
            }
        }
        Assert.Equal(SurfaceFrames, surf);   // the frame it turns solid is the strike
        Assert.Equal(FanBolts, seen.Count);
        foreach (var b in seen) Assert.Equal(BoltSpeed, Math.Sqrt(b.VX * b.VX + b.VY * b.VY), Close);
    }

    [Fact]
    public void The_fan_is_three_bolts_a_quarter_radian_apart_round_the_aim_locked_when_it_surfaced()
    {
        var g = EnemyKit.Game();
        const int MaxFrames = 60;
        const double AimSlack = 0.08, SpreadSlack = 0.03;
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        StepUntilMode(g, l, Surfacing);
        double lx = l.AimX, ly = l.AimY;
        Assert.Equal(g.PX, lx, Close);
        Assert.Equal(g.PY, ly, Close);
        g.Input.Key(GameKey.Left, true);   // the ship leaves: the lock does not follow
        var seen = new List<Bullet>();
        for (int f = 0; f < MaxFrames && seen.Count == 0; f++) StepAloneCollectingBolts(g, l, seen);
        g.Input.Key(GameKey.Left, false);
        Assert.Equal(lx, l.AimX, Close);
        Assert.Equal(FanBolts, seen.Count);
        var angles = seen.Select(b => Math.Atan2(b.VY, b.VX)).OrderBy(a => a).ToList();
        double centre = Math.Atan2(ly - seen[0].Y, lx - seen[0].X);
        Assert.Equal(centre, angles[1], AimSlack);
        Assert.Equal(Spread, angles[1] - angles[0], SpreadSlack);
        Assert.Equal(Spread, angles[2] - angles[1], SpreadSlack);
    }

    [Fact]
    public void A_ship_that_stands_through_the_surfacing_and_then_steps_seven_pixels_aside_lives()
    {
        var g = EnemyKit.Game();
        const int StepAsideFrames = 6, FlightFrames = 120;
        const double AsideAtLeast = 7, AsideAtMost = 9;
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        StepUntilMode(g, l, Surfacing, SurfaceFrames - StepAsideFrames);
        double start = g.PX;
        for (int f = 0; f < StepAsideFrames; f++)
        {
            g.Input.Key(GameKey.Left, l.Mode == Surfacing);
            StepAloneCollectingBolts(g, l);
        }
        g.Input.Key(GameKey.Left, false);
        double x0 = g.PX;
        for (int f = 0; f < FlightFrames; f++) { g.Step(); g.Enemies.RemoveAll(e => e != l); }
        Assert.Equal(0, g.Deaths);
        Assert.InRange(start - x0, AsideAtLeast, AsideAtMost);
    }

    // ---------------------------------------------------------------- pixels

    [Fact]
    public void Hidden_it_is_four_dim_dots_at_its_corners_and_nothing_else()
    {
        var g = EnemyKit.Game();
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        StepUntilMode(g, l, Hidden, HiddenAWhile);
        var (x0, y0) = HoldForDrawing(l);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        uint At((int X, int Y) cell) => f.Get(x0 + cell.X, y0 + cell.Y) & EnemyKit.RgbMask;
        int right = Lurk.Body.W - 1, bottom = Lurk.Body.H - 1;
        // cells of the drawing, as (column, row) from its top left corner
        (int X, int Y) middle = (4, 4), topMiddle = (4, 0), belowTopMiddle = (4, 1);
        foreach (var corner in new[] { (0, 0), (right, 0), (0, bottom), (right, bottom) }) Assert.Equal(Dark, At(corner));
        Assert.NotEqual(Smoke, At(middle));
        Assert.NotEqual(Palette.Plum, At(topMiddle));
        Assert.NotEqual(Dark, At(belowTopMiddle));
    }

    [Fact]
    public void Surfacing_it_fills_in_over_three_steps_and_its_eyes_are_white_for_the_last_twelve_frames()
    {
        var g = EnemyKit.Game();
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        // cells of the drawing, as (column, row) from its top left corner
        (int X, int Y) outlineCell = (4, 0), bodyCell = (2, 1), smokeCell = (2, 4), leftEye = (2, 3);
        StepUntilMode(g, l, Hidden, HiddenAWhile);
        (uint Outline, uint Body, uint Smoke, uint Eye) StepToAgeAndSample(int age)
        {
            StepUntilMode(g, l, Surfacing, age);
            var (x0, y0) = HoldForDrawing(l);
            var f = EnemyKit.ClearPickupsAndDraw(g);
            uint Cell((int X, int Y) cell) => f.Get(x0 + cell.X, y0 + cell.Y) & EnemyKit.RgbMask;
            return (Cell(outlineCell), Cell(bodyCell), Cell(smokeCell), Cell(leftEye));
        }
        var s1 = StepToAgeAndSample(StepFrames / 2);
        Assert.Equal(Palette.Plum, s1.Outline);                   // step one: the outline
        Assert.NotEqual(Dark, s1.Body);
        Assert.NotEqual(Smoke, s1.Smoke);
        var s2 = StepToAgeAndSample(StepFrames + StepFrames / 2);
        Assert.Equal(Palette.Plum, s2.Outline);                   // step two: and the dark body
        Assert.Equal(Dark, s2.Body);
        Assert.NotEqual(Smoke, s2.Smoke);
        Assert.NotEqual(Palette.White, s2.Eye);
        var s3 = StepToAgeAndSample(2 * StepFrames);
        Assert.NotEqual(Smoke, s3.Smoke);                 // still step two on the last frame of it
        Assert.NotEqual(Palette.White, s3.Eye);
        var s4 = StepToAgeAndSample(2 * StepFrames + 1);
        Assert.Equal(Smoke, s4.Smoke);                    // step three: everything, the eyes white
        Assert.Equal(Palette.White, s4.Eye);
        var s5 = StepToAgeAndSample(SurfaceFrames - 1);
        Assert.Equal(Palette.White, s5.Eye);
        StepUntilMode(g, l, Solid, SolidJustNow);
        var (sx, sy) = HoldForDrawing(l);
        Assert.Equal(Palette.Crimson, EnemyKit.ClearPickupsAndDraw(g).Get(sx + leftEye.X, sy + leftEye.Y) & EnemyKit.RgbMask);   // solid: the eyes are red again
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_lurks_in_it()
    {
        const int RunFrames = 900;
        (string, bool) Run(ulong seed)
        {
            var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtStage(ItsStage);
            bool seen = false;
            for (int f = 0; f < RunFrames; f++)
            {
                EnemyKit.BotFrame(g);
                g.Step();
                seen |= g.Enemies.Any(e => e.Kind == EnemyKind.Lurk);
            }
            return (string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}:{e.Mode}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}", seen);
        }
        var (a, seenA) = Run(RunSeed);
        var (b, _) = Run(RunSeed);
        Assert.Equal(a, b);
        Assert.True(seenA, $"no lurk came in {RunFrames} frames of stage {ItsStage}");
    }

    // ---------------------------------------------------------------- sound

    [Fact]
    public void It_opens_as_it_surfaces()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        var l = g.SpawnKind(EnemyKind.Lurk, MidWidth);
        StepUntilMode(g, l, Hidden, HiddenAWhile);
        played.Clear();
        StepUntilMode(g, l, Surfacing);
        Assert.Single(played, SfxNames.Open);
    }
}
