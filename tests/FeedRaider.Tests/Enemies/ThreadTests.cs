using System.Runtime.CompilerServices;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Thread (boss 4): a head and six cars that run lane by lane across the field. Only the last car can be hurt, and each one
/// that goes opens the one before; with no car left the head opens and shoots. These tests give its numbers, its parts and what
/// may be hurt, its lanes on the four fields, the chain that keeps its parts 8 pixels apart round a turn, its warnings, its voice
/// (the page count in the header, the longest post first, short pages, a slow pace) and the fight (a bot at stage 4's weapon level).
/// </summary>
public sealed class ThreadTests(ITestOutputHelper output)
{
    /// <summary>The colour of the dotted row that warns of the next lane.</summary>
    private const uint Pale = 0xc9a0ff;
    private const int Cars = 6;

    /// <summary>Its name: the header of its speech box, and the boss's name while it fights.</summary>
    private const string ThreadName = "THREAD";

    /// <summary>The stage whose gate brings the Thread; an arrival there is tougher than at stage 1.</summary>
    private const int ThreadStage = 4;

    /// <summary>The head's and a car's sprite sides; a car's hit box reaches half its side from its centre.</summary>
    private const int HeadSize = 9, CarSize = 7, CarHalf = CarSize / 2;
    private const double HeadHalf = HeadSize / 2.0;

    private const double HeadHp = 12, CarHp = 3, TrainHp = HeadHp + Cars * CarHp;
    private const int CarScore = 300, BossScore = 5000;

    /// <summary>The train's pace with every car, what each lost car adds, and the pace of the head alone; the speeds of a dropped
    /// bolt and of the head's aimed one.</summary>
    private const double CarSpeed = 0.9, CarSpeedPerLoss = 0.08, AloneSpeed = 1.4, DropSpeed = 0.9, AloneBoltSpeed = 1.2;

    /// <summary>The distance between neighbouring parts, and how far from the side edge the head's leading edge turns.</summary>
    private const double Gap = 8, TurnMargin = 6;

    /// <summary>Frames parked off the field between passes, frames between the head's bolts and frames its visor blinks before each one.</summary>
    private const int ParkFrames = 40, FireEvery = 45, BlinkFrames = 24;

    /// <summary>A visor is a 3 by 3 block: a pupil in the middle and a ring of eight round it.</summary>
    private const int VisorSide = 3, VisorRingPixels = VisorSide * VisorSide - 1;

    /// <summary>Damage that kills any part with one shot; damage a shot carries to show an armoured part shrugs it off; damage that
    /// hurts the open car without killing it.</summary>
    private const double KillingDamage = 1000, ArmourProbeDamage = 5, ChipDamage = 2;

    /// <summary>The ship's row in the tests that place it, below every lane; a column out of the way at the left, a column a car
    /// passes over in each lane, and a column the head aims at.</summary>
    private const double ShipY = 80, ShipAsideX = 10, ShipUnderTrainX = 30, ShipTargetX = 90;

    /// <summary>Long enough for the train to run its lanes once, and long enough for it to park and come back.</summary>
    private const int FullPassFrames = 60 * Tuning.Fps, ParkAndReturnFrames = 80 * Tuning.Fps;

    /// <summary>Frames for a kill, a speed change or the speech box to settle.</summary>
    private const int SettleFrames = 4;

    /// <summary>Tolerances: a row or a gap read off the trace, a value that must match to rounding, a pace, and the decimal places a
    /// hit point is compared to.</summary>
    private const double RowTolerance = 0.01, ExactTolerance = 1e-9, SpeedTolerance = 1e-6;
    private const int HpPlaces = 6;

    private static EnemyBehaviour Thread => RaiderGame.TableBehaviour(EnemyKind.Thread)!;

    /// <summary>A field with nothing on it. Stage 1 by default, where an arrival's hit points are the kind's own (a gate boss never scales).</summary>
    private static RaiderGame FieldGame(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows, ulong seed = EnemyKit.PlainSeed, int stage = 1)
    {
        var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtStage(stage);
        EnemyKit.Clear(g);
        return g;
    }

    /// <summary>Where across the field the Thread is asked to arrive, as a share of the width: the middle.</summary>
    private const double SpawnAcross = 0.5;

