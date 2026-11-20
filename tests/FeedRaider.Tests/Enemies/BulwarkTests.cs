using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Bulwark, stage 2's boss: a core between two shield pods. These tests give its kind and parts, its machine (enter, guarded,
/// half-guarded, bare) and where it stays on the four fields, the frame each mode fires on, the warnings before each bolt and each ring
/// (and that nothing can kill inside them), its pixels, its voice (a hidden reply first, a new post at each loss) and the fight (a bot
/// at stage 2's weapon level, a ship that never moves, and the same seed twice).
/// </summary>
public sealed class BulwarkTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Fields() => EnemyKit.FieldSizes.Select(f => new object[] { f.Cols, f.Rows });

    private const uint Blue = Palette.Shield, DeepBlue = Palette.ShieldDeep, Crimson = Palette.Crimson, Gold = Palette.Gold, White = Palette.White;

    /// <summary>The dark husk's body colour, the strut between a pod and the core.</summary>
    private static readonly uint DarkBody = Art.DarkPal['o'];
    private const int Guarded = 1, Half = 2, Bare = 3;
    private const double PodBolt = 1.15, RingBolt = 0.9;
    private const double DodgeStep = 12, EdgeMargin = 8;

    /// <summary>The slowest a bolt may fall and still count as a threat to the bot.</summary>
    private const double MinThreatSpeed = 0.05;

    /// <summary>The bot ignores bolts that need more than this many frames to reach its row.</summary>
    private const double LookAheadFrames = 26;

    /// <summary>A bolt that will land within this many pixels of the ship's centre hits it.</summary>
    private const double HitHalfWidth = 7;

    /// <summary>Frames after which a fight is given up as stalled.</summary>
    private const int FightFrameCap = 2400;

    /// <summary>Frames the boss needs to finish its entrance and settle into its guard.</summary>
    private const int EntranceFrames = 150;

    /// <summary>The body's size in pixels, the drawing's columns of the left pod's, the right pod's and the core's centres, and the row they share.</summary>
    private const int BodyW = 39, BodyH = 11, PodLeftCol = 4, PodRightCol = 34, CoreCol = 19, CentreRow = 5;

    /// <summary>The least half width and half height of the core's hit box: enough to cover the core's art.</summary>
    private const int CoreHalfW = 7, CoreHalfH = 5;

    /// <summary>The drawing's column of the outline on the core's left edge, and of the strut between the left pod and the core.</summary>
    private const int CoreLeft = CoreCol - CoreHalfW, StrutCol = 10;

    /// <summary>The rows under the body a pod's arc to the core may sag into.</summary>
    private const int ArcDepth = 4;

    /// <summary>The Bulwark's score, a pod's score, and the body colour, its own and apart from the Shutter's blue.</summary>
    private const int BulwarkScore = 4000, PodScore = 500;
    private const uint BodyColour = 0x3f6fd0;

    /// <summary>The stage the Bulwark ends, and the weapon level the fight bot plays it at.</summary>
    private const int BulwarkStage = 2, FightLevel = 2;

    /// <summary>Mode-age frames between guarded bolts, half-guarded spreads, bare spreads and rings, and the age of the first ring half guarded and bare.</summary>
    private const int PodGap = 40, HalfGap = 50, BareGap = 45, RingGap = 150, HalfRingAt = 85, BareRingAt = 110;

    /// <summary>Frames a pod's centre blinks before its bolt, the frame of the blink it turns white from, and frames the core charges before a ring.</summary>
    private const int BlinkFrames = 20, WhiteFrom = BlinkFrames * 2 / 3, ChargeFrames = 30;

    /// <summary>Bolts in a spread, in a half-guarded ring and in a bare ring, and the radius the ring's dots are drawn at.</summary>
    private const int SpreadBolts = 3, HalfRingBolts = 8, BareRingBolts = 10;
    private const double RingDotRadius = 10;

    /// <summary>The angle of a bolt straight down.</summary>
    private const double StraightDown = Math.PI / 2;

    /// <summary>Damage that kills a pod or the open core in one shot.</summary>
    private const double KillingDamage = 500;

    /// <summary>The narrowest field, and a tall one whose aimed bolts fly long enough to measure.</summary>
    private const int NarrowCols = 30, NarrowRows = 16, TallCols = 48, TallRows = 60;

    /// <summary>A mode age far from every warning, so nothing blinks, and a row well inside the big field to draw it on.</summary>
    private const int StillAge = 1, OpenRow = 30;

    /// <summary>Frames a test waits for the gate to bring the boss, and frames it watches for something to hold.</summary>
    private const int GateWait = 4000, LongWatch = 600;

    /// <summary>The frame from which the sway is measured, once the entrance and its easing are over.</summary>
    private const int SettledBy = 200;

    /// <summary>The share of the field's width it sways across guarded and bare, the least share of that a test must see, and the slack past it.</summary>
    private const double GuardedSwayShare = 0.1, BareSwayShare = 0.25, LeastSwaySeen = 0.85, SwaySlack = 0.5;

    /// <summary>Frames a test waits for the speech box to change, and for the first post once the boss has to enter.</summary>
    private const int SpeechWait = 400, LongSpeechWait = 600;

    /// <summary>Frames a run from stage 2's gate is watched for the same seed test.</summary>
    private const int GateRunFrames = 900;

    /// <summary>Frames past a mode's last expected volley a test steps, to see nothing more comes.</summary>
    private const int PastLastBolt = 5;

    /// <summary>The drawing, the art the parts are cut from: the picture of the boss when every part is whole and the core is shut.</summary>
    private static readonly string[] Drawing =
    [
        "................#######................",
        "..#####.......##ooooooo##.......#####..",
        ".#BBBBB#.....#oo+++++++oo#.....#BBBBB#.",
        "#BBbbbBB#...#oo++ooooo++oo#...#BBbbbBB#",
        "#BbbybbB#####o++oobbboo++o#####BbbybbB#",
        "#BbyyybB#ooo#o+oobbrbboo+o#ooo#BbyyybB#",
        "#BbbybbB#####o++oobbboo++o#####BbbybbB#",
        "#BBbbbBB#...#oo++ooooo++oo#...#BBbbbBB#",
        ".#BBBBB#.....#oo+++o+++oo#.....#BBBBB#.",
        "..#####.......##oo#o#oo##.......#####..",
        "................##...##................",
    ];

    private static EnemyBehaviour Bulwark => RaiderGame.TableBehaviour(EnemyKind.Bulwark)!;

    /// <summary>Where it holds: on the boss row, and on a short field with its top on the first row the health bar leaves free.</summary>
    private static double Hold(RaiderGame g) => g.PH < BroodTests.ShortFieldHeight ? BroodTests.TopRow + BodyH / 2.0 : EnemyKit.BossRow(g.PH);

    /// <summary>Steps one frame and sweeps away every enemy but the ones a test keeps (a filler wave, a wave the stage brings).</summary>
    private static void StepKeeping(RaiderGame g, params Enemy[] keep)
    {
        g.Step();
        g.Enemies.RemoveAll(e => !keep.Contains(e));
    }

    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);

    private static bool Alive(Enemy e, string pod) => PartOf(e, pod).State != PartState.Dead;

    /// <summary>A bulwark put at a spot in a mode, as the machine would have it, with the sway stilled and the ship beneath it.</summary>
    private static Enemy Put(RaiderGame g, int mode, int age = 0, double x = -1, double y = -1)
    {
        var e = g.SpawnKind(EnemyKind.Bulwark, 0.5);
        double px = x < 0 ? g.PW / 2.0 : x;
        e.X = px; e.BaseX = px; e.Y = y < 0 ? Hold(g) : y; e.Grace = 0;
        e.Mode = mode; e.ModeAge = age;
        if (mode >= Half) Lose(g, e, "pod-l", live: mode == Half ? "pod-r" : null);
        if (mode == Bare) Open(g, e);
        return e;
    }

    /// <summary>Takes the pods from it without the game's help, as a test sets a stage: the named one stays alive.</summary>
    private static void Lose(RaiderGame g, Enemy e, string dead, string? live)
    {
        foreach (var p in e.Parts!.Where(p => p.Name.StartsWith("pod", StringComparison.Ordinal)))
            if (p.Name != live && (p.Name == dead || live is null)) { p.State = PartState.Dead; p.Hp = 0; }
        g.SetParts(e, e.Parts!.ToArray());
    }

    private static void Open(RaiderGame g, Enemy e)
    {
        PartOf(e, "core").State = PartState.Open;
        g.SetParts(e, e.Parts!.ToArray());
    }

    /// <summary>Keeps the boss where a test put it (its sway is a function of its age) and steps one frame.</summary>
    private static void StepHoldingPlace(RaiderGame g, Enemy e)
    {
        e.Age = 0;
        StepKeeping(g, e);
    }

    /// <summary>Every hostile bolt the frame brought into being, with the boss's mode age and place at the frame (the sway eases while it widens).</summary>
    private static List<(int Age, List<Bullet> Bolts, double X, double Y)> StepAndRecordBolts(RaiderGame g, Enemy e, int frames)
    {
        var log = new List<(int, List<Bullet>, double, double)>();
        for (int f = 0; f < frames; f++)
        {
            g.Bullets.RemoveAll(b => !b.Friendly);   // the ship under it is never hit, so a hit's stop never skips a frame
            StepHoldingPlace(g, e);
            var fresh = g.Bullets.Where(b => !b.Friendly).ToList();
            if (fresh.Count > 0) log.Add((e.ModeAge, fresh, e.X, e.Y));
        }
        return log;
    }

    private static double Speed(Bullet b) => Math.Sqrt(b.VX * b.VX + b.VY * b.VY);

    /// <summary>Where the bolt began: the frame it was made it had already moved once.</summary>
    private static (double X, double Y) Origin(Bullet b) => (b.X - b.VX, b.Y - b.VY);

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Bulwark, Bulwark.Kind);
        Assert.Equal("BULWARK", Bulwark.Name);
        Assert.Equal(BulwarkScore, Bulwark.Score);
        Assert.True(Bulwark.Boss);
        Assert.True(Bulwark.Speaks);
        Assert.True(Bulwark.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(BodyW, Bulwark.Body.W);
        Assert.Equal(BodyH, Bulwark.Body.H);
        Assert.Equal(BodyColour, Bulwark.Colour);
    }

    [Fact]
    public void It_is_a_shut_core_between_two_open_pods()
    {
        var g = EnemyKit.Game();
        var e = g.SpawnKind(EnemyKind.Bulwark, 0.5);
        Assert.Equal(["core", "pod-l", "pod-r"], e.Parts!.Select(p => p.Name));
        var core = PartOf(e, "core"); var l = PartOf(e, "pod-l"); var r = PartOf(e, "pod-r");
        Assert.Equal(PartState.Armoured, core.State);
        Assert.Equal(PartState.Open, l.State);
        Assert.Equal(PartState.Open, r.State);
        Assert.Equal(0, core.OX);
        Assert.True(l.OX < 0 && r.OX > 0 && l.OX == -r.OX, "a pod on each side of the core");
        Assert.True(core.Vital);
        Assert.Equal(2 * l.Hp, core.Hp);
        Assert.Equal(l.Hp, r.Hp);
        Assert.Equal(e.Hp, core.Hp + l.Hp + r.Hp, 6);
        Assert.Equal(e.MaxHp, e.Hp);
        Assert.Equal(PodScore, l.Score);
        Assert.Equal(PodScore, r.Score);
        Assert.NotNull(l.Wreck);
        Assert.NotNull(r.Wreck);
        Assert.NotNull(core.OpenArt);
        Assert.True(core.HW >= CoreHalfW && core.HH >= CoreHalfH, "the core's box covers its art");
    }

    [Fact]
    public void Its_hit_points_are_fixed_and_the_gate_does_not_scale_them()
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(BulwarkStage);
        Assert.Equal(GateState.Warning, g.Gate);
        for (int i = 0; i < GateWait && g.Gate != GateState.Fighting; i++) { g.Step(); if (g.Gate != GateState.Fighting) EnemyKit.Clear(g); }
        Assert.Equal(GateState.Fighting, g.Gate);
        var boss = g.Boss!;
        Assert.Equal(EnemyKind.Bulwark, boss.Kind);
        Assert.Equal(Bulwark.Hp, boss.MaxHp, 6);
        Assert.Equal(Bulwark.Hp, PartOf(boss, "core").Hp + PartOf(boss, "pod-l").Hp + PartOf(boss, "pod-r").Hp, 6);
    }

    [Fact]
    public void Taking_the_pods_and_then_the_core_beats_the_gate_and_opens_stage_three()
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(BulwarkStage);
        for (int i = 0; i < GateWait && g.Gate != GateState.Fighting; i++) { g.Step(); if (g.Gate != GateState.Fighting) EnemyKit.Clear(g); }
        var boss = g.Boss!;
        for (int i = 0; i < EntranceFrames; i++) { g.Bullets.RemoveAll(b => !b.Friendly); g.Step(); }
        Assert.Equal(Guarded, boss.Mode);
        foreach (var name in new[] { "pod-l", "pod-r", "core" })
        {
            var p = PartOf(boss, name);
            EnemyKit.Shot(g, boss.X + p.OX, boss.Y + p.OY, KillingDamage);
            g.Step();
            g.Bullets.RemoveAll(b => !b.Friendly);
        }
        const int gateCloses = 5;   // frames for the kill's pause to end and the gate to let the stage go on
        for (int i = 0; i < gateCloses; i++) g.Step();
        Assert.Equal(GateState.None, g.Gate);
        Assert.Equal(BulwarkStage, g.BossesBeaten);
        Assert.Equal(BulwarkStage + 1, g.Stage);
        Assert.DoesNotContain(boss, g.Enemies);
    }

    [Fact]
    public void Stage_two_ends_in_it()
    {
        Assert.Equal(EnemyKind.Bulwark, Ramp.Boss(BulwarkStage));
    }

    // ---------------------------------------------------------------- the machine

    [Theory]
    [MemberData(nameof(Fields))]
    public void It_enters_holds_and_sways_across_a_tenth_of_the_field_and_stays_on_the_field(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var e = g.SpawnKind(EnemyKind.Bulwark, 0.5);
        Assert.True(e.Y < 0, "it enters from above");
        Assert.Equal(0, e.Mode);
        double floor = EnemyKit.BossFloor(g.PH);
        double minX = double.MaxValue, maxX = double.MinValue;
        for (int f = 1; f <= LongWatch; f++)
        {
            StepKeeping(g, e);
            Assert.Contains(e, g.Enemies);
            Assert.True(EnemyKit.InsideWidth(g, e), $"frame {f}: x {e.X} outside {g.PW}");
            Assert.True(EnemyKit.LowestPixel(e) <= floor, $"frame {f}: lowest pixel {EnemyKit.LowestPixel(e)} under the floor {floor}");
            foreach (var p in e.Parts!)
                Assert.True(e.Y + p.OY + p.HH <= floor, $"frame {f}: the box of {p.Name} is under the floor");
            if (e.Mode == Guarded && f >= SettledBy) { minX = Math.Min(minX, e.X); maxX = Math.Max(maxX, e.X); }
            if (e.Mode == Guarded) Assert.Equal(Hold(g), e.Y, 6);
            Assert.True(EnemyKit.Extent(e).T >= 0 || e.Y < Hold(g), $"frame {f}: the top is above the field once it has settled");
        }
        Assert.Equal(Guarded, e.Mode);
        double travel = maxX - minX;
        Assert.True(travel >= GuardedSwayShare * g.PW * LeastSwaySeen && travel <= GuardedSwayShare * g.PW + SwaySlack, $"it sways across a tenth of {g.PW}: {minX:0.0} to {maxX:0.0}");
        Assert.False(e.Leaving);
    }

    [Fact]
    public void The_screen_it_sways_over_widens_to_a_quarter_once_it_is_bare()
    {
        var g = EnemyKit.Game();
        var e = Put(g, Bare, y: Hold(g));
        e.BaseX = g.PW / 2.0;
        const int frames = 500;
        double minX = double.MaxValue, maxX = double.MinValue;
        for (int f = 0; f < frames; f++)
        {
            StepKeeping(g, e);
            if (f >= EntranceFrames) { minX = Math.Min(minX, e.X); maxX = Math.Max(maxX, e.X); }
            Assert.True(EnemyKit.InsideWidth(g, e));
        }
        double travel = maxX - minX;
        Assert.True(travel >= BareSwayShare * g.PW * LeastSwaySeen && travel <= BareSwayShare * g.PW + SwaySlack, $"bare it sways across a quarter of {g.PW}: {travel:0.0}");
    }

    [Fact]
    public void Bare_on_the_narrowest_field_it_still_stays_inside()
    {
        var g = EnemyKit.Game(NarrowCols, NarrowRows);
        var e = Put(g, Bare);
        for (int f = 0; f < LongWatch; f++)
        {
            StepKeeping(g, e);
            Assert.True(EnemyKit.InsideWidth(g, e), $"frame {f}: x {e.X} outside {g.PW}");
        }
    }

    [Fact]
    public void Told_to_leave_it_fires_nothing_and_rises_off_the_field()
    {
        var g = EnemyKit.Game();
        const int partWay = 20, riseFrames = 400;
        var e = Put(g, Guarded, age: partWay);
        e.Leaving = true;
        int hostile = 0;
        for (int f = 0; f < riseFrames && g.Enemies.Contains(e); f++)
        {
            StepKeeping(g, e);
            hostile += g.Bullets.Count(b => !b.Friendly);
        }
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(0, hostile);
        Assert.Equal(0, g.Kills);
    }

    // ---------------------------------------------------------------- the parts

    [Fact]
    public void A_shot_at_the_core_pings_and_does_nothing_while_a_pod_lives()
    {
        const double damage = 30;
        foreach (var live in new[] { "both", "pod-l", "pod-r" })
        {
            var g = EnemyKit.Game();
            var played = new List<string>();
            g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
            var e = Put(g, Guarded);
            if (live != "both") Lose(g, e, live == "pod-l" ? "pod-r" : "pod-l", live);
            var core = PartOf(e, "core");
            double hp = core.Hp;
            EnemyKit.Shot(g, e.X, e.Y, damage);
            StepHoldingPlace(g, e);
            Assert.Equal(hp, core.Hp);
            Assert.Equal(PartState.Armoured, core.State);
            Assert.Contains(SfxNames.Ping, played);
            Assert.DoesNotContain(g.Bullets, b => b.Friendly && Math.Abs(b.X - e.X) < 1);
        }
    }

    [Fact]
    public void A_pod_takes_a_shot_and_the_other_pod_and_the_core_do_not_feel_it()
    {
        var g = EnemyKit.Game();
        var e = Put(g, Guarded);
        var l = PartOf(e, "pod-l"); var r = PartOf(e, "pod-r"); var core = PartOf(e, "core");
        double hp = l.Hp, total = e.Hp;
        const double damage = 5;
        EnemyKit.Shot(g, e.X + l.OX, e.Y, damage);
        StepHoldingPlace(g, e);
        Assert.Equal(hp - damage, l.Hp, 6);
        Assert.Equal(r.MaxHp, r.Hp);
        Assert.Equal(core.MaxHp, core.Hp);
        Assert.Equal(total - damage, e.Hp, 6);
    }

    [Fact]
    public void The_first_pod_gone_leaves_it_half_guarded_with_the_core_still_shut()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        const int partWay = 25;
        var e = Put(g, Guarded, age: partWay);
        int before = g.Score;
        EnemyKit.Shot(g, e.X + PartOf(e, "pod-l").OX, e.Y, KillingDamage);
        StepHoldingPlace(g, e);
        Assert.Equal(PartState.Dead, PartOf(e, "pod-l").State);
        Assert.Equal(Half, e.Mode);
        Assert.Equal(0, e.ModeAge);   // the half-guarded clock starts over
        Assert.Equal(PartState.Armoured, PartOf(e, "core").State);
        Assert.Equal(before + PodScore, g.Score);
        Assert.Contains(SfxNames.Part, played);
        Assert.Contains(e, g.Enemies);
        Assert.Equal(PartOf(e, "core").Hp + PartOf(e, "pod-r").Hp, e.Hp, 6);
    }

    [Fact]
    public void The_second_pod_gone_opens_the_core_and_it_takes_a_shot_from_then_on()
    {
        var g = EnemyKit.Game();
        const int partWay = 60;
        var e = Put(g, Half, age: partWay);
        var core = PartOf(e, "core");
        int before = g.Score;
        EnemyKit.Shot(g, e.X + PartOf(e, "pod-r").OX, e.Y, KillingDamage);
        StepHoldingPlace(g, e);
        Assert.False(Alive(e, "pod-r"));
        Assert.Equal(Bare, e.Mode);
        Assert.Equal(PartState.Open, core.State);
        Assert.Equal(core.MaxHp, core.Hp);
        Assert.Equal(before + PodScore, g.Score);
        Assert.Equal(0, e.ModeAge);   // the new mode's clock starts over
        const double damage = 10;
        EnemyKit.Shot(g, e.X, e.Y, damage);
        StepHoldingPlace(g, e);
        Assert.Equal(core.MaxHp - damage, core.Hp, 6);
        Assert.Equal(core.Hp, e.Hp, 6);
    }

    [Fact]
    public void Shooting_the_open_core_dead_kills_it_for_four_thousand()
    {
        var g = EnemyKit.Game();
        const int partWay = 20;
        var e = Put(g, Bare, age: partWay);
        int before = g.Score;
        EnemyKit.Shot(g, e.X, e.Y, KillingDamage);
        StepHoldingPlace(g, e);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal(1, g.Kills);
        Assert.Equal(before + BulwarkScore, g.Score);
    }

    [Fact]
    public void A_dead_pods_wreck_still_stops_a_shot()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        const int partWay = 10;
        const double damage = 50;   // more than a pod's hit points, so only the wreck can stop it
        var e = Put(g, Half, age: partWay);
        var core = PartOf(e, "core");
        EnemyKit.Shot(g, e.X + PartOf(e, "pod-l").OX, e.Y, damage);
        StepHoldingPlace(g, e);
        Assert.Equal(core.MaxHp, core.Hp);
        Assert.Equal(PartState.Armoured, core.State);
        Assert.Contains(SfxNames.Ping, played);
    }

    [Fact]
    public void A_bomb_hurts_the_pods_and_not_the_shut_core()
    {
        var g = EnemyKit.Game();
        const int partWay = 10;
        var e = Put(g, Guarded, age: partWay);
        var core = PartOf(e, "core"); var l = PartOf(e, "pod-l");
        g.Input.BombPressed = true;
        StepKeeping(g, e);
        Assert.Equal(core.MaxHp, core.Hp);
        Assert.True(l.Hp < l.MaxHp || l.State == PartState.Dead, "a bomb hurts an open pod");
    }

    // ---------------------------------------------------------------- when it fires

    [Fact]
    public void Guarded_the_pods_take_turns_firing_one_aimed_bolt_every_forty_frames()
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        var e = Put(g, Guarded);
        var log = StepAndRecordBolts(g, e, 3 * PodGap + PastLastBolt);
        Assert.Equal([PodGap, 2 * PodGap, 3 * PodGap], log.Select(s => s.Age));
        Assert.All(log, s => Assert.Single(s.Bolts));
        double lo = PartOf(e, "pod-l").OX, ro = PartOf(e, "pod-r").OX;
        var origins = log.Select(s => Origin(s.Bolts[0]).X - s.X).ToArray();
        Assert.Equal(lo, origins[0], 3);
        Assert.Equal(ro, origins[1], 3);
        Assert.Equal(lo, origins[2], 3);
        foreach (var s in log)
        {
            var b = s.Bolts[0];
            Assert.Equal(PodBolt, Speed(b), 4);
            var (ox, oy) = Origin(b);
            double want = Math.Atan2(g.PY - oy, g.PX - ox), got = Math.Atan2(b.VY, b.VX);
            Assert.Equal(want, got, 3);
        }
    }

    [Fact]
    public void Half_guarded_the_live_pod_fires_a_spread_of_three_every_fifty_frames()
    {
        const double leastFan = 0.3;   // radians between the spread's outer bolts
        foreach (var live in new[] { "pod-l", "pod-r" })
        {
            var g = EnemyKit.Game(TallCols, TallRows);
            var e = Put(g, Half);
            if (live == "pod-l") { PartOf(e, "pod-l").State = PartState.Open; PartOf(e, "pod-l").Hp = PartOf(e, "pod-l").MaxHp; PartOf(e, "pod-r").State = PartState.Dead; PartOf(e, "pod-r").Hp = 0; g.SetParts(e, e.Parts!.ToArray()); }
            var log = StepAndRecordBolts(g, e, 2 * HalfGap + PastLastBolt).Where(s => s.Bolts.Count == SpreadBolts).ToList();
            Assert.Equal([HalfGap, 2 * HalfGap], log.Select(s => s.Age));
            double podX = PartOf(e, live).OX;
            foreach (var s in log)
            {
                Assert.All(s.Bolts, b => { Assert.Equal(PodBolt, Speed(b), 4); Assert.Equal(podX, Origin(b).X - s.X, 3); });
                var mid = s.Bolts.OrderBy(b => Math.Atan2(b.VY, b.VX)).ToArray();
                var (ox, oy) = Origin(mid[1]);
                Assert.Equal(Math.Atan2(g.PY - oy, g.PX - ox), Math.Atan2(mid[1].VY, mid[1].VX), 3);
                Assert.True(Math.Atan2(mid[2].VY, mid[2].VX) - Math.Atan2(mid[0].VY, mid[0].VX) > leastFan, "a fan");
            }
        }
    }

    [Fact]
    public void Half_guarded_every_hundred_and_fifty_frames_the_core_charges_thirty_and_throws_a_ring_of_eight()
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        var played = new List<(int, string)>();
        Enemy? boss = null;
        g.Sfx = new SfxBus((n, _, _, _) => played.Add((boss?.ModeAge ?? -1, n)));
        var e = boss = Put(g, Half);
        var log = StepAndRecordBolts(g, e, HalfRingAt + RingGap + PastLastBolt);
        var rings = log.Where(s => s.Bolts.Count == HalfRingBolts).ToList();
        Assert.Equal([HalfRingAt, HalfRingAt + RingGap], rings.Select(s => s.Age));
        foreach (var s in rings)
        {
            Assert.All(s.Bolts, b => { Assert.Equal(RingBolt, Speed(b), 4); Assert.Equal(s.X, Origin(b).X, 3); Assert.Equal(s.Y, Origin(b).Y, 3); });
            var angles = s.Bolts.Select(b => Math.Atan2(b.VY, b.VX)).OrderBy(a => a).ToArray();
            for (int i = 1; i < angles.Length; i++) Assert.Equal(2 * Math.PI / HalfRingBolts, angles[i] - angles[i - 1], 3);
        }
        Assert.Equal([HalfRingAt - ChargeFrames, HalfRingAt + RingGap - ChargeFrames], played.Where(p => p.Item2 == SfxNames.Charge).Select(p => p.Item1));
        Assert.All(log.Where(s => s.Bolts.Count != HalfRingBolts), s => Assert.Equal(SpreadBolts, s.Bolts.Count));
    }

    [Fact]
    public void Bare_the_core_fires_a_spread_of_three_every_forty_five_frames_and_a_ring_of_ten_every_hundred_and_fifty()
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        var played = new List<(int, string)>();
        Enemy? boss = null;
        g.Sfx = new SfxBus((n, _, _, _) => played.Add((boss?.ModeAge ?? -1, n)));
        var e = boss = Put(g, Bare);
        var log = StepAndRecordBolts(g, e, 6 * BareGap + PastLastBolt);
        var spreads = log.Where(s => s.Bolts.Count == SpreadBolts).ToList();
        var rings = log.Where(s => s.Bolts.Count == BareRingBolts).ToList();
        // the second spread falls inside the charge for the ring and is held back, so the ring is the only thing coming
        Assert.Equal([BareGap, 3 * BareGap, 4 * BareGap, 5 * BareGap, 6 * BareGap], spreads.Select(s => s.Age));
        Assert.Equal([BareRingAt, BareRingAt + RingGap], rings.Select(s => s.Age));
        Assert.Equal(log.Count, spreads.Count + rings.Count);
        foreach (var s in spreads)
        {
            Assert.All(s.Bolts, b => { Assert.Equal(PodBolt, Speed(b), 4); Assert.Equal(s.X, Origin(b).X, 3); });
            Assert.True(Origin(s.Bolts[0]).Y > s.Y, "from the core's underside");
        }
        foreach (var s in rings)
        {
            Assert.All(s.Bolts, b => { Assert.Equal(RingBolt, Speed(b), 4); Assert.Equal(s.X, Origin(b).X, 3); Assert.Equal(s.Y, Origin(b).Y, 3); });
            var angles = s.Bolts.Select(b => Math.Atan2(b.VY, b.VX)).OrderBy(a => a).ToArray();
            for (int i = 1; i < angles.Length; i++) Assert.Equal(2 * Math.PI / BareRingBolts, angles[i] - angles[i - 1], 3);
        }
        Assert.Equal([BareRingAt - ChargeFrames, BareRingAt + RingGap - ChargeFrames], played.Where(p => p.Item2 == SfxNames.Charge).Select(p => p.Item1));
    }

    [Fact]
    public void The_rings_take_turns_leaving_a_gap_straight_down_and_a_bolt_straight_down()
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        var e = Put(g, Half);
        const double tolerance = 0.01;
        var rings = StepAndRecordBolts(g, e, HalfRingAt + RingGap + PastLastBolt).Where(s => s.Bolts.Count == HalfRingBolts).ToList();
        Assert.Equal(2, rings.Count);
        double Nearest(List<Bullet> ring) => ring.Min(b => Math.Abs(Math.Atan2(b.VY, b.VX) - StraightDown));
        Assert.True(Nearest(rings[0].Bolts) > Math.PI / HalfRingBolts - tolerance, "the first ring has a gap straight down");
        Assert.True(Nearest(rings[1].Bolts) < tolerance, "the next has a bolt straight down");
    }

    [Fact]
    public void A_pod_lost_part_way_through_a_mode_starts_the_next_mode_s_clock_over()
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        var e = Put(g, Guarded);
        const int lossFrame = 45, frames = 140;
        var log = new List<int>();
        for (int f = 0; f < frames; f++)
        {
            g.Bullets.RemoveAll(b => !b.Friendly);
            if (f == lossFrame) EnemyKit.Shot(g, e.X + PartOf(e, "pod-l").OX, e.Y, KillingDamage);
            StepHoldingPlace(g, e);
            if (g.Bullets.Any(b => !b.Friendly)) log.Add(f);
        }
        Assert.Equal(Half, e.Mode);
        // the guarded clock's one bolt falls on loop frame PodGap - 1, as the loop counts from 0; the half-guarded clock counts from the frame after the loss
        Assert.Equal([PodGap - 1, lossFrame + HalfGap], log.Take(2));
    }

    // ---------------------------------------------------------------- warnings

    [Theory]
    [InlineData(Guarded, PodGap - BlinkFrames, PodGap - 1)]
    [InlineData(Half, HalfGap - BlinkFrames, HalfGap - 1)]
    [InlineData(Half, HalfRingAt - ChargeFrames, HalfRingAt - 1)]
    [InlineData(Bare, BareGap - BlinkFrames, BareGap - 1)]
    [InlineData(Bare, BareRingAt - ChargeFrames, BareRingAt - 1)]
    public void From_the_first_frame_of_a_wind_up_to_its_last_nothing_of_the_boss_can_kill(int mode, int first, int last)
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        var e = Put(g, mode, age: first - 1);
        g.Bullets.Clear();
        for (int age = first; age <= last; age++)
        {
            StepHoldingPlace(g, e);
            Assert.Equal(age, e.ModeAge);
            Assert.DoesNotContain(g.Bullets, b => !b.Friendly);
            Assert.False(Bulwark.Touches(g, e));
        }
        StepHoldingPlace(g, e);
        Assert.Contains(g.Bullets, b => !b.Friendly);   // and the frame after the wind-up the bolt is out
    }

    // ---------------------------------------------------------------- pixels

    private static (int X0, int Y0) Corner(Enemy e) => ((int)Math.Round(e.X - BodyW / 2.0), (int)Math.Round(e.Y - BodyH / 2.0));

    [Fact]
    public void It_is_the_drawing_with_the_pods_blue_the_core_crimson_in_the_middle_and_the_dark_palette()
    {
        var g = EnemyKit.Game();
        var e = Put(g, Guarded, y: OpenRow);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        var (x0, y0) = Corner(e);
        Assert.Equal(BodyW, e.Sprite.W);
        Assert.Equal(BodyH, e.Sprite.H);
        var expect = new Dictionary<char, uint>
        {
            ['#'] = Palette.Plum,
            ['o'] = DarkBody,
            ['+'] = Art.DarkViolet,
            ['r'] = Crimson,
            ['y'] = Gold,
            ['b'] = Blue,
            ['B'] = DeepBlue,
        };
        for (int y = 0; y < BodyH; y++)
            for (int x = 0; x < BodyW; x++)
                if (expect.TryGetValue(Drawing[y][x], out uint c))
                    Assert.True((f.Get(x0 + x, y0 + y) & EnemyKit.RgbMask) == c, $"({x}, {y}) of the drawing is '{Drawing[y][x]}': {f.Get(x0 + x, y0 + y) & EnemyKit.RgbMask:x6}, not {c:x6}");
    }

    [Fact]
    public void The_parts_are_drawn_exactly_over_the_body_for_every_whole_pixel_position()
    {
        const int left = 40, top = 20, shifts = 4;   // every even and odd column and row, somewhere in the open
        var g = EnemyKit.Game();
        for (int x = left; x < left + shifts; x++)
            for (int y = top; y < top + shifts; y++)
            {
                EnemyKit.Clear(g);
                var e = Put(g, Guarded, x: x + 0.5 * (x % 2), y: y);
                var f = EnemyKit.ClearPickupsAndDraw(g);
                var (x0, y0) = Corner(e);
                var whole = Sprite.FromArt(Drawing, Art.With(('b', Blue), ('B', DeepBlue)));
                for (int sy = 0; sy < BodyH; sy++)
                    for (int sx = 0; sx < BodyW; sx++)
                    {
                        uint v = whole.Px[sy * whole.W + sx];
                        if (v == 0) continue;
                        Assert.True((f.Get(x0 + sx, y0 + sy) & EnemyKit.RgbMask) == (v & EnemyKit.RgbMask), $"at ({e.X}, {e.Y}) the pixel ({sx}, {sy}) is off");
                    }
            }
    }

    [Fact]
    public void The_open_core_is_crimson_round_a_gold_middle()
    {
        var g = EnemyKit.Game();
        var e = Put(g, Bare, age: StillAge, y: OpenRow);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        var (x0, y0) = Corner(e);
        Assert.Equal(Gold, f.Get(x0 + CoreCol, y0 + CentreRow) & EnemyKit.RgbMask);
        foreach (var (dx, dy) in new[] { (-2, 0), (-1, 0), (1, 0), (2, 0), (-1, -1), (0, -1), (1, -1), (-1, 1), (0, 1), (1, 1) })
            Assert.Equal(Crimson, f.Get(x0 + CoreCol + dx, y0 + CentreRow + dy) & EnemyKit.RgbMask);
        int violetRow = Array.FindIndex(Drawing, row => row.Contains('+'));
        Assert.Equal(Art.DarkViolet, f.Get(x0 + Drawing[violetRow].IndexOf('+'), y0 + violetRow) & EnemyKit.RgbMask);   // the rest of the core is as it was
        Assert.Equal(Palette.Plum, f.Get(x0 + CoreLeft, y0 + CentreRow) & EnemyKit.RgbMask);
    }

    [Fact]
    public void A_dead_pod_leaves_a_husk_on_its_strut_that_keeps_the_outline()
    {
        var g = EnemyKit.Game();
        var e = Put(g, Half, age: StillAge, y: OpenRow);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        var (x0, y0) = Corner(e);
        const int ringRow = 3, outlineRow = 1;   // a row of the left pod's light blue ring, and its top outline
        Assert.Equal(Art.Husk, f.Get(x0 + PodLeftCol, y0 + CentreRow) & EnemyKit.RgbMask);   // the pod's gold middle, husked
        Assert.Equal(Art.Husk, f.Get(x0 + Drawing[ringRow].IndexOf('b'), y0 + ringRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Plum, f.Get(x0 + Drawing[outlineRow].IndexOf('#'), y0 + outlineRow) & EnemyKit.RgbMask);
        Assert.Equal(Gold, f.Get(x0 + PodRightCol, y0 + CentreRow) & EnemyKit.RgbMask);      // the live pod beside it is whole
        Assert.Equal(DarkBody, f.Get(x0 + StrutCol, y0 + CentreRow) & EnemyKit.RgbMask);  // the strut stays
    }

    [Fact]
    public void A_pods_gold_centre_blinks_for_twenty_frames_before_it_fires_and_is_white_for_the_last_third()
    {
        var g = EnemyKit.Game();
        var e = Put(g, Guarded, y: OpenRow);
        var (x0, y0) = Corner(e);
        uint Centre(int podCol, int age)
        {
            e.ModeAge = age;
            return EnemyKit.ClearPickupsAndDraw(g).Get(x0 + podCol, y0 + CentreRow) & EnemyKit.RgbMask;
        }
        // the first bolt is the left pod's; the right pod's is one gap later
        const int blinkFrom = PodGap - BlinkFrames;
        for (int age = 0; age < blinkFrom; age++) { Assert.Equal(Gold, Centre(PodLeftCol, age)); Assert.Equal(Gold, Centre(PodRightCol, age)); }
        var window = Enumerable.Range(blinkFrom, WhiteFrom).Select(a => Centre(PodLeftCol, a)).ToArray();
        Assert.Contains(Gold, window);
        Assert.Contains(White, window);
        for (int age = blinkFrom + WhiteFrom; age < PodGap; age++) Assert.Equal(White, Centre(PodLeftCol, age));
        for (int age = blinkFrom; age < PodGap; age++) Assert.Equal(Gold, Centre(PodRightCol, age));   // the other pod is not the one firing
        Assert.Equal(Gold, Centre(PodLeftCol, PodGap));
        for (int age = PodGap + blinkFrom; age < 2 * PodGap; age++) Assert.Equal(Gold, Centre(PodLeftCol, age));
        for (int age = PodGap + blinkFrom + WhiteFrom; age < 2 * PodGap; age++) Assert.Equal(White, Centre(PodRightCol, age));
    }

    [Fact]
    public void The_core_whitens_inward_in_thirds_round_its_gold_mark_over_the_thirty_frames_before_a_ring_and_a_ring_of_dots_shows_where_it_goes()
    {
        var g = EnemyKit.Game();
        var e = Put(g, Half, y: OpenRow);
        var (x0, y0) = Corner(e);
        var cluster = new[] { (-1, -1), (0, -1), (1, -1), (-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (-1, 1), (0, 1), (1, 1) };
        int Whites(int age, IEnumerable<(int, int)> px)
        {
            e.ModeAge = age;
            var f = EnemyKit.ClearPickupsAndDraw(g);
            return px.Count(p => (f.Get(x0 + CoreCol + p.Item1, y0 + CentreRow + p.Item2) & EnemyKit.RgbMask) == White);
        }
        const int chargeFrom = HalfRingAt - ChargeFrames, third = ChargeFrames / 3;
        Assert.Equal(0, Whites(chargeFrom - 1, cluster));
        Assert.Equal(0, Whites(HalfRingAt, cluster));      // the frame it throws, nothing is left of the warning
        Assert.Equal(2, Whites(chargeFrom, cluster));      // the first third: the two ends
        Assert.Equal(2, Whites(chargeFrom + third - 1, cluster));
        Assert.Equal(6, Whites(chargeFrom + third, cluster));      // the second: the corners too
        Assert.Equal(6, Whites(chargeFrom + 2 * third - 1, cluster));
        Assert.Equal(8, Whites(chargeFrom + 2 * third, cluster));      // the last third: all of it but the gold and the crimson either side
        Assert.Equal(8, Whites(HalfRingAt - 1, cluster));
        var mark = new[] { (-1, 0), (0, 0), (1, 0) };
        Assert.Equal(0, Whites(HalfRingAt - 1, mark));         // where to shoot stays gold on crimson through the whole warning

        // the dots: one for each bolt, on a circle round the core's middle, in crimson and then in white
        uint Dot(int age, int k, int n)
        {
            e.ModeAge = age;
            double a = StraightDown + (k + 0.5) * (2 * Math.PI / n);
            return EnemyKit.ClearPickupsAndDraw(g).Get((int)Math.Round(e.X + Math.Cos(a) * RingDotRadius), (int)Math.Round(e.Y + Math.Sin(a) * RingDotRadius)) & EnemyKit.RgbMask;
        }
        const double belowCore = 0.3;   // the sine of a dot's angle past which it sits under the core
        for (int k = 0; k < HalfRingBolts; k++)
        {
            Assert.Equal(Crimson, Dot(chargeFrom, k, HalfRingBolts));
            Assert.Equal(Crimson, Dot(chargeFrom + 2 * third - 1, k, HalfRingBolts));
            Assert.Equal(White, Dot(chargeFrom + 2 * third, k, HalfRingBolts));
            if (Math.Sin(StraightDown + (k + 0.5) * (2 * Math.PI / HalfRingBolts)) > belowCore)   // below the core: above it the health bar is crimson too
            {
                Assert.NotEqual(Crimson, Dot(chargeFrom - 1, k, HalfRingBolts));
                Assert.NotEqual(Crimson, Dot(HalfRingAt, k, HalfRingBolts));
            }
        }
    }

    [Fact]
    public void A_blue_arc_joins_each_live_pod_to_the_core_and_a_dead_pod_s_is_gone()
    {
        int Arcs(Surface f, Enemy e, int side)
        {
            var (x0, y0) = Corner(e);
            int n = 0;
            int from = side < 0 ? x0 : x0 + CoreCol + 1, to = side < 0 ? x0 + CoreCol - 1 : x0 + BodyW - 1;   // the core's own column belongs to neither
            for (int y = y0 + BodyH; y < y0 + BodyH + ArcDepth; y++)
                for (int x = from; x <= to; x++)
                    if ((f.Get(x, y) & EnemyKit.RgbMask) == Blue) n++;
            return n;
        }
        const int leastArc = 8;
        var g = EnemyKit.Game();
        var e = Put(g, Guarded, age: StillAge, y: OpenRow);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        Assert.True(Arcs(f, e, -1) >= leastArc, "an arc on the left");
        Assert.True(Arcs(f, e, +1) >= leastArc, "an arc on the right");

        EnemyKit.Clear(g);
        var h = Put(g, Half, age: StillAge, y: OpenRow);
        var f2 = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(0, Arcs(f2, h, -1));
        Assert.True(Arcs(f2, h, +1) >= leastArc);

        EnemyKit.Clear(g);
        var b = Put(g, Bare, age: StillAge, y: OpenRow);
        var f3 = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(0, Arcs(f3, b, -1));
        Assert.Equal(0, Arcs(f3, b, +1));
    }

    // ---------------------------------------------------------------- voice

    private static void Offer(RaiderGame g, ulong seed, string text, DarkSource source = DarkSource.Label) =>
        g.OfferDark(new DarkPost(DarknessKind.Rude, seed, text, source, source == DarkSource.Label ? "lab" + seed : null));

    private static void Until(RaiderGame g, Enemy e, Func<bool> done, int frames = SpeechWait)
    {
        for (int i = 0; i < frames && !done(); i++) StepHoldingPlace(g, e);
        Assert.True(done());
    }

    [Fact]
    public void It_takes_a_hidden_reply_before_an_older_post()
    {
        Assert.Equal(0, Bulwark.Taste(EnemyKit.Speaker(EnemyKind.Bulwark), []));
        var posts = new List<RudePost>
        {
            new(1, "oldest label", DarkSource.Label, "a", 1),
            new(2, "label two", DarkSource.Label, "b", 2),
            new(3, "hidden one", DarkSource.Hidden, null, 3),
            new(4, "hidden two", DarkSource.Hidden, null, 4),
        };
        Assert.Equal(2, Bulwark.Taste(EnemyKit.Speaker(EnemyKind.Bulwark), posts));
        Assert.Equal(1, Bulwark.Taste(EnemyKit.Speaker(EnemyKind.Bulwark), posts.Skip(1).ToList()));
        Assert.Equal(0, Bulwark.Taste(EnemyKit.Speaker(EnemyKind.Bulwark), posts.Where(p => p.Source == DarkSource.Label).ToList()));
        Assert.Equal(0, Bulwark.Taste(EnemyKit.Speaker(EnemyKind.Bulwark), posts.Where(p => p.Source == DarkSource.Hidden).ToList()));
    }

    [Fact]
    public void From_a_store_of_four_it_says_the_hidden_reply_on_arriving_and_a_new_post_at_each_loss()
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        Offer(g, 1, "oldest label");
        Offer(g, 2, "hidden one", DarkSource.Hidden);
        Offer(g, 3, "newest label");
        Offer(g, 4, "hidden two", DarkSource.Hidden);
        var e = g.SpawnKind(EnemyKind.Bulwark, 0.5);
        Until(g, e, () => e.Text is not null, LongSpeechWait);
        Assert.Equal("hidden one", e.Text);
        Assert.Equal(["oldest label", "newest label", "hidden two"], g.Rude.Waiting.Select(p => p.Text).ToArray());

        // lose a pod: a new post cuts in at once (the next hidden reply)
        e.Mode = Guarded;
        EnemyKit.Shot(g, e.X + PartOf(e, "pod-l").OX, e.Y, KillingDamage);
        StepHoldingPlace(g, e);
        Assert.Equal("hidden two", e.Text);

        // the other pod: the core is bared, and one post (the oldest left) cuts in; the loss itself adds no second
        EnemyKit.Shot(g, e.X + PartOf(e, "pod-r").OX, e.Y, KillingDamage);
        StepHoldingPlace(g, e);
        Assert.Equal("oldest label", e.Text);
        Assert.Equal(["newest label"], g.Rude.Waiting.Select(p => p.Text).ToArray());
    }

    [Fact]
    public void With_the_store_empty_it_says_its_own_words_under_its_name()
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        var e = g.SpawnKind(EnemyKind.Bulwark, 0.5);
        Until(g, e, () => e.Text is not null, LongSpeechWait);
        Assert.Equal("Replies are limited.", e.Text);
        Assert.Equal("BULWARK", e.Header);
        e.Mode = Guarded;
        EnemyKit.Shot(g, e.X + PartOf(e, "pod-l").OX, e.Y, KillingDamage);
        StepHoldingPlace(g, e);
        Assert.Equal("That was load-bearing.", e.Text);
        EnemyKit.Shot(g, e.X + PartOf(e, "pod-r").OX, e.Y, KillingDamage);
        StepHoldingPlace(g, e);
        Assert.Equal("Fine. Say it to my face.", e.Text);
        Assert.Equal("BULWARK", e.Header);
        EnemyKit.Shot(g, e.X, e.Y, KillingDamage);
        StepHoldingPlace(g, e);
        Assert.DoesNotContain(e, g.Enemies);
        Assert.Equal("Unblocked.", e.Text);
    }

    /// <summary>Seconds its box stays shut before the next post: behind the shield, and with both pods gone.</summary>
    private const int ShieldedSeconds = 10, BaredSeconds = 4;

    [Fact]
    public void Each_cue_gets_its_own_turn()
    {
        var g = EnemyKit.Game();
        var e = Put(g, Guarded);
        Assert.Equal(Turn.Wait, Bulwark.TurnFor(g, e, VoiceCue.Arrive));
        Assert.Equal(Turn.Cut, Bulwark.TurnFor(g, e, VoiceCue.PartLost));
        Assert.Equal(Turn.Cut, Bulwark.TurnFor(g, e, VoiceCue.Phase));
        Assert.Equal(Turn.Cut, Bulwark.TurnFor(g, e, VoiceCue.Dying));
        Assert.Equal(Turn.Cut, Bulwark.TurnFor(g, e, VoiceCue.Leaving));
        Assert.Equal(Turn.Wait, Bulwark.TurnFor(g, e, VoiceCue.ShipDied));
        Assert.Equal(Turn.Wait, Bulwark.TurnFor(g, e, VoiceCue.Idle));
        Assert.Equal(ShieldedSeconds * Tuning.Fps, Bulwark.IdleFrames(e));      // behind its shield it is in no hurry
        Lose(g, e, "pod-l", live: "pod-r");
        Assert.Equal(ShieldedSeconds * Tuning.Fps, Bulwark.IdleFrames(e));
        Assert.Equal(Turn.Cut, Bulwark.TurnFor(g, e, VoiceCue.PartLost));
        Lose(g, e, "pod-r", live: null);
        Open(g, e);
        Assert.Equal(Turn.Wait, Bulwark.TurnFor(g, e, VoiceCue.Idle));
        Assert.Equal(BaredSeconds * Tuning.Fps, Bulwark.IdleFrames(e));         // bared, it talks fast
        Assert.Equal(Turn.Skip, Bulwark.TurnFor(g, e, VoiceCue.PartLost));      // the loss that bared it is said by the Phase cue alone
    }

    /// <summary>Frames a silence may run before the test gives up, and the frames a measured silence may differ from the pace (the box's last frame and the turn's first).</summary>
    private const int SilenceLimit = 900, PaceSlack = 4;

    [Fact]
    public void Behind_its_shield_the_next_post_comes_slowly_and_once_bare_it_comes_fast()
    {
        var g = EnemyKit.Game(TallCols, TallRows);
        var e = g.SpawnKind(EnemyKind.Bulwark, 0.5);
        Until(g, e, () => e.Text is not null, LongSpeechWait);
        Offer(g, 9, "late post"); Offer(g, 10, "later post");
        void Step() { g.Bullets.RemoveAll(b => !b.Friendly); StepHoldingPlace(g, e); }
        int Silence(string until)
        {
            Step();   // the box wraps its text on its first frame
            for (int i = 0; i < SilenceLimit && e.SpeechBoxAge < EnemySpeechBox.Frames(e); i++) Step();
            int shut = 0;
            for (; shut < SilenceLimit && e.Text != until; shut++) Step();
            Assert.Equal(until, e.Text);
            return shut;
        }
        Assert.InRange(Silence("late post"), ShieldedSeconds * Tuning.Fps - PaceSlack, ShieldedSeconds * Tuning.Fps + PaceSlack);

        e.Mode = Half;
        Lose(g, e, "pod-l", live: "pod-r");
        Lose(g, e, "pod-r", live: null);
        Open(g, e);
        e.Mode = Bare;
        Assert.InRange(Silence("later post"), BaredSeconds * Tuning.Fps - PaceSlack, BaredSeconds * Tuning.Fps + PaceSlack);
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_the_bulwark_in_it()
    {
        (string, bool) Run(ulong seed)
        {
            var g = new RaiderGame(seed) { SignedIn = true };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtBoss(BulwarkStage);
            bool seen = false;
            for (int f = 0; f < GateRunFrames; f++)
            {
                EnemyKit.BotFrame(g, Dodge);
                g.Step();
                seen |= g.Enemies.Any(e => e.Kind == EnemyKind.Bulwark);
            }
            var state = string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}:{g.Bullets.Count}";
            return (state, seen);
        }
        const ulong seed = 11;
        var (a, seenA) = Run(seed);
        var (b, _) = Run(seed);
        Assert.Equal(a, b);
        Assert.True(seenA, $"stage 2's gate brings the bulwark inside {GateRunFrames} frames");
    }

    // ---------------------------------------------------------------- the fight

    /// <summary>Steps aside from the bolt that will pass nearest the ship soonest, and goes back to the pods once nothing threatens it.</summary>
    private static double? Dodge(RaiderGame g)
    {
        double best = double.MaxValue, at = 0;
        foreach (var b in g.Bullets)
        {
            if (b.Friendly || b.VY <= MinThreatSpeed || b.Y > g.PY) continue;
            double t = (g.PY - b.Y) / b.VY;
            if (t > LookAheadFrames) continue;
            double x = b.X + b.VX * t;
            if (Math.Abs(x - g.PX) > HitHalfWidth || t >= best) continue;
            best = t; at = x;
        }
        if (best == double.MaxValue) return null;
        double away = at >= g.PX ? g.PX - DodgeStep : g.PX + DodgeStep;
        if (away < EdgeMargin) away = g.PX + DodgeStep;
        if (away > g.PW - EdgeMargin) away = g.PX - DodgeStep;
        return Math.Clamp(away, EdgeMargin, g.PW - EdgeMargin);
    }

    /// <summary>Frames a bot that fires always, keeps under the lowest open part and steps aside from bolts needs to beat a bulwark at stage 2's weapon level; -1 if it does not.</summary>
    private static int Fight(ulong seed, int level, out int deaths)
    {
        var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtStage(BulwarkStage);
        EnemyKit.Clear(g);
        while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); EnemyKit.Clear(g); }
        // each seed brings it in at its own share of the width, stepped across the middle of the field
        const double spawnLeast = 0.35, spawnSpread = 0.3;
        const ulong seedStride = 7, spawnSteps = 10;
        var e = g.SpawnKind(EnemyKind.Bulwark, spawnLeast + spawnSpread * ((seed * seedStride) % spawnSteps) / (double)spawnSteps);
        // it has arrived and the fight begins from the first frame it holds: the entrance is not part of the time
        int result = -1;
        int start = -1;
        for (int f = 0; f < FightFrameCap; f++)
        {
            EnemyKit.BotFrame(g, Dodge);
            g.Step();
            g.Enemies.RemoveAll(x => x != e);
            if (start < 0 && e.Mode >= Guarded) start = f;
            if (!g.Enemies.Contains(e)) { result = g.Kills > 0 ? f + 1 - Math.Max(0, start) : -1; break; }
        }
        deaths = g.Deaths;
        return result;
    }

    [Fact]
    public void A_bot_at_stage_twos_weapon_level_takes_it_apart_in_ten_to_twenty_seven_seconds_with_a_median_of_fifteen_to_seventeen()
    {
        const int seeds = 6, quickestSeconds = 10, slowestSeconds = 27, medianLeastSeconds = 15, medianMostSeconds = 17;
        var times = new List<int>();
        int deaths = 0;
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            int t = Fight(seed, FightLevel, out int d);
            deaths += d;
            Assert.True(t > 0, $"seed {seed}: not down inside {FightFrameCap} frames");
            times.Add(t);
        }
        times.Sort();
        int median = times[times.Count / 2];
        output.WriteLine($"bulwark fight at level {FightLevel}, frames to kill: {string.Join(" ", times)}; median {median} ({median / (double)Tuning.Fps:0.0} s), the bot died {deaths} times");
        Assert.All(times, t => Assert.InRange(t, quickestSeconds * Tuning.Fps, slowestSeconds * Tuning.Fps));   // a good run is quick, none drags past the band
        Assert.InRange(median, medianLeastSeconds * Tuning.Fps, medianMostSeconds * Tuning.Fps);   // the median sits at the short end of the band
    }

    [Fact]
    public void A_ship_that_never_moves_dies()
    {
        const ulong seed = 7;
        const int standStill = 900;
        var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtStage(BulwarkStage);
        EnemyKit.Clear(g);
        var e = g.SpawnKind(EnemyKind.Bulwark, 0.5);
        for (int f = 0; f < standStill && g.Deaths == 0; f++) StepKeeping(g, e);
        Assert.True(g.Deaths > 0, "a ship that stays where it is is shot");
    }
}
