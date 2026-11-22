using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Glint: a small pane that is a mirror for 60 frames in every 150. Lit, a shot that strikes it comes straight back down its
/// column and it takes nothing; dull, it is hurt. It shoots nothing. These tests give its machine (enter, hold, slide, leave), what it
/// does with a shot, how a pair keep half a cycle apart, its shimmer and sounds, its safety, its pixels and its fit on the four fields.
/// </summary>
public sealed class GlintTests(ITestOutputHelper output)
{
    private const int Cycle = 150, LitFrames = 60, DullFrames = Cycle - LitFrames;

    /// <summary>The cycles it stays for before it leaves, and the last frames of its dull spell it shimmers for.</summary>
    private const int CyclesBeforeLeaving = 4, ShimmerFrames = 24;

    /// <summary>The row it holds at, as a share of the field's height, and how far it slides a frame toward the ship.</summary>
    private const double HoldRow = 0.16, SlideSpeed = 0.3;

    /// <summary>The speed a returned shot falls at, and the fewest frames between two returned shots.</summary>
    private const double ReturnSpeed = 1.3;
    private const int ReturnGap = 6;

    /// <summary>Its score, hit points and drawing size in the enemy table.</summary>
    private const int TableScore = 700, TableHp = 5, BodyW = 9, BodyH = 7;

    /// <summary>Decimal places two positions or hit points must agree to.</summary>
    private const int Places = 6;

    /// <summary>The blue-grey of a dull pane, the part of it a shot can hurt.</summary>
    private const uint Silver = 0x8f8a9a;

    /// <summary>Where a lone glint arrives, as a share of the field's width: well left of the ship.</summary>
    private const double LeftOfShip = 0.2;

    /// <summary>Where a pair arrives, as shares of the field's width, one either side of the middle.</summary>
    private const double PairLeft = 0.3, PairRight = 0.7;

    /// <summary>Where a pair arrives close together, as shares of the field's width.</summary>
    private const double NearLeft = 0.4, NearRight = 0.6;

    /// <summary>Whole cycles a cycle test watches.</summary>
    private const int WatchedCycles = 3;

    /// <summary>The seed both seeded runs share.</summary>
    private const ulong RunSeed = 7;

    /// <summary>Frames a wait may take before the test gives up on the state it waits for.</summary>
    private const int MaxWaitFrames = 400;

    /// <summary>A shot far heavier than any glint's hit points.</summary>
    private const double KillingShot = 9999;

    /// <summary>A shot of one hit point.</summary>
    private const double LightShot = 1;

    private static EnemyBehaviour Glint => RaiderGame.TableBehaviour(EnemyKind.Glint)!;

    /// <summary>A field with no enemy on it and no boss gate.</summary>
    private static RaiderGame Field(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows, ulong seed = EnemyKit.PlainSeed)
    {
        var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        EnemyKit.Clear(g);
        return g;
    }

    /// <summary>Steps once, keeping only the given enemies and clearing every bolt that is not a reflected one.</summary>
    private static void StepKeepingOnly(RaiderGame g, params Enemy[] keep)
    {
        g.Step();
        g.Enemies.RemoveAll(e => !keep.Contains(e));
        g.Bullets.RemoveAll(b => !b.Friendly && !b.Reflected);
    }

    private static Part Pane(Enemy e) => e.Parts!.Single(p => p.Name == "pane");

    private static bool Lit(Enemy e) => Pane(e).State == PartState.Armoured;

    /// <summary>A glint brought in and let to settle at its hold row.</summary>
    private static Enemy Settled(RaiderGame g)
    {
        var e = g.SpawnKind(EnemyKind.Glint, EnemyKit.MidWidth);
        double hold = HoldRow * g.PH;
        for (int i = 0; i < MaxWaitFrames && e.Y < hold; i++) StepKeepingOnly(g, e);
        Assert.True(e.Y >= hold);
        return e;
    }

    /// <summary>The top left pixel of the glint's drawing on the field.</summary>
    private static (int X0, int Y0) TopLeft(Enemy e) =>
        ((int)Math.Round(e.X - Glint.Body.W / 2.0), (int)Math.Round(e.Y - Glint.Body.H / 2.0));

