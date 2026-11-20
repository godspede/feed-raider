using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Yoke (stage 7 mini-boss): two nodes joined by a bar that kills on touch, coming down the field. These tests give its machine
/// (fall, warn, live, broken), its warning (sixty harmless frames, dotted and dim, then white for the last third), its pixels, its
/// fit on the four fields, its voice, and its fight (the bar goes out with the first node, the yoke dies with the second).
/// Modes the tests read: 0 falling in, 1 the bar warns, 2 the bar is live, 3 broken (one node gone, the bar out).
/// </summary>
public sealed class YokeTests(ITestOutputHelper output)
{
    private const uint Volt = 0xffe85a, Dim = 0x8a7a1f;
    private const int Falling = 0, Warning = 1, Live = 2, Broken = 3;
    private const int YokeStage = 7;
    /// <summary>Where the tests put a yoke, as a share of the field's width: the middle, and far enough left that the ship is on its right.</summary>
    private const double CentreShare = 0.5, LeftOfShipShare = 0.2;
    private const int NodeSize = 7, NodeSpacing = 28, NodeScore = 300;
    private const double NodeHp = 5;
    private const double NodeHalfSize = NodeSize / 2.0, NodeCentreOffset = NodeSpacing / 2;
    private const double FallSpeed = 0.45, DriftSpeed = 0.2;
    private const int WarnFrames = 60, DottedFrames = 40;
    /// <summary>Ages within a phase the drawing test sets: halfway through the dotted warning, halfway through its last third, and just
    /// after the bar goes live.</summary>
    private const int DottedAge = DottedFrames / 2, FlaringAge = (DottedFrames + WarnFrames) / 2, EarlyLiveAge = 3;

    /// <summary>Pixels of the drawing, counted from the sprite's top-left corner. The bar runs through the cores of the nodes.</summary>
    private const int NodeCore = NodeSize / 2, NodeRing = NodeCore - 1, BarRow = NodeCore, RimLeft = 1, RimTop = 0;
    private const int BarFrom = 7, BarTo = 27, FirstDot = BarFrom + 1, DotStep = 2;

    /// <summary>The bar's reach either side of the yoke's centre and its half-thickness, and how far inside or past them a touch probe goes.</summary>
    private const double BarReach = 14, BarHalfThick = 2, JustInside = 0.1, JustPast = 0.5;

    /// <summary>Frame caps for the waits: each loop stops as soon as what it waits for happens.</summary>
    private const int FallOutFrames = 1200, WarnWatchFrames = 200, LiveWaitFrames = 400, PassFrames = 700, VoiceWaitFrames = 300;
    /// <summary>Frames a yoke falls onto the field before a test shoots or bombs it.</summary>
    private const int OnFieldFrames = 40;
    private const int DriftFrames = 40, SurvivorDriftFrames = 30;
    private const double SurvivorMinDrift = 10;
    /// <summary>How far aside the ship walks from the live bar, well past its reach.</summary>
    private const double StepAside = 24;
    /// <summary>More damage than a node has, so one shot takes it.</summary>
    private const double KillingDamage = 100;
    /// <summary>Where the drawing tests pin the yoke: on a half pixel, so its odd-width sprite lands on whole pixels.</summary>
    private const double PinnedX = 60.5, PinnedY = 30;
    /// <summary>The corner and side of the square of whole-pixel positions the overlay test tries.</summary>
    private const int GridLeft = 40, GridTop = 20, GridSpan = 4;
    private const ulong RunSeed = 11, PostSeed = 1;
    private const int RunFrames = 900;
    private const int Places = 9, SpeedPlaces = 6;
    private const double DriftTolerance = 0.01, OnFieldSlack = 0.01;

