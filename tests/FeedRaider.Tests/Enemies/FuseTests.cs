using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Fuse (stage 10): it sinks to a row, plants, counts three beats of 40 frames and goes off, a disc of radius 26 that is lethal
/// for 6 frames and does 30 damage to what else is in it. Killed in time it fizzles. These tests give its machine, its warning (the
/// ring from the moment it plants, three pips going out with a tick, the ring white for the last beat), the blast and its reach,
/// its pixels, its fit on the four fields and the same run from the same seed.
/// </summary>
public sealed class FuseTests
{
    /// <summary>The smallest of <see cref="EnemyKit.FieldSizes"/>; the spots below are on the largest.</summary>
    private const int SmallCols = 30, SmallRows = 16;

    /// <summary>Its score, hit points and drawing size in the enemy table.</summary>
    private const int TableScore = 900, TableHp = 7, BodyW = 9, BodyH = 9;

    /// <summary>The fuse's modes: sinking to its row, then planted and counting.</summary>
    private const int SinkingMode = 0, PlantedMode = 1;

    /// <summary>Decimal places two positions or hit points must agree to.</summary>
    private const int Places = 9;

    /// <summary>The stage the fuse first comes in on.</summary>
    private const int FirstStage = 10;

    /// <summary>Thirty seconds of play, long enough for a fuse to come in, count and go off.</summary>
    private const int RunFrames = 30 * Tuning.Fps;

    /// <summary>Frames after which the ship's start-of-life cover has run out, with a margin.</summary>
    private const int CoverGone = Tuning.InvulnFrames + 25;

    /// <summary>The blast's radius, and the radius the warning ring is drawn at.</summary>
    private const double Reach = 26;

    /// <summary>The count: three beats, then the blast, lethal for its frames.</summary>
    private const int Beat = 40, Count = 3 * Beat, BlastFrames = 6;

    /// <summary>The planting row as a share of the field's height, and how far it sinks a frame until it gets there.</summary>
    private const double PlantRow = 0.3, SinkSpeed = 0.6;

    /// <summary>What the blast does to an open part in the disc.</summary>
    private const double BlastDamage = 30;

    /// <summary>The frame of the count a fuse is shot dead on: in the last beat, before the blast.</summary>
    private const int ShotInTheLastBeat = 100;

    /// <summary>A shot far heavier than any fuse's hit points.</summary>
    private const double KillingShot = 1000;

    /// <summary>The hit points of the shell and the pod of a made enemy (<see cref="Parted"/>).</summary>
    private const double ShellHp = 30, PodHp = 100;

    /// <summary>A pip is a two by two square of gold.</summary>
    private const int PipPixels = 4;

    /// <summary>Cooldowns long enough that a placed enemy never sidesteps or shoots in a test.</summary>
    private const int NeverEvades = 9999, NeverFires = 99999;

    /// <summary>A spot near the middle of the largest field with room for the whole disc around it.</summary>
    private const double SpotX = 64, SpotY = 28;

    /// <summary>Within half a frame's movement of the spot, the ship is as close to it as walking gets.</summary>
    private const double ShipHalfStep = Tuning.ShipSpeed / 2;

    /// <summary>Frames the walk may take before the test gives up on reaching the spot.</summary>
    private const int MaxWalkFrames = 600;

    private static EnemyBehaviour Fuse => RaiderGame.TableBehaviour(EnemyKind.Fuse)!;

    public static IEnumerable<object[]> Fields() => EnemyKit.FieldSizes.Select(f => new object[] { f.Cols, f.Rows });

    /// <summary>The hits the ship has taken: the shield takes the first (it has one charge), then a hit kills.</summary>
    private static int Taken(RaiderGame g) => Tuning.ShieldHits - g.Shield + g.Deaths;