    /// <summary>Steps until the pane is lit, or dull when <paramref name="lit"/> is false, then <paramref name="extra"/> frames more.</summary>
    private static void StepUntilLitIs(RaiderGame g, Enemy e, bool lit, int extra = 1)
    {
        for (int i = 0; i < MaxWaitFrames && Lit(e) != lit; i++) StepKeepingOnly(g, e);
        Assert.Equal(lit, Lit(e));
        for (int i = 0; i < extra; i++) StepKeepingOnly(g, e);
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Glint, Glint.Kind);
        Assert.Equal("GLINT", Glint.Name);
        Assert.Equal(TableScore, Glint.Score);
        Assert.Equal(TableHp, Glint.Hp);
        Assert.False(Glint.Boss);
        Assert.False(Glint.Speaks);
        Assert.True(Glint.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(BodyW, Glint.Body.W);
        Assert.Equal(BodyH, Glint.Body.H);
    }

    [Fact]
    public void Its_hit_points_are_the_tables_five_scaled_by_the_stage_like_any_other_kind()
    {
        var g = Field();
        var e = g.SpawnKind(EnemyKind.Glint, EnemyKit.MidWidth);
        Assert.Equal(Glint.Hp * Ramp.HpMul(g.Stage), e.Hp, Places);
        Assert.Equal(e.Hp, Pane(e).Hp, Places);
        Assert.True(Pane(e).Vital);
    }

    // ---------------------------------------------------------------- the machine

    [Fact]
    public void It_comes_in_holds_near_a_sixth_of_the_field_down_and_slides_toward_the_ship_at_three_tenths_of_a_pixel()
    {
        const int SettleFrames = 10, SlideFrames = 200;
        const double FarLeftOfShip = 10, SlidAtLeast = 20;
        var g = Field();
        var e = g.SpawnKind(EnemyKind.Glint, LeftOfShip);
        double hold = HoldRow * g.PH;
        double y = e.Y;
        for (int i = 0; i < MaxWaitFrames && e.Y < hold; i++) { StepKeepingOnly(g, e); Assert.True(e.Y >= y); y = e.Y; }
        Assert.Equal(hold, e.Y, Places);
        for (int i = 0; i < SettleFrames; i++) StepKeepingOnly(g, e);
        double x = e.X;
        Assert.True(g.PX > x + FarLeftOfShip);
        StepKeepingOnly(g, e);
        Assert.Equal(SlideSpeed, e.X - x, Places);
        Assert.Equal(hold, e.Y, Places);
        for (int i = 0; i < SlideFrames; i++) StepKeepingOnly(g, e);
        Assert.True(e.X > x + SlidAtLeast);
    }

    [Fact]
    public void A_cycle_is_a_hundred_and_fifty_frames_dull_for_ninety_and_lit_for_sixty()
    {
        var g = Field();
        var e = Settled(g);
        var run = new List<(bool Lit, int Frames)>();
        bool last = Lit(e); int n = 0;
        for (int f = 0; f < WatchedCycles * Cycle; f++)
        {
            StepKeepingOnly(g, e);
            if (Lit(e) != last) { run.Add((last, n)); last = Lit(e); n = 0; }
            n++;
        }
        (bool, int)[] twoCycles = [(true, LitFrames), (false, DullFrames), (true, LitFrames), (false, DullFrames)];
        var whole = run.Skip(1).Take(twoCycles.Length).ToList();   // the first spell is cut short by the settling
        Assert.Equal(twoCycles, whole);
    }

    [Fact]
    public void It_leaves_upward_after_four_cycles_and_is_gone_from_the_field()
    {
        var g = Field();
        var e = g.SpawnKind(EnemyKind.Glint, EnemyKit.MidWidth);
        const int MaxFrames = 10 * Cycle, Slack = 5;
        int left = -1;
        for (int f = 0; f < MaxFrames && g.Enemies.Contains(e); f++)
        {
            StepKeepingOnly(g, e);
            if (left < 0 && e.Leaving) left = f;
        }
        Assert.InRange(left, CyclesBeforeLeaving * Cycle - Slack, CyclesBeforeLeaving * Cycle + Slack);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(0, g.Kills);
    }

    // ---------------------------------------------------------------- a shot on it

    [Fact]
    public void Lit_it_returns_a_shot_straight_down_its_own_column_at_one_point_three_and_takes_nothing()
    {
        var g = Field();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        var e = Settled(g);
        StepUntilLitIs(g, e, true);
        const double OffCentre = 2, HeavyShot = 50;
        double x = e.X + OffCentre, hp = Pane(e).Hp;
        EnemyKit.Shot(g, x, e.Y, HeavyShot);
        StepKeepingOnly(g, e);
        var back = g.Bullets.Where(b => !b.Friendly).ToList();
        Assert.Single(back);
        Assert.Equal(x, back[0].X, Places);
        Assert.Equal(0, back[0].VX);
        Assert.Equal(ReturnSpeed, back[0].VY, Places);
        Assert.Equal(hp, Pane(e).Hp);
        Assert.Contains(SfxNames.Ping, played);
        Assert.DoesNotContain(g.Bullets, b => b.Friendly);
        Assert.Contains(e, g.Enemies);
    }

