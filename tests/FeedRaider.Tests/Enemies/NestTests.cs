using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Nest (stage 6's mini-boss) and the Gnat it and the Brood let out. A nest holds near the top, pulses its hatch for 24 frames and
/// lets two gnats out every 100 (up to eight alive), and bursts into three more when it dies. A gnat is one hit point whatever the stage,
/// harmless for its first 20 frames, flies at 0.75 turning at most four degrees a frame, and burns out harmlessly at 420 frames.
/// </summary>
public sealed class NestTests(ITestOutputHelper output)
{
    private const uint Bile = 0xc8e03c;
    /// <summary>A grace so long that a gnat given it never arms within a test.</summary>
    private const int NeverArmed = 100000;
    /// <summary>An age so far below zero that a gnat given it never burns out within a test.</summary>
    private const int NeverBurnsOutAge = -100000;
    private const uint NestBody = 0x4b2a78;
    private const int NestW = 13, NestH = 9;
    private const double NestHalfW = NestW / 2.0, NestHalfH = NestH / 2.0;
    private const int HatchColumn = 6, HatchRow = 6;
    private const double HoldFraction = 0.15, HoldMinimum = 8;
    private const int HudRows = 3;
    private const int SettledFrame = 100;
    private const int BotFrameCap = 2400, BotSecondsToKill = 20;

    /// <summary>What a gnat scores, the side of its square body, the frames it cannot hurt for and the frame it burns out on.</summary>
    private const int GnatScore = 30, GnatSide = 3, GnatGrace = 20, GnatLife = 420;

    /// <summary>A gnat's speed in pixels a frame, and the most it turns in a frame, in degrees.</summary>
    private const double GnatSpeed = 0.75, GnatTurnDegrees = 4;

    /// <summary>A nest's hit points at stage one and what it scores.</summary>
    private const int NestHp = 17, NestScore = 800;

    /// <summary>A nest pulses its hatch this many frames before each release, releases every period, lets out a pair each time,
    /// keeps at most the cap of its gnats alive, and bursts into this many when it dies.</summary>
    private const int PulseFrames = 24, ReleasePeriod = 100, PerRelease = 2, GnatCap = 8, BurstGnats = 3;

    /// <summary>A shot far heavier than a nest's hit points.</summary>
    private const double KillingShot = 100;

    /// <summary>The cell a drawing test centres its enemy on.</summary>
    private const int DrawX = 40, DrawY = 20;

    /// <summary>A spot well inside the field, clear of the ship, where a test lets a gnat out.</summary>
    private const double SpotX = 30, SpotY = 20;

    /// <summary>Frames to wait for a settled nest's first words.</summary>
    private const int SpeakWithin = 100;

    private static EnemyBehaviour Nest => RaiderGame.TableBehaviour(EnemyKind.Nest)!;
    private static EnemyBehaviour Gnat => RaiderGame.TableBehaviour(EnemyKind.Gnat)!;

    /// <summary>A game with no boss gate (so a long run is not cleared out from under the test), swept of its filler wave.</summary>
    private static RaiderGame Field(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows, ulong seed = EnemyKit.PlainSeed)
    {
        var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        EnemyKit.Clear(g);
        return g;
    }

    private static double Hold(RaiderGame g) => Math.Max(HoldFraction * g.PH, HoldMinimum);

    private static List<Enemy> Gnats(RaiderGame g) => g.Enemies.Where(e => e.Kind == EnemyKind.Gnat).ToList();

    private static (RaiderGame G, List<string> Played) Listening()
    {
        var g = Field();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        return (g, played);
    }

    /// <summary>A nest placed in the middle at the row it holds, with its sway stopped so it stays put.</summary>
    private static Enemy Settled(RaiderGame g)
    {
        var e = EnemyKit.Place(g, EnemyKind.Nest, g.PW / 2.0, Hold(g));
        e.Amp = 0;
        return e;
    }

    /// <summary>Makes every gnat on the field unable to hurt, so a test can watch the nest without losing the ship.</summary>
    private static void Soothe(RaiderGame g) { foreach (var x in Gnats(g)) x.Grace = NeverArmed; }

    // ---------------------------------------------------------------- the gnat

    [Fact]
    public void The_gnat_is_small_and_worth_thirty()
    {
        Assert.Equal(EnemyKind.Gnat, Gnat.Kind);
        Assert.Equal("GNAT", Gnat.Name);
        Assert.Equal(GnatScore, Gnat.Score);
        Assert.False(Gnat.Boss);
        Assert.False(Gnat.Speaks);
        Assert.Equal(GnatSide, Gnat.Body.W);
        Assert.Equal(GnatSide, Gnat.Body.H);
        Assert.Equal(Bile, Gnat.Colour);
    }

    [Fact]
    public void The_gnat_is_drawn_green_wings_and_a_gold_body()
    {
        var g = Field();
        var e = EnemyKit.Place(g, EnemyKind.Gnat, DrawX + 0.5, DrawY + 0.5);
        e.Grace = 0;
        var f = EnemyKit.ClearPickupsAndDraw(g);
        Assert.Equal(Bile, f.Get(DrawX - 1, DrawY - 1) & EnemyKit.RgbMask);
        Assert.Equal(Bile, f.Get(DrawX + 1, DrawY - 1) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Gold, f.Get(DrawX, DrawY) & EnemyKit.RgbMask);
        Assert.Equal(Bile, f.Get(DrawX - 1, DrawY + 1) & EnemyKit.RgbMask);
        Assert.Equal(Bile, f.Get(DrawX + 1, DrawY + 1) & EnemyKit.RgbMask);
    }

    [Fact]
    public void A_gnat_has_one_hit_point_whatever_the_stage()
    {
        const int LateStage = 8;
        var g = Field();
        g.StartAtStage(LateStage);
        EnemyKit.Clear(g);
        var gnat = g.SpawnAdd(EnemyKind.Gnat, SpotX, SpotY);
        Assert.Equal(1, gnat.Hp);
        Assert.Equal(1, gnat.MaxHp);
    }

    [Fact]
    public void A_gnat_cannot_hurt_for_20_frames_and_then_can()
    {
        const double AboveShip = 8, OverTheShip = 14;
        var (g, played) = Listening();
        var gnat = g.SpawnAdd(EnemyKind.Gnat, g.PX, g.PY - AboveShip);
        for (int f = 1; f < GnatGrace; f++)
        {
            EnemyKit.StepSweeping(g);
            Assert.False(gnat.Armed, $"frame {f}");
            Assert.Contains(gnat, g.Enemies);
            Assert.DoesNotContain(SfxNames.Hurt, played);
            Assert.DoesNotContain(SfxNames.ShieldHit, played);
        }
        Assert.True(Math.Abs(gnat.Y - g.PY) < OverTheShip, "it did pass over the ship");

        var (h, hurt) = Listening();
        var close = h.SpawnAdd(EnemyKind.Gnat, h.PX, h.PY);
        close.Age = GnatGrace - 2;
        EnemyKit.StepSweeping(h);
        Assert.DoesNotContain(SfxNames.ShieldHit, hurt);
        EnemyKit.StepSweeping(h);
        Assert.True(close.Armed);
        Assert.Contains(SfxNames.ShieldHit, hurt);
    }

    [Fact]
    public void A_gnat_flies_at_three_quarters_of_a_pixel_a_frame_toward_the_ship()
    {
        const double AboveShip = 40;
        var g = Field();
        var gnat = g.SpawnAdd(EnemyKind.Gnat, g.PX, g.PY - AboveShip);
        gnat.Grace = NeverArmed;
        double d0 = Math.Abs(gnat.Y - g.PY);
        EnemyKit.StepSweeping(g);
        double px = gnat.X, py = gnat.Y;
        EnemyKit.StepSweeping(g);
        Assert.Equal(GnatSpeed, Math.Sqrt((gnat.X - px) * (gnat.X - px) + (gnat.Y - py) * (gnat.Y - py)), 6);
        Assert.True(gnat.Y > py, "it flies at the ship");
        Assert.True(Math.Abs(gnat.Y - g.PY) < d0);
    }

    [Fact]
    public void A_gnat_turns_at_most_four_degrees_a_frame_and_does_turn_that_much()
    {
        const double BesideShip = 24, HalfTurn = 180, FullTurn = 360;
        const double RoundingOver = 0.0001, FullTurnSlack = 0.01;
        const int Frames = 80, LeastFullTurns = 10;
        var g = Field();
        var gnat = g.SpawnAdd(EnemyKind.Gnat, g.PX + BesideShip, g.PY);   // level with the ship, to the side: it must swing round from where it set off
        gnat.Grace = NeverArmed;
        double px = gnat.X, py = gnat.Y, last = double.NaN, most = 0;
        int turned = 0;
        for (int f = 1; f <= Frames; f++)
        {
            EnemyKit.StepSweeping(g);
            double a = Math.Atan2(gnat.Y - py, gnat.X - px) * HalfTurn / Math.PI;
            if (!double.IsNaN(last))
            {
                double d = Math.Abs(((a - last + FullTurn + HalfTurn) % FullTurn) - HalfTurn);
                most = Math.Max(most, d);
                Assert.True(d <= GnatTurnDegrees + RoundingOver, $"frame {f}: turned {d:0.000} degrees");
                if (d > GnatTurnDegrees - FullTurnSlack) turned++;
            }
            last = a; px = gnat.X; py = gnat.Y;
        }
        Assert.True(turned >= LeastFullTurns, $"it turns the full four degrees when it must ({turned} frames, most {most:0.00})");
    }

    [Fact]
    public void A_moving_ship_outruns_a_gnat()
    {
        const double BehindShip = 30, AtTheWall = 4;
        const int Frames = 40;
        var g = Field();
        var gnat = g.SpawnAdd(EnemyKind.Gnat, g.PX - BehindShip, g.PY);
        gnat.Grace = 0;
        g.Input.Key(GameKey.Right, true);
        double gap0 = Math.Abs(g.PX - gnat.X);
        for (int f = 1; f <= Frames; f++) EnemyKit.StepSweeping(g);
        Assert.True(g.Hp > 0);
        Assert.True(Math.Abs(g.PX - gnat.X) > gap0 || g.PX >= g.PW - AtTheWall, "the ship pulls away");
    }

    [Fact]
    public void A_gnat_burns_out_at_420_frames_leaving_no_score_and_no_kill()
    {
        var g = Field();
        var gnat = g.SpawnAdd(EnemyKind.Gnat, SpotX, SpotY);
        gnat.Grace = NeverArmed;
        int score = g.Score, kills = g.Kills;
        for (int f = 1; f < GnatLife; f++) EnemyKit.StepSweeping(g);
        Assert.Contains(gnat, g.Enemies);
        EnemyKit.StepSweeping(g);
        Assert.DoesNotContain(gnat, g.Enemies);
        Assert.Equal(score, g.Score);
        Assert.Equal(kills, g.Kills);
    }

    [Fact]
    public void A_shot_gnat_dies_for_thirty()
    {
        const double ShotX = 40, ShotY = 30;
        var g = Field();
        var gnat = g.SpawnAdd(EnemyKind.Gnat, ShotX, ShotY);
        gnat.Grace = NeverArmed;
        int score = g.Score;
        EnemyKit.Shot(g, gnat.X, gnat.Y, 1);
        EnemyKit.StepSweeping(g);
        Assert.DoesNotContain(gnat, g.Enemies);
        Assert.Equal(score + GnatScore, g.Score);
    }

    // ---------------------------------------------------------------- the nest: what it is

    [Fact]
    public void The_nest_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Nest, Nest.Kind);
        Assert.Equal("NEST", Nest.Name);
        Assert.Equal(NestHp, Nest.Hp);
        Assert.Equal(NestScore, Nest.Score);
        Assert.False(Nest.Boss);
        Assert.True(Nest.Speaks);
        Assert.True(Nest.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(NestW, Nest.Body.W);
        Assert.Equal(NestH, Nest.Body.H);
    }

    private static readonly string[] Drawing =
    [
        "...#######...",
        ".##ogooogo##.",
        "#oggoogooggo#",
        "#ogoogogoogo#",
        "#oogoogoogoo#",
        "#ogoo###oogo#",
        ".#oo#ooo#oo#.",
        "..###ooo###..",
        "....#####....",
    ];

    private static uint Colour(char c) => c switch { '#' => Palette.Plum, 'o' => NestBody, 'g' => Bile, _ => 0 };

    [Fact]
    public void The_nest_is_drawn_as_the_drawing()
    {
        var g = Field();
        var e = EnemyKit.Place(g, EnemyKind.Nest, DrawX + 0.5, DrawY + 0.5);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(e.X - NestHalfW), y0 = (int)Math.Round(e.Y - NestHalfH);
        for (int r = 0; r < NestH; r++)
            for (int c = 0; c < NestW; c++)
            {
                uint want = Colour(Drawing[r][c]);
                if (want == 0) continue;
                Assert.Equal(want, f.Get(x0 + c, y0 + r) & EnemyKit.RgbMask);
            }
    }

    [Fact]
    public void The_nest_has_seventeen_hit_points_at_stage_one()
    {
        var g = Field();
        var e = g.SpawnKind(EnemyKind.Nest, 0.5);
        Assert.Equal(NestHp * Ramp.HpMul(1), e.Hp, 9);
    }

    // ---------------------------------------------------------------- the nest: releasing

    [Fact]
    public void The_nest_pulses_for_24_frames_then_lets_two_gnats_out_and_goes_on_every_100()
    {
        const int Releases = 3, FramesForThreeReleases = 330;
        var (g, played) = Listening();
        Settled(g);
        var released = new List<int>();
        int seen = 0, firstPulse = -1;
        for (int f = 1; f <= FramesForThreeReleases; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            int now = Gnats(g).Count;
            if (now > seen) released.Add(f);
            if (now > seen) Assert.Equal(PerRelease, now - seen);
            seen = now;
            if (firstPulse < 0 && played.Contains(SfxNames.Hatch)) firstPulse = f;
        }
        Assert.Equal(Releases, released.Count);
        Assert.Equal(ReleasePeriod, released[1] - released[0]);
        Assert.Equal(ReleasePeriod, released[2] - released[1]);
        Assert.Equal(released[0] - PulseFrames, firstPulse);
        Assert.Contains(SfxNames.Sting, played);
    }

    [Fact]
    public void The_pulse_blinks_the_hatch_green_then_white_and_stops_at_the_release()
    {
        const int MaxFrames = 200;
        var g = Field();
        var n = Settled(g);
        uint Hatch() => EnemyKit.ClearPickupsAndDraw(g).Get((int)Math.Round(n.X - NestHalfW) + HatchColumn, (int)Math.Round(n.Y - NestHalfH) + HatchRow) & EnemyKit.RgbMask;
        uint first = 0;
        var seen = new HashSet<uint>();
        for (int f = 1; f <= MaxFrames; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            if (Gnats(g).Count > 0) break;
            if (f == 1) first = Hatch();
            seen.Add(Hatch());
        }
        Assert.Equal(NestBody, first);
        Assert.Contains(Bile, seen);
        Assert.Contains(Palette.Tell, seen);
        Assert.Equal(NestBody, Hatch());
    }

    [Fact]
    public void New_gnats_are_harmless_and_come_out_below_the_hatch()
    {
        const int MaxFrames = 400;
        const double SpreadX = 4, DropTop = 3, DropBottom = 9;
        var g = Field();
        var n = Settled(g);
        for (int f = 1; f <= MaxFrames && Gnats(g).Count == 0; f++) EnemyKit.StepSweeping(g);
        var gnats = Gnats(g);
        Assert.Equal(PerRelease, gnats.Count);
        Assert.All(gnats, x =>
        {
            Assert.False(x.Armed);
            Assert.InRange(x.X, n.X - SpreadX, n.X + SpreadX);
            Assert.InRange(x.Y, n.Y + DropTop, n.Y + DropBottom);
            Assert.Same(n, x.Parent);
        });
    }

    [Fact]
    public void A_nest_keeps_at_most_eight_gnats_alive_and_reaches_eight()
    {
        const int Frames = 1600;
        var g = Field();
        var n = Settled(g);
        int most = 0;
        for (int f = 1; f <= Frames; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            int c = Gnats(g).Count(x => ReferenceEquals(x.Parent, n));
            Assert.True(c <= GnatCap, $"frame {f}: {c} gnats");
            most = Math.Max(most, c);
            Assert.Contains(n, g.Enemies);
        }
        Assert.Equal(GnatCap, most);
    }

    /// <summary>Where a test parks gnats it lets out by hand: the first column, the gap between them, and a row low on the field.</summary>
    private const double ParkX = 10, ParkGap = 3, ParkY = 50;

    [Fact]
    public void A_nest_with_eight_alive_waits_for_room_and_lets_out_what_fits()
    {
        var g = Field();
        var n = Settled(g);
        for (int i = 0; i < GnatCap - 1; i++) { var x = g.SpawnAdd(EnemyKind.Gnat, ParkX + i * ParkGap, ParkY, n); x.Grace = NeverArmed; x.Age = NeverBurnsOutAge; }
        for (int f = 1; f <= ReleasePeriod; f++) { EnemyKit.StepSweeping(g); Soothe(g); }
        Assert.Equal(GnatCap, Gnats(g).Count);   // seven, and the one of the pair that fitted
    }

    [Fact]
    public void A_nest_bursts_into_three_gnats_when_it_dies()
    {
        const double SpreadX = 6, RiseTop = 5, DropBottom = 7;
        var g = Field();
        var n = Settled(g);
        int score = g.Score;
        EnemyKit.Shot(g, n.X, n.Y, KillingShot);
        EnemyKit.StepSweeping(g);
        Assert.DoesNotContain(n, g.Enemies);
        Assert.Equal(score + NestScore, g.Score);
        var burst = Gnats(g);
        Assert.Equal(BurstGnats, burst.Count);
        Assert.All(burst, x =>
        {
            Assert.False(x.Armed, "the burst is harmless at first too");
            Assert.InRange(x.X, n.X - SpreadX, n.X + SpreadX);
            Assert.InRange(x.Y, n.Y - RiseTop, n.Y + DropBottom);
        });
        Assert.Equal(BurstGnats, burst.Select(x => Math.Round(x.X, 3)).Distinct().Count());
    }

    [Fact]
    public void The_burst_ignores_the_cap_of_eight()
    {
        var g = Field();
        var n = Settled(g);
        for (int i = 0; i < GnatCap; i++) { var x = g.SpawnAdd(EnemyKind.Gnat, ParkX + i * ParkGap, ParkY, n); x.Grace = NeverArmed; }
        EnemyKit.Shot(g, n.X, n.Y, KillingShot);
        EnemyKit.StepSweeping(g);
        Assert.Equal(GnatCap + BurstGnats, Gnats(g).Count);
    }

    [Fact]
    public void A_nest_never_fires_and_never_leaves()
    {
        var g = Field();
        var n = Settled(g);
        for (int f = 1; f <= BotFrameCap; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            Assert.DoesNotContain(g.Bullets, x => !x.Friendly);
            Assert.False(n.Leaving);
            Assert.Contains(n, g.Enemies);
        }
    }

    // ---------------------------------------------------------------- the four fields

    [Theory]
    [InlineData(30, 16)]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void It_enters_holds_and_stays_inside_the_field_above_the_floor(int cols, int rows)
    {
        const int Frames = 600;
        var g = Field(cols, rows);
        var n = g.SpawnKind(EnemyKind.Nest, 0.5);
        Assert.True(n.Y < 0);
        double floor = EnemyKit.BossFloor(g.PH);
        for (int f = 1; f <= Frames; f++)
        {
            EnemyKit.StepSweeping(g);
            Soothe(g);
            Assert.Contains(n, g.Enemies);
            Assert.True(EnemyKit.InsideWidth(g, n), $"frame {f}: x {n.X}");
            Assert.True(EnemyKit.LowestPixel(n) <= floor, $"frame {f}: lowest {EnemyKit.LowestPixel(n)} under the floor {floor}");
            if (f >= SettledFrame) Assert.Equal(Hold(g), n.Y, 6);
            if (f >= SettledFrame) Assert.True(EnemyKit.Extent(n).T >= HudRows, "the top is clear of the HUD");
        }
    }

    [Theory]
    [InlineData(30, 16)]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void A_gnat_stays_on_the_field_it_chases_the_ship_across(int cols, int rows)
    {
        const double NearTopLeft = 3;
        const int Frames = 400;
        const double FieldMargin = 1;   // a pixel of slack past each edge: this asks that a gnat stays on the field, not how near an edge the engine holds it
        var g = Field(cols, rows);
        var gnat = g.SpawnAdd(EnemyKind.Gnat, NearTopLeft, NearTopLeft);
        gnat.Grace = NeverArmed;
        for (int f = 1; f <= Frames; f++)
        {
            EnemyKit.StepSweeping(g);
            Assert.InRange(gnat.X, -FieldMargin, g.PW + FieldMargin);
            Assert.InRange(gnat.Y, -FieldMargin, g.PH + FieldMargin);
        }
    }

    // ---------------------------------------------------------------- voice

    [Fact]
    public void The_nest_says_the_oldest_waiting_post_else_tell_your_friends()
    {
        var g = Field();
        g.OfferDark(new DarkPost(DarknessKind.Rude, 1, "first rude post"));
        g.OfferDark(new DarkPost(DarknessKind.Rude, 2, "second rude post"));
        var n = Settled(g);
        for (int i = 0; i < SpeakWithin && n.Text is null; i++) EnemyKit.StepSweeping(g);
        Assert.Equal("first rude post", n.Text);

        var h = Field();
        var m = Settled(h);
        for (int i = 0; i < SpeakWithin && m.Text is null; i++) EnemyKit.StepSweeping(h);
        Assert.Equal("Tell your friends.", m.Text);
        Assert.Equal("NEST", m.Header);
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_a_nest_and_its_gnats()
    {
        const int Frames = 700;
        string Run()
        {
            var g = Field();
            var n = Settled(g);
            for (int f = 0; f < Frames; f++)
            {
                NestBot(g, n);
                EnemyKit.StepSweeping(g);
            }
            return string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.00}")) + $"#{g.Score}:{g.Kills}:{g.PX:0.0}";
        }
        Assert.Equal(Run(), Run());
    }

    /// <summary>The bot steps aside from an armed gnat within this many pixels down and across, by a side step.</summary>
    private const double GnatReachY = 14, GnatReachX = 12, SideStep = 10;

    /// <summary>Fires without a break and lines up under the nest, stepping aside from an armed gnat that has got close.</summary>
    private static void NestBot(RaiderGame g, Enemy nest)
    {
        g.Input.Key(GameKey.Fire, true);
        double target = nest.X;
        var near = Gnats(g).Where(x => x.Armed && Math.Abs(x.Y - g.PY) < GnatReachY && Math.Abs(x.X - g.PX) < GnatReachX)
            .OrderBy(x => Math.Abs(x.X - g.PX)).FirstOrDefault();
        if (near is not null) target = g.PX + (near.X > g.PX ? -SideStep : SideStep);
        g.Input.Key(GameKey.Left, g.PX > target + EnemyKit.SteerDeadZone);
        g.Input.Key(GameKey.Right, g.PX < target - EnemyKit.SteerDeadZone);
    }

    // ---------------------------------------------------------------- the fight

    [Fact]
    public void A_bot_clears_a_nest_and_its_gnats_in_good_time_and_the_field_is_never_empty_meanwhile()
    {
        const ulong Seeds = 5;
        var times = new List<int>();
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            var g = Field(seed: seed);
            var n = Settled(g);
            int frames = 0;
            bool empty = false;
            for (; frames < BotFrameCap && g.Enemies.Contains(n); frames++)
            {
                NestBot(g, n);
                if (g.Enemies.Count == 0) empty = true;
                EnemyKit.StepSweeping(g);
                if (g.Hp <= 0) break;
            }
            Assert.False(empty, "a nest fight leaves no empty field");
            Assert.DoesNotContain(n, g.Enemies);
            times.Add(frames);
        }
        times.Sort();
        output.WriteLine($"nest frames to kill: {string.Join(" ", times)}");
        Assert.True(times[times.Count / 2] <= BotSecondsToKill * Tuning.Fps, "a nest falls to the bot in under twenty seconds");
    }
}