    private static Enemy MakeThread(RaiderGame g) => g.SpawnKind(EnemyKind.Thread, SpawnAcross);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PX")]
    private static extern void SetPX(RaiderGame g, double value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PY")]
    private static extern void SetPY(RaiderGame g, double value);

    private static void SetShipPosition(RaiderGame g, double x, double y)
    {
        SetPX(g, x);
        SetPY(g, y);
    }

    /// <summary>One frame, then every enemy but the one a test keeps is swept away (a stage's own waves).</summary>
    private static void StepKeepingEnemy(RaiderGame g, Enemy keep)
    {
        g.Step();
        g.Enemies.RemoveAll(e => !ReferenceEquals(e, keep));
    }

    /// <summary>One frame as <see cref="StepKeepingEnemy"/>, then every bolt is removed too, because a bolt a car drops
    /// is not what most tests are about.</summary>
    private static void StepKeepingEnemyClearingHostileBolts(RaiderGame g, Enemy keep)
    {
        StepKeepingEnemy(g, keep);
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    /// <summary>A part counts as on the field once its centre is a car's half-width in from the left edge.</summary>
    private const double OnFieldX = CarHalf;
    private const int ArriveFramesCap = 10 * Tuning.Fps;

    /// <summary>The train comes in from the left edge one part at a time; this runs it until every part is on the field.</summary>
    private static void Arrive(RaiderGame g, Enemy e)
    {
        for (int f = 0; f < ArriveFramesCap && e.Parts!.Any(p => p.State != PartState.Dead && e.X + p.OX < OnFieldX); f++)
        {
            SetShipPosition(g, ShipAsideX, ShipY);
            StepKeepingEnemyClearingHostileBolts(g, e);
        }
        Assert.All(Alive(e), p => Assert.True(e.X + p.OX >= OnFieldX, p.Name + " is on the field"));
    }

    /// <summary>The head's part name, and the start of each car's: "car1" is the car behind the head.</summary>
    private const string HeadPart = "head", CarPart = "car";

    private static Part Car(Enemy e, int n) => e.Parts!.Single(p => p.Name == CarPart + n);
    private static Part Head(Enemy e) => e.Parts!.Single(p => p.Name == HeadPart);
    private static IEnumerable<Part> Alive(Enemy e) => e.Parts!.Where(p => p.State != PartState.Dead);

    /// <summary>The first lane's row is this share of the field's height, but never higher than this row; each lane is this far under the one before.</summary>
    private const double FirstLaneShare = 0.12, FirstLaneMin = 10, LaneSpacing = 11;

    private static double FirstLane(RaiderGame g) => Math.Max(FirstLaneMin, FirstLaneShare * g.PH);

    /// <summary>Shoots the last car dead with one heavy shot, and runs the frame the shot lands in.</summary>
    private static void KillLastCar(RaiderGame g, Enemy e)
    {
        var last = e.Parts!.Last(p => p.Name.StartsWith(CarPart) && p.State != PartState.Dead);
        EnemyKit.Shot(g, e.X + last.OX, e.Y + last.OY, KillingDamage);
        StepKeepingEnemyClearingHostileBolts(g, e);
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Thread, Thread.Kind);
        Assert.Equal(ThreadName, Thread.Name);
        Assert.True(Thread.Boss);
        Assert.True(Thread.Speaks);
        Assert.Equal(BossScore, Thread.Score);
        Assert.Equal(TrainHp, Thread.Hp);
        Assert.Equal(HeadSize, Thread.Body.W);
        Assert.Equal(HeadSize, Thread.Body.H);
        Assert.True(Thread.Name.Length <= EnemySpeechBox.MaxLabel);
    }

    [Fact]
    public void It_is_a_head_of_twelve_and_six_cars_of_three()
    {
        var g = FieldGame();
        var e = MakeThread(g);
        Assert.Equal(1 + Cars, e.Parts!.Count);
        Assert.Equal(HeadSize, Head(e).HW * 2 + 1);
        Assert.Equal(HeadHp, Head(e).Hp);
        Assert.True(Head(e).Vital);
        for (int n = 1; n <= Cars; n++)
        {
            var c = Car(e, n);
            Assert.Equal(CarHp, c.Hp);
            Assert.True(c.Vital);
            Assert.Equal(CarScore, c.Score);
            Assert.Equal(CarHalf, c.HW);
            Assert.Null(c.Wreck);
            Assert.Equal(CarSize, c.Closed.W);
            Assert.Equal(CarSize, c.Closed.H);
        }
        Assert.Equal(TrainHp, e.Hp);
        Assert.Equal(TrainHp, e.MaxHp);
    }

    [Fact]
    public void Parked_off_the_field_every_part_sits_on_the_trains_own_position()
    {
        var g = FieldGame();
        var e = MakeThread(g);
        bool parked = false;
        Run(g, e, ParkAndReturnFrames, _ =>
        {
            if (!e.Untouchable || parked) return;
            parked = true;
            Assert.All(e.Parts!, p => { Assert.Equal(0, p.OX); Assert.Equal(0, p.OY); });
        });
        Assert.True(parked, "the train parked");
    }

    [Fact]
    public void Only_the_last_car_is_open_and_the_head_stays_shut_while_a_car_is_left()
    {
        var g = FieldGame();
        var e = MakeThread(g);
        Assert.Equal(PartState.Armoured, Head(e).State);
        for (int n = 1; n < Cars; n++) Assert.Equal(PartState.Armoured, Car(e, n).State);
        Assert.Equal(PartState.Open, Car(e, Cars).State);
    }

    [Fact]
    public void A_boss_made_for_a_tougher_lap_scales_every_part_alike()
    {
        var g = FieldGame(stage: ThreadStage);
        var e = MakeThread(g);
        double k = e.Hp / TrainHp;
        Assert.True(k > 1);
        Assert.Equal(HeadHp * k, Head(e).Hp, HpPlaces);
        Assert.Equal(CarHp * k, Car(e, 3).Hp, HpPlaces);
        Assert.Equal(e.Hp, e.Parts!.Where(p => p.Vital).Sum(p => p.Hp), HpPlaces);
    }

    // ---------------------------------------------------------------- what takes damage

    [Fact]
    public void Only_the_last_car_takes_damage_and_a_shot_on_any_other_part_pings_off()
    {
        var g = FieldGame();
        var e = MakeThread(g);
        Arrive(g, e);
        SetShipPosition(g, ShipAsideX, ShipY);
        double before = e.Hp;
        foreach (var p in new[] { Head(e), Car(e, 1), Car(e, 3), Car(e, 5) })
        {
            var b = EnemyKit.Shot(g, e.X + p.OX, e.Y + p.OY, ArmourProbeDamage);
            StepKeepingEnemyClearingHostileBolts(g, e);
            Assert.DoesNotContain(b, g.Bullets);
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.Equal(before, e.Hp);
        }
        var last = Car(e, Cars);
        EnemyKit.Shot(g, e.X + last.OX, e.Y + last.OY, ChipDamage);
        StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.Equal(CarHp - ChipDamage, last.Hp, HpPlaces);
        Assert.Equal(before - ChipDamage, e.Hp, HpPlaces);
    }

    [Fact]
    public void Each_car_that_goes_opens_the_one_before_and_lets_shots_through()
    {
        var g = FieldGame();
        var e = MakeThread(g);
        Arrive(g, e);
        SetShipPosition(g, ShipAsideX, ShipY);
        KillLastCar(g, e);
        Assert.Equal(PartState.Dead, Car(e, Cars).State);
        Assert.Equal(PartState.Open, Car(e, Cars - 1).State);
        Assert.Equal(PartState.Armoured, Car(e, Cars - 2).State);
        Assert.Equal(PartState.Armoured, Head(e).State);
        var gone = Car(e, Cars);
        var b = EnemyKit.Shot(g, e.X + gone.OX, e.Y + gone.OY, ArmourProbeDamage);
        StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.Contains(b, g.Bullets);
        Assert.Equal(CarScore, g.Score);
    }

    [Fact]
    public void With_no_car_left_the_head_opens_and_the_visor_turns_crimson()
    {
        var g = FieldGame();
        var e = MakeThread(g);
        Arrive(g, e);
        SetShipPosition(g, ShipAsideX, ShipY);
        StepKeepingEnemyClearingHostileBolts(g, e);
        var shut = EnemyKit.ClearPickupsAndDraw(g);
        Assert.True(CountRoundHeadCentre(shut, e, Palette.Shield) >= VisorRingPixels, "a shut head has a blue visor");
        for (int i = 0; i < Cars; i++)
        {
            Assert.Equal(PartState.Armoured, Head(e).State);
            KillLastCar(g, e);
        }
        Assert.DoesNotContain(Alive(e), p => p.Name.StartsWith(CarPart));
        Assert.Equal(PartState.Open, Head(e).State);
        Assert.Equal(HeadHp, e.Hp, HpPlaces);
        var open = EnemyKit.ClearPickupsAndDraw(g);
        Assert.True(CountRoundHeadCentre(open, e, Palette.Crimson) >= VisorRingPixels, "an open head has a crimson visor");
        Assert.Equal(0, CountRoundHeadCentre(open, e, Palette.Shield));
        Assert.True(CountRoundHeadCentre(open, e, Palette.Gold) >= 1, "gold on crimson marks the part to shoot");
    }

    [Fact]
    public void The_head_takes_damage_once_open_and_the_kill_scores_five_thousand()
    {
        var g = FieldGame();
        var e = MakeThread(g);
        Arrive(g, e);
        SetShipPosition(g, ShipAsideX, ShipY);
        for (int i = 0; i < Cars; i++) KillLastCar(g, e);
        int before = g.Score;
        EnemyKit.Shot(g, e.X, e.Y, EnemyKit.PlainShotDamage);
        StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.Equal(HeadHp - EnemyKit.PlainShotDamage, e.Hp, HpPlaces);
        EnemyKit.Shot(g, e.X, e.Y, KillingDamage);
        StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(before + BossScore, g.Score);
    }

    /// <summary>How far the counted square reaches from the head's centre: over the visor, inside the head's outline.</summary>
    private const int HeadCentreReach = 3;

    /// <summary>The pixels of the given colour in the square round the head's centre.</summary>
    private static int CountRoundHeadCentre(Surface f, Enemy e, uint colour)
    {
        int n = 0, cx = (int)Math.Round(e.X), cy = (int)Math.Round(e.Y);
        for (int x = cx - HeadCentreReach; x <= cx + HeadCentreReach; x++)
            for (int y = cy - HeadCentreReach; y <= cy + HeadCentreReach; y++)
                if ((f.Get(x, y) & EnemyKit.RgbMask) == colour) n++;
        return n;
    }

    // ---------------------------------------------------------------- lanes and the chain

    /// <summary>How far above the field's bottom edge <see cref="Run"/> holds the ship.</summary>
    private const double RunShipAboveBottom = 14;

    /// <summary>Runs a thread and returns, per frame, a snapshot of where the head was and whether it could be hit.</summary>
    private static List<(double X, double Y, bool Hit)> Run(RaiderGame g, Enemy e, int frames, Action<int>? each = null)
    {
        var trace = new List<(double, double, bool)>();
        for (int f = 0; f < frames; f++)
        {
            SetShipPosition(g, g.PW / 2.0, g.PH - RunShipAboveBottom);
            StepKeepingEnemyClearingHostileBolts(g, e);
            trace.Add((e.X, e.Y, !e.Untouchable));
            each?.Invoke(f);
        }
        return trace;
    }

    /// <summary>A row the head holds this many frames running is a lane, not a step of a drop.</summary>
    private const int LaneHoldFrames = 20;

    /// <summary>The lanes the head ran, in the order it met them.</summary>
    private static List<double> Rows(List<(double X, double Y, bool Hit)> trace)
    {
        var rows = new List<double>();
        int run = 0;
        for (int i = 1; i < trace.Count; i++)
        {
            run = trace[i].Y >= 0 && Math.Abs(trace[i].Y - trace[i - 1].Y) < ExactTolerance ? run + 1 : 0;
            if (run == LaneHoldFrames && !rows.Any(r => Math.Abs(r - trace[i].Y) < RowTolerance)) rows.Add(trace[i].Y);
        }
        return rows;
    }

    [Theory]
    [InlineData(30, 16, 1)]
    [InlineData(48, 60, 3)]
    [InlineData(80, 24, 2)]
    [InlineData(120, 46, 3)]
    public void It_runs_three_lanes_on_a_tall_field_two_on_a_short_one_and_one_on_the_smallest(int cols, int rows, int lanes)
    {
        var g = FieldGame(cols, rows);
        var e = MakeThread(g);
        var seen = Rows(Run(g, e, FullPassFrames));
        var want = Enumerable.Range(0, lanes).Select(i => FirstLane(g) + LaneSpacing * i).ToList();
        Assert.Equal(want.Count, seen.Count);
        for (int i = 0; i < want.Count; i++) Assert.Equal(want[i], seen[i], RowTolerance);
    }

    [Theory]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void Its_parts_stay_eight_pixels_apart_all_the_way_round_a_turn(int cols, int rows)
    {
        var g = FieldGame(cols, rows);
        var e = MakeThread(g);
        // a frame the head moves down by more than this is a frame of a drop; more than a few such frames is a whole drop
        const double DropStepLeast = 0.1;
        const int DropFramesLeast = 5;
        double worst = 0;
        int dropFrames = 0;
        double lastY = double.NaN;
        Run(g, e, FullPassFrames, _ =>
        {
            var live = e.Parts!;
            for (int i = 1; i < live.Count && e.Y >= 0; i++)
            {
                double dx = live[i].OX - live[i - 1].OX, dy = live[i].OY - live[i - 1].OY;
                worst = Math.Max(worst, Math.Abs(Math.Sqrt(dx * dx + dy * dy) - Gap));
            }
            if (e.Y >= 0 && !double.IsNaN(lastY) && e.Y - lastY > DropStepLeast) dropFrames++;
            if (e.Y >= 0) lastY = e.Y;
        });
        Assert.True(dropFrames > DropFramesLeast, "the head dropped a lane at least once");
        Assert.True(worst < RowTolerance, $"parts drifted {worst:0.000} px from {Gap} apart");
    }

    [Fact]
    public void The_head_turns_with_its_leading_edge_six_pixels_from_the_far_edge()
    {
        // how far past the turning point, toward the edge, the head's leading edge may reach
        const double TurnSlack = 1.5;
        var g = FieldGame();
        var e = MakeThread(g);
        var trace = Run(g, e, FullPassFrames);
        double maxX = trace.Where(t => t.Y >= 0 && Math.Abs(t.Y - FirstLane(g)) < RowTolerance).Max(t => t.X);
        Assert.InRange(maxX + HeadHalf, g.PW - TurnMargin, g.PW - TurnMargin + TurnSlack);
        double minX = trace.Where(t => t.Y >= 0 && Math.Abs(t.Y - (FirstLane(g) + LaneSpacing)) < RowTolerance).Min(t => t.X);
        Assert.InRange(minX - HeadHalf, TurnMargin - TurnSlack, TurnMargin);
    }

    [Theory]
    [InlineData(30, 16)]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void It_never_comes_closer_to_the_ship_than_the_floor_rule_on_any_field(int cols, int rows)
    {
        var g = FieldGame(cols, rows);
        var e = MakeThread(g);
        double floor = EnemyKit.BossFloor(g.PH), lowest = 0;
        Run(g, e, FullPassFrames, _ => { if (e.Y >= 0) lowest = Math.Max(lowest, EnemyKit.LowestPixel(e)); });
        Assert.True(lowest <= floor, $"lowest pixel {lowest:0.0} is under the floor {floor:0.0}");
        Assert.True(e.Y < 0 || EnemyKit.ClearOfShip(g, e));
    }

    [Theory]
    [InlineData(30, 16)]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void It_survives_a_full_run_of_lanes_and_comes_back_after_forty_frames(int cols, int rows)
    {
        var g = FieldGame(cols, rows);
        var e = MakeThread(g);
        const int ParkSlack = 2;
        int parked = 0, longest = 0, laps = 0;
        bool wasParked = false;
        Run(g, e, ParkAndReturnFrames, _ =>
        {
            Assert.Contains(e, g.Enemies);   // never off the field's list, so the fight never goes empty
            if (e.Untouchable) { parked++; longest = Math.Max(longest, parked); wasParked = true; }
            else { parked = 0; if (wasParked) laps++; wasParked = false; }
        });
        Assert.InRange(longest, ParkFrames - ParkSlack, ParkFrames + ParkSlack);
        Assert.True(laps >= 1, "it came back for a second pass");
    }

    [Fact]
    public void It_goes_faster_for_each_car_lost_and_fastest_with_the_head_alone()
    {
        double Speed(int lost)
        {
            var g = FieldGame();
            var e = MakeThread(g);
            Arrive(g, e);
            SetShipPosition(g, ShipAsideX, ShipY);
            for (int i = 0; i < lost; i++) KillLastCar(g, e);
            for (int i = 0; i < SettleFrames; i++) StepKeepingEnemyClearingHostileBolts(g, e);
            double x0 = e.X;
            StepKeepingEnemyClearingHostileBolts(g, e);
            return Math.Abs(e.X - x0);
        }
        Assert.Equal(CarSpeed, Speed(0), SpeedTolerance);
        foreach (int lost in new[] { 2, Cars - 1 })
            Assert.Equal(CarSpeed + CarSpeedPerLoss * lost, Speed(lost), SpeedTolerance);
        Assert.Equal(AloneSpeed, Speed(Cars), SpeedTolerance);
    }

    // ---------------------------------------------------------------- fire and warnings

    [Fact]
    public void A_car_over_the_ship_drops_one_slow_bolt_straight_down_once_a_lane()
    {
        const int BigFieldLanes = 3;
        var g = FieldGame();
        var e = MakeThread(g);
        var seen = new List<Bullet>();
        for (int f = 0; f < FullPassFrames; f++)
        {
            SetShipPosition(g, ShipUnderTrainX, ShipY);
            StepKeepingEnemy(g, e);
            foreach (var b in g.Bullets) if (!b.Friendly && !seen.Contains(b)) seen.Add(b);
            g.Bullets.RemoveAll(b => !b.Friendly);
            if (e.Untouchable) break;   // one whole cycle of lanes
        }
        Assert.Equal(BigFieldLanes, seen.Count);
        foreach (var b in seen)
        {
            Assert.Equal(0, b.VX, ExactTolerance);
            Assert.Equal(DropSpeed, b.VY, ExactTolerance);
        }
    }

    [Fact]
    public void The_dropped_bolt_is_not_made_faster_by_the_stage()
    {
        const int FirstDropFramesCap = 20 * Tuning.Fps, LateStage = 10;
        double Speed(int stage)
        {
            var g = FieldGame(stage: stage);
            var e = MakeThread(g);
            for (int f = 0; f < FirstDropFramesCap; f++)
            {
                SetShipPosition(g, ShipUnderTrainX, ShipY);
                StepKeepingEnemy(g, e);
                if (g.Bullets.FirstOrDefault(b => !b.Friendly) is { } b) return Math.Sqrt(b.VX * b.VX + b.VY * b.VY);
            }
            Assert.Fail($"no bolt dropped at stage {stage} inside {FirstDropFramesCap} frames");
            throw new System.Diagnostics.UnreachableException();
        }
        Assert.Equal(DropSpeed, Speed(ThreadStage), ExactTolerance);
        Assert.Equal(DropSpeed, Speed(LateStage), ExactTolerance);
    }

    [Fact]
    public void While_a_car_is_left_it_fires_nothing_but_the_dropped_bolt()
    {
        // several lanes of running, each with a car passing over the ship
        const int WatchFrames = 700;
        var g = FieldGame();
        var e = MakeThread(g);
        int bolts = 0;
        for (int f = 0; f < WatchFrames; f++)
        {
            SetShipPosition(g, ShipUnderTrainX, ShipY);
            StepKeepingEnemy(g, e);
            foreach (var b in g.Bullets.Where(b => !b.Friendly)) { Assert.Equal(0, b.VX, ExactTolerance); Assert.Equal(DropSpeed, b.VY, ExactTolerance); bolts++; }
            g.Bullets.RemoveAll(b => !b.Friendly);
        }
        Assert.True(bolts > 0, "a car dropped a bolt to check");
    }

    [Fact]
    public void The_head_alone_fires_one_aimed_bolt_of_speed_one_point_two_every_forty_five_frames_after_a_blink_of_twenty_four()
    {
        const int WatchFrames = 200, BoltsLeast = 3;
        // the angle, in radians, by which a bolt's course may miss the line to the ship
        const double AimTolerance = 0.2;
        var g = FieldGame();
        var e = MakeThread(g);
        Arrive(g, e);
        SetShipPosition(g, ShipTargetX, ShipY);
        for (int i = 0; i < Cars; i++) KillLastCar(g, e);
        var bolts = new List<int>();
        var blink = new List<int>();
        var seen = new List<Bullet>();
        for (int f = 0; f < WatchFrames; f++)
        {
            SetShipPosition(g, ShipTargetX, ShipY);
            StepKeepingEnemy(g, e);
            var fresh = g.Bullets.Where(b => !b.Friendly && !seen.Contains(b)).ToList();
            g.Bullets.RemoveAll(b => !b.Friendly);
            foreach (var b in fresh)
            {
                seen.Add(b);
                bolts.Add(f);
                Assert.Equal(AloneBoltSpeed, Math.Sqrt(b.VX * b.VX + b.VY * b.VY), ExactTolerance);
                Assert.True(b.VY > 0);
                double toShip = Math.Atan2(ShipY - b.Y, ShipTargetX - b.X), flies = Math.Atan2(b.VY, b.VX);
                Assert.InRange(Math.Abs(toShip - flies), 0, AimTolerance);
            }
            if (e.Y >= 0 && CountRoundHeadCentre(EnemyKit.ClearPickupsAndDraw(g), e, Palette.Tell) > 0) blink.Add(f);
        }
        Assert.True(bolts.Count >= BoltsLeast, $"bolts at {string.Join(",", bolts)}");
        for (int i = 1; i < bolts.Count; i++) Assert.Equal(FireEvery, bolts[i] - bolts[i - 1]);
        // the visor blinks white from BlinkFrames before each bolt, and not before that
        int first = blink.First(f => f > bolts[0] - FireEvery - 1 && f <= bolts[0]);
        Assert.Equal(BlinkFrames, bolts[0] - first);
        Assert.DoesNotContain(blink, f => f > bolts[0] && f < bolts[1] - BlinkFrames);
    }

    [Fact]
    public void The_next_lane_is_a_dotted_row_for_the_thirty_six_frames_before_the_turn()
    {
        // a pixel this close to a part may be the part's own, not a dot
        const double PartReach = 6;
        const int FirstLaneFramesCap = 400;
        // frames before the turn when the row shows pale: inside the warning, before its last stretch in the tell colour, with a
        // frame or two of room each side; and a count of frames well before the warning starts
        const double PaleFramesLeast = 10, PaleFramesMost = 34, QuietFramesLeast = 40;
        const int DotsLeast = 5;
        var g = FieldGame();
        var e = MakeThread(g);
        double next = FirstLane(g) + LaneSpacing;
        int DotsOnNextLane(Surface f)
        {
            int n = 0;
            for (int x = 0; x < g.PW; x++)
                if ((f.Get(x, (int)Math.Round(next)) & EnemyKit.RgbMask) == Pale && !Alive(e).Any(p => Math.Abs(e.X + p.OX - x) <= PartReach && Math.Abs(e.Y + p.OY - next) <= PartReach)) n++;
            return n;
        }
        var counts = new List<(double Frames, int Dots)>();
        for (int f = 0; f < FirstLaneFramesCap; f++)
        {
            SetShipPosition(g, g.PW / 2.0, ShipY);
            StepKeepingEnemyClearingHostileBolts(g, e);
            if (e.Y < 0 || Math.Abs(e.Y - FirstLane(g)) > RowTolerance) break;
            counts.Add(((g.PW - TurnMargin - (e.X + HeadHalf)) / CarSpeed, DotsOnNextLane(EnemyKit.ClearPickupsAndDraw(g))));
        }
        Assert.Contains(counts, c => c.Frames is > PaleFramesLeast and < PaleFramesMost && c.Dots > DotsLeast);
        Assert.All(counts.Where(c => c.Frames > QuietFramesLeast), c => Assert.Equal(0, c.Dots));
    }

    // ---------------------------------------------------------------- voice

    /// <summary>Frames for a waiting post to be taken up and shown in the speech box.</summary>
    private const int PostTakenFrames = 6;

    /// <summary>Its own lines on arriving, at three cars left, alone and dying.</summary>
    private const string ArriveLine = "A thread. 1/7", PartLostLine = "4/7 And furthermore", PhaseLine = "7/7 In conclusion", DyingLine = "Muted.";

    /// <summary>A rude post short enough for one page of the speech box.</summary>
    private const string ShortPost = "grr you again";

    private static string PostOfWords(int words) => string.Join(" ", Enumerable.Range(0, words).Select(i => "word" + i));

    [Fact]
    public void Its_header_carries_the_page_count_of_the_post_on_show()
    {
        const int PostWords = 300, PagesLeast = 3;
        var g = FieldGame();
        g.OfferDarkness(new DarknessPost(DarknessKind.Rude, 1, PostOfWords(PostWords)));
        var e = MakeThread(g);
        for (int i = 0; i < SettleFrames; i++) StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.True(EnemySpeechBox.Speaks(e));
        int pages = SpeechBoxText.Pages(e.SpeechLines!, e.PageLines);
        Assert.True(pages >= PagesLeast, "the post is long enough to page");
        Assert.StartsWith($"1/{pages} ", EnemySpeechBox.Title(e));
        Assert.EndsWith(e.Header!, EnemySpeechBox.Title(e));
        int age = 0;
        while (SpeechBoxText.Progress(e.SpeechLines!, e.PageLines, age).Page < 1) age++;
        e.SpeechBoxAge = age;
        StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.StartsWith($"2/{pages} ", EnemySpeechBox.Title(e));
    }

    [Fact]
    public void A_short_post_and_a_post_with_no_words_show_the_header_with_no_count()
    {
        var g = FieldGame();
        var e = MakeThread(g);
        Assert.Null(Thread.Prefix(g, e));
        g.OfferDarkness(new DarknessPost(DarknessKind.Rude, 1, ShortPost));
        for (int i = 0; i < PostTakenFrames; i++) StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.Equal(ShortPost, e.Text);
        Assert.Null(Thread.Prefix(g, e));
        Assert.Equal(e.Header, EnemySpeechBox.Title(e));
    }

    [Fact]
    public void It_takes_the_longest_waiting_post_first_and_the_oldest_of_equals()
    {
        RudePost Post(ulong n, string t) => new(n, t, DarknessSource.Label, null, (long)n);
        Assert.Equal(1, Thread.Taste(EnemyKit.Speaker(EnemyKind.Thread), [Post(1, "aa"), Post(2, "aaaaaa"), Post(3, "aaa")]));
        Assert.Equal(0, Thread.Taste(EnemyKit.Speaker(EnemyKind.Thread), [Post(1, "aaaa"), Post(2, "bbbb")]));
        Assert.Equal(0, Thread.Taste(EnemyKit.Speaker(EnemyKind.Thread), [Post(1, "a")]));
    }

    [Fact]
    public void A_post_comes_at_idle_at_its_slow_pace_at_each_second_car_lost_and_when_the_head_is_alone()
    {
        const int IdleGapSeconds = 8;
        var g = FieldGame();
        var e = MakeThread(g);
        Assert.Equal(Turn.Wait, Thread.TurnFor(g, e, VoiceCue.Idle));
        Assert.Equal(IdleGapSeconds * Tuning.Fps, Thread.IdleFrames(e));
        Assert.Equal(SpeechBoxText.MinPageLines, Thread.PageCap);   // few lines to a page, so a long post is read in instalments
        Assert.Equal(Turn.Wait, Thread.TurnFor(g, e, VoiceCue.Arrive));
        Assert.Equal(Turn.Wait, Thread.TurnFor(g, e, VoiceCue.Phase));
        Assert.Equal(Turn.Cut, Thread.TurnFor(g, e, VoiceCue.Dying));
        g.OfferDarkness(new DarknessPost(DarknessKind.Rude, 1, "grr"));
        var turns = new List<Turn>();
        for (int lost = 1; lost <= Cars; lost++)
        {
            Car(e, Cars - lost + 1).State = PartState.Dead;
            turns.Add(Thread.TurnFor(g, e, VoiceCue.PartLost));
        }
        // with a post waiting: the second, fourth (and third, which has its own words) are answered; the rest are not
        Assert.Equal([Turn.Skip, Turn.Wait, Turn.Wait, Turn.Wait, Turn.Skip, Turn.Skip], turns);
        // with none waiting, only the third (three cars left) has words to say
        var g2 = FieldGame();
        var e2 = MakeThread(g2);
        var none = new List<Turn>();
        for (int lost = 1; lost <= Cars; lost++)
        {
            Car(e2, Cars - lost + 1).State = PartState.Dead;
            none.Add(Thread.TurnFor(g2, e2, VoiceCue.PartLost));
        }
        Assert.Equal([Turn.Skip, Turn.Skip, Turn.Wait, Turn.Skip, Turn.Skip, Turn.Skip], none);
    }

    [Fact]
    public void Its_own_words_are_these_and_the_rest_are_silent()
    {
        Assert.Equal(ArriveLine, Thread.Line(EnemyKit.Speaker(EnemyKind.Thread), VoiceCue.Arrive));
        Assert.Equal(PartLostLine, Thread.Line(EnemyKit.Speaker(EnemyKind.Thread), VoiceCue.PartLost));
        Assert.Equal(PhaseLine, Thread.Line(EnemyKit.Speaker(EnemyKind.Thread), VoiceCue.Phase));
        Assert.Equal(DyingLine, Thread.Line(EnemyKit.Speaker(EnemyKind.Thread), VoiceCue.Dying));
        Assert.Null(Thread.Line(EnemyKit.Speaker(EnemyKind.Thread), VoiceCue.Idle));
        Assert.Null(Thread.Line(EnemyKit.Speaker(EnemyKind.Thread), VoiceCue.Opens));
    }

    [Fact]
    public void With_no_post_it_says_its_own_line_on_arriving_at_three_cars_left_and_alone_and_dying()
    {
        // an age past the last page of any line, so the box has been read to its end
        const int ReadToEndAge = 100000;
        var g = FieldGame();
        var e = MakeThread(g);
        SetShipPosition(g, ShipAsideX, ShipY);
        for (int i = 0; i < SettleFrames; i++) StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.Equal(ArriveLine, e.Text);
        Assert.Equal(ThreadName, e.Header);
        Arrive(g, e);
        KillLastCar(g, e);
        KillLastCar(g, e);
        e.SpeechBoxAge = ReadToEndAge;
        KillLastCar(g, e);
        Assert.Equal(PartLostLine, e.Text);
        KillLastCar(g, e);
        KillLastCar(g, e);
        e.SpeechBoxAge = ReadToEndAge;
        KillLastCar(g, e);
        Assert.Equal(PhaseLine, e.Text);
        e.SpeechBoxAge = ReadToEndAge;
        EnemyKit.Shot(g, e.X, e.Y, KillingDamage);
        StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.Equal(DyingLine, e.Text);
    }

    [Fact]
    public void It_says_a_waiting_post_on_arriving()
    {
        var g = FieldGame();
        g.OfferDarkness(new DarknessPost(DarknessKind.Rude, 1, ShortPost));
        var e = MakeThread(g);
        for (int i = 0; i < PostTakenFrames; i++) StepKeepingEnemyClearingHostileBolts(g, e);
        Assert.Equal(ShortPost, e.Text);
    }

    // ---------------------------------------------------------------- the gate

    [Fact]
    public void Boss_four_is_the_thread_and_the_gate_brings_it_whole()
    {
        const ulong GateSeed = 3;
        const int GateFramesCap = 100 * Tuning.Fps;
        var g = new RaiderGame(GateSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(ThreadStage);
        for (int i = 0; i < GateFramesCap && g.Boss is null; i++) g.Step();
        Assert.NotNull(g.Boss);
        Assert.Equal(EnemyKind.Thread, g.Boss!.Kind);
        Assert.Equal(ThreadName, g.BossName);
        Assert.Equal(TrainHp, g.Boss.Hp, HpPlaces);
        Assert.Equal(1 + Cars, g.Boss.Parts!.Count);
    }

    // ---------------------------------------------------------------- determinism and the fight

    [Fact]
    public void The_same_seed_gives_the_same_thread()
    {
        const ulong ReplaySeed = 9;
        const int ReplayFrames = 30 * Tuning.Fps;
        string Replay(ulong seed)
        {
            var g = FieldGame(seed: seed);
            var e = MakeThread(g);
            var parts = new List<string>();
            for (int f = 0; f < ReplayFrames; f++)
            {
                EnemyKit.BotFrame(g);
                StepKeepingEnemyClearingHostileBolts(g, e);
                if (f % Tuning.Fps == 0) parts.Add($"{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}:{g.Bullets.Count}:{g.PX:0.0}");
            }
            return string.Join("|", parts);
        }
        Assert.Equal(Replay(ReplaySeed), Replay(ReplaySeed));
    }

    /// <summary>The weapon level a player has reached by stage 4, and the time a fight may take before it counts as lost.</summary>
    private const int StageFourWeaponLevel = 5, FightSeconds = 60;

    /// <summary>The bot's ship starts this far in from either side, at one of this many evenly spread columns, picked by stepping
    /// the seed by a stride that shares no factor with the count.</summary>
    private const int StartInset = 8, StartColumns = 100, StartStride = 37;

    /// <summary>How far above the ship's centre the bot reckons its shot starts; how far either side of the aim point a shot still
    /// hits, tried in steps of this size; how close to a side edge the bot will go; how far off its target it lets the ship sit.</summary>
    private const double MuzzleAbove = 4, AimReach = 2.5, AimStep = 0.5, EdgeKeep = 4, SteerDeadZone = 0.6;

    /// <summary>How far past a lower part's hit box the bot keeps its shot's course, so a part in the way is never grazed.</summary>
    private const double BlockerMargin = 1;

    /// <summary>How many past positions the bot keeps of each part to read its step from.</summary>
    private const int TrackedSteps = 8;

    /// <summary>Frames a bot at stage 4's weapon level needs to kill a Thread, or -1. It leads its target by the shot's travel time
    /// and steps out of the way of a dropped bolt, as a player would.</summary>
    private static int Fight(ulong seed, int cols, int rows)
    {
        var g = FieldGame(cols, rows, seed);
        while (g.WeaponLevel < StageFourWeaponLevel) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); EnemyKit.Clear(g); }
        SetShipPosition(g, StartInset + ((int)seed * StartStride % StartColumns) * (g.PW - 2 * StartInset) / (double)StartColumns, g.PY);   // each seed starts the ship somewhere else
        var e = MakeThread(g);
        var prev = new Dictionary<Part, Queue<double>>();
        for (int f = 0; f < FightSeconds * Tuning.Fps; f++)
        {
            double target = g.PX;
            var open = e.Parts!.Where(p => p.State == PartState.Open).OrderByDescending(p => e.Y + p.OY).FirstOrDefault();
            if (open is not null)
            {
                // where a part will be when a shot fired now passes its row: its last step, carried on
                double Flight(Part p) => Math.Max(0, (g.PY - MuzzleAbove - (e.Y + p.OY)) / (Tuning.ShipShotSpeed * Weapons.At(g.WeaponLevel).Shots[0].VyMul));
                double LeadX(Part p) => e.X + p.OX + (prev.TryGetValue(p, out var o) && o.Count > 0 ? (e.X + p.OX - o.Peek()) / o.Count : 0) * Flight(p);
                double aim = LeadX(open);
                bool ShotIsClear(double x) => Math.Abs(x - aim) <= AimReach && !e.Parts!.Any(q => q != open && (q.State != PartState.Dead || q.Wreck is not null)
                    && e.Y + q.OY > e.Y + open.OY && Math.Abs(x - LeadX(q)) <= q.HW + BlockerMargin);
                target = Math.Clamp(aim, EdgeKeep, g.PW - EdgeKeep);
                double best = double.MaxValue;
                for (double x = Math.Max(EdgeKeep, aim - AimReach); x <= Math.Min(g.PW - EdgeKeep, aim + AimReach); x += AimStep)
                    if (ShotIsClear(x) && Math.Abs(x - g.PX) < best) { best = Math.Abs(x - g.PX); target = x; }
            }
            g.Input.Key(GameKey.Fire, true);
            g.Input.Key(GameKey.Left, g.PX > target + SteerDeadZone);
            g.Input.Key(GameKey.Right, g.PX < target - SteerDeadZone);
            foreach (var p in e.Parts!)
            {
                if (!prev.TryGetValue(p, out var q)) prev[p] = q = new Queue<double>();
                q.Enqueue(e.X + p.OX);
                if (q.Count > TrackedSteps) q.Dequeue();
            }
            StepKeepingEnemyClearingHostileBolts(g, e);
            if (!g.Enemies.Contains(e)) return f + 1;
        }
        return -1;
    }