    [Fact]
    public void Dull_it_is_hurt_and_a_heavy_shot_kills_it_for_seven_hundred()
    {
        var g = Field();
        var e = Settled(g);
        StepUntilLitIs(g, e, false);
        double hp = Pane(e).Hp;
        EnemyKit.Shot(g, e.X, e.Y, LightShot);
        StepKeepingOnly(g, e);
        Assert.Equal(hp - LightShot, Pane(e).Hp, Places);
        Assert.DoesNotContain(g.Bullets, b => !b.Friendly);
        int score = g.Score;
        EnemyKit.Shot(g, e.X, e.Y, KillingShot);
        g.Step();
        Assert.Equal(1, g.Kills);
        Assert.Equal(score + Glint.Score, g.Score);
    }

    [Fact]
    public void It_returns_at_most_one_shot_in_six_frames_and_the_rest_only_ping()
    {
        var g = Field();
        var e = Settled(g);
        StepUntilLitIs(g, e, true, 0);
        const int ShootingFrames = 50;
        const double ShotDamage = 5;
        var sent = new HashSet<Bullet>();
        int frames = 0;
        for (int f = 0; f < ShootingFrames && Lit(e); f++)
        {
            EnemyKit.Shot(g, e.X, e.Y, ShotDamage);
            EnemyKit.Shot(g, e.X + 1, e.Y, ShotDamage);
            StepKeepingOnly(g, e);
            foreach (var r in g.Bullets.Where(x => x.Reflected)) sent.Add(r);
            frames++;
            Assert.DoesNotContain(g.Bullets, x => x.Friendly);
        }
        Assert.InRange(sent.Count, 1, (frames + ReturnGap - 1) / ReturnGap);
        Assert.Equal(Pane(e).MaxHp, Pane(e).Hp);
    }