    private static EnemyBehaviour Yoke => RaiderGame.TableBehaviour(EnemyKind.Yoke)!;
    private static double SpriteHalfWidth => Yoke.Body.W / 2.0;
    private static double SpriteHalfHeight => Yoke.Body.H / 2.0;
    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);

    private static void StepAlone(RaiderGame g, Enemy y)
    {
        g.Step();
        g.Enemies.RemoveAll(e => e != y);
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    private static void Shoot(RaiderGame g, Enemy y, string node, double dmg)
    {
        var p = PartOf(y, node);
        EnemyKit.Shot(g, y.X + p.OX, y.Y + p.OY, dmg);
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Yoke, Yoke.Kind);
        Assert.Equal("YOKE", Yoke.Name);
        Assert.False(Yoke.Boss);
        Assert.True(Yoke.Speaks);
        Assert.True(Yoke.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(35, Yoke.Body.W);
        Assert.Equal(7, Yoke.Body.H);
    }

    [Fact]
    public void It_is_two_open_vital_seven_point_nodes_twenty_eight_pixels_apart_worth_three_hundred_each()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        Assert.Equal(["node-l", "node-r"], y.Parts!.Select(p => p.Name));
        var l = PartOf(y, "node-l"); var r = PartOf(y, "node-r");
        foreach (var n in new[] { l, r })
        {
            Assert.Equal(PartState.Open, n.State);
            Assert.True(n.Vital);
            Assert.Equal(NodeSize, n.HW * 2 + 1); Assert.Equal(NodeSize, n.HH * 2 + 1);
            Assert.Equal(NodeScore, n.Score);
            Assert.Equal(NodeHp, n.Hp, Places);
        }
        Assert.Equal(NodeSpacing, r.OX - l.OX);
        Assert.Equal(2 * NodeHp, y.Hp, Places);
    }

    [Fact]
    public void Its_hit_points_follow_the_ramp_like_any_other_kind()
    {
        var g = EnemyKit.Game();
        g.StartAtStage(YokeStage);
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        Assert.Equal(NodeHp * Ramp.HpMul(YokeStage), PartOf(y, "node-l").Hp, Places);
        Assert.Equal(PartOf(y, "node-l").Hp + PartOf(y, "node-r").Hp, y.Hp, Places);
    }

    [Fact]
    public void Stage_seven_has_it_in_its_unlocks()
    {
        Assert.Contains(EnemyKind.Yoke, Ramp.Unlocks(YokeStage));
    }

    // ---------------------------------------------------------------- the machine

    [Theory]
    [InlineData(30, 16)]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void It_falls_in_warns_for_sixty_frames_then_the_bar_is_live_and_it_falls_out_alive(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        Assert.Equal(Falling, y.Mode);
        Assert.True(y.Y < 0);
        int warnAt = -1, liveAt = -1, gone = -1;
        double last = y.Y;
        bool pushed = false;
        for (int f = 1; f <= FallOutFrames; f++)
        {
            int shield = g.Shield;
            StepAlone(g, y);
            if (!g.Enemies.Contains(y)) { gone = f; break; }
            if (g.Shield != shield) pushed = true;   // a node on the ship pops its shield, and the pop shoves it for a while
            if (!pushed) Assert.Equal(FallSpeed, y.Y - last, SpeedPlaces);
            last = y.Y;
            if (warnAt < 0 && y.Mode == Warning) { warnAt = y.Age; Assert.True(y.Y - y.Sprite.H / 2.0 >= -OnFieldSlack, "the whole bar is on the field when the warning starts"); }
            if (liveAt < 0 && y.Mode == Live) liveAt = y.Age;
            Assert.True(EnemyKit.InsideWidth(g, y), $"frame {f}: {EnemyKit.Extent(y)} outside {g.PW}");
        }
        Assert.True(warnAt > 0);
        Assert.Equal(WarnFrames, liveAt - warnAt);   // counted in the yoke's own frames: a ship that dies on a node freezes the field a while
        Assert.True(gone > liveAt, "it goes out of the bottom of the field");
        Assert.Equal(0, g.Kills);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void It_drifts_toward_the_ship_at_two_tenths_a_frame()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, LeftOfShipShare);
        double start = y.X;
        for (int i = 0; i < DriftFrames; i++) StepAlone(g, y);
        Assert.Equal(DriftFrames * DriftSpeed, y.X - start, DriftTolerance);   // the ship is on the right of it
        Assert.True(y.X + NodeCentreOffset + NodeHalfSize <= g.PW);
    }

    // ---------------------------------------------------------------- warning

    [Fact]
    public void The_bar_cannot_kill_for_sixty_frames_even_with_the_ship_on_it_and_then_it_does()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        int warn = 0, firstDeath = -1;
        for (int f = 0; f < WarnWatchFrames && firstDeath < 0; f++)
        {
            y.X = g.PX; y.BaseX = g.PX; y.Y = g.PY;   // the bar is lying right across the ship
            StepAlone(g, y);
            if (y.Mode == Warning) { warn++; Assert.False(Yoke.Touches(g, y), $"warning frame {warn}"); }
            if (g.Deaths > 0 && firstDeath < 0) firstDeath = f;
        }
        Assert.InRange(warn, WarnFrames - 1, WarnFrames);
        Assert.True(firstDeath >= 0, "and then it kills a ship that stays on it");
    }

    [Fact]
    public void Touches_is_the_bar_between_the_nodes_two_pixels_either_side_of_its_row_and_only_while_live()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int i = 0; i < LiveWaitFrames && y.Mode != Live; i++) StepAlone(g, y);
        Assert.Equal(Live, y.Mode);
        // the ship cannot be teleported, so the yoke is moved to it
        void At(double dx, double dy) { y.X = g.PX + dx; y.Y = g.PY + dy; }
        At(0, 0); Assert.True(Yoke.Touches(g, y));
        At(BarReach - JustInside, BarHalfThick - JustInside); Assert.True(Yoke.Touches(g, y));
        At(-(BarReach - JustInside), -(BarHalfThick - JustInside)); Assert.True(Yoke.Touches(g, y));
        At(BarReach + JustPast, 0); Assert.False(Yoke.Touches(g, y));
        At(0, BarHalfThick + JustPast); Assert.False(Yoke.Touches(g, y));
        At(0, -(BarHalfThick + JustPast)); Assert.False(Yoke.Touches(g, y));
        y.Mode = Warning; At(0, 0); Assert.False(Yoke.Touches(g, y));
        y.Mode = Broken; At(0, 0); Assert.False(Yoke.Touches(g, y));
    }

    [Fact]
    public void A_ship_that_stands_through_the_warning_and_then_steps_out_of_the_way_lives()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        // the yoke is above the ship the whole time: while it warns the ship stands, and once it is live the ship walks aside
        int side = 0;
        for (int f = 0; f < PassFrames && g.Enemies.Contains(y); f++)
        {
            if (y.Mode == Live && side == 0) side = g.PX < y.X ? -1 : 1;
            double target = side == 0 ? g.PX : y.X + side * StepAside;
            g.Input.Key(GameKey.Left, g.PX > target + EnemyKit.SteerDeadZone);
            g.Input.Key(GameKey.Right, g.PX < target - EnemyKit.SteerDeadZone);
            StepAlone(g, y);
        }
        Assert.DoesNotContain(y, g.Enemies);
        Assert.Equal(0, g.Deaths);
    }

    [Fact]
    public void A_ship_that_stays_under_the_bar_as_it_falls_is_hit()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int f = 0; f < PassFrames && g.Deaths == 0 && g.Shield > 0 && g.Enemies.Contains(y); f++) StepAlone(g, y);
        Assert.True(g.Deaths > 0 || g.Shield == 0);   // the shield takes the first hit
    }

    // ---------------------------------------------------------------- pixels

    [Fact]
    public void It_is_drawn_with_gold_cored_crimson_nodes_and_a_dim_dotted_bar_that_goes_white_in_its_last_third()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        y.X = PinnedX; y.BaseX = PinnedX; y.Y = PinnedY;
        // falling in, the bar is the body's own dim dots
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(y.X - SpriteHalfWidth), y0 = (int)Math.Round(y.Y - SpriteHalfHeight);
        Assert.Equal(Palette.Gold, f.Get(x0 + NodeCore, y0 + NodeCore) & EnemyKit.RgbMask);
        Assert.Equal(Palette.WeakBody, f.Get(x0 + NodeRing, y0 + NodeCore) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Gold, f.Get(x0 + NodeSpacing + NodeCore, y0 + NodeCore) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Plum, f.Get(x0 + RimLeft, y0 + RimTop) & EnemyKit.RgbMask);
        Assert.Equal(Dim, f.Get(x0 + FirstDot, y0 + BarRow) & EnemyKit.RgbMask);
        y.Mode = Warning; y.ModeAge = DottedAge;
        Assert.Equal(Dim, EnemyKit.ClearPickupsAndDraw(g).Get(x0 + FirstDot, y0 + BarRow) & EnemyKit.RgbMask);
        Assert.NotEqual(Dim, EnemyKit.ClearPickupsAndDraw(g).Get(x0 + FirstDot + 1, y0 + BarRow) & EnemyKit.RgbMask);   // dotted: every other pixel
        y.ModeAge = FlaringAge;
        var flaring = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(Palette.Tell, flaring.Get(x0 + FirstDot, y0 + BarRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Tell, flaring.Get(x0 + FirstDot + DotStep, y0 + BarRow) & EnemyKit.RgbMask);
        y.Mode = Live; y.ModeAge = EarlyLiveAge;
        var live = EnemyKit.ClearPickupsAndDraw(g);
        for (int x = BarFrom; x <= BarTo; x++) Assert.Equal(Volt, live.Get(x0 + x, y0 + BarRow) & EnemyKit.RgbMask);
    }

    [Fact]
    public void The_bar_goes_out_with_the_first_node_and_the_survivor_is_drawn_alone()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        y.X = PinnedX; y.BaseX = PinnedX; y.Y = PinnedY;
        Shoot(g, y, "node-l", KillingDamage);
        StepAlone(g, y);
        Assert.Equal(Broken, y.Mode);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(y.X - SpriteHalfWidth), y0 = (int)Math.Round(y.Y - SpriteHalfHeight);
        for (int x = BarFrom; x <= BarTo; x++) Assert.NotEqual(Volt, f.Get(x0 + x, y0 + BarRow) & EnemyKit.RgbMask);
        Assert.NotEqual(Dim, f.Get(x0 + FirstDot, y0 + BarRow) & EnemyKit.RgbMask);
        var survivor = PartOf(y, "node-r");
        int sx = (int)Math.Round(y.X + survivor.OX - NodeHalfSize), sy = (int)Math.Round(y.Y + survivor.OY - NodeHalfSize);
        Assert.Equal(Palette.Gold, f.Get(sx + NodeCore, sy + NodeCore) & EnemyKit.RgbMask);
    }

    [Fact]
    public void The_parts_are_drawn_exactly_over_the_body_for_every_whole_pixel_position()
    {
        var g = EnemyKit.Game();
        for (int x = GridLeft; x < GridLeft + GridSpan; x++)
            for (int yy = GridTop; yy < GridTop + GridSpan; yy++)
            {
                EnemyKit.Clear(g);
                var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
                y.X = x + 0.5; y.BaseX = y.X; y.Y = yy + 0.5;
                var snapshot = (uint[])EnemyKit.ClearPickupsAndDraw(g).P.Clone();
                y.Parts!.Clear();
                Assert.True(snapshot.SequenceEqual(EnemyKit.ClearPickupsAndDraw(g).P), $"at ({y.X}, {y.Y}) the parts disagree with the body");
            }
    }

    // ---------------------------------------------------------------- the fight

    [Fact]
    public void The_first_node_scores_three_hundred_ends_the_bar_and_the_yoke_is_still_alive()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int i = 0; i < LiveWaitFrames && y.Mode != Live; i++) StepAlone(g, y);
        int before = g.Score;
        Shoot(g, y, "node-r", KillingDamage);
        StepAlone(g, y);
        Assert.Equal(before + NodeScore, g.Score);
        Assert.Contains(y, g.Enemies);
        Assert.Equal(Broken, y.Mode);
        Assert.Equal(PartState.Dead, PartOf(y, "node-r").State);
        Assert.Equal(0, g.Kills);
        // the bar is out: lying across the ship it kills nothing
        y.X = g.PX; y.Y = g.PY;
        Assert.False(Yoke.Touches(g, y));
    }

    [Fact]
    public void The_survivor_drifts_off_sideways_still_worth_three_hundred_and_the_second_node_kills_the_yoke()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int i = 0; i < OnFieldFrames; i++) StepAlone(g, y);
        Shoot(g, y, "node-l", KillingDamage);
        StepAlone(g, y);
        var r = PartOf(y, "node-r");
        double ox = r.OX;
        for (int i = 0; i < SurvivorDriftFrames; i++) StepAlone(g, y);
        Assert.True(r.OX - ox >= SurvivorMinDrift, $"the survivor moved {r.OX - ox} to the side");
        int before = g.Score;
        Shoot(g, y, "node-r", KillingDamage);
        StepAlone(g, y);
        Assert.DoesNotContain(y, g.Enemies);
        Assert.Equal(1, g.Kills);
        Assert.Equal(before + NodeScore, g.Score);
    }

    [Fact]
    public void A_survivor_that_drifts_off_the_field_leaves_without_a_kill()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int i = 0; i < OnFieldFrames; i++) StepAlone(g, y);
        Shoot(g, y, "node-l", KillingDamage);
        for (int i = 0; i < FallOutFrames && g.Enemies.Contains(y); i++) StepAlone(g, y);
        Assert.DoesNotContain(y, g.Enemies);
        Assert.Equal(0, g.Kills);
    }

    [Fact]
    public void A_bomb_hurts_both_open_nodes()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int i = 0; i < OnFieldFrames; i++) StepAlone(g, y);
        g.Input.BombPressed = true;
        g.Step();
        g.Enemies.RemoveAll(e => e != y);
        Assert.DoesNotContain(y, g.Enemies);
        Assert.Equal(1, g.Kills);
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_yokes_in_it()
    {
        (string, bool) Run(ulong seed)
        {
            var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtStage(YokeStage);
            bool seen = false;
            for (int f = 0; f < RunFrames; f++)
            {
                EnemyKit.BotFrame(g);
                g.Step();
                seen |= g.Enemies.Any(e => e.Kind == EnemyKind.Yoke);
            }
            return (string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}:{e.Mode}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}", seen);
        }
        var (a, seenA) = Run(RunSeed);
        var (b, _) = Run(RunSeed);
        Assert.Equal(a, b);
        output.WriteLine($"a yoke was seen in {RunFrames} frames of stage {YokeStage}: {seenA}");
    }

    // ---------------------------------------------------------------- voice

    [Fact]
    public void It_says_a_waiting_post_on_arriving()
    {
        var g = EnemyKit.Game();
        g.OfferDark(new DarkPost(DarknessKind.Rude, PostSeed, "grr you again"));
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int i = 0; i < VoiceWaitFrames && y.Text is null; i++) StepAlone(g, y);
        Assert.Equal("grr you again", y.Text);
        Assert.True(EnemySpeechBox.Speaks(y));
    }

    [Fact]
    public void With_no_post_waiting_it_says_its_own_line_under_its_name()
    {
        var g = EnemyKit.Game();
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int i = 0; i < VoiceWaitFrames && y.Text is null; i++) StepAlone(g, y);
        Assert.Equal("Stay in your lane.", y.Text);
        Assert.Equal("YOKE", y.Header);
    }

    [Fact]
    public void It_sounds_a_sting_on_arriving_a_beam_when_the_bar_goes_live_and_a_part_when_a_node_goes()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        var y = g.SpawnKind(EnemyKind.Yoke, CentreShare);
        for (int i = 0; i < LiveWaitFrames && y.Mode != Live; i++) StepAlone(g, y);
        Assert.Contains(SfxNames.Sting, played);
        Assert.Single(played, SfxNames.Beam);
        Shoot(g, y, "node-l", KillingDamage);
        StepAlone(g, y);
        Assert.Contains(SfxNames.Part, played);
        Assert.Single(played, SfxNames.Beam);
    }
}