    /// <summary>Walks the ship to a spot, lets go of the keys, and steps on until its start-of-life cover has run out.</summary>
    private static void MoveShipAndWaitOutCover(RaiderGame g, double x, double y)
    {
        for (int i = 0; i < MaxWalkFrames && (Math.Abs(g.PX - x) > ShipHalfStep || Math.Abs(g.PY - y) > ShipHalfStep); i++)
        {
            g.Input.Key(GameKey.Left, g.PX > x + ShipHalfStep);
            g.Input.Key(GameKey.Right, g.PX < x - ShipHalfStep);
            g.Input.Key(GameKey.Up, g.PY > y + ShipHalfStep);
            g.Input.Key(GameKey.Down, g.PY < y - ShipHalfStep);
            EnemyKit.StepSweeping(g);
        }
        foreach (var k in new[] { GameKey.Left, GameKey.Right, GameKey.Up, GameKey.Down }) g.Input.Key(k, false);
        EnemyKit.StepSweeping(g, CoverGone);
        Assert.True(Math.Abs(g.PX - x) <= 1 && Math.Abs(g.PY - y) <= 1, $"the ship is at {g.PX:0.0},{g.PY:0.0}, not {x:0.0},{y:0.0}");
    }

    /// <summary>A fuse put down at a spot on or below its row, so it plants on the first step; returns it planted (<c>ModeAge</c> 0).</summary>
    private static Enemy Planted(RaiderGame g, double x, double y)
    {
        var f = EnemyKit.Place(g, EnemyKind.Fuse, x, y);
        f.VY = 0; f.Amp = 0; f.EvadeCd = NeverEvades;
        EnemyKit.StepSweeping(g);
        Assert.Equal(PlantedMode, f.Mode);
        Assert.Equal(0, f.ModeAge);
        return f;
    }

    /// <summary>An enemy of the given kind made of an armoured shell over an open pod, the pod the only vital part.</summary>
    private static (Part Shell, Part Pod, Enemy E) Parted(RaiderGame g, EnemyKind kind, double x, double y)
    {
        const int ShellAbove = 6, ShellHalfW = 8, ShellHalfH = 3, PodBelow = 4, PodHalfSide = 3;
        var shell = EnemyKit.MakePart("shell", 0, -ShellAbove, ShellHalfW, ShellHalfH, ShellHp, PartState.Armoured, vital: false);
        var pod = EnemyKit.MakePart("pod", 0, PodBelow, PodHalfSide, PodHalfSide, PodHp);
        return (shell, pod, g.SpawnParts(kind, x, y, shell, pod));
    }

    /// <summary>Holds a placed enemy where it is: it neither drifts, sidesteps nor shoots.</summary>
    private static Enemy Still(Enemy e)
    {
        e.VY = 0; e.Amp = 0; e.EvadeCd = NeverEvades; e.FireCd = NeverFires;
        return e;
    }