    [Fact]
    public void A_returned_shot_hurts_the_ship_it_falls_on()
    {
        var g = Field();
        var e = Settled(g);
        StepUntilLitIs(g, e, true);
        const double AboveShip = 6;
        const int FallFrames = 10;   // long enough to fall the gap at the return speed
        g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY - AboveShip, VX = 0, VY = ReturnSpeed, Friendly = false, Reflected = true });
        int hp = g.Hp, shield = g.Shield;
        for (int f = 0; f < FallFrames; f++) StepKeepingOnly(g, e);
        Assert.True(g.Hp < hp || g.Shield < shield);
    }

    // ---------------------------------------------------------------- a pair

    [Fact]
    public void A_pair_keeps_half_a_cycle_apart_so_one_of_them_is_nearly_always_dull()
    {
        var g = Field();
        var a = g.SpawnKind(EnemyKind.Glint, PairLeft);
        var b = g.SpawnKind(EnemyKind.Glint, PairRight);
        int bothLit = 0, aLit = 0, bLit = 0;
        for (int f = 0; f < WatchedCycles * Cycle; f++)
        {
            StepKeepingOnly(g, a, b);
            if (Lit(a)) aLit++;
            if (Lit(b)) bLit++;
            if (Lit(a) && Lit(b)) bothLit++;
        }
        output.WriteLine($"lit: a {aLit}, b {bLit}, both {bothLit} of {WatchedCycles * Cycle}");
        Assert.Equal(0, bothLit);
        Assert.Equal(aLit, bLit);
        Assert.Equal(WatchedCycles * LitFrames, aLit);
    }

    [Fact]
    public void A_second_glint_spawned_thirty_seven_frames_later_is_half_a_cycle_apart_too()
    {
        var g = Field();
        var a = g.SpawnKind(EnemyKind.Glint, NearLeft);
        const int SecondSpawnsAfter = 37, PairCycles = 2;
        for (int i = 0; i < SecondSpawnsAfter; i++) StepKeepingOnly(g, a);
        var b = g.SpawnKind(EnemyKind.Glint, NearRight);
        int both = 0;
        for (int f = 0; f < PairCycles * Cycle; f++) { StepKeepingOnly(g, a, b); if (Lit(a) && Lit(b)) both++; }
        Assert.Equal(0, both);
    }

    // ---------------------------------------------------------------- the shimmer and sounds

    [Fact]
    public void It_shimmers_for_its_last_twenty_four_frames_dull_and_plays_open_as_it_lights_and_shut_as_it_dulls()
    {
        var g = Field();
        var played = new List<(string Name, int Frame)>();
        int frame = 0;
        g.Sfx = new SfxBus((n, _, _, _) => played.Add((n, frame)));
        var e = Settled(g);
        var (x0, y0) = TopLeft(e);
        var body = Glint.Body;
        int shimmer = 0;
        for (frame = 0; frame < Cycle; frame++)
        {
            StepKeepingOnly(g, e);
            var f = EnemyKit.ClearPickupsAndDraw(g);
            bool differs = false;
            for (int y = 0; y < body.H && !differs; y++)
                for (int x = 0; x < body.W; x++)
                    if (body.Px[y * body.W + x] != 0 && (f.Get(x0 + x, y0 + y) & EnemyKit.RgbMask) != (body.Px[y * body.W + x] & EnemyKit.RgbMask)) { differs = true; break; }
            if (differs && !Lit(e)) shimmer++;
        }
        Assert.Equal(ShimmerFrames, shimmer);   // any whole cycle holds exactly one shimmer
        Assert.Contains(played, s => s.Name == SfxNames.Open);
        Assert.Contains(played, s => s.Name == SfxNames.Shut);
    }

    // ---------------------------------------------------------------- safety

    [Fact]
    public void It_fires_nothing_and_never_touches_the_ship()
    {
        var g = Field();
        var e = Settled(g);
        for (int f = 0; f < CyclesBeforeLeaving * Cycle; f++)
        {
            StepKeepingOnly(g, e);
            Assert.Equal(0, g.Bullets.Count(b => !b.Friendly));
            Assert.False(Glint.Touches(g, e));
        }
        Assert.Equal(0, g.Deaths);
    }

    // ---------------------------------------------------------------- pixels and the four fields

    [Fact]
    public void Lit_it_is_all_ice_with_a_shield_blue_edge_and_dull_it_is_silver_round_a_gold_mark_on_crimson()
    {
        var g = Field();
        var e = Settled(g);
        // cells of the drawing, as (column, row) from its top left corner
        (int X, int Y) topPoint = (4, 0), middle = (4, 3), leftOfMiddle = (2, 3);
        var part = Pane(e);
        Assert.All(part.Closed.Px.Where(v => (v & EnemyKit.RgbMask) != 0), v => Assert.Contains(v & EnemyKit.RgbMask, new[] { Palette.Ice, Palette.ShieldGlow }));
        Assert.Contains(part.OpenArt!.Px, v => (v & EnemyKit.RgbMask) == Silver);
        Assert.Contains(part.OpenArt.Px, v => (v & EnemyKit.RgbMask) == Palette.WeakMark);
        Assert.Contains(part.OpenArt.Px, v => (v & EnemyKit.RgbMask) == Palette.WeakBody);
        StepUntilLitIs(g, e, true);
        var (x0, y0) = TopLeft(e);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        uint At((int X, int Y) cell) => f.Get(x0 + cell.X, y0 + cell.Y) & EnemyKit.RgbMask;
        Assert.Equal(Palette.ShieldGlow, At(topPoint));
        Assert.Equal(Palette.Ice, At(middle));
        StepUntilLitIs(g, e, false, 0);
        f = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(Silver, At(leftOfMiddle));
    }

    [Fact]
    public void It_is_small_and_stays_inside_every_field_the_whole_time()
    {
        const int LeavingFrames = 50;    // long enough to rise off the top of any field
        const double TopShare = 0.4;     // it keeps to the top two fifths of the field
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
        {
            var g = Field(cols, rows);
            var e = g.SpawnKind(EnemyKind.Glint, LeftOfShip);
            for (int f = 0; f < CyclesBeforeLeaving * Cycle + LeavingFrames && g.Enemies.Contains(e); f++)
            {
                StepKeepingOnly(g, e);
                if (!g.Enemies.Contains(e)) break;
                Assert.True(e.Leaving || EnemyKit.InsideWidth(g, e), $"{cols}x{rows} frame {f}");
                Assert.True(EnemyKit.LowestPixel(e) < g.PH * TopShare, $"{cols}x{rows}: lowest {EnemyKit.LowestPixel(e)}");
            }
        }
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_pair()
    {
        string Run(ulong seed)
        {
            var g = Field(seed: seed);
            var a = g.SpawnKind(EnemyKind.Glint, PairLeft);
            var b = g.SpawnKind(EnemyKind.Glint, NearRight);
            for (int f = 0; f < MaxWaitFrames; f++) { EnemyKit.BotFrame(g); StepKeepingOnly(g, a, b); }
            return $"{a.X:0.000}:{a.Hp:0.000}:{b.X:0.000}:{b.Hp:0.000}:{g.Score}:{g.Kills}";
        }
        Assert.Equal(Run(RunSeed), Run(RunSeed));
    }
}