    /// <summary>The frames of <see cref="Fight"/> for each seed from 1 to <paramref name="seeds"/>, each one asserted to end inside
    /// <see cref="FightSeconds"/>.</summary>
    private static List<int> FightTimes(ulong seeds, int cols, int rows)
    {
        var times = new List<int>();
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            int t = Fight(seed, cols, rows);
            Assert.True(t > 0, $"seed {seed}: not dead inside {FightSeconds} s");
            times.Add(t);
        }
        return times;
    }

    [Fact]
    public void A_bot_at_stage_fours_weapon_level_kills_it_inside_the_fight_band()
    {
        const int MedianLeastSeconds = 18, MedianMostSeconds = 30, LeastSeconds = 15, MostSeconds = 35;
        const ulong Seeds = 9;
        var times = FightTimes(Seeds, EnemyKit.BigCols, EnemyKit.BigRows);
        times.Sort();
        int median = times[times.Count / 2];
        output.WriteLine($"thread fight at level {StageFourWeaponLevel}, frames to kill: {string.Join(" ", times)}; median {median} ({median / (double)Tuning.Fps:0.0} s)");
        Assert.InRange(median, MedianLeastSeconds * Tuning.Fps, MedianMostSeconds * Tuning.Fps);   // the structure of the fight (park, lanes) sets the floor of the band
        Assert.All(times, t => Assert.InRange(t, LeastSeconds * Tuning.Fps, MostSeconds * Tuning.Fps));
    }

    [Theory]
    [InlineData(30, 16)]
    [InlineData(80, 24)]
    public void The_fight_ends_on_the_small_fields_too(int cols, int rows)
    {
        const ulong Seeds = 3;
        var times = FightTimes(Seeds, cols, rows);
        output.WriteLine($"thread fight on {cols}x{rows}: {string.Join(" ", times)}");
    }
}
