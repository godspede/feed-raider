using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Brood (stage 6's boss): a fat sac with four hatches under it. A hatch is hurt only while open; it opens for 45 frames every 70
/// (every 45 below half the sac's hit points), bulging for 24 first, and lets two gnats out; no more than ten gnats of the Brood live at
/// once; with every hatch dead the sac charges for 30 frames and throws a ring of eight spores every 80. These tests give its parts,
/// its machine on the four fields, its warnings, its safety, its pixels, its voice (it takes a new post only when a hatch dies) and the
/// fight (a bot at weapon level 3).
/// </summary>
public sealed class BroodTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Fields() => EnemyKit.FieldSizes.Select(f => new object[] { f.Cols, f.Rows });

    private const uint Bile = 0xc8e03c;
    private const uint Plum = 0x4b2a78;
    private const uint Outline = Palette.Plum;

    /// <summary>The body's size in pixels.</summary>
    private const int BodyW = 33, BodyH = 13;

    /// <summary>A hatch's size in pixels and the body row its top is on.</summary>
    private const int HatchW = 5, HatchH = 4, HatchRow = 9;

    /// <summary>The body column of each hatch's left edge.</summary>
    private static readonly int[] HatchLefts = [3, 10, 18, 25];

    /// <summary>The sac's and a hatch's hit points at the first stage, a hatch's score and the Brood's.</summary>
    private const int SacHp = 48, HatchHp = 10, HatchScore = 300, BroodScore = 6000;

    /// <summary>A sac hit point count under half of <see cref="SacHp"/>, which sends the Brood into frenzy.</summary>
    private const int FrenzySacHp = 20;

    /// <summary>A hatch's turn comes every <see cref="LayGap"/> frames (<see cref="FrenzyGap"/> in frenzy): it bulges, then stays open.</summary>
    private const int LayGap = 70, FrenzyGap = 45, BulgeFrames = 24, OpenFrames = 45;

    /// <summary>The most gnats of one Brood alive at once, and how many a hatch lays.</summary>
    private const int GnatCap = 10, GnatsPerHatch = 2;

    /// <summary>Frames a test watches to see that something holds for good: ten hatch turns.</summary>
    private const int LongWatch = 10 * LayGap;

    /// <summary>With every hatch dead: frames of charge before the first ring, frames between rings, spores in a ring, their speed
    /// and the radius the ring is drawn at.</summary>
    private const int ChargeFrames = 30, RingGap = 80, RingSpores = 8;
    private const double SporeSpeed = 0.85, RingRadius = 8;

    /// <summary>The Brood's modes after laying.</summary>
    private const int ModeFrenzy = 2, ModeBarren = 3;

    /// <summary>On a field under this many pixels tall the Brood's top sits on <see cref="TopRow"/>, the first row the health bar leaves free.</summary>
    internal const int ShortFieldHeight = 48, TopRow = 5;

    private const int BroodStage = 6;

    /// <summary>Damage that kills a hatch, and damage that kills the sac, in one shot.</summary>
    private const double HatchKillingDamage = 50, SacKillingDamage = 500;

    /// <summary>How far above the Brood's centre a shot meets the sac.</summary>
    private const double SacAboveCentre = 4;

    /// <summary>Frames a test waits for the speech box to change, and for a change that first waits for the box on show to be read out.</summary>
    private const int SpeechWait = 300, LongSpeechWait = 600;

    /// <summary>The weapon level the fight bot plays at.</summary>
    private const int FightLevel = 3;

    /// <summary>Frames a fight may run before the bot is counted as beaten: five minutes.</summary>
    private const int FightTimeLimit = 5 * 60 * Tuning.Fps;

    /// <summary>A grace longer than any test runs.</summary>
    private const int HarmlessGrace = 100000;

    /// <summary>The bot shoots a gnat when it is this close (x distance plus <see cref="RowWeight"/> of the y distance).</summary>
    private const double GnatReach = 36;

    /// <summary>The widest offset from the sac's centre the lane search tries, and its step.</summary>
    private const double LaneRange = 14, LaneStep = 0.5;

    /// <summary>A lane clearer than this many pixels of gap is as good as any wider one.</summary>
    private const double EnoughClearance = 1.5;

    /// <summary>Score weights of the lane search: clearance counts most, then staying near the current lane, then the sac's centre.</summary>
    private const double ClearanceWeight = 10, StayWeight = 0.1, CentreWeight = 0.02;

    /// <summary>How much less a row of distance counts than a column when the bot picks its nearest gnat.</summary>
    private const double RowWeight = 0.5;

    /// <summary>The bot's two main shots fly this many pixels either side of the ship.</summary>
    private const double MainShotOffset = 2;

    private static EnemyBehaviour Brood => RaiderGame.TableBehaviour(EnemyKind.Brood)!;

    /// <summary>Where the Brood holds its centre: on a short field its top is on <see cref="TopRow"/>, else on the boss row; the Brood
    /// keeps its centre on a half pixel, so its parts and its body round alike.</summary>
    private static double Hold(RaiderGame g) => g.PH < ShortFieldHeight ? TopRow + BodyH / 2.0 : Math.Floor(EnemyKit.BossRow(g.PH)) + 0.5;

    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);
    private static IEnumerable<Part> Hatches(Enemy e) => e.Parts!.Where(p => p.Name.StartsWith("hatch"));

    /// <summary>A Brood placed at its hold, settled, in the middle of the field.</summary>
    private static Enemy Settled(RaiderGame g)
    {
        var e = EnemyKit.Place(g, EnemyKind.Brood, g.PW / 2.0 + 0.5, Hold(g));
        e.Amp = 0;
        return e;
    }

    private static List<Enemy> Gnats(RaiderGame g, Enemy owner) => g.Enemies.Where(e => e.Kind == EnemyKind.Gnat && ReferenceEquals(e.Parent, owner)).ToList();

    /// <summary>Makes every gnat of the Brood harmless for good, so a test can watch the timeline without the ship being hurt (a hit stops the game for a few frames).</summary>
    private static void Soothe(RaiderGame g) { foreach (var x in g.Enemies) if (x.Kind == EnemyKind.Gnat) x.Grace = HarmlessGrace; }

    private static void Kill(Enemy brood, string hatch)
    {
        var p = PartOf(brood, hatch);
        p.Hp = 0; p.State = PartState.Dead;
    }

    private static void Open(Enemy brood, string hatch)
    {
        var p = PartOf(brood, hatch);
        p.State = PartState.Open; p.Timer = OpenFrames;
    }

    /// <summary>A game with a cleared field and a recording of the sounds it plays.</summary>
    private static (RaiderGame G, List<string> Played) Listening()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        return (g, played);
    }

    /// <summary>The top left corner the body is drawn at (the Brood keeps to half pixels, so this is a whole pixel).</summary>
    private static (int X, int Y) Corner(Enemy e) => ((int)Math.Round(e.X - e.Sprite.W / 2.0), (int)Math.Round(e.Y - e.Sprite.H / 2.0));

    /// <summary>The middle of the second row of a hatch, counted from 0, relative to the body's corner: the plate a bulge blinks.</summary>
    private static (int X, int Y) PlateOf(int hatch) => (HatchLefts[hatch] + HatchW / 2, HatchRow + 1);

    /// <summary>The colour drawn at a pixel relative to the body's corner.</summary>
    private static uint At(Surface f, (int X, int Y) corner, int x, int y) => f.Get(corner.X + x, corner.Y + y) & EnemyKit.RgbMask;

    /// <summary>A game at the Brood's gate on the big field.</summary>
    private static RaiderGame AtTheGate(ulong seed)
    {
        var g = new RaiderGame(seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(BroodStage);
        return g;
    }

    private static void KillSac(RaiderGame g, Enemy b) => EnemyKit.Shot(g, b.X, b.Y - SacAboveCentre, SacKillingDamage);

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Brood, Brood.Kind);
        Assert.Equal("BROOD", Brood.Name);
        Assert.Equal(BroodScore, Brood.Score);
        Assert.True(Brood.Boss);
        Assert.True(Brood.Speaks);
        Assert.True(Brood.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(Bile, Brood.Colour);
        Assert.Equal(BodyW, Brood.Body.W);
        Assert.Equal(BodyH, Brood.Body.H);
    }

    [Fact]
    public void It_is_a_sac_over_four_armoured_hatches_of_five_by_four()
    {
        var g = EnemyKit.Game();
        var b = g.SpawnKind(EnemyKind.Brood, EnemyKit.MidWidth);
        Assert.Equal(["sac", "hatch-1", "hatch-2", "hatch-3", "hatch-4"], b.Parts!.Select(p => p.Name));
        var sac = PartOf(b, "sac");
        Assert.Equal(PartState.Open, sac.State);
        Assert.True(sac.Vital);
        Assert.Equal(BodyW, sac.HW * 2 + 1);
        Assert.Equal(SacHp * Ramp.HpMul(1), sac.Hp, 9);
        Assert.Equal(sac.Hp, b.Hp, 9);
        for (int i = 0; i < HatchLefts.Length; i++)
        {
            var h = PartOf(b, $"hatch-{i + 1}");
            Assert.Equal(PartState.Armoured, h.State);
            Assert.False(h.Vital);
            Assert.Equal(HatchHp, h.Hp);
            Assert.Equal(HatchScore, h.Score);
            Assert.Equal(HatchW, h.Closed.W);
            Assert.Equal(HatchH, h.Closed.H);
            Assert.Equal(HatchW, h.OpenArt!.W);
            Assert.Null(h.Wreck);   // nothing is left when it dies: the shots fly on through the hole
            Assert.Equal(HatchLefts[i] + HatchW / 2.0 - BodyW / 2.0, h.OX, 9);
            Assert.Equal(HatchRow + HatchH / 2.0 - BodyH / 2.0, h.OY, 9);
            Assert.Equal(HatchW, h.HW * 2 + 1);
        }
    }

    [Fact]
    public void A_boss_from_the_gate_has_the_sacs_hit_points_written_for_it()
    {
        var g = AtTheGate(3);
        const int gateWait = 200;
        for (int i = 0; i < gateWait && g.Gate != GateState.Fighting; i++) g.Step();
        Assert.Equal(GateState.Fighting, g.Gate);
        Assert.Equal(EnemyKind.Brood, g.Boss!.Kind);
        Assert.Equal(SacHp, PartOf(g.Boss, "sac").Hp, 9);
        Assert.Equal(SacHp, g.Boss.MaxHp, 9);
        Assert.Equal(HatchHp, PartOf(g.Boss, "hatch-1").Hp, 9);
    }

    // ---------------------------------------------------------------- hatches: hurt only while open

    [Fact]
    public void A_closed_hatch_stops_a_shot_with_a_ping_and_takes_nothing()
    {
        var (g, played) = Listening();
        var b = Settled(g);
        var h = PartOf(b, "hatch-2");
        double sac = PartOf(b, "sac").Hp;
        EnemyKit.Shot(g, b.X + h.OX, b.Y + h.OY, HatchHp / 2.0);
        EnemyKit.StepSweeping(g);
        Assert.Equal(HatchHp, h.Hp);
        Assert.Equal(PartState.Armoured, h.State);
        Assert.Equal(sac, PartOf(b, "sac").Hp);
        Assert.Contains(SfxNames.Ping, played);
        Assert.DoesNotContain(g.Bullets, x => x.Friendly);
    }

    [Fact]
    public void An_open_hatch_takes_the_damage_and_a_bomb_hurts_only_the_open_ones()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        Open(b, "hatch-3");
        var h = PartOf(b, "hatch-3");
        const double half = HatchHp / 2.0;
        EnemyKit.Shot(g, b.X + h.OX, b.Y + h.OY, half);
        EnemyKit.StepSweeping(g);
        Assert.Equal(half, h.Hp, 6);
        g.Input.BombPressed = true;
        EnemyKit.StepSweeping(g);
        Assert.True(h.Hp < half || h.State == PartState.Dead, "a bomb hurts an open hatch");
        Assert.Equal(HatchHp, PartOf(b, "hatch-1").Hp);
        Assert.Equal(PartState.Armoured, PartOf(b, "hatch-1").State);
    }

    [Fact]
    public void A_hatch_that_opens_closes_again_unhurt_by_the_shots_that_met_its_plate()
    {
        const double underCentre = 3, scratch = 0.5;   // a weak shot at the plate, just under the hatch's centre
        var (g, _) = Listening();
        var b = Settled(g);
        for (int f = 1; f <= 2 * LayGap; f++)
        {
            EnemyKit.StepSweeping(g);
            foreach (var h in Hatches(b))
            {
                EnemyKit.Shot(g, b.X + h.OX, b.Y + h.OY + underCentre, scratch);
                if (h.State == PartState.Armoured) Assert.Equal(HatchHp, h.Hp);
            }
        }
    }

    [Fact]
    public void Killing_a_hatch_scores_three_hundred_and_leaves_the_sac_to_be_shot_through_the_gap()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        Open(b, "hatch-2");
        var h = PartOf(b, "hatch-2");
        int before = g.Score;
        EnemyKit.Shot(g, b.X + h.OX, b.Y + h.OY, HatchKillingDamage);
        EnemyKit.StepSweeping(g);
        Assert.Equal(PartState.Dead, h.State);
        Assert.Equal(before + HatchScore, g.Score);
        Assert.Contains(b, g.Enemies);
        double sac = PartOf(b, "sac").Hp;
        const double scratch = 2;
        const int flightToSac = 2;   // frames for a shot at the dead hatch's spot to fly on and meet the sac above it
        EnemyKit.Shot(g, b.X + h.OX, b.Y + h.OY, scratch);
        EnemyKit.StepSweeping(g, flightToSac);
        Assert.True(PartOf(b, "sac").Hp < sac, "a dead hatch stands in the way");
    }

    // ---------------------------------------------------------------- the machine: laying

    [Fact]
    public void The_first_hatch_bulges_for_24_frames_then_opens_for_45_and_lays_two_gnats()
    {
        var (g, played) = Listening();
        var b = Settled(g);
        var h = PartOf(b, "hatch-1");
        const int bulge = LayGap - BulgeFrames, close = LayGap + OpenFrames;
        for (int f = 1; f < bulge; f++) EnemyKit.StepSweeping(g);
        Assert.Equal(0, h.Timer);
        Assert.DoesNotContain(SfxNames.Open, played);
        EnemyKit.StepSweeping(g);
        Assert.Equal(BulgeFrames, h.Timer);
        Assert.Equal(PartState.Armoured, h.State);
        Assert.Contains(SfxNames.Open, played);                          // the warning sound is the bulge's, not the opening's
        for (int f = bulge + 1; f < LayGap; f++) EnemyKit.StepSweeping(g);
        Assert.Equal(1, h.Timer);
        Assert.Equal(PartState.Armoured, h.State);
        Assert.Empty(Gnats(g, b));
        EnemyKit.StepSweeping(g);
        Assert.Equal(PartState.Open, h.State);
        Assert.Equal(OpenFrames, h.Timer);
        Assert.Equal(GnatsPerHatch, Gnats(g, b).Count);
        Assert.Contains(SfxNames.Hatch, played);
        for (int f = LayGap + 1; f < close; f++) EnemyKit.StepSweeping(g);
        Assert.Equal(PartState.Open, h.State);
        Assert.Equal(1, h.Timer);
        EnemyKit.StepSweeping(g);
        Assert.Equal(PartState.Armoured, h.State);
        Assert.Equal(0, h.Timer);
    }

    [Fact]
    public void The_hatches_take_their_turn_one_after_another_every_70_frames()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        const int frames = 300;   // every hatch's turn, and some of the next round
        var opened = new List<(int Frame, string Name)>();
        var was = new Dictionary<string, PartState>();
        for (int f = 1; f <= frames; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            foreach (var h in Hatches(b))
            {
                if (h.State == PartState.Open && was.GetValueOrDefault(h.Name) != PartState.Open) opened.Add((f, h.Name));
                was[h.Name] = h.State;
            }
        }
        Assert.Equal([(LayGap, "hatch-1"), (2 * LayGap, "hatch-2"), (3 * LayGap, "hatch-3"), (4 * LayGap, "hatch-4")], opened);
    }

    [Fact]
    public void The_gnats_come_out_of_the_hatch_that_opened()
    {
        const double side = 4, below = 8;   // a gnat starts just beside and under its hatch's centre
        var (g, _) = Listening();
        var b = Settled(g);
        for (int f = 1; f <= LayGap; f++) EnemyKit.StepSweeping(g);
        var h = PartOf(b, "hatch-1");
        foreach (var gnat in Gnats(g, b))
        {
            Assert.InRange(gnat.X, b.X + h.OX - side, b.X + h.OX + side);
            Assert.InRange(gnat.Y, b.Y + h.OY, b.Y + h.OY + below);
        }
    }

    [Fact]
    public void A_dead_hatch_lays_nothing_and_its_turn_goes_to_the_next()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        Kill(b, "hatch-1");
        Kill(b, "hatch-2");
        const int frames = 150;   // long enough for two turns, too short for a third
        var opened = new List<string>();
        var was = new Dictionary<string, PartState>();
        for (int f = 1; f <= frames; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            foreach (var h in Hatches(b))
            {
                if (h.State == PartState.Open && was.GetValueOrDefault(h.Name) != PartState.Open) opened.Add(h.Name);
                was[h.Name] = h.State;
            }
        }
        Assert.Equal(["hatch-3", "hatch-4"], opened);
        Assert.Equal(2 * GnatsPerHatch, Gnats(g, b).Count);
        Assert.Equal(PartState.Dead, PartOf(b, "hatch-1").State);
        Assert.Equal(PartState.Dead, PartOf(b, "hatch-2").State);
    }

    [Fact]
    public void With_every_hatch_dead_no_gnat_is_ever_laid()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        foreach (var h in Hatches(b).ToList()) Kill(b, h.Name);
        for (int f = 1; f <= LongWatch; f++)
        {
            EnemyKit.StepSweeping(g);
            Assert.DoesNotContain(g.Enemies, e => e.Kind == EnemyKind.Gnat);
            foreach (var h in Hatches(b)) Assert.Equal(PartState.Dead, h.State);
        }
    }

    [Fact]
    public void A_hatch_killed_while_open_does_not_lay_again_when_its_time_comes()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        for (int f = 1; f <= LayGap; f++) EnemyKit.StepSweeping(g);
        Kill(b, "hatch-1");
        int gnats = Gnats(g, b).Count;
        g.Enemies.RemoveAll(e => e.Kind == EnemyKind.Gnat);
        for (int f = LayGap + 1; f <= 2 * LayGap; f++) EnemyKit.StepSweeping(g);
        Assert.Equal(GnatsPerHatch, gnats);
        Assert.Equal(PartState.Open, PartOf(b, "hatch-2").State);
        Assert.Equal(GnatsPerHatch, Gnats(g, b).Count);   // only hatch-2's pair
    }

    // ---------------------------------------------------------------- never more than ten gnats

    [Fact]
    public void A_hatch_that_cannot_lay_still_opens_and_the_brood_never_has_more_than_ten()
    {
        const double rowLeft = 10, apart = 3, rowY = 40;   // a row of gnats well away from the hatches
        var (g, _) = Listening();
        var b = Settled(g);
        for (int i = 0; i < GnatCap - 1; i++) g.SpawnAdd(EnemyKind.Gnat, rowLeft + i * apart, rowY, b);
        Soothe(g);
        EnemyKit.StepSweeping(g);
        Assert.Equal(GnatCap - 1, Gnats(g, b).Count);
        for (int f = 2; f <= LayGap; f++) EnemyKit.StepSweeping(g);
        Assert.Equal(PartState.Open, PartOf(b, "hatch-1").State);
        Assert.Equal(GnatCap, Gnats(g, b).Count);   // one of the pair, the other has no room
        for (int f = LayGap + 1; f <= 2 * LayGap; f++) EnemyKit.StepSweeping(g);
        Assert.Equal(PartState.Open, PartOf(b, "hatch-2").State);
        Assert.Equal(GnatCap, Gnats(g, b).Count);
    }

    [Fact]
    public void Over_a_long_fight_the_count_reaches_ten_and_goes_no_higher_even_in_frenzy()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        PartOf(b, "sac").Hp = FrenzySacHp / 2;   // frenzy
        const int frames = 1500;
        int most = 0;
        for (int f = 1; f <= frames; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            int n = Gnats(g, b).Count;
            Assert.True(n <= GnatCap, $"frame {f}: {n} gnats");
            most = Math.Max(most, n);
        }
        Assert.Equal(GnatCap, most);
    }

    // ---------------------------------------------------------------- frenzy and barren

    [Fact]
    public void Below_half_the_sacs_hit_points_a_hatch_opens_every_45_frames()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        PartOf(b, "sac").Hp = FrenzySacHp;
        const int frames = 200;
        var opened = new List<int>();
        var was = new Dictionary<string, PartState>();
        for (int f = 1; f <= frames; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            foreach (var h in Hatches(b))
            {
                if (h.State == PartState.Open && was.GetValueOrDefault(h.Name) != PartState.Open) opened.Add(f);
                was[h.Name] = h.State;
            }
        }
        Assert.Equal(Enumerable.Range(1, HatchLefts.Length).Select(k => k * FrenzyGap), opened);
    }

    [Fact]
    public void With_every_hatch_dead_the_sac_charges_for_30_frames_then_throws_eight_spores_every_80()
    {
        var (g, played) = Listening();
        var b = Settled(g);
        foreach (var h in Hatches(b).ToList()) Kill(b, h.Name);
        var thrown = new List<int>();
        const int pastLastRing = 10;
        for (int f = 1; f <= ChargeFrames + 2 * RingGap + pastLastRing; f++)
        {
            EnemyKit.StepSweeping(g);
            int now = g.Bullets.Count(x => !x.Friendly);
            if (f < ChargeFrames) Assert.Equal(0, now);                      // nothing hurts while it charges
            if (now == RingSpores) thrown.Add(f);
            else if (now != 0) Assert.Fail($"frame {f}: {now} new spores");
            g.Bullets.RemoveAll(x => !x.Friendly);
        }
        Assert.Equal([ChargeFrames, ChargeFrames + RingGap, ChargeFrames + 2 * RingGap], thrown);
        Assert.Contains(SfxNames.Charge, played);
    }

    [Fact]
    public void The_ring_is_eight_spores_at_speed_085_spread_evenly_and_turned_by_half_a_gap_each_time()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        const double fullTurn = 360, gap = fullTurn / RingSpores, reach = 12;   // the spores leave from within this many pixels of the centre
        foreach (var h in Hatches(b).ToList()) Kill(b, h.Name);
        for (int f = 1; f <= ChargeFrames; f++) EnemyKit.StepSweeping(g);
        var ring = g.Bullets.Where(x => !x.Friendly).ToList();
        Assert.Equal(RingSpores, ring.Count);
        Assert.All(ring, s => Assert.Equal(SporeSpeed, Math.Sqrt(s.VX * s.VX + s.VY * s.VY), 6));
        var angles = ring.Select(s => double.RadiansToDegrees(Math.Atan2(s.VY, s.VX))).Select(a => (a + fullTurn) % fullTurn).Order().ToList();
        for (int i = 1; i < RingSpores; i++) Assert.Equal(gap, angles[i] - angles[i - 1], 3);
        Assert.All(ring, s => Assert.InRange(s.X, b.X - reach, b.X + reach));
        Assert.All(ring, s => Assert.InRange(s.Y, b.Y - reach, b.Y + reach));
        g.Bullets.RemoveAll(x => !x.Friendly);
        for (int f = ChargeFrames + 1; f <= ChargeFrames + RingGap; f++) EnemyKit.StepSweeping(g);
        var second = g.Bullets.Where(x => !x.Friendly).ToList();
        Assert.Equal(RingSpores, second.Count);
        var turned = second.Select(s => (double.RadiansToDegrees(Math.Atan2(s.VY, s.VX)) + fullTurn) % fullTurn).Order().ToList();
        Assert.Equal(gap / 2, (turned[0] - angles[0] + gap) % gap, 3);
    }

    [Fact]
    public void It_stays_in_barren_and_the_ring_goes_on_while_the_sac_lives()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        foreach (var h in Hatches(b).ToList()) Kill(b, h.Name);
        const int rings = 4, pastLastRing = 50;
        const double shipColumn = 0.5;
        for (int f = 1; f <= ChargeFrames + rings * RingGap + pastLastRing; f++)
        {
            EnemyKit.StepSweeping(g);
            g.Bullets.RemoveAll(x => !x.Friendly && Math.Abs(x.X - g.PX) < shipColumn);   // keep the test ship out of it; spores go on
            Assert.Contains(b, g.Enemies);
        }
        Assert.Equal(ModeBarren, b.Mode);
    }

    [Fact]
    public void Killing_the_sac_scores_six_thousand_and_takes_the_brood_with_it()
    {
        var (g, _) = Listening();
        var b = Settled(g);
        for (int f = 1; f <= LayGap; f++) EnemyKit.StepSweeping(g);
        Assert.NotEmpty(Gnats(g, b));
        int before = g.Score;
        KillSac(g, b);
        EnemyKit.StepSweeping(g);
        Assert.DoesNotContain(b, g.Enemies);
        Assert.Equal(before + BroodScore, g.Score);
        Assert.Equal(1, g.Kills);
        // its gnats are out of the field and marked to go (the game sweeps them when the kill's pause lets it)
        Assert.DoesNotContain(g.Enemies, e => e.Kind == EnemyKind.Gnat && (!e.Leaving || e.Y > 0));
    }

    // ---------------------------------------------------------------- the machine on four fields

    [Theory]
    [MemberData(nameof(Fields))]
    public void It_enters_holds_and_stays_inside_the_field_above_the_floor(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var b = g.SpawnKind(EnemyKind.Brood, EnemyKit.MidWidth);
        const int settledBy = 120, leastSway = 3;
        Assert.True(b.Y < 0, "it enters from above");
        double floor = EnemyKit.BossFloor(g.PH);
        double minX = double.MaxValue, maxX = double.MinValue;
        for (int f = 1; f <= LongWatch; f++)
        {
            EnemyKit.StepSweeping(g);
            // the ship keeps clear of the test: gnats are swept so only the Brood's own size is judged
            g.Enemies.RemoveAll(e => e.Kind == EnemyKind.Gnat);
            Assert.Contains(b, g.Enemies);
            Assert.True(EnemyKit.InsideWidth(g, b), $"frame {f}: x {b.X} outside {g.PW}");
            Assert.True(EnemyKit.LowestPixel(b) <= floor, $"frame {f}: lowest pixel {EnemyKit.LowestPixel(b)} under the floor {floor}");
            if (f >= settledBy)
            {
                Assert.Equal(Hold(g), b.Y, 6);
                minX = Math.Min(minX, b.X); maxX = Math.Max(maxX, b.X);
            }
            Assert.False(b.Leaving);
        }
        Assert.True(EnemyKit.Extent(b).T >= TopRow - 0.01, "the top never clips the health bar's row");
        Assert.True(maxX - minX >= leastSway, $"it sways: {minX:0.0} to {maxX:0.0}");
        if (g.PH < ShortFieldHeight) Assert.Equal(TopRow, EnemyKit.Extent(b).T, 6);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void Its_parts_open_and_throw_inside_the_field_on_every_size(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var b = g.SpawnKind(EnemyKind.Brood, EnemyKit.MidWidth);
        const int frenzyAt = 400, barrenAt = 800, frames = 1200, mostBolts = 24;
        double floor = EnemyKit.BossFloor(g.PH);
        int frenzy = 0;
        for (int f = 1; f <= frames; f++)
        {
            if (f == frenzyAt) PartOf(b, "sac").Hp = FrenzySacHp;
            if (f == barrenAt) foreach (var h in Hatches(b).ToList()) Kill(b, h.Name);
            EnemyKit.StepSweeping(g);
            g.Enemies.RemoveAll(e => e.Kind == EnemyKind.Gnat);
            Assert.True(EnemyKit.InsideWidth(g, b));
            Assert.True(EnemyKit.LowestPixel(b) <= floor);
            frenzy += b.Mode == ModeFrenzy ? 1 : 0;
            Assert.True(g.Bullets.Count(x => !x.Friendly) <= mostBolts, $"frame {f}: more than {mostBolts} own bolts");
            Assert.False(Brood.Touches(g, b));
        }
        Assert.True(frenzy > 0, "it went through frenzy");
        Assert.Equal(ModeBarren, b.Mode);
    }

    // ---------------------------------------------------------------- warnings

    [Fact]
    public void A_bulging_hatch_blinks_its_plate_bile_green_for_24_frames_then_white_for_the_last_third()
    {
        var g = EnemyKit.Game();
        var b = Settled(g);
        // the Brood sways, so find the body each time
        var plate = PlateOf(0);
        uint Plate() => At(EnemyKit.ClearPickupsAndDraw(g), Corner(b), plate.X, plate.Y);
        Assert.Equal(Plum, Plate());
        var seen = new HashSet<uint>();
        for (int f = 1; f < LayGap; f++)
        {
            EnemyKit.StepSweeping(g);
            if (f < LayGap - BulgeFrames) { Assert.Equal(Plum, Plate()); continue; }
            uint c = Plate();
            seen.Add(c);
            int left = LayGap - f;
            if (left <= BulgeFrames / 3) Assert.Equal(Palette.White, c);
            else Assert.True(c == Bile || c == Plum, $"frame {f}: plate {c:x6}");
        }
        Assert.Contains(Bile, seen);
        Assert.Contains(Plum, seen);
        Assert.Contains(Palette.White, seen);
    }

    [Fact]
    public void Nothing_in_the_brood_can_hurt_during_a_bulge_and_a_gnat_lays_out_harmless()
    {
        var g = EnemyKit.Game();
        var b = Settled(g);
        for (int f = 1; f < LayGap; f++)
        {
            EnemyKit.StepSweeping(g);
            Assert.DoesNotContain(g.Bullets, x => !x.Friendly);
        }
        Assert.Equal(Tuning.ShipHp, g.Hp);
        EnemyKit.StepSweeping(g);
        foreach (var gn in Gnats(g, b)) Assert.False(gn.Armed, "a new gnat is harmless");
    }

    [Fact]
    public void The_barren_ring_is_drawn_while_it_charges_and_gone_once_thrown()
    {
        var g = EnemyKit.Game();
        var b = Settled(g);
        const double ringAboveCentre = 1.5;   // the ring is drawn about the sac's middle, which sits above the body's centre
        const int midCharge = 12, mostMarks = 6, fewMarks = 3;
        foreach (var h in Hatches(b).ToList()) Kill(b, h.Name);
        int Marks()
        {
            var f = EnemyKit.ClearPickupsAndDraw(g);
            int n = 0;
            double cx = b.X, cy = b.Y - ringAboveCentre;
            for (int a = 0; a < RingSpores; a++)
            {
                double ang = a * 2 * Math.PI / RingSpores;
                int x = (int)Math.Round(cx + Math.Cos(ang) * RingRadius), y = (int)Math.Round(cy + Math.Sin(ang) * RingRadius);
                uint c = f.Get(x, y) & EnemyKit.RgbMask;
                if (c == Bile || c == Palette.White) n++;
            }
            return n;
        }
        for (int f = 1; f <= midCharge; f++) EnemyKit.StepSweeping(g);
        Assert.True(Marks() >= mostMarks, "the ring shows at the radius the spores will leave from");
        for (int f = midCharge + 1; f <= ChargeFrames; f++) EnemyKit.StepSweeping(g);
        g.Bullets.RemoveAll(x => !x.Friendly);
        Assert.True(Marks() < fewMarks, "the ring is not drawn once it has been thrown");
    }

    // ---------------------------------------------------------------- pixels

    private static readonly string[] Drawing =
    [
        "...........###########...........",
        ".......####ooooooooooo####.......",
        "....###oooogggooooogggoooo###....",
        "..##ooogggooooogggooooogggooo##..",
        ".#oogggoooooggorrroggooooogggoo#.",
        "#ooggoooogggoooryrooogggooooggoo#",
        "#oooooggoooooggrrrggoooooggooooo#",
        "#oggooooooggoooooooooggooooooggo#",
        ".#ooooooooooooooooooooooooooooo#.",
        "..#############################..",
        "...#ooo#..#ooo#...#ooo#..#ooo#...",
        "...#o#o#..#o#o#...#o#o#..#o#o#...",
        "....###....###.....###....###....",
    ];

    private static uint DrawingColour(char c) => c switch
    {
        '#' => Outline,
        'o' => Plum,
        'g' => Bile,
        'r' => Palette.Crimson,
        'y' => Palette.Gold,
        _ => 0,
    };

    [Fact]
    public void The_corners_and_the_empty_hatch_rows_show_the_field_not_black()
    {
        var g = EnemyKit.Game();
        const double x = 60.5, y = 25.5;   // in the open, on a half pixel as the Brood keeps
        var b = EnemyKit.Place(g, EnemyKind.Brood, x, y);
        var (x0, y0) = Corner(b);
        var with = EnemyKit.ClearPickupsAndDraw(g);
        var cells = new[] { (0, 0), (BodyW - 1, 0), (0, HatchRow), (BodyW / 2, BodyH - 1), (BodyW - 1, BodyH - 1), (1, HatchRow + 1) };
        var got = cells.Select(c => with.Get(x0 + c.Item1, y0 + c.Item2)).ToArray();
        EnemyKit.Clear(g);
        var without = EnemyKit.ClearPickupsAndDraw(g);
        for (int i = 0; i < cells.Length; i++)
            Assert.Equal(without.Get(x0 + cells[i].Item1, y0 + cells[i].Item2), got[i]);
    }

    [Fact]
    public void The_body_and_the_closed_hatches_are_drawn_as_the_drawing_at_every_half_pixel_position()
    {
        const int left = 40, top = 20, shifts = 4;   // every even and odd column and row, somewhere in the open
        var g = EnemyKit.Game();
        for (int x = left; x < left + shifts; x++)
            for (int y = top; y < top + shifts; y++)
            {
                EnemyKit.Clear(g);
                var b = EnemyKit.Place(g, EnemyKind.Brood, x + 0.5, y + 0.5);
                var f = EnemyKit.ClearPickupsAndDraw(g);
                var corner = Corner(b);
                for (int r = 0; r < Drawing.Length; r++)
                    for (int c = 0; c < Drawing[r].Length; c++)
                    {
                        uint want = DrawingColour(Drawing[r][c]);
                        uint got = At(f, corner, c, r);
                        if (want == 0) continue;   // the field behind may show
                        Assert.True(got == want, $"({x + 0.5}, {y + 0.5}) pixel {c},{r}: {got:x6}, the drawing has {want:x6}");
                    }
            }
    }

    [Fact]
    public void An_open_hatch_shows_a_crimson_mouth_with_a_gold_eye_and_a_dead_one_is_a_husk_or_a_gap()
    {
        var g = EnemyKit.Game();
        var b = Settled(g);
        var corner = Corner(b);
        var (mx, my) = PlateOf(1);
        var next = PlateOf(0);
        Open(b, "hatch-2");
        var f = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(Palette.WeakBody, At(f, corner, mx, my));
        Assert.Equal(Palette.WeakBody, At(f, corner, mx - 1, my + 1));
        Assert.Equal(Palette.Gold, At(f, corner, mx, my + 1));
        Kill(b, "hatch-2");
        f = EnemyKit.ClearPickupsAndDraw(g);
        uint dead = At(f, corner, mx, my);
        Assert.NotEqual(Palette.WeakBody, dead);
        Assert.NotEqual(Plum, dead);
        Assert.Equal(Plum, At(f, corner, next.X, next.Y));   // its neighbours are as they were
    }

    [Fact]
    public void The_sac_has_a_gold_heart_on_crimson_and_a_plum_outline_and_the_health_bar_shows_once_hurt()
    {
        const int barStart = 2;   // a pixel near the left end of the health bar, inside its fill
        var g = EnemyKit.Game();
        var b = Settled(g);
        var corner = Corner(b);
        int heartRow = Array.FindIndex(Drawing, row => row.Contains('r'));
        var f = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(Palette.Crimson, At(f, corner, Drawing[heartRow].IndexOf('r'), heartRow));
        int markRow = Array.FindIndex(Drawing, row => row.Contains('y'));
        Assert.Equal(Palette.Gold, At(f, corner, Drawing[markRow].IndexOf('y'), markRow));
        Assert.Equal(Outline, At(f, corner, Drawing[0].IndexOf('#'), 0));
        PartOf(b, "sac").Hp = SacHp / 2;
        b.Hp = SacHp / 2;
        f = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(Palette.Crimson, f.Get(corner.X + barStart, RaiderView.BarRow(corner.Y)) & EnemyKit.RgbMask);
    }

    // ---------------------------------------------------------------- safe

    [Fact]
    public void In_laying_it_fires_nothing_of_its_own_and_touches_only_by_body_and_gnat()
    {
        var g = EnemyKit.Game();
        var b = Settled(g);
        const int frames = 300;
        for (int f = 1; f <= frames; f++)
        {
            EnemyKit.StepSweeping(g);
            g.Enemies.RemoveAll(e => e.Kind == EnemyKind.Gnat);
            Assert.DoesNotContain(g.Bullets, x => !x.Friendly);
            Assert.False(Brood.Touches(g, b));
        }
    }

    [Fact]
    public void A_ship_that_never_moves_is_found_by_the_brood_in_the_end()
    {
        var g = AtTheGate(4);
        const int frames = 80 * Tuning.Fps;
        for (int f = 0; f < frames && g.Deaths == 0; f++) { g.Step(); }
        Assert.True(g.Deaths > 0, "a ship that stands still is hit by a gnat or a spore");
    }

    // ---------------------------------------------------------------- voice

    private static void Offer(RaiderGame g, ulong seed, string text) => g.OfferDarkness(new DarknessPost(DarknessKind.Rude, seed, text));

    [Fact]
    public void It_takes_the_newest_waiting_post_first()
    {
        var g = EnemyKit.Game();
        Offer(g, 1, "oldest post"); Offer(g, 2, "middle post"); Offer(g, 3, "newest post");
        var b = Settled(g);
        for (int i = 0; i < SpeechWait && b.Text is null; i++) EnemyKit.StepSweeping(g);
        Assert.Equal("newest post", b.Text);
        Assert.Equal(["oldest post", "middle post"], g.Rude.Waiting.Select(p => p.Text));
    }

    [Fact]
    public void With_no_post_it_says_follow_for_follow_under_its_own_name()
    {
        var g = EnemyKit.Game();
        var b = Settled(g);
        for (int i = 0; i < SpeechWait && b.Text is null; i++) EnemyKit.StepSweeping(g);
        Assert.Equal("Follow for follow.", b.Text);
        Assert.Equal("BROOD", b.Header);
    }

    [Fact]
    public void Left_alone_it_soon_says_the_next_post_the_newest_first()
    {
        const int BroodSeconds = 5;
        var g = EnemyKit.Game();
        Offer(g, 1, "the first post");
        var b = Settled(g);
        Assert.Equal(BroodSeconds * Tuning.Fps, Brood.IdleFrames(b));
        for (int i = 0; i < SpeechWait && b.Text is null; i++) EnemyKit.StepSweeping(g);
        Assert.Equal("the first post", b.Text);
        Offer(g, 2, "an older post"); Offer(g, 3, "a fresher post");
        Assert.Equal(Turn.Wait, Brood.TurnFor(g, b, VoiceCue.Idle));
        const int idleMargin = 10, pastIdle = BroodSeconds * Tuning.Fps + idleMargin;   // long enough past the box for an Idle to come round
        for (int i = 0; i <= SpeechWait + pastIdle && b.Text == "the first post"; i++)
        {
            EnemyKit.StepSweeping(g);
            g.Enemies.RemoveAll(e => e.Kind == EnemyKind.Gnat);
        }
        Assert.Equal("a fresher post", b.Text);
        Assert.Equal("an older post", Assert.Single(g.Rude.Waiting).Text);
    }

    [Fact]
    public void It_takes_a_new_post_when_a_hatch_is_destroyed()
    {
        var g = EnemyKit.Game();
        Offer(g, 1, "the first post");
        var b = Settled(g);
        for (int i = 0; i < SpeechWait && b.Text is null; i++) EnemyKit.StepSweeping(g);
        Offer(g, 2, "a fresher post");
        Open(b, "hatch-1");
        var h = PartOf(b, "hatch-1");
        EnemyKit.Shot(g, b.X + h.OX, b.Y + h.OY, HatchKillingDamage);
        EnemyKit.StepSweeping(g);
        Assert.Equal(PartState.Dead, h.State);
        for (int i = 0; i < LongSpeechWait && b.Text != "a fresher post"; i++) { EnemyKit.StepSweeping(g); g.Enemies.RemoveAll(e => e.Kind == EnemyKind.Gnat); }
        Assert.Equal("a fresher post", b.Text);
        Assert.Empty(g.Rude.Waiting);
    }

    [Fact]
    public void With_no_post_a_destroyed_hatch_brings_engagement_is_down_and_the_last_one_the_regret()
    {
        var g = EnemyKit.Game();
        var b = Settled(g);
        for (int i = 0; i < SpeechWait && b.Text is null; i++) EnemyKit.StepSweeping(g);
        Assert.Equal("Follow for follow.", b.Text);
        void Shoot(string name)
        {
            Open(b, name);
            var h = PartOf(b, name);
            EnemyKit.Shot(g, b.X + h.OX, b.Y + h.OY, HatchKillingDamage);
            EnemyKit.StepSweeping(g);
            Assert.Equal(PartState.Dead, h.State);
        }
        Shoot("hatch-1");
        for (int i = 0; i < SpeechWait && b.Text != "Engagement is down."; i++) { EnemyKit.StepSweeping(g); g.Enemies.RemoveAll(e => e.Kind == EnemyKind.Gnat); }
        Assert.Equal("Engagement is down.", b.Text);
        Kill(b, "hatch-2"); Kill(b, "hatch-3");
        Shoot("hatch-4");
        for (int i = 0; i < LongSpeechWait && b.Text != "You will regret the reach."; i++) { EnemyKit.StepSweeping(g); g.Bullets.RemoveAll(x => !x.Friendly); }
        Assert.Equal("You will regret the reach.", b.Text);
        Assert.Equal(ModeBarren, b.Mode);
    }

    [Fact]
    public void Its_last_words_are_unfollowed()
    {
        var g = EnemyKit.Game();
        var b = Settled(g);
        for (int i = 0; i < SpeechWait && b.Text is null; i++) EnemyKit.StepSweeping(g);
        KillSac(g, b);
        EnemyKit.StepSweeping(g);
        Assert.Equal("Unfollowed.", b.Text);
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_the_brood_in_it()
    {
        const ulong seed = 11;
        const int frames = 40 * Tuning.Fps;
        (string, bool) Run()
        {
            var g = AtTheGate(seed);
            bool seen = false;
            for (int f = 0; f < frames; f++)
            {
                BroodBot(g, aimAtHatches: true);
                g.Step();
                seen |= g.Boss?.Kind == EnemyKind.Brood;
            }
            var state = string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}";
            return (state, seen);
        }
        var (a, seenA) = Run();
        var (b, _) = Run();
        Assert.Equal(a, b);
        Assert.True(seenA);
    }

    // ---------------------------------------------------------------- the fight

    /// <summary>The fight bot: always fires; shoots the nearest gnat in its way, then an open hatch when <paramref name="aimAtHatches"/>,
    /// then the sac from a lane clear of the closed hatches (or straight under it once they are dead).</summary>
    private static void BroodBot(RaiderGame g, bool aimAtHatches)
    {
        g.Input.Key(GameKey.Fire, true);
        double target = g.PX;
        var boss = g.Boss ?? g.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.Brood);
        Enemy? gnat = null;
        foreach (var e in g.Enemies)
            if (e.Kind == EnemyKind.Gnat && e.Y > 0 && e.Y < g.PY && (gnat is null || Dist(g, e) < Dist(g, gnat))) gnat = e;
        if (gnat is not null && Dist(g, gnat) < GnatReach) target = gnat.X;
        else if (boss is not null)
        {
            var open = boss.Parts!.Where(p => aimAtHatches && p.Name != "sac" && p.State == PartState.Open).OrderBy(p => Math.Abs(boss.X + p.OX - g.PX)).FirstOrDefault();
            if (open is not null) target = boss.X + open.OX;
            else target = boss.X + Lane(boss, g.PX - boss.X);
        }
        g.Input.Key(GameKey.Left, g.PX > target + EnemyKit.SteerDeadZone);
        g.Input.Key(GameKey.Right, g.PX < target - EnemyKit.SteerDeadZone);
    }

    private static double Dist(RaiderGame g, Enemy e) => Math.Abs(e.X - g.PX) + Math.Abs(e.Y - g.PY) * RowWeight;

    /// <summary>The ship's offset from the sac's centre that keeps both main shots off every standing hatch.</summary>
    private static double Lane(Enemy boss, double current)
    {
        double best = 0, bestScore = double.MinValue;
        for (double off = -LaneRange; off <= LaneRange; off += LaneStep)
        {
            double clear = double.MaxValue;
            foreach (var p in boss.Parts!.Where(p => p.Name != "sac" && p.State != PartState.Dead))
                foreach (double dx in new[] { -MainShotOffset, MainShotOffset })
                    clear = Math.Min(clear, Math.Abs(off + dx - p.OX) - p.HW);
            double score = Math.Min(clear, EnoughClearance) * ClearanceWeight - Math.Abs(off - current) * StayWeight - Math.Abs(off) * CentreWeight;
            if (score > bestScore) { bestScore = score; best = off; }
        }
        return best;
    }

    /// <summary>Frames the bot needs to kill the boss, from the first frame of the fight, at the given weapon level; -1 if it does not in five minutes.</summary>
    private static int Fight(ulong seed, int level)
    {
        var g = AtTheGate(seed);
        while (g.Gate != GateState.Fighting) { EnemyKit.Clear(g); g.Step(); }
        while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); }
        for (int f = 0; f < FightTimeLimit; f++)
        {
            BroodBot(g, aimAtHatches: true);
            g.Step();
            if (g.Gate != GateState.Fighting) return g.Kills > 0 ? f + 1 : -1;
        }
        return -1;
    }

    [Fact]
    public void A_bot_at_weapon_level_three_beats_the_brood_in_eighteen_to_thirty_seconds()
    {
        const int seeds = 7, fastest = 18, slowest = 30;
        var times = new List<int>();
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            int t = Fight(seed, FightLevel);
            Assert.True(t > 0, $"seed {seed}: the Brood was not beaten");
            times.Add(t);
        }
        times.Sort();
        double median = times[times.Count / 2] / (double)Tuning.Fps;
        output.WriteLine($"brood fight at level {FightLevel}, frames to kill: {string.Join(" ", times)}; median {median:0.0} s");
        Assert.InRange(median, fastest, slowest);
    }

    [Fact]
    public void Even_without_killing_a_hatch_the_sac_can_be_shot_through_the_gaps()
    {
        // a bot that never aims at a hatch still wins, so the fight cannot be stalled by a hatch that never opens
        var g = AtTheGate(2);
        while (g.Gate != GateState.Fighting) { EnemyKit.Clear(g); g.Step(); }
        while (g.WeaponLevel < FightLevel) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); }
        const int frames = 4000;
        for (int f = 0; f < frames && g.Gate == GateState.Fighting; f++)
        {
            BroodBot(g, aimAtHatches: false);
            g.Step();
        }
        Assert.NotEqual(GateState.Fighting, g.Gate);
        Assert.True(g.Kills > 0);
    }
}