    private static (int X, int Y) RingPoint(Enemy f, int degrees)
    {
        double radians = double.DegreesToRadians(degrees);
        return ((int)Math.Round(f.X + Reach * Math.Cos(radians)), (int)Math.Round(f.Y + Reach * Math.Sin(radians)));
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Fuse, Fuse.Kind);
        Assert.Equal("FUSE", Fuse.Name);
        Assert.Equal(TableScore, Fuse.Score);
        Assert.Equal(TableHp, Fuse.Hp);
        Assert.False(Fuse.Boss);
        Assert.Equal(Palette.Crimson, Fuse.Colour);
        Assert.Equal(BodyW, Fuse.Body.W);
        Assert.Equal(BodyH, Fuse.Body.H);
        Assert.True(Fuse.Name.Length <= EnemySpeechBox.MaxLabel);
    }

    [Fact]
    public void Its_hit_points_follow_the_ramp_like_any_other_kind_and_stage_ten_has_it_in_its_unlocks()
    {
        var g = EnemyKit.Game();
        g.StartAtStage(FirstStage);
        var f = g.SpawnKind(EnemyKind.Fuse, 0.5);
        Assert.Equal(Fuse.Hp * Ramp.HpMul(FirstStage), f.Hp, Places);
        Assert.Contains(EnemyKind.Fuse, Ramp.Unlocks(FirstStage));
    }

    // ---------------------------------------------------------------- the machine

    [Theory, MemberData(nameof(Fields))]
    public void It_sinks_at_point_six_plants_on_its_row_and_goes_off_a_hundred_and_twenty_frames_later(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        const double ShipBelowFuse = 12;   // inside the ring, clear of the body
        const int MaxSinkFrames = 500;
        double target = PlantRow * g.PH;
        MoveShipAndWaitOutCover(g, g.PW / 2.0, target + ShipBelowFuse);
        var f = g.SpawnKind(EnemyKind.Fuse, 0.5);
        Assert.True(f.Y < 0, "it comes in from above");
        double was = f.Y;
        int sank = 0;
        for (; f.Mode == SinkingMode && sank < MaxSinkFrames; sank++)
        {
            EnemyKit.StepSweeping(g);
            if (f.Mode == SinkingMode) Assert.Equal(was + SinkSpeed, f.Y, Places);
            was = f.Y;
        }
        Assert.Equal(PlantedMode, f.Mode);
        Assert.Equal(0, f.ModeAge);
        Assert.Equal(target, f.Y, Places);
        Assert.Equal(g.PW / 2.0, f.X, Places);
        Assert.Equal(0, Taken(g));

        for (int k = 1; k < Count + BlastFrames; k++)
        {
            EnemyKit.StepSweeping(g);
            Assert.Contains(f, g.Enemies);
            Assert.Equal(k, f.ModeAge);
            if (k < Count) Assert.Equal(target, f.Y, Places);     // after the blast the ship's shield shoves what is near it
            if (k < Count) { Assert.False(Fuse.Touches(g, f), $"step {k}: lethal before the count ends"); Assert.Equal(0, Taken(g)); }
            else { Assert.True(Fuse.Touches(g, f), $"step {k}: the blast is lethal for {BlastFrames} frames"); Assert.Equal(1, Taken(g)); }
        }
        EnemyKit.StepSweeping(g);                                       // the blast is over: gone, with no score and no kill
        Assert.DoesNotContain(f, g.Enemies);
        Assert.Equal(0, g.Kills);
        Assert.Equal(0, g.Score);
        Assert.Equal(1, Taken(g));                      // the ship in its disc was the only thing it hurt (its shield took it)
    }

    [Theory, MemberData(nameof(Fields))]
    public void It_shoots_nothing_and_stays_inside_the_field_through_the_count(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        const int MaxFrames = 400;   // enough to sink, count and blast on the tallest field
        var f = g.SpawnKind(EnemyKind.Fuse, 0.5);
        for (int k = 0; k < MaxFrames && g.Enemies.Contains(f); k++)
        {
            EnemyKit.StepSweeping(g);
            Assert.DoesNotContain(g.Bullets, b => !b.Friendly);
            if (!g.Enemies.Contains(f)) break;
            Assert.True(EnemyKit.InsideWidth(g, f), $"frame {k}: x {f.X}");
            Assert.True(EnemyKit.LowestPixel(f) <= g.PH, $"frame {k}: lowest pixel {EnemyKit.LowestPixel(f)}");
        }
        Assert.DoesNotContain(f, g.Enemies);
    }

    // ---------------------------------------------------------------- the warning

    [Theory]
    [InlineData(Reach - 1, true)]
    [InlineData(Reach + 1, false)]
    public void The_blast_is_a_disc_of_radius_twenty_six(double distance, bool hurt)
    {
        var g = EnemyKit.Game(EnemyKit.BigCols, EnemyKit.BigRows);
        EnemyKit.StepSweeping(g, CoverGone);
        var f = Planted(g, g.PX + distance, g.PY);
        EnemyKit.StepSweeping(g, Count - 1);
        Assert.False(Fuse.Touches(g, f));
        Assert.Equal(0, Taken(g));
        EnemyKit.StepSweeping(g);
        Assert.Equal(hurt, Fuse.Touches(g, f));
        EnemyKit.StepSweeping(g, BlastFrames - 1);
        Assert.Equal(hurt, Fuse.Touches(g, f));
        Assert.Equal(hurt ? 1 : 0, Taken(g));
    }

    [Theory]
    [InlineData(SmallCols, SmallRows)]
    [InlineData(EnemyKit.BigCols, EnemyKit.BigRows)]
    public void A_ship_that_stands_still_through_the_count_and_then_walks_twenty_frames_out_lives(int cols, int rows)
    {
        const double FuseAboveShip = 8;
        const int WalkFrames = 20;
        int Go(int walkFrom)
        {
            var g = EnemyKit.Game(cols, rows);
            EnemyKit.StepSweeping(g, CoverGone);
            double px = g.PX, py = g.PY;
            Planted(g, px, py - FuseAboveShip);
            var side = px < g.PW / 2.0 ? GameKey.Right : GameKey.Left;
            for (int k = 1; k <= Count + BlastFrames; k++)
            {
                g.Input.Key(side, k > walkFrom);
                EnemyKit.StepSweeping(g);
            }
            g.Input.Key(side, false);
            return Taken(g);
        }
        Assert.Equal(0, Go(Count - WalkFrames));      // out of the ring in time
        Assert.Equal(1, Go(Count + BlastFrames - 1));     // a ship that never moved is hit
    }

    [Fact]
    public void Its_ring_is_drawn_from_the_moment_it_plants_and_is_white_for_the_last_beat()
    {
        var g = EnemyKit.Game(EnemyKit.BigCols, EnemyKit.BigRows);
        var f = Planted(g, SpotX, SpotY);
        var angles = new[] { 0, 45, 90, 135, 180 };
        const int MidLastBeat = Count - 10;
        foreach (var (age, colour) in new[] { (1, Palette.Crimson), (Beat - 1, Palette.Crimson), (Beat, Palette.Crimson), (2 * Beat - 1, Palette.Crimson), (2 * Beat, Palette.White), (MidLastBeat, Palette.White), (Count - 1, Palette.White) })
        {
            while (f.ModeAge < age) EnemyKit.StepSweeping(g);
            var field = EnemyKit.ClearPickupsAndDraw(g);
            foreach (int a in angles)
            {
                var (x, y) = RingPoint(f, a);
                Assert.Equal(colour, field.Get(x, y) & EnemyKit.RgbMask);
            }
        }
    }

    [Fact]
    public void Three_pips_go_out_one_at_a_time_on_the_beats_with_a_tick_of_rising_pitch()
    {
        var g = EnemyKit.Game(EnemyKit.BigCols, EnemyKit.BigRows);
        var sounds = new List<(string Name, int Step, int At)>();
        int at = 0;
        g.Sfx = new SfxBus((name, _, step, _) => sounds.Add((name, step, at)));
        var f = Planted(g, SpotX, SpotY);
        // the box the pips are counted in: a row of three above the body, with room to spare on every side
        const int BoxTop = 11, BoxBottom = 6, BoxHalfWidth = 7;
        const int EarlyInFirstBeat = 10;
        int Pips()
        {
            var field = EnemyKit.ClearPickupsAndDraw(g);
            int n = 0;
            for (int y = (int)f.Y - BoxTop; y <= (int)f.Y - BoxBottom; y++)
                for (int x = (int)f.X - BoxHalfWidth; x <= (int)f.X + BoxHalfWidth; x++)
                    if ((field.Get(x, y) & EnemyKit.RgbMask) == Palette.Gold) n++;
            return n;
        }
        var lit = new Dictionary<int, int>();
        for (at = 1; at < Count; at++)
        {
            EnemyKit.StepSweeping(g);
            if (at is EarlyInFirstBeat or Beat - 1 or Beat + 1 or 2 * Beat - 1 or 2 * Beat + 1 or Count - 1) lit[at] = Pips();
        }
        Assert.Equal(3 * PipPixels, lit[EarlyInFirstBeat]);
        Assert.Equal(3 * PipPixels, lit[Beat - 1]);
        Assert.Equal(2 * PipPixels, lit[Beat + 1]);
        Assert.Equal(2 * PipPixels, lit[2 * Beat - 1]);
        Assert.Equal(PipPixels, lit[2 * Beat + 1]);
        Assert.Equal(PipPixels, lit[Count - 1]);
        at = Count;
        EnemyKit.StepSweeping(g);
        var ticks = sounds.Where(s => s.Name == SfxNames.Tick).ToList();
        Assert.Equal(new[] { Beat, 2 * Beat, Count }, ticks.Select(t => t.At).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, ticks.Select(t => t.Step).ToArray());
        Assert.Equal(new[] { Count }, sounds.Where(s => s.Name == SfxNames.Blast).Select(s => s.At).ToArray());
    }

    [Fact]
    public void While_the_blast_is_lethal_the_disc_is_shown_and_after_it_the_fuse_is_gone()
    {
        var g = EnemyKit.Game(EnemyKit.BigCols, EnemyKit.BigRows);
        var f = Planted(g, SpotX, SpotY);
        // a six-pixel box 6 to 12 across and 12 to 18 down from the centre: inside the disc, clear of the body and the ring
        const int BoxRightOfCentre = 6, BoxBelowCentre = 12, BoxSide = 6;
        const int BoxLeft = (int)SpotX + BoxRightOfCentre, BoxTop = (int)SpotY + BoxBelowCentre;
        const int DiscWhitesAtLeast = 12;   // two thirds of the 18 a white checker lights in the box
        const int BeforeTheBlast = Count - 2, InTheBlast = Count + 1;
        EnemyKit.StepSweeping(g, BeforeTheBlast);
        int Whites()
        {
            var field = EnemyKit.ClearPickupsAndDraw(g);
            int n = 0;
            for (int y = BoxTop; y < BoxTop + BoxSide; y++) for (int x = BoxLeft; x < BoxLeft + BoxSide; x++) if ((field.Get(x, y) & EnemyKit.RgbMask) == Palette.White) n++;
            return n;
        }
        Assert.Equal(0, Whites());
        EnemyKit.StepSweeping(g, InTheBlast - BeforeTheBlast);
        Assert.Equal(InTheBlast, f.ModeAge);
        Assert.True(Whites() >= DiscWhitesAtLeast, "the disc is on show");
        EnemyKit.StepSweeping(g, BlastFrames);
        Assert.DoesNotContain(f, g.Enemies);
        Assert.Equal(0, Whites());
    }

    // ---------------------------------------------------------------- killed in time

    [Fact]
    public void Killed_in_time_it_does_nothing_and_scores_nine_hundred()
    {
        var g = EnemyKit.Game(EnemyKit.BigCols, EnemyKit.BigRows);
        EnemyKit.StepSweeping(g, CoverGone);
        int before = g.Score;
        var sounds = new List<string>();
        g.Sfx = new SfxBus((name, _, _, _) => sounds.Add(name));
        const double FuseAboveShip = 12;
        var f = Planted(g, g.PX, g.PY - FuseAboveShip);
        EnemyKit.StepSweeping(g, ShotInTheLastBeat);
        EnemyKit.Shot(g, f.X, f.Y, KillingShot);
        EnemyKit.StepSweeping(g);
        Assert.DoesNotContain(f, g.Enemies);
        Assert.Equal(1, g.Kills);
        Assert.Equal(before + Fuse.Score, g.Score);
        sounds.Clear();
        for (int k = 0; k < Count - ShotInTheLastBeat + Beat; k++)
        {
            EnemyKit.StepSweeping(g);
            Assert.False(Fuse.Touches(g, f));
        }
        Assert.Equal(0, Taken(g));
        Assert.DoesNotContain(SfxNames.Blast, sounds);
        Assert.DoesNotContain(SfxNames.Tick, sounds);
        Assert.DoesNotContain(g.Bullets, b => !b.Friendly);
    }

    [Fact]
    public void Killed_in_time_it_leaves_other_enemies_unhurt_and_the_ring_is_not_drawn()
    {
        var g = EnemyKit.Game(EnemyKit.BigCols, EnemyKit.BigRows);
        const double PlainBelow = 12, PartedBelow = 16;   // both well inside the reach
        var by = EnemyKit.Place(g, EnemyKind.Yoke, SpotX, SpotY + PlainBelow);
        var (_, pod, near) = Parted(g, EnemyKind.Yoke, SpotX, SpotY + PartedBelow);
        var f = Planted(g, SpotX, SpotY);
        EnemyKit.StepSweeping(g, ShotInTheLastBeat);
        EnemyKit.Shot(g, f.X, f.Y, KillingShot);
        const int PastTheBlast = 30;   // by then the count and the blast it would have made are both over
        EnemyKit.StepSweeping(g, PastTheBlast);
        Assert.DoesNotContain(f, g.Enemies);
        Assert.Equal(PodHp, pod.Hp, Places);
        var field = EnemyKit.ClearPickupsAndDraw(g);
        const int StraightDown = 90;
        var (x, y) = RingPoint(f, StraightDown);
        Assert.NotEqual(Palette.Crimson, field.Get(x, y) & EnemyKit.RgbMask);
        Assert.NotEqual(Palette.White, field.Get(x, y) & EnemyKit.RgbMask);
        Assert.Contains(near, g.Enemies);
        Assert.True(by.Hp > 0);
    }

    // ---------------------------------------------------------------- what it does to others

    [Fact]
    public void Its_blast_does_thirty_damage_to_other_enemies_in_the_disc_to_open_parts_only_and_nothing_beyond()
    {
        var g = EnemyKit.Game(EnemyKit.BigCols, EnemyKit.BigRows);
        EnemyKit.StepSweeping(g, CoverGone);
        const double InsideAcross = 14, PartedBelow = 12;   // well inside the reach
        const double OutsideAcross = 40;                    // well outside it
        const double GruntAcross = 10, GruntBelow = 10;     // a plain enemy in the disc
        const double FarAcross = 60;                        // and one out of it
        var (shellIn, podIn, inside) = Parted(g, EnemyKind.Bulwark, SpotX - InsideAcross, SpotY + PartedBelow);
        var (_, podOut, outside) = Parted(g, EnemyKind.Bulwark, SpotX + OutsideAcross, SpotY + PartedBelow);
        var f = Planted(g, SpotX, SpotY);
        var grunt = Still(EnemyKit.Place(g, EnemyKind.Grunt, SpotX + GruntAcross, SpotY + GruntBelow));
        var far = Still(EnemyKit.Place(g, EnemyKind.Grunt, SpotX - FarAcross, SpotY));
        double farHp = far.Hp;
        var keep = new HashSet<Enemy> { f, inside, outside, grunt, far };
        void StepKeeping(int frames)
        {
            for (int k = 0; k < frames; k++) { g.Step(); g.Enemies.RemoveAll(e => !keep.Contains(e)); }
        }
        StepKeeping(Count - 1);
        Assert.Equal(PodHp, podIn.Hp, Places);
        Assert.Contains(grunt, g.Enemies);
        const int IntoTheBlast = 3;
        StepKeeping(IntoTheBlast);
        Assert.Equal(PodHp - BlastDamage, podIn.Hp, Places);  // an open part
        Assert.Equal(ShellHp, shellIn.Hp, Places);           // armour: none
        Assert.Equal(PodHp, podOut.Hp, Places);              // beyond the reach: none
        Assert.DoesNotContain(grunt, g.Enemies);        // thirty is far more than a grunt has
        Assert.Contains(far, g.Enemies);
        Assert.Equal(farHp, far.Hp, Places);
        Assert.Equal(1, g.Kills);
    }

    // ---------------------------------------------------------------- pixels

    [Fact]
    public void It_is_drawn_crimson_with_a_gold_core_a_gold_spark_and_a_plum_outline()
    {
        var g = EnemyKit.Game();
        // cells of the drawing, as (column, row) from its top left corner
        (int X, int Y) core = (4, 4), spark = (4, 0), leftOfCore = (1, 3), belowCore = (4, 6), leftEdge = (0, 3), topEdge = (4, 1);
        var f = Planted(g, SpotX, SpotY);
        var field = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(f.X - f.Sprite.W / 2.0), y0 = (int)Math.Round(f.Y - f.Sprite.H / 2.0);
        uint At((int X, int Y) cell) => field.Get(x0 + cell.X, y0 + cell.Y) & EnemyKit.RgbMask;
        Assert.Equal(Palette.Gold, At(core));
        Assert.Equal(Palette.Gold, At(spark));
        Assert.Equal(Palette.Crimson, At(leftOfCore));
        Assert.Equal(Palette.Crimson, At(belowCore));
        Assert.Equal(Palette.Plum, At(leftEdge));
        Assert.Equal(Palette.Plum, At(topEdge));
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_fuses_in_it()
    {
        const ulong Seed = 11;
        const double LeftOfMiddle = 0.4;
        (string, bool) Go(ulong seed)
        {
            var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtStage(FirstStage);
            g.SpawnKind(EnemyKind.Fuse, LeftOfMiddle);
            bool seen = false;
            for (int f = 0; f < RunFrames; f++)
            {
                EnemyKit.BotFrame(g);
                g.Step();
                seen |= g.Enemies.Any(e => e.Kind == EnemyKind.Fuse);
            }
            var state = string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}:{e.Mode}:{e.ModeAge}")) + $"#{g.Score}:{g.Kills}:{Taken(g)}:{g.PX:0.0}";
            return (state, seen);
        }
        var (a, seenA) = Go(Seed);
        var (b, _) = Go(Seed);
        Assert.Equal(a, b);
        Assert.True(seenA);
    }
}
