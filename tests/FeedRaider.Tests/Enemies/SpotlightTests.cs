using System.Runtime.CompilerServices;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Spotlight (boss 5): a gantry with a lamp that follows the ship, locks on a column and fills it with light. These tests give
/// its machine (track, charge, fire, cool, and the sweep below half), its safety (nothing lethal through the whole wind-up), its
/// pixels, its fit on the four fields and the short form, its voice (a new post held up at each lock) and the fight.
/// </summary>
public sealed class SpotlightTests(ITestOutputHelper output)
{
    private const uint Lamp = 0xfff3a0, Dim = 0x8a7a3a, Husk = 0x3a2c4f;
    private static readonly uint Violet = Art.DarkPal['o'];
    private const int EdgeMargin = 8, BeamStandoff = 14, DodgeStep = 9, LockWindow = 15;
    private const int TrackFrames = 70, ChargeFrames = 40, BatteryChargeBonus = 8, FireFrames = 18, SweepFireFrames = 58, CoolFrames = 12;
    private const int FirstVolleyAge = 20, SecondVolleyAge = 50;

    /// <summary>Rows above the bottom of the field: where a test parks the ship, a row deep in the beam, and the foot of the beam where a sweep reaches furthest.</summary>
    private const int ShipRowFromBottom = 14, LowRowFromBottom = 6, FootRowFromBottom = 3;

    /// <summary>The beam's half width in whole columns either side of its centre column.</summary>
    private const int BeamHalf = 4;

    /// <summary>A ship this far from the beam's centre is clear of it with room to spare.</summary>
    private const int BeamClearance = 12;

    /// <summary>The share of the lens's hit points that puts it below half.</summary>
    private const double BelowHalfShare = 0.4;

    /// <summary>The stage whose boss is the spotlight.</summary>
    private const int SpotlightStage = 5;

    /// <summary>Its score for the kill, and each battery's hit points and score.</summary>
    private const int KillScore = 6000, BatteryHp = 18, BatteryScore = 400;

    /// <summary>The form on fields under this many pixels tall: its height, the row it holds at, and the row of its lens from the top.</summary>
    private const int ShortBelow = 48, ShortHeight = 15, ShortLensRow = 10;
    private const double ShortHold = 11.5;

    /// <summary>The full form: its height, its width (25 drawn plus one clear column), and the row of its lens from the top.</summary>
    private const int TallHeight = 17, BodyWidth = 26, TallLensRow = 12;

    /// <summary>The lens is this many rows tall, so its centre is half that below its top row.</summary>
    private const int LensHeight = 4;

    /// <summary>A shot this strong kills a battery, and one this strong kills the lens, in one hit.</summary>
    private const double BatteryOverkill = 99, LensOverkill = 999;

    /// <summary>The bot steps aside from a bolt within this many pixels above the ship and this many either side of it.</summary>
    private const int BoltLookAhead = 30, BoltReach = 6;

    /// <summary>How far to the side of the beam a test parks the ship, and how far from the field's edge when it keeps out of the light.</summary>
    private const double SideStep = 30, CornerInset = 12;

    /// <summary>Frames after which a wait for a state, or a fight, is given up as stalled.</summary>
    private const int StepLimit = 3000;
    private const int Track = 1, Charge = 2, Fire = 3, Cool = 4;

