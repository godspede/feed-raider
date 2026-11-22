using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Schism (boss 7): one body that tears into an ice half and a rose half. These tests give its machine (enter, joined, apart,
/// swap, mend), its warnings (rose's eye and path, the swap's lanes, the mending fill), its pixels, its fit on the four fields, its
/// two voices that cut each other off, and the fight (a bot at weapon level 3 over six seeds, and a ship that never moves).
/// </summary>
public sealed class SchismTests(ITestOutputHelper output)
{
    private const uint FrostLight = 0xd8d0e8, FrostDark = 0x8a7fa6, RoseLight = 0xff7fa8, RoseDark = 0x9f2f5a, Husk = 0x3a2c4f, Outline = 0x2a1740;

    /// <summary>Two speeds or two places this close are the same.</summary>
    private const double SameWithin = 0.01;

    /// <summary>Decimal places a row or hit points must agree to.</summary>
    private const int Places = 6;

    /// <summary>A pixel's rounding either way, for a place the view snaps to whole pixels.</summary>
    private const double HalfPixel = 0.5;
    private const double RoseSpeed = 1.6;
    private const int Entering = 0, Joined = 1, Apart = 2, Swapping = 3, Mending = 4;

    private static bool IsRoseBolt(Bullet b) => Math.Abs(Math.Sqrt(b.VX * b.VX + b.VY * b.VY) - RoseSpeed) < SameWithin;

    /// <summary>A half's sprite, the whole body's width, and the seam's sprite.</summary>
    private const int HalfSpriteW = 15, HalfSpriteH = 13, BodyW = 31, SeamW = 5, SeamH = 3;

    /// <summary>Half the width and half the height of a half's sprite, and half the width of the whole body.</summary>
    private const double HalfW = HalfSpriteW / 2.0, HalfH = HalfSpriteH / 2.0, BodyHalfW = BodyW / 2.0;

    /// <summary>How fast the halves swap sides, in pixels a frame.</summary>
    private const double SwapSpeed = 0.8;

    /// <summary>The stage the schism guards.</summary>
    private const int SchismStage = 7;

    /// <summary>The score for the whole boss, and for the first half to die.</summary>
    private const int BossScore = 7000, HalfScore = 3000;

    /// <summary>Frames it holds joined, between swaps, of a swap's warning, of mending and of rose's warning; frames between rose's
    /// bolts while both halves stand, and while the other half mends (forty per cent faster, rounded).</summary>
    private const int JoinedFrames = 300, SwapEvery = 360, SwapWarn = 30, MendFrames = 240, RoseWarn = 24, RoseEvery = 50, RoseEveryMending = 36;

    /// <summary>Frames between the ticks of the mending sound.</summary>
    private const int TickEvery = 60;

    /// <summary>The share of its hit points a mended half comes back with.</summary>
    private const double ReturnShare = 0.4;

    /// <summary>Where the halves stand apart, as shares of the field's width, and the most ice stands above rose after a swap.</summary>
    private const double NearShare = 0.25, FarShare = 0.75, StaggerMax = 10;

    /// <summary>The row the body holds: a fixed row on a field shorter than <see cref="ShortFieldH"/>, else the row every boss holds.</summary>
    private const int ShortFieldH = 48;
    private const double ShortHoldY = 11.5;

    /// <summary>The crimson above rose's gold eye, from its half's top left, where its warning shows; the row of both eyes' middles;
    /// and how far below rose's middle its path starts.</summary>
    private const int RoseBrowX = 11, BrowY = 5, EyeY = 6, RoseAimDrop = 4;

    /// <summary>The most hostile bolts it keeps on the field at once.</summary>
    private const int MaxHostile = 24;

    /// <summary>Damage enough to break the seam, and to kill a half outright.</summary>
    private const double BreakSeam = 1000, KillHalf = 100000;

    /// <summary>Frames given to a fresh schism to reach a mode, and to a quiet one to start speaking.</summary>
    private const int ModeWatchFrames = 1500, SpeakWatchFrames = 400;

    private static EnemyBehaviour Schism => RaiderGame.TableBehaviour(EnemyKind.Schism)!;
    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);

    private static double HoldY(RaiderGame g) => g.PH < ShortFieldH ? ShortHoldY : EnemyKit.BossRow(g.PH);
    private static double Floor(RaiderGame g) => EnemyKit.BossFloor(g.PH);

    /// <summary>One frame with the enemies other than the boss swept away and every hostile bolt taken off the field after it has been seen.</summary>
    private static void StepAlone(RaiderGame g, Enemy s, List<Bullet>? seen = null)
    {
        g.Step();
        g.Enemies.RemoveAll(e => e != s);
        if (seen is not null) foreach (var b in g.Bullets) if (!b.Friendly && !seen.Contains(b)) seen.Add(b);
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    /// <summary>A schism on the field, quiet and untroubled, stepped until it is in <paramref name="mode"/>.</summary>
    private static Enemy SpawnInMode(RaiderGame g, int mode)
    {
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        for (int i = 0; i < ModeWatchFrames && s.Mode != mode; i++) StepAlone(g, s);
        Assert.Equal(mode, s.Mode);
        return s;
    }

    /// <summary>A shot of this much damage on a part's middle, put there this frame.</summary>
    private static void Shoot(RaiderGame g, Enemy s, string part, double dmg)
    {
        var p = PartOf(s, part);
        EnemyKit.Shot(g, s.X + p.OX, s.Y + p.OY, dmg);
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Schism, Schism.Kind);
        Assert.Equal("SCHISM", Schism.Name);
        Assert.Equal(BossScore, Schism.Score);
        Assert.True(Schism.Boss);
        Assert.True(Schism.Speaks);
        Assert.True(Schism.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(BodyW, Schism.Body.W);
        Assert.Equal(HalfSpriteH, Schism.Body.H);
    }

    [Fact]
    public void It_is_an_ice_half_and_a_rose_half_round_a_gold_seam()
    {
        var g = EnemyKit.Game();
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        Assert.Equal(["ice", "rose", "seam"], s.Parts!.Select(p => p.Name));
        var ice = PartOf(s, "ice"); var rose = PartOf(s, "rose"); var seam = PartOf(s, "seam");
        Assert.True(ice.Vital); Assert.True(rose.Vital); Assert.False(seam.Vital);
        Assert.Equal(PartState.Open, seam.State);
        Assert.Equal(PartState.Armoured, ice.State);   // the halves are shut behind the seam while the body is whole
        Assert.Equal(PartState.Armoured, rose.State);
        Assert.Equal(ice.MaxHp, rose.MaxHp);
        Assert.Equal(HalfSpriteW, ice.HW * 2 + 1); Assert.Equal(HalfSpriteW, rose.HW * 2 + 1);
        Assert.Equal(SeamW, seam.HW * 2 + 1); Assert.Equal(SeamH, seam.HH * 2 + 1);
        Assert.True(seam.Hp < ice.Hp);
        Assert.Equal(ice.Hp + rose.Hp, s.Hp, Places);
        Assert.Equal(s.Hp, s.MaxHp, Places);
        Assert.True(ice.OX < 0 && rose.OX > 0 && seam.OX == 0);
    }

    // ---------------------------------------------------------------- the machine

    [Theory, MemberData(nameof(PileOnTests.Fields), MemberType = typeof(PileOnTests))]
    public void It_enters_holds_joined_then_comes_apart_when_the_seam_gives_or_three_hundred_frames_pass(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        Assert.Equal(Entering, s.Mode);
        Assert.True(s.Y < 0, "it enters from above");
        const int watchFrames = 700, mostEntryFrames = 200, frameSlack = 2;
        int joinedAt = -1, apartAt = -1;
        for (int f = 1; f <= watchFrames && apartAt < 0; f++)
        {
            StepAlone(g, s);
            if (joinedAt < 0 && s.Mode == Joined) { joinedAt = f; Assert.Equal(HoldY(g), s.Y, Places); Assert.True(s.ModeAge <= 1); }
            if (s.Mode == Joined) Assert.Equal(HoldY(g), s.Y, Places);
            if (apartAt < 0 && s.Mode == Apart) apartAt = f;
        }
        Assert.True(joinedAt > 0 && joinedAt < mostEntryFrames, $"joined at {joinedAt}");
        Assert.InRange(apartAt - joinedAt, JoinedFrames - 1, JoinedFrames + frameSlack);
        Assert.Equal(PartState.Dead, PartOf(s, "seam").State);
        Assert.Equal(0, g.Score);   // the seam gave way of itself: no score for it
        Assert.Equal(PartState.Open, PartOf(s, "ice").State);
        Assert.Equal(PartState.Open, PartOf(s, "rose").State);
    }

    [Fact]
    public void Breaking_the_seam_comes_apart_at_once()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        const int joinedFor = 20;
        for (int i = 0; i < joinedFor; i++) StepAlone(g, s);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        Assert.Equal(Apart, s.Mode);
        Assert.Equal(PartState.Dead, PartOf(s, "seam").State);
        Assert.Contains(s, g.Enemies);
        Assert.True(s.Hp > 0);
    }

    [Theory, MemberData(nameof(PileOnTests.Fields), MemberType = typeof(PileOnTests))]
    public void Apart_the_halves_slide_to_a_quarter_and_three_quarters_and_swap_every_three_hundred_sixty_frames(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var s = SpawnInMode(g, Joined);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        var ice = PartOf(s, "ice"); var rose = PartOf(s, "rose");
        double Cx(Part p) => s.X + p.OX;
        var swaps = new List<int>();
        const int watchFrames = 900, settledAt = 150, firstSwapSlack = 5;
        int prev = Apart;
        for (int f = 1; f <= watchFrames; f++)
        {
            StepAlone(g, s);
            if (s.Mode == Swapping && prev != Swapping) swaps.Add(f);
            prev = s.Mode;
            if (f == settledAt)
            {
                Assert.Equal(g.PW * NearShare, Cx(ice), 0);
                Assert.Equal(g.PW * FarShare, Cx(rose), 0);
            }
            Assert.True(Floor(g) >= EnemyKit.LowestPixel(s), $"frame {f}: lowest {EnemyKit.LowestPixel(s)} under the floor {Floor(g)}");
            Assert.True(EnemyKit.InsideWidth(g, s), $"frame {f}: outside the field {EnemyKit.Extent(s)}");
            Assert.True(EnemyKit.Extent(s).T >= -HalfPixel, $"frame {f}: above the field");
        }
        Assert.Equal(2, swaps.Count);
        Assert.Equal(SwapEvery, swaps[1] - swaps[0]);
        Assert.InRange(swaps[0], SwapEvery - firstSwapSlack, SwapEvery + firstSwapSlack);   // the first swap comes a swap's gap after coming apart
        // after the swap ice stands higher than rose by the stagger the field leaves room for
        double stagger = Math.Min(StaggerMax, Floor(g) - (HoldY(g) + HalfH));
        Assert.True(stagger >= 1);
        Assert.Equal(stagger, (s.Y + rose.OY) - (s.Y + ice.OY), HalfPixel + SameWithin);
    }

    [Fact]
    public void A_swap_takes_the_halves_across_at_eight_tenths_and_leaves_ice_on_the_right()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        var ice = PartOf(s, "ice"); var rose = PartOf(s, "rose");
        const int watchFrames = 600, swapSlack = 3;
        int swapFrames = 0;
        double lastIce = s.X + ice.OX;
        for (int f = 0; f < watchFrames; f++)
        {
            StepAlone(g, s);
            if (s.Mode != Swapping) { if (swapFrames > 0) break; lastIce = s.X + ice.OX; continue; }
            swapFrames++;
            Assert.True(Math.Abs((s.X + ice.OX) - lastIce) <= SwapSpeed + SameWithin);
            lastIce = s.X + ice.OX;
        }
        Assert.InRange(swapFrames, (int)(g.PW / 2 / SwapSpeed) - swapSlack, (int)(g.PW / 2 / SwapSpeed) + swapSlack);
        Assert.Equal(Apart, s.Mode);
        Assert.True(s.X + ice.OX > s.X + rose.OX, "ice is on the right after the swap");
    }

    [Fact]
    public void A_dead_half_mends_for_two_hundred_forty_frames_and_returns_with_forty_per_cent()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        const int apartFor = 100;
        for (int i = 0; i < apartFor; i++) StepAlone(g, s);
        var ice = PartOf(s, "ice"); var rose = PartOf(s, "rose");
        double max = ice.MaxHp;
        int before = g.Score;
        Shoot(g, s, "ice", KillHalf);
        StepAlone(g, s);
        Assert.Equal(PartState.Dead, ice.State);
        Assert.Equal(Mending, s.Mode);
        Assert.Equal(before + HalfScore, g.Score);   // only the first half to die scores
        for (int i = 1; i < MendFrames; i++) { StepAlone(g, s); Assert.Equal(PartState.Dead, ice.State); Assert.Equal(Mending, s.Mode); }
        StepAlone(g, s);
        Assert.Equal(PartState.Open, ice.State);
        Assert.Equal(Apart, s.Mode);
        Assert.Equal(ReturnShare * max, ice.Hp, Places);
        Assert.Equal(ice.Hp + rose.Hp, s.Hp, Places);
        before = g.Score;
        Shoot(g, s, "ice", KillHalf);
        StepAlone(g, s);
        Assert.Equal(before, g.Score);
        Assert.Equal(Mending, s.Mode);
    }

    [Fact]
    public void If_the_other_half_dies_inside_the_count_the_schism_is_dead_for_seven_thousand()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        Shoot(g, s, "ice", KillHalf);
        StepAlone(g, s);
        const int mendingFor = 200;
        for (int i = 0; i < mendingFor; i++) StepAlone(g, s);
        Assert.Equal(Mending, s.Mode);
        int before = g.Score;
        Shoot(g, s, "rose", KillHalf);
        StepAlone(g, s);
        Assert.DoesNotContain(s, g.Enemies);
        Assert.Equal(1, g.Kills);
        Assert.Equal(before + HalfScore + BossScore, g.Score);
    }

    [Fact]
    public void While_a_half_mends_the_survivor_shoots_forty_per_cent_faster_and_nothing_swaps()
    {
        const int apartFor = 60, mendWatchFrames = 230, apartWatchFrames = 300, fewestGaps = 3, intervalSlack = 1;
        double Interval(bool mending)
        {
            var g = EnemyKit.Game();
            var s = SpawnInMode(g, Joined);
            Shoot(g, s, "seam", BreakSeam);
            StepAlone(g, s);
            for (int i = 0; i < apartFor; i++) StepAlone(g, s);
            if (mending) { Shoot(g, s, "ice", KillHalf); StepAlone(g, s); }
            var seen = new List<Bullet>();
            var frames = new List<int>();
            for (int f = 0; f < (mending ? mendWatchFrames : apartWatchFrames); f++)
            {
                int n = seen.Count;
                StepAlone(g, s, seen);
                if (mending) { Assert.NotEqual(Swapping, s.Mode); Assert.Equal(0, s.Windup); }
                for (int k = n; k < seen.Count; k++) if (IsRoseBolt(seen[k])) frames.Add(f);
            }
            var gaps = frames.Distinct().Zip(frames.Distinct().Skip(1), (a, b) => b - a).ToList();
            Assert.True(gaps.Count >= fewestGaps, $"only {frames.Count} rose bolts");
            return gaps.Average();
        }
        double slow = Interval(false), fast = Interval(true);
        output.WriteLine($"rose interval apart {slow:0.0}, mending {fast:0.0}");
        Assert.InRange(slow, RoseEvery - intervalSlack, RoseEvery + intervalSlack);
        Assert.InRange(fast, RoseEveryMending - intervalSlack, RoseEveryMending + intervalSlack);
    }

    [Fact]
    public void It_does_not_leave_while_alive()
    {
        const int aliveFor = 2000;
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };   // no gate to send it off at the end of the stage
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        EnemyKit.Clear(g);
        var s = SpawnInMode(g, Joined);
        for (int i = 0; i < aliveFor; i++) { StepAlone(g, s); Assert.False(s.Leaving); }
        Assert.Contains(s, g.Enemies);
    }

    // ---------------------------------------------------------------- warnings

    [Fact]
    public void Rose_blinks_white_and_marks_its_path_for_twenty_four_frames_before_its_bolt_and_the_bolt_goes_where_it_marked()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        var rose = PartOf(s, "rose");
        var seen = new List<Bullet>();
        const int watchFrames = 400, wanderPeriod = 40;
        const double aimWithin = 0.15;
        int warnFrames = 0;
        bool bolt = false;
        double lockX = 0, lockY = 0;
        for (int f = 0; f < watchFrames && !bolt; f++)
        {
            int n = seen.Count;
            g.Input.Key(GameKey.Left, f % wanderPeriod < wanderPeriod / 2);   // the ship wanders: the lock must not follow it
            g.Input.Key(GameKey.Right, f % wanderPeriod >= wanderPeriod / 2);
            StepAlone(g, s, seen);
            if (rose.Timer > 0)
            {
                warnFrames++;
                if (warnFrames == 1) { lockX = s.AimX; lockY = s.AimY; }
                Assert.Equal(lockX, s.AimX); Assert.Equal(lockY, s.AimY);
                Assert.DoesNotContain(seen.Skip(n), b => IsRoseBolt(b));
                Assert.False(Schism.Touches(g, s));
            }
            else if (warnFrames > 0)
            {
                var fast = seen.Skip(n).Where(b => IsRoseBolt(b)).ToList();
                Assert.Single(fast);
                double a = Math.Atan2(lockY - fast[0].Y, lockX - fast[0].X);
                Assert.Equal(Math.Cos(a) * RoseSpeed, fast[0].VX, aimWithin);
                Assert.Equal(Math.Sin(a) * RoseSpeed, fast[0].VY, aimWithin);
                bolt = true;
            }
        }
        Assert.True(bolt, "rose fired");
        Assert.Equal(RoseWarn, warnFrames);
    }

    [Fact]
    public void Rose_s_brow_blinks_and_is_solid_white_for_the_last_third_and_a_dotted_line_runs_to_the_lock()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        var rose = PartOf(s, "rose");
        bool whiteInLastThird = true, blinkedOn = false, blinkedOff = false;
        const int watchFrames = 400, fewestDots = 4;
        int dotsAtStart = -1;
        for (int f = 0; f < watchFrames && !(rose.Timer == 1 && blinkedOn); f++)
        {
            StepAlone(g, s);
            if (rose.Timer <= 0) continue;
            var field = EnemyKit.ClearPickupsAndDraw(g);
            int ex = (int)Math.Round(s.X + rose.OX - HalfW) + RoseBrowX, ey = (int)Math.Round(s.Y + rose.OY - HalfH) + BrowY;
            bool white = (field.Get(ex, ey) & EnemyKit.RgbMask) == Palette.Tell;
            Assert.Equal(Husk, field.Get(ex, ey + 1) & EnemyKit.RgbMask);   // joined, the eye under the brow stays shut
            if (rose.Timer <= RoseWarn / 3) whiteInLastThird &= white;
            else if (white) blinkedOn = true; else blinkedOff = true;
            if (rose.Timer == RoseWarn) dotsAtStart = CountPathDots(field, s.AimX, s.AimY, s.X + rose.OX, s.Y + rose.OY + RoseAimDrop);
        }
        Assert.True(whiteInLastThird, "the brow is solid white for the last third");
        Assert.True(blinkedOn && blinkedOff, "and blinks before it");
        Assert.True(dotsAtStart >= fewestDots, $"{dotsAtStart} dots on the path to the lock");
    }

    /// <summary>Counts the white and rose-light pixels, the path's dots, on the line from (fx, fy) to (tx, ty).</summary>
    private static int CountPathDots(Surface f, double tx, double ty, double fx, double fy)
    {
        int n = 0, steps = (int)Math.Max(Math.Abs(tx - fx), Math.Abs(ty - fy));
        for (int i = 0; i <= steps; i++)
        {
            int x = (int)Math.Round(fx + (tx - fx) * i / steps), y = (int)Math.Round(fy + (ty - fy) * i / steps);
            if ((f.Get(x, y) & EnemyKit.RgbMask) == Palette.White || (f.Get(x, y) & EnemyKit.RgbMask) == RoseLight) n++;
        }
        return n;
    }

    [Fact]
    public void A_swap_is_announced_thirty_frames_ahead_by_both_halves_flashing_with_nothing_lethal_in_it()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        const int watchFrames = 500;
        int warn = 0;
        for (int f = 0; f < watchFrames && s.Mode != Swapping; f++)
        {
            StepAlone(g, s);
            if (s.Windup > 0)
            {
                warn++;
                Assert.Equal(Apart, s.Mode);
                Assert.False(Schism.Touches(g, s));
                if (s.Windup == 1) Assert.True(PartOf(s, "ice").Flash > 0);
            }
        }
        Assert.Equal(SwapWarn, warn);
        Assert.Equal(Swapping, s.Mode);
        Assert.Equal(0, s.Windup);
    }

    [Fact]
    public void A_ship_that_stands_through_rose_s_warning_and_then_steps_aside_lives()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        var rose = PartOf(s, "rose");
        g.Input.Key(GameKey.Fire, false);
        const int watchFrames = 600, stepAside = 6, afterFrames = 120;
        bool lockedYet = false;
        for (int f = 0; f < watchFrames; f++)
        {
            g.Step();
            g.Enemies.RemoveAll(e => e != s);
            // only rose's bolts are left to fly: ice's fan is left out
            g.Bullets.RemoveAll(b => !b.Friendly && !IsRoseBolt(b));
            if (rose.Timer == 1) lockedYet = true;
            if (lockedYet)
            {
                // the lock was taken at the start of the warning, where the ship stood, so a step aside clears the bolt
                g.Input.Key(GameKey.Left, g.PX > s.AimX - stepAside);
                g.Input.Key(GameKey.Right, false);
            }
            if (lockedYet && f > 0 && rose.Timer == 0 && g.Bullets.Any(b => !b.Friendly)) break;
        }
        Assert.True(lockedYet);
        for (int f = 0; f < afterFrames; f++)
        {
            g.Step();
            g.Enemies.RemoveAll(e => e != s);
            g.Bullets.RemoveAll(b => !b.Friendly && !IsRoseBolt(b));
            g.Input.Key(GameKey.Left, false);
            if (rose.Timer > 0) break;
        }
        Assert.Equal(0, g.Deaths);
    }

    [Fact]
    public void Mending_shows_the_dead_half_s_outline_filling_from_the_bottom_with_a_tick_every_sixty_frames()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        var s = SpawnInMode(g, Joined);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        var ice = PartOf(s, "ice");
        Shoot(g, s, "ice", KillHalf);
        StepAlone(g, s);
        played.Clear();
        int left = (int)Math.Round(s.X + ice.OX - HalfW), top = (int)Math.Round(s.Y + ice.OY - HalfH);
        // one column of the art is frost-dark low down and high up in the living half: the fill reaches the low row first
        const int artCol = 8, lowRow = 11, highRow = 1, halfMend = MendFrames / 2, nearlyMended = 236;
        uint At(int row) => EnemyKit.ClearPickupsAndDraw(g).Get(left + artCol, top + row) & EnemyKit.RgbMask;
        for (int i = 1; i <= halfMend; i++) StepAlone(g, s);
        Assert.Equal(FrostDark, At(lowRow));
        Assert.NotEqual(FrostDark, At(highRow));
        for (int i = halfMend + 1; i <= nearlyMended; i++) StepAlone(g, s);
        Assert.Equal(FrostDark, At(highRow));
        for (int i = nearlyMended + 1; i <= MendFrames; i++) StepAlone(g, s);
        Assert.Equal(MendFrames / TickEvery, played.Count(n => n == SfxNames.Tick));
    }

    // ---------------------------------------------------------------- pixels

    [Fact]
    public void Joined_it_is_drawn_in_shield_blue_and_rose_round_a_gold_knot_with_both_eyes_shut()
    {
        var g = EnemyKit.Game();
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        const double bodyX = 60.5, bodyY = 30;
        // art points: the knot at the middle and a crimson corner above it, a light and a dark shade on each half, the outline's top, and each eye
        const int knotX = 15, knotY = 6, cornerX = 14, cornerY = 5, shadeRow = 3, iceLightX = 4, iceDarkX = 2, roseLightX = 24, roseDarkX = 20;
        const int outlineX = 6, outlineY = 0, iceEyeX = 3, roseEyeX = 27;
        s.X = bodyX; s.BaseX = bodyX; s.Y = bodyY;
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(s.X - BodyHalfW), y0 = (int)Math.Round(s.Y - HalfH);
        Assert.Equal(Palette.Gold, f.Get(x0 + knotX, y0 + knotY) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Crimson, f.Get(x0 + cornerX, y0 + cornerY) & EnemyKit.RgbMask);
        Assert.Equal(Palette.ShieldGlow, f.Get(x0 + iceLightX, y0 + shadeRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.ShieldDeep, f.Get(x0 + iceDarkX, y0 + shadeRow) & EnemyKit.RgbMask);
        Assert.Equal(RoseLight, f.Get(x0 + roseLightX, y0 + shadeRow) & EnemyKit.RgbMask);
        Assert.Equal(RoseDark, f.Get(x0 + roseDarkX, y0 + shadeRow) & EnemyKit.RgbMask);
        Assert.Equal(Outline, f.Get(x0 + outlineX, y0 + outlineY) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Shield, f.Get(x0 + iceEyeX, y0 + EyeY) & EnemyKit.RgbMask);
        Assert.Equal(Husk, f.Get(x0 + roseEyeX, y0 + EyeY) & EnemyKit.RgbMask);
    }

    [Fact]
    public void The_seam_is_a_gold_target_on_crimson_and_the_halves_are_closed_art_until_it_breaks()
    {
        var g = EnemyKit.Game();
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        Assert.Equal(PartState.Armoured, PartOf(s, "ice").State);
        var seam = PartOf(s, "seam");
        var art = seam.OpenArt ?? seam.Closed;
        const int midRow = 1, midCol = 2, topRow = 0, crimsonCol = 1;
        Assert.Equal(Palette.Gold, art.Px[midRow * SeamW + midCol] & EnemyKit.RgbMask);
        Assert.Equal(Palette.Crimson, art.Px[topRow * SeamW + crimsonCol] & EnemyKit.RgbMask);
        Assert.Null(seam.Wreck);
    }

    [Fact]
    public void The_halves_are_drawn_exactly_over_the_body_for_every_whole_pixel_position()
    {
        const int firstX = 40, firstY = 20, positions = 4;
        var g = EnemyKit.Game();
        for (int x = firstX; x < firstX + positions; x++)
            for (int y = firstY; y < firstY + positions; y++)
            {
                EnemyKit.Clear(g);
                var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
                s.X = x + 0.5; s.BaseX = s.X; s.Y = y;
                var withParts = EnemyKit.ClearPickupsAndDraw(g);
                var snapshot = (uint[])withParts.P.Clone();
                s.Parts!.RemoveAll(p => p.Name != "seam");   // the knot is the seam's alone: the body under it is clear
                var bodyAndSeam = EnemyKit.ClearPickupsAndDraw(g);
                Assert.True(snapshot.SequenceEqual(bodyAndSeam.P), $"at ({s.X}, {y}) the halves disagree with the body");
            }
    }

    [Fact]
    public void A_dead_half_is_a_husk_and_the_other_half_is_unchanged()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        const int apartFor = 150, roseCol = 5, roseRow = 1, iceCol = 4, iceRow = 3;
        for (int i = 0; i < apartFor; i++) StepAlone(g, s);
        Shoot(g, s, "rose", KillHalf);
        StepAlone(g, s);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        var rose = PartOf(s, "rose"); var ice = PartOf(s, "ice");
        int rl = (int)Math.Round(s.X + rose.OX - HalfW), rt = (int)Math.Round(s.Y + rose.OY - HalfH);
        int il = (int)Math.Round(s.X + ice.OX - HalfW), it = (int)Math.Round(s.Y + ice.OY - HalfH);
        // a rose-dark pixel of the living art, so the husk shows it has been swapped out
        Assert.Equal(Husk, f.Get(rl + roseCol, rt + roseRow) & EnemyKit.RgbMask);
        Assert.Equal(FrostLight, f.Get(il + iceCol, it + iceRow) & EnemyKit.RgbMask);
    }

    // ---------------------------------------------------------------- sizes

    [Theory, MemberData(nameof(PileOnTests.Fields), MemberType = typeof(PileOnTests))]
    public void It_stays_inside_the_field_and_above_the_floor_through_a_whole_fight(int cols, int rows)
    {
        const ulong fightSeed = 3;
        var g = new RaiderGame(fightSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtBoss(SchismStage);
        const int watchFrames = 1500;
        Enemy? boss = null;
        int own = 0;
        for (int f = 0; f < watchFrames; f++)
        {
            EnemyKit.BotFrame(g, Dodge);
            g.Step();
            boss = g.Boss ?? boss;
            if (g.Boss is not { Kind: EnemyKind.Schism } s) { if (g.Gate == GateState.None && boss is not null) break; continue; }
            own = Math.Max(own, g.Bullets.Count(b => !b.Friendly));
            Assert.True(EnemyKit.InsideWidth(g, s), $"frame {f}: {EnemyKit.Extent(s)} outside {g.PW}");
            Assert.True(EnemyKit.LowestPixel(s) <= Floor(g) + SameWithin, $"frame {f}: lowest {EnemyKit.LowestPixel(s)} under the floor {Floor(g)}");
            if (s.Y >= HoldY(g)) Assert.True(EnemyKit.Extent(s).T >= -HalfPixel, $"frame {f}: top above the field");
            Assert.False(Schism.Touches(g, s));
        }
        Assert.NotNull(boss);
        Assert.True(own <= MaxHostile, $"{own} hostile bolts at once");
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_a_schism_in_it()
    {
        const int runFrames = 400;
        const ulong runSeed = 11;
        string Run(ulong seed)
        {
            var g = new RaiderGame(seed) { SignedIn = true };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtBoss(SchismStage);
            for (int f = 0; f < runFrames; f++)
            {
                EnemyKit.BotFrame(g, Dodge);
                g.Step();
            }
            return string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}:{e.Mode}")) + "#" +
                string.Join(",", g.Bullets.Select(b => $"{b.X:0.00}:{b.Y:0.00}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}";
        }
        string a = Run(runSeed);
        Assert.Equal(a, Run(runSeed));
        Assert.Contains("Schism", a, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- voice

    private static RaiderGame VoiceGame(params (string Text, int Age)[] posts)
    {
        var g = EnemyKit.Game();
        ulong seed = 1;
        int at = 0;
        foreach (var (text, age) in posts)
        {
            while (at < age) { g.Step(); at++; EnemyKit.Clear(g); }
            g.OfferDarkness(new DarknessPost(DarknessKind.Rude, seed++, text, DarknessSource.Label, "rude"));
        }
        return g;
    }

    // four posts that differ in length and in age: the first is oldest, the last the newest
    private static readonly (string Text, int Age)[] Four =
    [
        ("you absolute clown, log off and touch grass for once in your life", 0),
        ("ratio", 40),
        ("nobody asked, nobody cares, nobody is reading this far down", 80),
        ("lol no", 120),
    ];

    [Fact]
    public void Ice_takes_the_post_that_has_waited_longest_and_rose_the_one_that_has_waited_least_and_they_take_turns()
    {
        var g = VoiceGame(Four);
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        g.Enemies.RemoveAll(e => e != s);
        for (int i = 0; i < SpeakWatchFrames && s.Text is null; i++) StepAlone(g, s);
        Assert.Equal(Four[0].Text, s.Text);               // ice: the longest waited
        Assert.Equal("FROST", s.Voice!.Prefix);
        Assert.Equal("SCHISM", Schism.Name);
        g.Cue(s, VoiceCue.Phase);
        Assert.Equal(Four[3].Text, s.Text);               // rose: the shortest
        g.Step();
        Assert.Equal("ROSE", s.Voice!.Prefix);
        g.Cue(s, VoiceCue.Phase);
        Assert.Equal(Four[1].Text, s.Text);               // ice again: the longest of those left
        g.Step();
        Assert.Equal("FROST", s.Voice!.Prefix);
        g.Cue(s, VoiceCue.Phase);
        Assert.Equal(Four[2].Text, s.Text);               // and the last one is rose's
        g.Step();
        Assert.Equal("ROSE", s.Voice!.Prefix);
    }

    [Fact]
    public void Each_half_cuts_the_other_off_in_the_middle_of_a_page()
    {
        var g = VoiceGame(Four);
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        g.Enemies.RemoveAll(e => e != s);
        for (int i = 0; i < SpeakWatchFrames && s.Text is null; i++) StepAlone(g, s);
        const int intoThePage = 6;
        for (int i = 0; i < intoThePage; i++) StepAlone(g, s);
        Assert.True(s.SpeechBoxAge < EnemySpeechBox.Frames(s), "ice is still typing its page");
        string first = s.Text!;
        g.Cue(s, VoiceCue.Phase);
        Assert.NotEqual(first, s.Text);
        Assert.Equal(0, s.SpeechBoxAge);   // rose's words begin at once: nothing waited for ice to finish
    }

    [Fact]
    public void Breaking_the_seam_gets_a_no_they_do_not_from_an_empty_store()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        const int joinedFor = 30;
        for (int i = 0; i < joinedFor; i++) StepAlone(g, s);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        Assert.Equal("No they do not.", s.Text);
        Assert.Equal("SCHISM", s.Header);
    }

    [Fact]
    public void With_the_store_empty_it_says_its_own_lines_under_its_own_name()
    {
        var g = EnemyKit.Game();
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        for (int i = 0; i < SpeakWatchFrames && s.Text is null; i++) StepAlone(g, s);
        Assert.Equal("Both sides have a point.", s.Text);
        Assert.Equal("SCHISM", s.Header);
        Assert.Equal("Both sides have a point.", Schism.Line(s, VoiceCue.Arrive));
        Assert.Equal("No they do not.", Schism.Line(s, VoiceCue.Phase));
        Assert.Equal("We are working on it.", Schism.Line(s, VoiceCue.PartLost));
        Assert.Equal("Agree to dis-", Schism.Line(s, VoiceCue.Dying));
    }

    [Fact]
    public void A_half_that_dies_says_we_are_working_on_it_and_the_survivor_speaks_alone_while_it_mends()
    {
        var g = EnemyKit.Game();
        var s = SpawnInMode(g, Joined);
        Shoot(g, s, "seam", BreakSeam);
        StepAlone(g, s);
        const int apartFor = 50;
        for (int i = 0; i < apartFor; i++) StepAlone(g, s);
        Shoot(g, s, "ice", KillHalf);
        StepAlone(g, s);
        Assert.Equal("We are working on it.", s.Text);
        Assert.Equal("ROSE", s.Voice!.Prefix);   // ice is gone: only rose can speak
        g.Cue(s, VoiceCue.Phase);
        g.Step();
        Assert.Equal("ROSE", s.Voice!.Prefix);
    }

    [Fact]
    public void With_none_waiting_the_halves_go_on_arguing_with_the_heard_posts_ice_the_oldest_rose_the_newest()
    {
        var g = VoiceGame(Four);
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        g.Enemies.RemoveAll(e => e != s);
        for (int i = 0; i < SpeakWatchFrames && s.Text is null; i++) StepAlone(g, s);
        for (int i = 1; i < Four.Length; i++) g.Cue(s, VoiceCue.Phase);
        Assert.Empty(g.Rude.Waiting);
        // heard, in the order said: Four[0] (ice), Four[3] (rose), Four[1] (ice), Four[2] (rose, on show)
        g.Cue(s, VoiceCue.Phase);
        Assert.Equal(Four[0].Text, s.Text);   // ice: the oldest heard
        g.Step();
        Assert.Equal("FROST", s.Voice!.Prefix);
        g.Cue(s, VoiceCue.Phase);
        Assert.Equal(Four[2].Text, s.Text);   // rose: the newest heard that this round has not had
        g.Step();
        Assert.Equal("ROSE", s.Voice!.Prefix);
        Assert.Equal("RUDE", s.Header);
    }

    [Fact]
    public void Left_alone_the_halves_trade_posts_at_their_own_quick_pace()
    {
        const int ArgueSeconds = 4;
        Assert.Equal(ArgueSeconds * Tuning.Fps, Schism.IdleFrames(EnemyKit.Speaker(EnemyKind.Schism)));
        Assert.True(Schism.IdleFrames(EnemyKit.Speaker(EnemyKind.Schism)) < Tuning.VoiceIdleFrames);
    }

    [Fact]
    public void The_ship_dying_does_not_hand_the_floor_to_the_other_half()
    {
        var g = VoiceGame(Four[0]);
        var s = g.SpawnKind(EnemyKind.Schism, EnemyKit.MidWidth);
        g.Enemies.RemoveAll(e => e != s);
        for (int i = 0; i < SpeakWatchFrames && s.Text is null; i++) StepAlone(g, s);
        Assert.Equal("FROST", s.Voice!.Prefix);
        Assert.Equal(Turn.Wait, Schism.TurnFor(g, s, VoiceCue.ShipDied));
        g.Step();
        Assert.Equal("FROST", s.Voice!.Prefix);
    }

    // ---------------------------------------------------------------- the fight

    /// <summary>The bot ignores bolts slower than this or more than this many frames away, dodges those landing within the half width, ends a step away from them, and keeps this far inside the field's edges.</summary>
    private const double MinThreatSpeed = 0.05, LookAheadFrames = 40, HitHalfWidth = 7, DodgeStep = 9, EdgeMargin = 4;

    /// <summary>A plain bot's dodge: steps away from the nearest hostile bolt that will cross the ship's row within the look-ahead.</summary>
    private static double? Dodge(RaiderGame g)
    {
        double? best = null; double bestT = LookAheadFrames;
        foreach (var b in g.Bullets)
        {
            if (b.Friendly || b.VY <= MinThreatSpeed) continue;
            double t = (g.PY - b.Y) / b.VY;
            if (t < 0 || t > bestT) continue;
            double x = b.X + b.VX * t;
            if (Math.Abs(x - g.PX) > HitHalfWidth) continue;
            bestT = t;
            best = x < g.PX ? Math.Min(g.PW - EdgeMargin, x + DodgeStep) : Math.Max(EdgeMargin, x - DodgeStep);
        }
        return best;
    }

    /// <summary>The longest a fight runs before it counts as not won, in minutes and in frames.</summary>
    private const int FightMinutes = 5, FightFrameCap = FightMinutes * 60 * Tuning.Fps;

    /// <summary>The weapon level the bot fights at.</summary>
    private const int BotWeaponLevel = 3;

    /// <summary>Frames from the boss appearing until it dies, for a bot at <see cref="BotWeaponLevel"/>; for a ship that stands
    /// <paramref name="still"/>, until the ship's first death instead; -1 if neither comes inside <see cref="FightFrameCap"/>.</summary>
    private static int Fight(ulong seed, bool still = false)
    {
        var g = new RaiderGame(seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtStage(SchismStage);
        EnemyKit.Clear(g);
        while (g.WeaponLevel < BotWeaponLevel) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); EnemyKit.Clear(g); }
        g.StartAtBoss(SchismStage);
        int start = -1;
        for (int f = 0; f < FightFrameCap; f++)
        {
            if (!still) EnemyKit.BotFrame(g, Dodge);
            g.Step();
            if (start < 0 && g.Boss is not null) start = f;
            if (start >= 0 && g.Boss is null && g.Kills > 0) return f - start;
            if (still && g.Deaths > 0) return f - start;
        }
        return -1;
    }

    [Fact]
    public void A_bot_at_weapon_level_three_beats_it_inside_the_band_over_six_seeds()
    {
        const ulong seeds = 6;
        const int fastestSeconds = 21, slowestSeconds = 33;
        var times = new List<int>();
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            int t = Fight(seed);
            Assert.True(t > 0, $"seed {seed}: not beaten in {FightMinutes} minutes");
            times.Add(t);
        }
        times.Sort();
        double median = (times[times.Count / 2 - 1] + times[times.Count / 2]) / 2.0;
        output.WriteLine($"schism fight at level {BotWeaponLevel}, frames to kill: {string.Join(" ", times)}; median {median} ({median / (double)Tuning.Fps:0.0} s)");
        Assert.InRange(median, fastestSeconds * Tuning.Fps, slowestSeconds * Tuning.Fps);   // tuned to the short end
    }

    [Fact]
    public void A_ship_that_never_moves_dies_in_the_fight()
    {
        const ulong seeds = 3;
        const int hitWithinFrames = 1200;
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            int t = Fight(seed, still: true);
            Assert.True(t > 0, $"seed {seed}: a ship that never moves lived");
            Assert.True(t < hitWithinFrames, $"seed {seed}: it took {t} frames to hit a ship that stands still");
        }
    }
}