    private static EnemyBehaviour Spot => RaiderGame.TableBehaviour(EnemyKind.Spotlight)!;

    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);

    /// <summary>The row the boss holds at: on a full field the boss row rounded, plus a half pixel so that its top edge is a whole row.</summary>
    private static double Hold(RaiderGame g) => g.PH < ShortBelow ? ShortHold : Math.Round(EnemyKit.BossRow(g.PH)) + 0.5;

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PX")]
    private static extern void SetPX(RaiderGame g, double value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PY")]
    private static extern void SetPY(RaiderGame g, double value);

    private static void SetShipPosition(RaiderGame g, double x, double y)
    {
        SetPX(g, x);
        SetPY(g, y);
    }

    /// <summary>Places a spotlight settled at its hold row in column <paramref name="x"/>, and moves the ship under it near the bottom.</summary>
    private static Enemy PlaceBossAndShip(RaiderGame g, double x = PlacedX)
    {
        var e = EnemyKit.Place(g, EnemyKind.Spotlight, x, Hold(g));
        e.VY = 0; e.Amp = 0;
        SetShipPosition(g, x, ShipRow(g));
        return e;
    }

    /// <summary>The column a test places the boss at unless it names another.</summary>
    private const double PlacedX = 64;

    private static double ShipRow(RaiderGame g) => g.PH - ShipRowFromBottom;

    private static double LowRow(RaiderGame g) => g.PH - LowRowFromBottom;

    /// <summary>One frame, sweeping away the filler enemies and every hostile bolt so none confuses a check of the beam.</summary>
    private static void TickClean(RaiderGame g)
    {
        g.Step();
        g.Enemies.RemoveAll(e => e.Kind < EnemyKind.Gnat);
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    /// <summary>Steps until the boss is at the given mode and age, moving the ship out of the beam on the way so a hit never cuts the count
    /// short; when heading for the fire, or through the charge it waits in, it leaves the ship where the test put it.</summary>
    private static void AdvanceToKeepingShipClear(RaiderGame g, Enemy b, int mode, int age)
    {
        for (int i = 0; i < StepLimit && !(b.Mode == mode && b.ModeAge == age); i++) { if (mode != Fire && !(mode == Charge && b.Mode == Charge)) StepShipOutOfBeam(g, b); TickClean(g); }
        Assert.True(b.Mode == mode && b.ModeAge == age, $"never reached mode {mode} frame {age} (at {b.Mode}/{b.ModeAge})");
    }

    private static double BeamX(Enemy b) => b.X + PartOf(b, "lens").OX;

    /// <summary>Moves the ship a side step to the side of the beam while it charges, fires or cools, so that a count of frames is not cut short by a hit.</summary>
    private static void StepShipOutOfBeam(RaiderGame g, Enemy b)
    {
        if (b.Mode is not (Charge or Fire or Cool) || Math.Abs(g.PX - BeamX(b)) >= BeamClearance) return;
        SetShipPosition(g, BeamX(b) < g.PW / 2.0 ? BeamX(b) + SideStep : BeamX(b) - SideStep, g.PY);
    }

    /// <summary>Steps <paramref name="frames"/> frames, moving the ship out of the beam each frame, and gives each mode the boss passed through with its length in frames.</summary>
    private static List<(int Mode, int Len)> CollectModeRunsKeepingShipClear(RaiderGame g, Enemy b, int frames)
    {
        var runs = new List<(int, int)>();
        for (int f = 0; f < frames; f++)
        {
            StepShipOutOfBeam(g, b);
            TickClean(g);
            if (runs.Count > 0 && runs[^1].Item1 == b.Mode) runs[^1] = (b.Mode, runs[^1].Item2 + 1);
            else runs.Add((b.Mode, 1));
        }
        return runs;
    }

    private static void KillBattery(RaiderGame g, Enemy b, string name)
    {
        var p = PartOf(b, name);
        EnemyKit.Shot(g, b.X + p.OX, b.Y + p.OY, BatteryOverkill);
        TickClean(g);
        Assert.Equal(PartState.Dead, p.State);
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Spotlight, Spot.Kind);
        Assert.Equal("SPOTLIGHT", Spot.Name);
        Assert.Equal(KillScore, Spot.Score);
        Assert.True(Spot.Boss);
        Assert.True(Spot.Speaks);
        Assert.True(Spot.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(BodyWidth, Spot.Body.W);   // 25 wide plus one clear column so that the width is even and its parts round with its body
        Assert.Equal(TallHeight, Spot.Body.H);
    }

    [Fact]
    public void It_is_a_gantry_over_two_open_batteries_and_a_lens_that_is_the_only_thing_that_kills_it()
    {
        var g = EnemyKit.Game();
        var b = g.SpawnKind(EnemyKind.Spotlight, EnemyKit.MidWidth);
        Assert.Equal(["gantry", "mast", "hull", "battery-l", "battery-r", "lens"], b.Parts!.Select(p => p.Name));
        foreach (var n in new[] { "gantry", "mast", "hull" })
        {
            Assert.Equal(PartState.Armoured, PartOf(b, n).State);
            Assert.False(PartOf(b, n).Vital);
        }
        foreach (var n in new[] { "battery-l", "battery-r" })
        {
            var p = PartOf(b, n);
            Assert.Equal(PartState.Open, p.State);
            Assert.False(p.Vital);
            Assert.Equal(BatteryHp, p.Hp);
            Assert.Equal(BatteryScore, p.Score);
        }
        var lens = PartOf(b, "lens");
        Assert.True(lens.Vital);
        Assert.Equal(PartState.Armoured, lens.State);
        Assert.Equal(b.Hp, lens.Hp, 9);
        Assert.Equal(lens.Hp, b.MaxHp, 9);
        Assert.True(lens.HW >= 1 && lens.HH >= 1, "a target a shot can hit");
    }

    // ---------------------------------------------------------------- the machine

    [Fact]
    public void It_tracks_seventy_frames_charges_forty_fires_eighteen_and_cools_twelve()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        var runs = CollectModeRunsKeepingShipClear(g, b, TrackFrames + ChargeFrames + FireFrames + CoolFrames + TrackFrames + ChargeFrames);
        Assert.Equal([(Track, TrackFrames), (Charge, ChargeFrames), (Fire, FireFrames), (Cool, CoolFrames), (Track, TrackFrames), (Charge, ChargeFrames)], runs);
    }

    [Fact]
    public void The_lens_is_open_only_in_charge_and_cool()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        var lens = PartOf(b, "lens");
        var seen = new HashSet<int>();
        for (int f = 0; f < 2 * (TrackFrames + ChargeFrames + FireFrames + CoolFrames); f++)
        {
            TickClean(g);
            seen.Add(b.Mode);
            Assert.Equal(b.Mode is Charge or Cool ? PartState.Open : PartState.Armoured, lens.State);
        }
        Assert.Equal([Track, Charge, Fire, Cool], seen.Order());
    }

    [Fact]
    public void A_shot_on_the_lens_is_shrugged_off_in_track_and_fire_and_hurts_in_charge_and_cool()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        var lens = PartOf(b, "lens");
        const int InsideTheMode = 5;
        foreach (var (mode, hurts) in new[] { (Track, false), (Charge, true), (Fire, false), (Cool, true) })
        {
            AdvanceToKeepingShipClear(g, b, mode, InsideTheMode);
            double hp = lens.Hp;
            EnemyKit.Shot(g, b.X + lens.OX, b.Y + lens.OY, 1);
            TickClean(g);
            Assert.Equal(hurts ? hp - 1 : hp, lens.Hp, 9);
        }
    }

    [Fact]
    public void Each_dead_battery_adds_eight_frames_of_charge()
    {
        int ChargeLength(params string[] kill)
        {
            var g = EnemyKit.Game();
            var b = PlaceBossAndShip(g);
            foreach (var k in kill) KillBattery(g, b, k);
            AdvanceToKeepingShipClear(g, b, Charge, 1);
            int n = 1;
            while (b.Mode == Charge) { StepShipOutOfBeam(g, b); TickClean(g); if (b.Mode == Charge) n++; }
            Assert.Equal(Fire, b.Mode);
            return n;
        }
        Assert.Equal(ChargeFrames, ChargeLength());
        Assert.Equal(ChargeFrames + BatteryChargeBonus, ChargeLength("battery-l"));
        Assert.Equal(ChargeFrames + BatteryChargeBonus, ChargeLength("battery-r"));
        Assert.Equal(ChargeFrames + 2 * BatteryChargeBonus, ChargeLength("battery-l", "battery-r"));
    }

    [Fact]
    public void Each_live_battery_fires_one_aimed_bolt_at_frames_twenty_and_fifty_of_tracking()
    {
        const int Batteries = 2, VolleysPerTrack = 2;
        int Volley(params string[] kill)
        {
            var g = EnemyKit.Game();
            var b = PlaceBossAndShip(g);
            foreach (var k in kill) KillBattery(g, b, k);
            AdvanceToKeepingShipClear(g, b, Track, 1);
            int total = 0;
            var when = new List<int>();
            while (b.Mode == Track)
            {
                int before = g.Bullets.Count(x => !x.Friendly);
                StepShipOutOfBeam(g, b);
                g.Step();
                int n = g.Bullets.Count(x => !x.Friendly) - before;
                if (n > 0) { total += n; when.Add(b.ModeAge); }
                g.Bullets.RemoveAll(x => !x.Friendly);
                if (b.Mode == Track && b.ModeAge >= TrackFrames) break;
            }
            if (total > 0) Assert.Equal([FirstVolleyAge, SecondVolleyAge], when);
            return total;
        }
        Assert.Equal(Batteries * VolleysPerTrack, Volley());
        Assert.Equal(VolleysPerTrack, Volley("battery-r"));
        Assert.Equal(0, Volley("battery-l", "battery-r"));
    }

    [Fact]
    public void A_battery_bolt_is_aimed_at_the_ship_and_slow()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        const double ShipRightOfBoss = 100, BoltSpeed = 1.1, SpeedTolerance = 0.01;
        SetShipPosition(g, ShipRightOfBoss, ShipRow(g));
        AdvanceToKeepingShipClear(g, b, Track, FirstVolleyAge - 1);
        g.Step();
        var bolts = g.Bullets.Where(x => !x.Friendly).ToList();
        Assert.Equal(2, bolts.Count);
        foreach (var s in bolts)
        {
            Assert.InRange(Math.Sqrt(s.VX * s.VX + s.VY * s.VY), BoltSpeed - SpeedTolerance, BoltSpeed + SpeedTolerance);
            Assert.True(s.VX > 0 && s.VY > 0, "down and toward the ship");
        }
    }

    [Fact]
    public void It_slides_toward_the_ship_while_tracking_and_stands_still_to_charge()
    {
        var g = EnemyKit.Game();
        const double BossLeftX = 40, ShipRightX = 90, ShipFarLeftX = 20, SlideSpeed = 0.8, SlideSlackBehind = 2, SlideSlackAhead = 1;
        const int SlideFrames = 20;
        var b = PlaceBossAndShip(g, BossLeftX);
        SetShipPosition(g, ShipRightX, ShipRow(g));
        AdvanceToKeepingShipClear(g, b, Track, 1);
        double x0 = b.X;
        for (int i = 0; i < SlideFrames; i++) TickClean(g);
        Assert.InRange(b.X - x0, SlideFrames * SlideSpeed - SlideSlackBehind, SlideFrames * SlideSpeed + SlideSlackAhead);
        AdvanceToKeepingShipClear(g, b, Charge, 1);
        double x1 = b.X;
        for (int i = 0; i < ChargeFrames - 1; i++) { SetShipPosition(g, ShipFarLeftX, ShipRow(g)); TickClean(g); Assert.Equal(x1, b.X, 9); }
    }

    // ---------------------------------------------------------------- the beam

    [Fact]
    public void The_beam_kills_in_its_column_and_nowhere_else()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        const double JustPastTheEdge = 7;
        const int InsideTheFire = 3;
        AdvanceToKeepingShipClear(g, b, Fire, InsideTheFire);
        double bx = BeamX(b);
        double low = LowRow(g);
        foreach (var dx in new[] { -BeamHalf, -BeamHalf / 2.0, 0, BeamHalf / 2.0, BeamHalf })
        {
            SetShipPosition(g, bx + dx, low);
            Assert.True(Spot.Touches(g, b), $"dx {dx} is in the light");
        }
        foreach (var dx in new[] { -JustPastTheEdge, -BeamClearance, BeamClearance, JustPastTheEdge, SideStep, -SideStep })
        {
            SetShipPosition(g, bx + dx, low);
            Assert.False(Spot.Touches(g, b), $"dx {dx} is clear of the light");
        }
        const double AboveTheBoss = 12;
        SetShipPosition(g, bx, b.Y - AboveTheBoss);   // a ship higher than the lens: nothing there
        Assert.False(Spot.Touches(g, b));
    }

    [Fact]
    public void A_ship_in_the_column_when_it_fires_is_hurt_and_the_game_sees_it()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        AdvanceToKeepingShipClear(g, b, Charge, ChargeFrames);
        SetShipPosition(g, BeamX(b), LowRow(g));
        int hp = g.Hp + g.Shield;   // the shield takes the first hit
        TickClean(g);
        Assert.Equal(Fire, b.Mode);
        Assert.True(g.Hp + g.Shield < hp || g.Deaths > 0);
    }

    [Fact]
    public void Nothing_is_lethal_from_the_first_frame_of_the_charge_to_its_last()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        AdvanceToKeepingShipClear(g, b, Charge, 1);
        SetShipPosition(g, BeamX(b), LowRow(g));
        for (int f = 1; f <= ChargeFrames; f++)
        {
            Assert.Equal(Charge, b.Mode);
            Assert.False(Spot.Touches(g, b), $"charge frame {f}");
            int hp = g.Hp;
            TickClean(g);
            if (f < ChargeFrames) Assert.Equal(hp, g.Hp);   // the last tick of the charge is the first of the fire, and the ship is under it
            if (f < ChargeFrames) SetShipPosition(g, BeamX(b), LowRow(g));
        }
        Assert.Equal(Fire, b.Mode);
    }

    [Fact]
    public void A_ship_that_stands_still_dies_and_one_that_moves_for_six_frames_after_the_last_third_begins_lives()
    {
        bool Lives(bool moves)
        {
            var g = EnemyKit.Game();
            var b = PlaceBossAndShip(g);
            AdvanceToKeepingShipClear(g, b, Charge, 1);
            const int MoveFrames = 6;
            SetShipPosition(g, BeamX(b), LowRow(g));
            int hp = g.Hp + g.Shield;   // the shield takes the first hit
            while (b.Mode == Charge)
            {
                if (moves && b.ModeAge > ChargeFrames - MoveFrames) g.Input.Key(GameKey.Left, true);
                TickClean(g);
            }
            g.Input.Key(GameKey.Left, false);
            for (int f = 0; f < FireFrames + CoolFrames; f++) TickClean(g);
            return g.Hp + g.Shield == hp && g.Deaths == 0;
        }
        Assert.False(Lives(false));
        Assert.True(Lives(true));
    }

    [Fact]
    public void Each_sound_of_the_beam_plays_on_the_frame_its_mode_begins()
    {
        var g = EnemyKit.Game();
        var played = new List<(string Name, int Frame)>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add((n, (int)g.Frame)));
        var b = PlaceBossAndShip(g);
        AdvanceToKeepingShipClear(g, b, Charge, 1);
        Assert.Contains((SfxNames.Charge, (int)g.Frame), played);
        Assert.Contains((SfxNames.Open, (int)g.Frame), played);
        AdvanceToKeepingShipClear(g, b, Fire, 1);
        Assert.Contains((SfxNames.Beam, (int)g.Frame), played);
        AdvanceToKeepingShipClear(g, b, Track, 1);
        Assert.Contains((SfxNames.Shut, (int)g.Frame), played);
    }

    // ---------------------------------------------------------------- the sweep

    /// <summary>A spotlight below half its lens, ship on a side of the column, at the first frame of the charge.</summary>
    private static (RaiderGame G, Enemy B) BelowHalfAtChargeStart(double shipDx)
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        const int FramesBeforeLock = 4;
        PartOf(b, "lens").Hp = PartOf(b, "lens").MaxHp * BelowHalfShare;
        b.Hp = PartOf(b, "lens").Hp;
        AdvanceToKeepingShipClear(g, b, Track, TrackFrames - FramesBeforeLock);
        SetShipPosition(g, b.X + shipDx, LowRow(g));
        AdvanceToKeepingShipClear(g, b, Charge, 1);
        return (g, b);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void Below_half_the_beam_sweeps_twenty_four_pixels_toward_the_side_the_ship_stood_on(int side)
    {
        const double InsideTheSweep = 22;   // past the beam's edge, inside the 24 pixels the sweep reaches
        var (g, b) = BelowHalfAtChargeStart(side * SideStep);
        double bx = BeamX(b);
        double low = g.PH - FootRowFromBottom;
        SetShipPosition(g, bx + side * InsideTheSweep, low);
        for (int f = 1; f <= ChargeFrames; f++) { TickClean(g); SetShipPosition(g, bx + side * InsideTheSweep, low); }
        Assert.Equal(Fire, b.Mode);
        Assert.Equal(1, b.ModeAge);
        SetShipPosition(g, bx, low);
        Assert.True(Spot.Touches(g, b));
        SetShipPosition(g, bx + side * InsideTheSweep, low);
        Assert.False(Spot.Touches(g, b), "the beam has not leaned yet");
        for (int f = 1; f < FireFrames; f++) TickClean(g);
        Assert.Equal(Fire, b.Mode);
        Assert.Equal(FireFrames, b.ModeAge);
        for (int f = FireFrames; f < SweepFireFrames; f++) { SetShipPosition(g, bx - side * InsideTheSweep, low); TickClean(g); Assert.Equal(Fire, b.Mode); }
        Assert.Equal(SweepFireFrames, b.ModeAge);
        SetShipPosition(g, bx + side * InsideTheSweep, low);
        Assert.True(Spot.Touches(g, b), "the foot of the beam has swept toward the side the ship stood on");
        SetShipPosition(g, bx - side * InsideTheSweep, low);
        Assert.False(Spot.Touches(g, b), "and not toward the other");
        TickClean(g);
        Assert.Equal(Cool, b.Mode);
    }

    [Fact]
    public void The_sweep_is_slower_than_the_ship_and_a_ship_that_walks_ahead_of_it_lives()
    {
        const double InsideTheBeam = 3;
        var (g, b) = BelowHalfAtChargeStart(-SideStep);
        double bx = BeamX(b);
        SetShipPosition(g, bx - InsideTheBeam, LowRow(g));
        int hp = g.Hp;
        while (b.Mode == Charge) { g.Input.Key(GameKey.Right, g.PX < bx + BeamStandoff); TickClean(g); }
        g.Input.Key(GameKey.Right, false);
        while (b.Mode == Fire) { TickClean(g); }
        Assert.Equal(hp, g.Hp);
    }

    [Fact]
    public void Above_half_the_beam_does_not_sweep()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        Assert.Equal(Fire, CollectModeRunsKeepingShipClear(g, b, TrackFrames + ChargeFrames + 1)[^1].Mode);
        AdvanceToKeepingShipClear(g, b, Fire, FireFrames);
        TickClean(g);
        Assert.Equal(Cool, b.Mode);
    }

    [Fact]
    public void Below_half_the_fire_runs_the_sweep_length_on_every_pass()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        PartOf(b, "lens").Hp = PartOf(b, "lens").MaxHp * BelowHalfShare;
        b.Hp = PartOf(b, "lens").Hp;
        var runs = CollectModeRunsKeepingShipClear(g, b, TrackFrames + ChargeFrames + SweepFireFrames + CoolFrames + TrackFrames + ChargeFrames + SweepFireFrames);
        Assert.Equal([(Track, TrackFrames), (Charge, ChargeFrames), (Fire, SweepFireFrames), (Cool, CoolFrames), (Track, TrackFrames), (Charge, ChargeFrames), (Fire, SweepFireFrames)], runs);
    }

    // ---------------------------------------------------------------- pixels

    private static (int X0, int Y0) TopLeft(Enemy e) =>
        ((int)Math.Round(e.X - e.Sprite.W / 2.0), (int)Math.Round(e.Y - e.Sprite.H / 2.0));

    /// <summary>Pixels of the full form's art, from its top left: the row of the battery lamps and the column of each,
    /// the middle column the mast and lens stand on, a row of the mast, and the lens's top-left corner and centre.</summary>
    private const int LampRow = 3, LeftLampX = 2, RightLampX = 21, MidColumn = 12, MastRow = 5;
    private const int LensLeft = MidColumn - 1, LensCentreRow = TallLensRow + 1;

    [Fact]
    public void It_is_drawn_plum_outlined_with_gold_on_crimson_lamps_in_dim_amber_and_a_closed_plum_lens()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        var (x0, y0) = TopLeft(b);
        uint At(int x, int y) => f.Get(x0 + x, y0 + y) & EnemyKit.RgbMask;
        Assert.Equal(Palette.Plum, At(0, 0));
        Assert.Equal(Violet, At(LeftLampX, 1));   // the gantry's shade over the left lamp
        Assert.Equal(Palette.WeakBody, At(LeftLampX, LampRow));
        Assert.Equal(Dim, At(LeftLampX - 1, LampRow));
        Assert.Equal(Palette.Gold, At(LeftLampX + 1, LampRow + 1));
        Assert.Equal(Palette.WeakBody, At(RightLampX, LampRow));
        Assert.Equal(Violet, At(MidColumn, MastRow));
        Assert.Equal(Palette.Plum, At(MidColumn, LensCentreRow));       // the lens, shut, is the body's plum
        Assert.Equal(Palette.Plum, At(LensLeft, TallLensRow));
    }

    [Fact]
    public void The_gaps_in_the_art_are_clear_so_the_field_shows_through_them()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        const int BetweenLampsAndMastX = 8, UnderTheHullX = 3, UnderTheLampsRow = 5;
        static uint Px(Sprite s, int x, int y) => s.Px[y * s.W + x];
        var battery = PartOf(b, "battery-l").Closed!;
        Assert.Equal(0u, Px(b.Sprite, BetweenLampsAndMastX, LampRow + 1));
        Assert.Equal(0u, Px(b.Sprite, UnderTheHullX, TallHeight - 1));
        Assert.Equal(0u, Px(b.Sprite, BodyWidth - 1, 0));   // the spare column
        Assert.Equal(0u, Px(battery, battery.W - 1, UnderTheLampsRow));   // the corner under the lamps
    }

    [Fact]
    public void The_open_lens_shows_gold_on_crimson_and_the_dead_one_a_husk()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        const int KillEffectFrames = 12, EarlyInTheCharge = 3;
        AdvanceToKeepingShipClear(g, b, Charge, EarlyInTheCharge);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        var (x0, y0) = TopLeft(b);
        Assert.Equal(Palette.Gold, f.Get(x0 + MidColumn, y0 + LensCentreRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.WeakBody, f.Get(x0 + LensLeft, y0 + TallLensRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.WeakBody, f.Get(x0 + MidColumn + 1, y0 + TallLensRow + LensHeight - 1) & EnemyKit.RgbMask);
        KillBattery(g, b, "battery-l");
        for (int i = 0; i < KillEffectFrames; i++) TickClean(g);   // the flash and the burst of the kill are over
        var f2 = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(Husk, f2.Get(x0 + LeftLampX, y0 + LampRow) & EnemyKit.RgbMask);   // the dead battery's lamp
        Assert.Equal(Palette.WeakBody, f2.Get(x0 + RightLampX, y0 + LampRow) & EnemyKit.RgbMask);  // the live one still shows its mark
    }

    [Fact]
    public void The_parts_are_drawn_exactly_over_the_body_for_every_whole_pixel_position()
    {
        const int FirstX = 40, FirstY = 20, Positions = 4;
        var g = EnemyKit.Game();
        for (int x = FirstX; x < FirstX + Positions; x++)
            for (int y = FirstY; y < FirstY + Positions; y++)
            {
                EnemyKit.Clear(g);
                var b = EnemyKit.Place(g, EnemyKind.Spotlight, x, y + 0.5);
                var withParts = EnemyKit.ClearPickupsAndDraw(g);
                var snapshot = (uint[])withParts.P.Clone();
                b.Parts!.Clear();
                var bodyOnly = EnemyKit.ClearPickupsAndDraw(g);
                Assert.True(snapshot.SequenceEqual(bodyOnly.P), $"at ({b.X}, {b.Y}) the parts disagree with the body");
            }
    }

    /// <summary>The colour of every pixel of the column under the lens, from its foot down.</summary>
    private static (uint[] Left, uint[] Mid, uint[] Right) ColumnsUnderLens(RaiderGame g, Enemy b, int rowFrom, int rowTo)
    {
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int c = (int)Math.Floor(BeamX(b));
        uint[] Col(int x) => Enumerable.Range(rowFrom, rowTo - rowFrom).Select(y => f.Get(x, y) & EnemyKit.RgbMask).ToArray();
        return (Col(c - 1), Col(c), Col(c + 1));
    }

    /// <summary>The rows of the warning line a test reads: from this far below the boss's centre, clear of its body, to this far above
    /// the bottom, clear of the chevron and the ship.</summary>
    private const int LineTopBelowBoss = 12, LineBottomInset = 22;

    private static (int From, int To) LineRows(RaiderGame g, Enemy b) => ((int)(b.Y + LineTopBelowBoss), g.PH - LineBottomInset);

    [Fact]
    public void The_warning_line_is_dotted_then_solid_then_three_white_pixels_for_the_last_third()
    {
        const int InFirstThird = 5, InSecondThird = 20, InLastThird = 34;
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        var (from, to) = LineRows(g, b);

        AdvanceToKeepingShipClear(g, b, Charge, InFirstThird);
        var (l1, m1, r1) = ColumnsUnderLens(g, b, from, to);
        Assert.True(m1.Count(p => p == Dim) >= (to - from) / 3, "first third: a dotted line of the dim amber");
        Assert.True(m1.Count(p => p == Dim) <= (to - from) / 2 + 1, "dotted, not solid");
        Assert.DoesNotContain(Dim, l1);
        Assert.DoesNotContain(Dim, r1);

        AdvanceToKeepingShipClear(g, b, Charge, InSecondThird);
        var (l2, m2, r2) = ColumnsUnderLens(g, b, from, to);
        Assert.All(m2, p => Assert.Equal(Dim, p));
        Assert.DoesNotContain(Dim, l2);
        Assert.DoesNotContain(Palette.White, r2);

        AdvanceToKeepingShipClear(g, b, Charge, InLastThird);
        var (l3, m3, r3) = ColumnsUnderLens(g, b, from, to);
        Assert.All(l3, p => Assert.Equal(Palette.White, p));
        Assert.All(m3, p => Assert.Equal(Palette.White, p));
        Assert.All(r3, p => Assert.Equal(Palette.White, p));
    }

    [Fact]
    public void The_thirds_follow_a_longer_charge()
    {
        const int InSecondThird = 30, InLastThird = 47;   // of a charge of fifty-six frames
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        KillBattery(g, b, "battery-l"); KillBattery(g, b, "battery-r");
        var (from, to) = LineRows(g, b);
        AdvanceToKeepingShipClear(g, b, Charge, InSecondThird);
        var (_, m, _) = ColumnsUnderLens(g, b, from, to);
        Assert.All(m, p => Assert.Equal(Dim, p));
        AdvanceToKeepingShipClear(g, b, Charge, InLastThird);
        var (l, m2, _) = ColumnsUnderLens(g, b, from, to);
        Assert.All(m2, p => Assert.Equal(Palette.White, p));
        Assert.All(l, p => Assert.Equal(Palette.White, p));
    }

    [Fact]
    public void The_beam_is_nine_columns_of_solid_lamp_yellow_from_the_lens_to_the_bottom()
    {
        const int InsideTheFire = 5, InsideTheCool = 3, BelowMidField = 10;
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        AdvanceToKeepingShipClear(g, b, Charge, 1);
        StepShipOutOfBeam(g, b);   // a hit on the shield would draw its rings over the beam
        AdvanceToKeepingShipClear(g, b, Fire, InsideTheFire);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int c = (int)Math.Floor(BeamX(b));
        foreach (int y in new[] { (int)b.Y + LineTopBelowBoss, g.PH / 2 + BelowMidField, g.PH - 1 })
        {
            for (int x = c - BeamHalf; x <= c + BeamHalf; x++) Assert.Equal(Lamp, f.Get(x, y) & EnemyKit.RgbMask);
            Assert.NotEqual(Lamp, f.Get(c - BeamHalf - 1, y) & EnemyKit.RgbMask);
            Assert.NotEqual(Lamp, f.Get(c + BeamHalf + 1, y) & EnemyKit.RgbMask);
        }
        AdvanceToKeepingShipClear(g, b, Cool, InsideTheCool);
        var f2 = EnemyKit.ClearPickupsAndDraw(g);
        Assert.NotEqual(Lamp, f2.Get(c, g.PH - 1) & EnemyKit.RgbMask);
    }

    /// <summary>The chevron sits in this many rows above the bottom, from this many columns out from the beam's centre to short of this many.</summary>
    private const int ChevronRows = 12, ChevronInner = BeamHalf + 1, ChevronOuter = 14;

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_chevron_at_the_foot_of_the_line_shows_the_side_it_will_sweep_and_only_below_half(int side)
    {
        const int InTheCharge = 10, LeastChevronPixels = 4;
        var (g, b) = BelowHalfAtChargeStart(side * SideStep);
        AdvanceToKeepingShipClear(g, b, Charge, InTheCharge);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int c = (int)Math.Floor(BeamX(b));
        int Count(int s)
        {
            int n = 0;
            for (int y = g.PH - ChevronRows; y < g.PH; y++)
                for (int x = c + s * ChevronInner; x != c + s * ChevronOuter; x += s)
                    if ((f.Get(x, y) & EnemyKit.RgbMask) == Lamp) n++;
            return n;
        }
        Assert.True(Count(side) >= LeastChevronPixels, "a chevron on the side it will sweep");
        Assert.Equal(0, Count(-side));

        var g2 = EnemyKit.Game();
        var b2 = PlaceBossAndShip(g2);
        AdvanceToKeepingShipClear(g2, b2, Charge, InTheCharge);
        var f2 = EnemyKit.ClearPickupsAndDraw(g2);
        int c2 = (int)Math.Floor(BeamX(b2));
        for (int y = g2.PH - ChevronRows; y < g2.PH; y++)
            for (int x = c2 - ChevronOuter; x <= c2 + ChevronOuter; x++)
                if (Math.Abs(x - c2) >= ChevronInner) Assert.NotEqual(Lamp, f2.Get(x, y) & EnemyKit.RgbMask);
    }

    // ---------------------------------------------------------------- sizes

    [Theory]
    [InlineData(SmallestCols, SmallestRows)]
    [InlineData(NarrowCols, NarrowRows)]
    [InlineData(WideCols, WideRows)]
    [InlineData(EnemyKit.BigCols, EnemyKit.BigRows)]
    public void It_stays_inside_the_field_and_nothing_that_can_hurt_by_touch_is_under_the_floor(int cols, int rows)
    {
        const int WatchFrames = 700, SidePeriod = 200, SettleFrames = 100;
        var g = EnemyKit.Game(cols, rows);
        var b = g.SpawnKind(EnemyKind.Spotlight, EnemyKit.MidWidth);
        double floor = EnemyKit.BossFloor(g.PH);
        Assert.True(b.Y < 0, "it enters from above");
        for (int f = 1; f <= WatchFrames; f++)
        {
            SetShipPosition(g, f % SidePeriod < SidePeriod / 2 ? EdgeMargin : g.PW - EdgeMargin, ShipRow(g));
            TickClean(g);
            Assert.Contains(b, g.Enemies);
            Assert.True(EnemyKit.InsideWidth(g, b), $"frame {f}: x {b.X} outside {g.PW}");
            Assert.True(EnemyKit.LowestPixel(b) <= floor, $"frame {f}: lowest pixel {EnemyKit.LowestPixel(b)} under the floor {floor}");
            foreach (var p in b.Parts!.Where(p => p.State != PartState.Dead))
                Assert.True(EnemyKit.Box(b, p).B <= floor, $"frame {f}: {p.Name} reaches {EnemyKit.Box(b, p).B}");
            if (f >= SettleFrames) Assert.True(EnemyKit.Extent(b).T >= 0, $"frame {f}: the top is above the field once it has settled");
            if (f >= SettleFrames) Assert.Equal(Hold(g), b.Y, 6);
        }
    }

    /// <summary>The smallest field the game lays out, in character cells.</summary>
    internal const int SmallestCols = 30, SmallestRows = 16;

    /// <summary>A narrow field taller than it is wide, and a wide short one, in character cells.</summary>
    internal const int NarrowCols = 48, NarrowRows = 60, WideCols = 80, WideRows = 24;

    [Fact]
    public void On_the_smallest_field_it_has_the_short_form_fifteen_rows_tall_with_its_parts_moved_up()
    {
        const int SettleFrames = 80, ShortBatteryHeight = 4, TallBatteryHeight = 6;
        const double HalfShort = ShortHeight / 2.0, HalfTall = TallHeight / 2.0, HalfLens = LensHeight / 2.0;
        var small = EnemyKit.Game(SmallestCols, SmallestRows);
        var s = small.SpawnKind(EnemyKind.Spotlight, EnemyKit.MidWidth);
        Assert.Equal(ShortHeight, s.Sprite.H);
        Assert.Equal(BodyWidth, s.Sprite.W);
        var lens = PartOf(s, "lens");
        Assert.Equal(LensHeight, lens.HH * 2);
        var batL = PartOf(s, "battery-l");
        Assert.Equal(ShortBatteryHeight, batL.HH * 2);
        foreach (var p in s.Parts!)
        {
            var bx = EnemyKit.Box(s, p);
            Assert.True(bx.T >= s.Y - HalfShort - 0.5 && bx.B <= s.Y + HalfShort + 0.5, $"{p.Name} sticks out of the short body");
        }
        Assert.Equal(s.Y - HalfShort + ShortLensRow + HalfLens, s.Y + lens.OY, 6);
        for (int i = 0; i < SettleFrames; i++) TickClean(small);
        Assert.Equal(ShortHold, s.Y, 6);

        foreach (var (c, r) in new[] { (NarrowCols, NarrowRows), (WideCols, WideRows), (EnemyKit.BigCols, EnemyKit.BigRows) })
        {
            var g = EnemyKit.Game(c, r);
            var b = g.SpawnKind(EnemyKind.Spotlight, EnemyKit.MidWidth);
            Assert.True(g.PH >= ShortBelow);
            Assert.Equal(TallHeight, b.Sprite.H);
            Assert.Equal(TallBatteryHeight, PartOf(b, "battery-l").HH * 2);
            Assert.Equal(b.Y - HalfTall + TallLensRow + HalfLens, b.Y + PartOf(b, "lens").OY, 6);
        }
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_nine_hundred_frames_at_the_boss()
    {
        const int RunFrames = 900;
        (string, bool) Run(ulong seed)
        {
            var g = AtTheGate(seed);
            bool seen = false;
            var safeColumn = SafeColumn(new BotMemory());
            for (int f = 0; f < RunFrames; f++)
            {
                EnemyKit.BotFrame(g, safeColumn);
                g.Step();
                seen |= g.Boss?.Kind == EnemyKind.Spotlight;
            }
            var state = string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}:{e.Mode}:{e.ModeAge}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}";
            return (state, seen);
        }
        const ulong Seed = 11;
        var (a, seenA) = Run(Seed);
        var (b, _) = Run(Seed);
        Assert.Equal(a, b);
        Assert.True(seenA, "the gate brings the spotlight");
    }

    // ---------------------------------------------------------------- the voice

    private static RudePost Post(string text, long arrived, DarknessSource src = DarknessSource.Label) => new((ulong)arrived, text, src, "x", arrived);

    [Fact]
    public void It_takes_the_loudest_waiting_post_then_the_most_exclamation_marks_then_the_oldest()
    {
        var a = Post("you are all fools", 1);               // quiet, long, the oldest
        var b = Post("WHO ASKED", 2, DarknessSource.Hidden);    // all capitals, no exclamation
        var c = Post("LOUD!!! post", 3);                    // half capitals, three marks
        var d = Post("WHO ASKED!!", 4, DarknessSource.Demo);    // all capitals, two marks
        var store = new List<RudePost> { a, b, c, d };
        var order = new List<RudePost>();
        while (store.Count > 0)
        {
            int at = Spot.Taste(EnemyKit.Speaker(EnemyKind.Spotlight), store);
            order.Add(store[at]);
            store.RemoveAt(at);
        }
        Assert.Equal([d, b, c, a], order);

        var older = Post("WHO ASKED", 1);
        var newer = Post("WHO ASKED", 9);
        Assert.Equal(0, Spot.Taste(EnemyKit.Speaker(EnemyKind.Spotlight), new List<RudePost> { older, newer }));
        Assert.Equal(0, Spot.Taste(EnemyKit.Speaker(EnemyKind.Spotlight), new List<RudePost> { Post("no", 1) }));
    }

    [Fact]
    public void Each_cue_gets_its_stated_turn()
    {
        var g = EnemyKit.Game();
        var b = PlaceBossAndShip(g);
        Assert.Equal(Turn.Wait, Spot.TurnFor(g, b, VoiceCue.Arrive));
        Assert.Equal(Turn.Skip, Spot.TurnFor(g, b, VoiceCue.PartLost));
        Assert.Equal(Turn.Cut, Spot.TurnFor(g, b, VoiceCue.Phase));
        Assert.Equal(Turn.Cut, Spot.TurnFor(g, b, VoiceCue.Opens));
        Assert.Equal(Turn.Cut, Spot.TurnFor(g, b, VoiceCue.ShipDied));
        Assert.Equal(Turn.Wait, Spot.TurnFor(g, b, VoiceCue.Idle));
        Assert.Equal(Turn.Cut, Spot.TurnFor(g, b, VoiceCue.Dying));
        Assert.Equal(Turn.Cut, Spot.TurnFor(g, b, VoiceCue.Leaving));
    }

    /// <summary>A signed-in game on the fight's field, started at the gate of the spotlight's stage.</summary>
    private static RaiderGame AtTheGate(ulong seed)
    {
        var g = new RaiderGame(seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(SpotlightStage);
        return g;
    }

    private static (RaiderGame G, Enemy B) AtTheBoss(params string[] posts)
    {
        const ulong Seed = 3;
        const int FirstWordsLimit = 600;
        var g = AtTheGate(Seed);
        ulong i = 1;
        foreach (var p in posts) g.OfferDarkness(new DarknessPost(DarknessKind.Rude, i++, p));
        for (int f = 0; f < FirstWordsLimit && (g.Boss is null || g.Boss.Text is null); f++) { g.Step(); g.Bullets.RemoveAll(x => !x.Friendly); }
        Assert.NotNull(g.Boss);
        return (g, g.Boss!);
    }

    [Fact]
    public void With_no_post_waiting_it_says_its_own_line_under_its_name()
    {
        var (_, b) = AtTheBoss();
        Assert.Equal("Today's main character: you.", b.Text);
        Assert.Equal("SPOTLIGHT", b.Header);
    }

    [Fact]
    public void At_each_lock_it_holds_up_another_post_the_loudest_left_first_and_then_the_heard_ones_again()
    {
        var (g, b) = AtTheBoss("quiet one", "WHO ASKED!!", "loud One!");
        Assert.Equal("WHO ASKED!!", b.Text);
        const int JustSaid = 2;
        // two still wait, in its order of taste; then none waits and the loudest heard post that is not on show comes round again
        string[] atEachLock = ["loud One!", "quiet one", "WHO ASKED!!", "loud One!"];
        foreach (string held in atEachLock)
        {
            for (int i = 0; i < StepLimit && !(b.Mode == Charge && b.ModeAge == 1); i++)
            {
                g.Step(); g.Bullets.RemoveAll(x => !x.Friendly);
                if (b.Mode is Charge or Fire or Cool) SetShipPosition(g, b.X < g.PW / 2.0 ? g.PW - CornerInset : CornerInset, LowRow(g));
            }
            Assert.Equal(Charge, b.Mode);
            Assert.Equal(held, b.Text);
            Assert.InRange(b.SpeechBoxAge, 0, JustSaid);
            g.Step(); g.Bullets.RemoveAll(x => !x.Friendly);
        }
        Assert.Empty(g.Rude.Waiting);
    }

    [Fact]
    public void The_post_changes_below_half_at_a_lock_and_when_the_ship_dies_but_not_for_a_part_lost_taking_the_store_in_taste_order()
    {
        var (g, b) = AtTheBoss("you are all fools", "WHO ASKED", "LOUD!!! post", "WHO ASKED!!");
        Assert.Equal("WHO ASKED!!", b.Text);
        g.Cue(b, VoiceCue.PartLost);
        Assert.Equal("WHO ASKED!!", b.Text);
        var lens = PartOf(b, "lens");
        lens.Hp = lens.MaxHp / 2 - 1; b.Hp = lens.Hp;
        for (int i = 0; i < StepLimit && b.Mode != Charge; i++) { g.Step(); g.Bullets.RemoveAll(x => !x.Friendly); SetShipPosition(g, b.X, LowRow(g)); }
        Assert.Equal(Charge, b.Mode);
        Assert.Equal("LOUD!!! post", b.Text);   // below half it cut to the second loudest, and at this lock to the third
        g.Cue(b, VoiceCue.ShipDied);
        Assert.Equal("you are all fools", b.Text);
        Assert.Equal(["WHO ASKED!!", "WHO ASKED", "LOUD!!! post", "you are all fools"], g.Rude.Heard.Select(p => p.Text).ToArray());
        g.Cue(b, VoiceCue.ShipDied);
        Assert.Equal("WHO ASKED!!", b.Text);   // none waits: the loudest heard, again
    }

    [Fact]
    public void Below_half_with_the_store_empty_it_says_its_own_words_and_at_the_end_it_says_fade_out()
    {
        var (g, b) = AtTheBoss();
        var lens = PartOf(b, "lens");
        lens.Hp = lens.MaxHp / 2 - 1; b.Hp = lens.Hp;
        for (int i = 0; i < StepLimit && b.Mode != Charge; i++) { g.Step(); g.Bullets.RemoveAll(x => !x.Friendly); SetShipPosition(g, b.X, LowRow(g)); }
        Assert.Equal("Do not leave. They are watching.", b.Text);
        Assert.Equal("SPOTLIGHT", b.Header);
        EnemyKit.Shot(g, b.X + lens.OX, b.Y + lens.OY, LensOverkill);
        g.Step();
        Assert.True(b.Hp <= 0);
        Assert.Equal("Fade out.", b.Text);
    }

    // ---------------------------------------------------------------- the fight

    private sealed class BotMemory { public int Side; public double Column; }

    /// <summary>The column where the bot is safe from the beam: across the column from the side it stood on at the lock, in the last
    /// <see cref="LockWindow"/> frames of the charge and through the fire. Null while it is free to shoot the lens. It also steps aside
    /// from a bolt that is coming.</summary>
    private static Func<RaiderGame, double?> SafeColumn(BotMemory m) => g =>
    {
        var b = g.Boss;
        if (b is null || b.Parts is null) return null;
        var lens = b.Parts.First(p => p.Name == "lens");
        double beam = b.X + lens.OX;
        if (b.Mode == Charge && b.ModeAge == 1) { m.Side = g.PX < beam ? -1 : g.PX > beam ? 1 : (beam < g.PW / 2.0 ? 1 : -1); m.Column = beam; }
        int chargeLength = ChargeFrames + BatteryChargeBonus * b.Parts.Count(p => p.Name.StartsWith("battery") && p.State == PartState.Dead);
        if ((b.Mode == Charge && b.ModeAge >= chargeLength - LockWindow) || b.Mode == Fire)
            return Math.Clamp(m.Column - m.Side * BeamStandoff, EdgeMargin, g.PW - EdgeMargin);
        foreach (var bu in g.Bullets)
            if (!bu.Friendly && bu.Y > g.PY - BoltLookAhead && bu.Y < g.PY && Math.Abs(bu.X - g.PX) < BoltReach)
                return Math.Clamp(g.PX + (bu.X < g.PX ? DodgeStep : -DodgeStep), EdgeMargin, g.PW - EdgeMargin);
        return null;
    };

    /// <summary>Frames a bot needs to beat the spotlight at the given weapon level through the real gate, or -1.</summary>
    private static int Fight(ulong seed, int level)
    {
        var g = AtTheGate(seed);
        while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); }
        var memory = new BotMemory();
        var safeColumn = SafeColumn(memory);
        Enemy? boss = null; int start = 0;
        for (int f = 0; f < StepLimit; f++)
        {
            EnemyKit.BotFrame(g, safeColumn);
            g.Step();
            if (g.Boss is { } nb && boss is null) { boss = nb; start = f; }
            if (boss is not null && boss.Hp <= 0) return f - start;
        }
        return -1;
    }

    [Fact]
    public void A_bot_beats_it_in_eighteen_to_thirty_seconds_with_the_light_laser()
    {
        const int Seeds = 6, WeaponLevel = 1;
        const int MedianFastestSeconds = 18, MedianSlowestSeconds = 30, AnyFastestSeconds = 15, AnySlowestSeconds = 40;
        var times = new List<int>();
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            int t = Fight(seed, WeaponLevel);
            Assert.True(t > 0, $"seed {seed}: not beaten");
            times.Add(t);
        }
        times.Sort();
        int median = (times[Seeds / 2 - 1] + times[Seeds / 2]) / 2;
        output.WriteLine($"spotlight fight at level {WeaponLevel}, frames to beat: {string.Join(" ", times)}; median {median} ({median / (double)Tuning.Fps:0.0} s)");
        Assert.InRange(median, MedianFastestSeconds * Tuning.Fps, MedianSlowestSeconds * Tuning.Fps);
        Assert.InRange(times[0], AnyFastestSeconds * Tuning.Fps, AnySlowestSeconds * Tuning.Fps);
    }

    [Fact]
    public void A_ship_that_never_moves_dies()
    {
        const ulong Seed = 4;
        const int DeathWithin = 1500;
        var g = AtTheGate(Seed);
        int f = 0;
        for (; f < StepLimit && g.Deaths == 0; f++) g.Step();
        output.WriteLine($"an idle ship dies at frame {f}");
        Assert.True(g.Deaths > 0);
        Assert.True(f < DeathWithin);
    }
}
