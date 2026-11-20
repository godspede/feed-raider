using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Shutter (stage 3's boss): a slab with three eyes under it, one open at a time in a pattern the player can learn. A closed eye is
/// armour. These tests give its machine (the pattern for three eyes, two and one), its warning (a slit before an eye opens, an iris
/// that whitens before it shuts), its safety, its pixels, its fit on the four fields, its voice (its box types only while an eye is open)
/// and the fight (a bot at stage 3's weapon level).
/// </summary>
public sealed class ShutterTests(ITestOutputHelper output)
{
    private const uint Ivory = 0xf2e6d0;
    private static readonly uint Violet = Art.DarkPal['o'];
    private static readonly string[] EyeNames = ["eye-l", "eye-c", "eye-r"];
    private const int EyeCount = 3;

    /// <summary>The stage whose boss the shutter is.</summary>
    private const int ShutterStage = 3;

    /// <summary>Its score for the kill, and each eye's.</summary>
    private const int KillScore = 5000, EyeScore = 800;

    /// <summary>The drawing is this wide and tall; each eye's socket is this wide, its centre this far from the next one's.</summary>
    private const int BodyWidth = 41, BodyHeight = 12, EyeWidth = 9, EyePitch = 15;

    /// <summary>The left socket's first column in the drawing, and the slab's height, which the sockets sit under.</summary>
    private const int SocketLeft = 1, SlabHeight = 5;

    /// <summary>Pixels of an eye from its socket's top left: the slit's first column, width and row, the iris's centre, a pixel of the
    /// open eye's ivory ring, the row of the socket's outline, and a pixel of a closed lid's outline.</summary>
    private const int SlitX = 3, SlitWidth = 3, SlitRow = 2, IrisX = 4, IrisRow = 2, RingX = 2, RingRow = 1, OutlineRow = 3, LidX = 3;

    /// <summary>Pixels of the slab from the drawing's top left: a column of its outline and its body, the body's row, and a stud.</summary>
    private const int SlabProbeX = 10, SlabBodyRow = 3, StudX = 3, StudRow = 2;

    /// <summary>The row a shot aimed at the slab is fired at, above the shutter's centre.</summary>
    private const double SlabAboveCentre = 4;

    /// <summary>Beat and open lengths in frames with three, two and one eye left.</summary>
    private const int BeatThree = 80, BeatTwo = 65, BeatOne = 60, OpenThree = 50, OpenTwo = 45, OpenOne = 40;

    /// <summary>The order the eyes open in with all three alive: left, middle, right, middle.</summary>
    private static readonly int[] ThreeEyePattern = [0, 1, 2, 1];

    /// <summary>The slit shows for this many frames before an eye opens; the iris whitens through this many before it shuts.</summary>
    private const int SlitFrames = 20, IrisFrames = 24;

    /// <summary>Each volley is this many bolts at this speed, the two outer ones leaning out from the middle one by this much.</summary>
    private const int BoltsPerVolley = 3;
    private const double BoltSpeed = 1.2, LeanLeast = 0.15, LeanMost = 0.25;

    /// <summary>The hold row: a fixed one on fields under this many pixels tall, else the boss row rounded to a whole pixel.</summary>
    private const int ShortField = 48, ShortHold = 11;

    /// <summary>The pixels a frame it drifts across the field.</summary>
    private const double DriftSpeed = 0.25;

    /// <summary>Some eye opens within this many frames of the shutter arriving, and within this many of any moment.</summary>
    private const int FirstOpenWithin = 120, EyeOpenWithin = 400;

    /// <summary>A shot this strong kills an eye in one hit.</summary>
    private const double EyeOverkill = 100000;

    /// <summary>The shutter speaks its first words within this many frames.</summary>
    private const int FirstWordsWithin = 400;

    /// <summary>The frames past the end of the boss warning by which the gate has made the boss.</summary>
    private const int ArrivalSlack = 2;

    /// <summary>The seed of a game that runs through the real boss gate when the luck of the run is not what a test checks.</summary>
    private const ulong GateSeed = 3;

    /// <summary>The slowest a bolt may fall and still count as a threat to the bot.</summary>
    private const double MinThreatSpeed = 0.05;

    /// <summary>The bot counts bolts that reach the ship's row within this many frames, and ones already this many pixels below it.</summary>
    private const int LookaheadFrames = 90;
    private const double BoltRowSlack = 2;

    /// <summary>The bot checks the moment a bolt arrives and this many frames either side of it.</summary>
    private const double TimingSlack = 1.5;

    /// <summary>A bolt this close to the ship's centre when it arrives hits it.</summary>
    private const double ShipClearance = 4.5;

    /// <summary>How many columns either side of the wanted one the bot searches for a safe spot, and how near the edge it keeps.</summary>
    private const int SearchWidth = 60;
    private const double EdgeMargin = 3;

    /// <summary>Frames after which a fight is given up as stalled.</summary>
    private const int FightFrameCap = 3000;

    private static EnemyBehaviour Shutter => RaiderGame.TableBehaviour(EnemyKind.Shutter)!;

    private static Part Eye(Enemy e, int i) => e.Parts!.Single(p => p.Name == EyeNames[i]);

    private static int Hold(RaiderGame g) => g.PH < ShortField ? ShortHold : (int)Math.Round(EnemyKit.BossRow(g.PH));

    private static int OpenEye(Enemy e)
    {
        for (int i = 0; i < EyeCount; i++) if (Eye(e, i).State == PartState.Open) return i;
        return -1;
    }

    /// <summary>A game with a shutter on the field and nothing else, at stage 3.</summary>
    private static (RaiderGame G, Enemy S) Fresh(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.StartAtStage(ShutterStage);
        EnemyKit.Clear(g);
        return (g, g.SpawnKind(EnemyKind.Shutter, 0.5));
    }

    private static void StepKeeping(RaiderGame g, Enemy s)
    {
        g.Step();
        g.Enemies.RemoveAll(e => !ReferenceEquals(e, s));
    }

    /// <summary>The open eye (or -1) at the end of each of <paramref name="frames"/> frames.</summary>
    private static int[] Timeline(RaiderGame g, Enemy s, int frames)
    {
        var open = new int[frames];
        for (int f = 0; f < frames; f++)
        {
            StepKeeping(g, s);
            g.Bullets.RemoveAll(b => !b.Friendly);   // a ship that is hit freezes the game for a few frames: the beat is counted in frames, so none is hit
            open[f] = OpenEye(s);
        }
        return open;
    }

    /// <summary>The frames at which an eye goes from shut to open, with the eye.</summary>
    private static List<(int Frame, int Eye, int Length)> Opens(int[] open)
    {
        var list = new List<(int, int, int)>();
        for (int f = 0; f < open.Length; f++)
        {
            if (open[f] < 0 || f > 0 && open[f - 1] == open[f]) continue;
            int n = 0;
            while (f + n < open.Length && open[f + n] == open[f]) n++;
            list.Add((f, open[f], n));
        }
        return list;
    }

    /// <summary>Steps until an eye is open, then shoots that eye dead with one shot; returns which.</summary>
    private static int KillOpenEye(RaiderGame g, Enemy s)
    {
        int i = OpenEye(s);
        for (int f = 0; i < 0 && f < EyeOpenWithin; f++) { StepKeeping(g, s); i = OpenEye(s); }
        Assert.True(i >= 0, "an eye opens");
        var p = Eye(s, i);
        EnemyKit.Shot(g, s.X + p.OX, s.Y + p.OY, EyeOverkill);
        StepKeeping(g, s);
        Assert.Equal(PartState.Dead, p.State);
        return i;
    }

    /// <summary>Steps until an eye is open, giving up after <paramref name="within"/> frames.</summary>
    private static void StepUntilAnEyeOpens(RaiderGame g, Enemy s, int within = FirstOpenWithin)
    {
        for (int f = 0; f < within && OpenEye(s) < 0; f++) StepKeeping(g, s);
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_is_a_boss_and_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Shutter, Shutter.Kind);
        Assert.Equal("SHUTTER", Shutter.Name);
        Assert.Equal(KillScore, Shutter.Score);
        Assert.True(Shutter.Boss);
        Assert.True(Shutter.Speaks);
        Assert.True(Shutter.Name.Length <= EnemySpeechBox.MaxLabel);
        Assert.Equal(EnemyKind.Shutter, Ramp.LapBoss(Ramp.GateFrame(ShutterStage)).Kind);
    }

    [Fact]
    public void It_is_a_slab_of_armour_over_three_eyes_that_share_its_hit_points()
    {
        var (g, s) = Fresh();
        Assert.Equal(["slab", "eye-l", "eye-c", "eye-r"], s.Parts!.Select(p => p.Name));
        var slab = s.Parts!.Single(p => p.Name == "slab");
        Assert.Equal(PartState.Armoured, slab.State);
        Assert.False(slab.Vital);
        Assert.Equal(BodyWidth, s.Sprite.W);
        Assert.Equal(BodyHeight, s.Sprite.H);
        for (int i = 0; i < EyeCount; i++)
        {
            var eye = Eye(s, i);
            Assert.Equal(PartState.Armoured, eye.State);
            Assert.True(eye.Vital);
            Assert.Equal(EyeScore, eye.Score);
            Assert.Equal(s.Hp / EyeCount, eye.Hp, 9);
            Assert.NotNull(eye.OpenArt);
            Assert.NotNull(eye.Wreck);
            Assert.Equal(EyeWidth, eye.Closed.W);
        }
        Assert.Equal([-EyePitch, 0, EyePitch], Enumerable.Range(0, EyeCount).Select(i => (int)Eye(s, i).OX));
        Assert.Equal(s.MaxHp, s.Hp);
    }

    [Fact]
    public void Its_hit_points_are_not_scaled_by_the_stage_when_the_gate_makes_it()
    {
        var g = new RaiderGame(GateSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(ShutterStage);
        Assert.Equal("SHUTTER", g.BossName);
        for (int i = 0; i < Tuning.BossWarnFrames + ArrivalSlack && g.Boss is null; i++) g.Step();
        var boss = g.Boss!;
        Assert.Equal(EnemyKind.Shutter, boss.Kind);
        Assert.Equal(Shutter.Hp, boss.MaxHp, 6);
        Assert.Equal(Shutter.Hp / EyeCount, Eye(boss, 0).Hp, 6);
    }

    // ---------------------------------------------------------------- the machine

    [Fact]
    public void With_three_eyes_it_opens_left_middle_right_middle_on_an_eighty_frame_beat_for_fifty_frames()
    {
        const int WatchFrames = 900;
        int[] order = [0, 1, 2, 1, 0, 1, 2, 1, 0];
        var (g, s) = Fresh();
        var opens = Opens(Timeline(g, s, WatchFrames));
        Assert.True(opens.Count >= order.Length, $"{opens.Count} openings in {WatchFrames} frames");
        Assert.Equal(order, opens.Take(order.Length).Select(o => o.Eye));
        for (int i = 1; i < order.Length; i++) Assert.Equal(BeatThree, opens[i].Frame - opens[i - 1].Frame);
        foreach (var o in opens.Take(order.Length)) Assert.Equal(OpenThree, o.Length);
    }

    [Fact]
    public void Never_two_eyes_are_open_at_once()
    {
        const int WatchFrames = 700;
        var (g, s) = Fresh();
        for (int f = 0; f < WatchFrames; f++)
        {
            StepKeeping(g, s);
            Assert.True(s.Parts!.Count(p => p.State == PartState.Open) <= 1, $"frame {f}");
        }
    }

    [Fact]
    public void With_two_eyes_they_alternate_on_a_sixty_five_frame_beat()
    {
        const int WatchFrames = 700, OpeningsChecked = 8;
        var (g, s) = Fresh();
        int dead = KillOpenEye(g, s);
        var opens = Opens(Timeline(g, s, WatchFrames));
        var alive = Enumerable.Range(0, EyeCount).Where(i => i != dead).ToArray();
        Assert.True(opens.Count >= OpeningsChecked);
        for (int i = 0; i < OpeningsChecked; i++) Assert.Equal(alive[(i + (opens[0].Eye == alive[0] ? 0 : 1)) % alive.Length], opens[i].Eye);
        for (int i = 1; i < OpeningsChecked; i++) Assert.Equal(BeatTwo, opens[i].Frame - opens[i - 1].Frame);
        foreach (var o in opens.Take(OpeningsChecked)) Assert.Equal(OpenTwo, o.Length);
    }

    [Fact]
    public void With_one_eye_it_is_open_forty_frames_of_every_sixty()
    {
        const int WatchFrames = 600, OpeningsChecked = 8;
        var (g, s) = Fresh();
        KillOpenEye(g, s);
        KillOpenEye(g, s);
        var left = Enumerable.Range(0, EyeCount).Single(i => Eye(s, i).State != PartState.Dead);
        var opens = Opens(Timeline(g, s, WatchFrames));
        Assert.True(opens.Count >= OpeningsChecked);
        Assert.All(opens.Take(OpeningsChecked), o => Assert.Equal(left, o.Eye));
        for (int i = 1; i < OpeningsChecked; i++) Assert.Equal(BeatOne, opens[i].Frame - opens[i - 1].Frame);
        foreach (var o in opens.Take(OpeningsChecked)) Assert.Equal(OpenOne, o.Length);
    }

    [Fact]
    public void After_an_eye_is_lost_the_next_opens_inside_twenty_five_frames_so_the_field_is_never_idle()
    {
        const int NextOpensWithin = 25, WaitCap = 200;
        var (g, s) = Fresh();
        KillOpenEye(g, s);
        int wait = 0;
        while (OpenEye(s) < 0 && wait < WaitCap) { StepKeeping(g, s); wait++; }
        Assert.InRange(wait, 1, NextOpensWithin);
    }

    [Fact]
    public void A_dead_eye_stays_shut_and_never_opens_again()
    {
        const int WatchFrames = 600;
        var (g, s) = Fresh();
        int dead = KillOpenEye(g, s);
        var open = Timeline(g, s, WatchFrames);
        Assert.DoesNotContain(dead, open);
        Assert.Equal(PartState.Dead, Eye(s, dead).State);
    }

    // ---------------------------------------------------------------- armour

    [Fact]
    public void A_closed_eye_and_the_slab_take_nothing_and_ping()
    {
        const double ShotDamage = 10;
        var (g, s) = Fresh();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        StepUntilAnEyeOpens(g, s);
        int open = OpenEye(s);
        Assert.True(open >= 0);
        int shut = (open + 1) % EyeCount;
        double hp = s.Hp;
        var p = Eye(s, shut);
        EnemyKit.Shot(g, s.X + p.OX, s.Y + p.OY, ShotDamage);
        EnemyKit.Shot(g, s.X, s.Y - SlabAboveCentre, ShotDamage);
        StepKeeping(g, s);
        Assert.Equal(hp, s.Hp);
        Assert.Equal(Eye(s, shut).MaxHp, Eye(s, shut).Hp);
        Assert.Contains(SfxNames.Ping, played);
    }

    [Fact]
    public void The_open_eye_takes_a_shot_and_the_owner_follows_it()
    {
        const double ShotDamage = 3;
        var (g, s) = Fresh();
        StepUntilAnEyeOpens(g, s);
        int open = OpenEye(s);
        var p = Eye(s, open);
        double each = p.Hp;
        EnemyKit.Shot(g, s.X + p.OX, s.Y + p.OY, ShotDamage);
        StepKeeping(g, s);
        Assert.Equal(each - ShotDamage, p.Hp, 6);
        Assert.Equal(s.MaxHp - ShotDamage, s.Hp, 6);
    }

    [Fact]
    public void A_bomb_hurts_the_open_eye_only()
    {
        var (g, s) = Fresh();
        StepUntilAnEyeOpens(g, s);
        int open = OpenEye(s);
        double before = Eye(s, open).Hp;
        g.Input.BombPressed = true;
        StepKeeping(g, s);
        for (int i = 0; i < EyeCount; i++)
            if (i != open) Assert.Equal(Eye(s, i).MaxHp, Eye(s, i).Hp);
        Assert.True(Eye(s, open).Hp < before || Eye(s, open).State == PartState.Dead);
    }

    [Fact]
    public void Killing_the_last_eye_scores_eight_hundred_and_five_thousand()
    {
        var (g, s) = Fresh();
        int before = g.Score;
        KillOpenEye(g, s);
        Assert.Equal(before + EyeScore, g.Score);
        KillOpenEye(g, s);
        int mid = g.Score;
        Assert.Equal(before + 2 * EyeScore, mid);
        KillOpenEye(g, s);
        Assert.DoesNotContain(s, g.Enemies);
        Assert.Equal(mid + EyeScore + KillScore, g.Score);
        Assert.Equal(1, g.Kills);
    }

    // ---------------------------------------------------------------- warning and volleys

    /// <summary>The hostile bolts made this frame (those that did not exist before it), taken off the field after, so none can reach the ship.</summary>
    private static List<Bullet> TakeHostile(RaiderGame g)
    {
        var made = g.Bullets.Where(b => !b.Friendly).ToList();
        g.Bullets.RemoveAll(b => !b.Friendly);
        return made;
    }

    [Fact]
    public void An_eye_that_shuts_fires_three_bolts_from_its_underside_at_speed_one_point_two_and_nothing_else_fires()
    {
        const int WatchFrames = 800, LeastShuttings = 8;
        const double MuzzleHalfWidth = 1.5, MuzzleAbove = 0.01, MuzzleBelow = 1.3;
        var (g, s) = Fresh();
        int shuts = 0, prev = -1;
        for (int f = 0; f < WatchFrames; f++)
        {
            StepKeeping(g, s);
            var made = TakeHostile(g);
            bool shut = prev >= 0 && Eye(s, prev).State != PartState.Open;
            Assert.False(Shutter.Touches(g, s));
            if (!shut) { Assert.Empty(made); }
            else
            {
                shuts++;
                var p = Eye(s, prev);
                Assert.Equal(BoltsPerVolley, made.Count);
                foreach (var b in made)
                {
                    Assert.Equal(BoltSpeed, Math.Sqrt(b.VX * b.VX + b.VY * b.VY), 6);
                    Assert.InRange(b.X, s.X + p.OX - MuzzleHalfWidth, s.X + p.OX + MuzzleHalfWidth);
                    Assert.InRange(b.Y, s.Y + p.OY + p.HH - MuzzleAbove, s.Y + p.OY + p.HH + MuzzleBelow);
                }
            }
            prev = OpenEye(s);
        }
        Assert.True(shuts >= LeastShuttings, $"{shuts} shuttings in {WatchFrames} frames");
    }

    [Fact]
    public void The_middle_bolt_of_a_volley_is_aimed_at_the_ship_and_the_two_beside_it_lean_out_by_a_fifth_of_a_radian()
    {
        const int WatchFrames = 900;
        const double AimTolerance = 0.08;
        var (g, s) = Fresh();
        g.Input.Key(GameKey.Right, true);
        List<Bullet> made = [];
        for (int f = 0; f < WatchFrames && made.Count == 0; f++)
        {
            StepKeeping(g, s);
            made = TakeHostile(g);
        }
        Assert.Equal(BoltsPerVolley, made.Count);
        var angles = made.Select(b => Math.Atan2(b.VY, b.VX)).Order().ToList();
        var mid = made.OrderBy(b => Math.Atan2(b.VY, b.VX)).ElementAt(1);
        double toShip = Math.Atan2(g.PY - mid.Y, g.PX - mid.X);
        Assert.True(Math.Abs(angles[1] - toShip) < AimTolerance, $"the middle bolt is {angles[1]:0.000} and the ship lies at {toShip:0.000}");
        Assert.InRange(angles[1] - angles[0], LeanLeast, LeanMost);
        Assert.InRange(angles[2] - angles[1], LeanLeast, LeanMost);
    }

    /// <summary>The eye whose closed lid shows the slit at this frame (three ivory pixels across the middle of the lid), or -1.</summary>
    private static int SlitEye(RaiderGame g, Enemy s)
    {
        var f = EnemyKit.ClearPickupsAndDraw(g);
        for (int i = 0; i < EyeCount; i++)
        {
            var p = Eye(s, i);
            if (p.State != PartState.Armoured) continue;
            int x0 = (int)Math.Round(s.X + p.OX - p.Closed.W / 2.0), y0 = (int)Math.Round(s.Y + p.OY - p.Closed.H / 2.0);
            if (Enumerable.Range(SlitX, SlitWidth).All(dx => (f.Get(x0 + dx, y0 + SlitRow) & EnemyKit.RgbMask) == Ivory)) return i;
        }
        return -1;
    }

    [Fact]
    public void A_slit_shows_in_the_lid_of_the_eye_that_opens_next_for_exactly_the_twenty_frames_before_it_opens()
    {
        const int WatchFrames = 420, OpeningsChecked = 3;
        var (g, s) = Fresh();
        var open = new List<int>();
        var slit = new List<int>();
        for (int f = 0; f < WatchFrames; f++)
        {
            StepKeeping(g, s);
            open.Add(OpenEye(s));
            slit.Add(SlitEye(g, s));
        }
        var opens = Opens(open.ToArray());
        Assert.True(opens.Count >= OpeningsChecked + 1);
        foreach (var o in opens.Skip(1).Take(OpeningsChecked))
        {
            for (int k = 1; k <= SlitFrames; k++) Assert.Equal(o.Eye, slit[o.Frame - k]);
            Assert.Equal(-1, slit[o.Frame - SlitFrames - 1]);
            Assert.Equal(-1, slit[o.Frame]);   // an open eye has no lid to show the slit in
        }
    }

    [Fact]
    public void The_iris_stays_crimson_then_whitens_a_little_more_each_frame_of_the_last_twenty_four_before_the_eye_shuts()
    {
        const int WatchFrames = 400, NearlyWhiteGain = 150;
        var (g, s) = Fresh();
        var iris = new List<uint>();
        for (int f = 0; f < WatchFrames && iris.Count < OpenThree; f++)
        {
            StepKeeping(g, s);
            int o = OpenEye(s);
            if (o < 0) { iris.Clear(); continue; }
            var p = Eye(s, o);
            var fld = EnemyKit.ClearPickupsAndDraw(g);
            int x0 = (int)Math.Round(s.X + p.OX - p.OpenArt!.W / 2.0), y0 = (int)Math.Round(s.Y + p.OY - p.OpenArt.H / 2.0);
            iris.Add(fld.Get(x0 + IrisX, y0 + IrisRow) & EnemyKit.RgbMask);
        }
        Assert.Equal(OpenThree, iris.Count);
        for (int i = 0; i < OpenThree - IrisFrames; i++) Assert.Equal(Palette.Crimson, iris[i]);
        for (int i = OpenThree - IrisFrames; i < OpenThree; i++) Assert.True(Lum(iris[i]) > Lum(iris[i - 1]), $"frame {i + 1} of the opening is whiter than the one before");
        Assert.True(Lum(iris[OpenThree - 1]) > Lum(Palette.Crimson) + NearlyWhiteGain, "the last frame before it shuts is nearly white");
    }

    private static int Lum(uint c) => (int)(c >> 16 & 0xff) + (int)(c >> 8 & 0xff) + (int)(c & 0xff);

    // ---------------------------------------------------------------- a ship that never moves

    [Fact]
    public void A_ship_that_never_moves_dies()
    {
        const int DeathWithin = 900;
        var (g, s) = Fresh();
        for (int f = 0; f < DeathWithin && g.Deaths == 0; f++) StepKeeping(g, s);
        Assert.True(g.Deaths > 0);
    }


    // ---------------------------------------------------------------- pixels

    [Fact]
    public void It_is_drawn_with_an_ivory_ringed_crimson_iris_when_open_and_a_plum_outline_over_a_plum_slab()
    {
        const int OpenWithin = 200;
        var (g, s) = Fresh();
        StepUntilAnEyeOpens(g, s, OpenWithin);
        int open = OpenEye(s);
        Assert.True(open >= 0);
        var fld = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(s.X - s.Sprite.W / 2.0), y0 = (int)Math.Round(s.Y - s.Sprite.H / 2.0);
        Assert.Equal(Palette.Plum, fld.Get(x0 + SlabProbeX, y0) & EnemyKit.RgbMask);
        Assert.Equal(Violet, fld.Get(x0 + SlabProbeX, y0 + SlabBodyRow) & EnemyKit.RgbMask);
        Assert.Equal(Art.DarkViolet, fld.Get(x0 + StudX, y0 + StudRow) & EnemyKit.RgbMask);
        int ex = x0 + SocketLeft + EyePitch * open, ey = y0 + SlabHeight;
        Assert.Equal(Ivory, fld.Get(ex + RingX, ey + RingRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Crimson, fld.Get(ex + IrisX, ey + IrisRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Plum, fld.Get(ex, ey + OutlineRow) & EnemyKit.RgbMask);
        int shut = (open + 1) % EyeCount;
        int sx = x0 + SocketLeft + EyePitch * shut;
        Assert.Equal(Palette.Plum, fld.Get(sx + LidX, ey + RingRow) & EnemyKit.RgbMask);
        Assert.NotEqual(Ivory, fld.Get(sx + RingX, ey + RingRow) & EnemyKit.RgbMask);
        Assert.Equal(Violet, fld.Get(sx + IrisX, ey + IrisRow) & EnemyKit.RgbMask);
    }

    [Fact]
    public void A_dead_eye_is_a_grey_wreck_with_the_plum_outline_kept()
    {
        const int FlashFadeFrames = 15;
        var (g, s) = Fresh();
        int dead = KillOpenEye(g, s);
        var p = Eye(s, dead);
        for (int f = 0; f < FlashFadeFrames; f++) StepKeeping(g, s);   // the hit flash has faded
        var fld = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(s.X - s.Sprite.W / 2.0), y0 = (int)Math.Round(s.Y - s.Sprite.H / 2.0);
        int ex = x0 + SocketLeft + EyePitch * dead, ey = y0 + SlabHeight;
        Assert.Equal(Palette.Plum, fld.Get(ex, ey + OutlineRow) & EnemyKit.RgbMask);
        uint inside = fld.Get(ex + IrisX, ey + IrisRow) & EnemyKit.RgbMask;
        Assert.Equal(p.Wreck!.Px[IrisRow * p.Wreck.W + IrisX] & EnemyKit.RgbMask, inside);
        Assert.NotEqual(Palette.Crimson, inside);
        Assert.NotEqual(Ivory, inside);
    }

    [Fact]
    public void The_parts_are_drawn_exactly_over_the_body_for_every_whole_pixel_position()
    {
        const int FirstX = 50, FirstY = 18, Positions = 4;
        var (g, s) = Fresh();
        for (int x = FirstX; x < FirstX + Positions; x++)
            for (int y = FirstY; y < FirstY + Positions; y++)
            {
                s.X = x + 0.5; s.BaseX = s.X; s.Y = y;
                foreach (var p in s.Parts!) { p.State = PartState.Armoured; p.Timer = 0; }
                var snapshot = (uint[])EnemyKit.ClearPickupsAndDraw(g).P.Clone();
                var saved = s.Parts!.ToList();
                s.Parts!.Clear();
                var bodyOnly = EnemyKit.ClearPickupsAndDraw(g);
                Assert.True(snapshot.SequenceEqual(bodyOnly.P), $"at ({s.X}, {s.Y}) the parts disagree with the body");
                s.Parts.AddRange(saved);
            }
    }

    // ---------------------------------------------------------------- sizes

    [Theory]
    [InlineData(SpotlightTests.SmallestCols, SpotlightTests.SmallestRows)]
    [InlineData(SpotlightTests.NarrowCols, SpotlightTests.NarrowRows)]
    [InlineData(SpotlightTests.WideCols, SpotlightTests.WideRows)]
    [InlineData(EnemyKit.BigCols, EnemyKit.BigRows)]
    public void It_enters_holds_drifts_and_stays_inside_the_field_above_the_floor(int cols, int rows)
    {
        const int WatchFrames = 900, SettleFrames = 100, TopRowUnderTheBar = 5;
        const double LeastDrift = 8, PathSlack = 6;
        var (g, s) = Fresh(cols, rows);
        Assert.True(s.Y < 0, "it enters from above");
        double floor = EnemyKit.BossFloor(g.PH), minX = double.MaxValue, maxX = double.MinValue, path = 0, last = double.NaN;
        for (int f = 1; f <= WatchFrames; f++)
        {
            StepKeeping(g, s);
            g.Bullets.RemoveAll(b => !b.Friendly);   // a ship that is hit freezes the game for a few frames
            Assert.Contains(s, g.Enemies);
            Assert.True(EnemyKit.InsideWidth(g, s), $"frame {f}: x {s.X} outside {g.PW}");
            Assert.True(EnemyKit.LowestPixel(s) <= floor, $"frame {f}: lowest pixel {EnemyKit.LowestPixel(s)} under the floor {floor}");
            if (f >= SettleFrames)
            {
                Assert.Equal(Hold(g), s.Y, 6);
                Assert.True(EnemyKit.Extent(s).T >= TopRowUnderTheBar - 1e-9, $"its top is on row {TopRowUnderTheBar} or lower, under the bar");
                minX = Math.Min(minX, s.X); maxX = Math.Max(maxX, s.X);
                if (!double.IsNaN(last)) path += Math.Abs(s.X - last);
                last = s.X;
            }
        }
        Assert.True(maxX - minX >= LeastDrift, $"it drifts: {minX:0.0} to {maxX:0.0}");
        double expected = DriftSpeed * (WatchFrames - SettleFrames + 1);
        Assert.InRange(path, expected - PathSlack, expected + PathSlack);   // the slack allows for the turns at the ends
        Assert.False(s.Leaving);
    }

    [Fact]
    public void It_leaves_when_the_fight_reaches_five_minutes()
    {
        const int StopShortOfStay = 5, CheckEvery = 600, PastTheStay = 10;
        var g = new RaiderGame(GateSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(ShutterStage);
        for (int i = 0; i < Tuning.BossWarnFrames + ArrivalSlack && g.Boss is null; i++) g.Step();
        var boss = g.Boss!;
        for (int f = 0; f < Tuning.BossStayFrames - StopShortOfStay; f++)
        {
            g.Step();
            g.Enemies.RemoveAll(e => !ReferenceEquals(e, boss));
            g.Bullets.RemoveAll(b => !b.Friendly);
            if (f % CheckEvery == 0 && g.Deaths > 0) Assert.Contains(boss, g.Enemies);
        }
        Assert.Contains(boss, g.Enemies);
        Assert.False(boss.Leaving);
        for (int f = 0; f < PastTheStay; f++) g.Step();
        Assert.True(boss.Leaving || !g.Enemies.Contains(boss));
    }

    // ---------------------------------------------------------------- voice

    private static RudePost Post(ulong seed, string text) => new(seed, text, DarkSource.Label, null, 0);

    [Fact]
    public void It_takes_the_shortest_waiting_post_first_and_the_oldest_of_equals()
    {
        var waiting = new List<RudePost> { Post(1, "a long and rambling post about nothing"), Post(2, "short"), Post(3, "medium sized one"), Post(4, "other") };
        Assert.Equal(1, Shutter.Taste(EnemyKit.Speaker(EnemyKind.Shutter), waiting));
        Assert.Equal(0, Shutter.Taste(EnemyKit.Speaker(EnemyKind.Shutter), [Post(1, "same"), Post(2, "same")]));
        Assert.Equal(0, Shutter.Taste(EnemyKit.Speaker(EnemyKind.Shutter), [Post(1, "only")]));
    }

    [Fact]
    public void Posts_are_taken_shortest_first_from_a_store_of_four()
    {
        var (g, s) = Fresh();
        g.OfferDark(new DarkPost(DarknessKind.Rude, 11, "this is a really quite long post indeed"));
        g.OfferDark(new DarkPost(DarknessKind.Rude, 12, "mid length post"));
        g.OfferDark(new DarkPost(DarknessKind.Rude, 13, "no"));
        g.OfferDark(new DarkPost(DarknessKind.Rude, 14, "a bit longer than that"));
        for (int f = 0; f < FirstWordsWithin && s.Text is null; f++) StepKeeping(g, s);
        Assert.Equal("no", s.Text);
    }

    [Fact]
    public void With_no_post_waiting_it_says_its_own_line_under_its_name()
    {
        var (g, s) = Fresh();
        for (int f = 0; f < FirstWordsWithin && s.Text is null; f++) StepKeeping(g, s);
        Assert.Equal("I see every post.", s.Text);
        Assert.Equal("SHUTTER", s.Header);
        Assert.Equal("I was not looking anyway.", Shutter.Line(EnemyKit.Speaker(EnemyKind.Shutter), VoiceCue.PartLost));
        Assert.Equal("Do not make me blink.", Shutter.Line(EnemyKit.Speaker(EnemyKind.Shutter), VoiceCue.Phase));
        Assert.Equal("Lights out.", Shutter.Line(EnemyKit.Speaker(EnemyKind.Shutter), VoiceCue.Dying));
    }

    [Fact]
    public void Its_box_holds_while_every_eye_is_shut_and_types_while_one_is_open()
    {
        const int LongPostWords = 40, WatchFrames = 700, LeastTypingFrames = 50, LeastHeldFrames = 20;
        var (g, s) = Fresh();
        g.OfferDark(new DarkPost(DarknessKind.Rude, 21, string.Join(' ', Enumerable.Repeat("word", LongPostWords))));
        int movedOpen = 0, movedShut = 0, heldShut = 0;
        bool wasOpen = false;
        int prevAge = 0;
        for (int f = 0; f < WatchFrames; f++)
        {
            StepKeeping(g, s);
            bool open = OpenEye(s) >= 0;
            Assert.Equal(!open, Shutter.HoldBox(g, s));
            if (s.Text is not null)
            {
                // the box reads the state the frame before: an open eye lets it type, a shut one holds it
                if (s.SpeechBoxAge != prevAge) { if (wasOpen) movedOpen++; else movedShut++; }
                else if (!wasOpen && s.SpeechBoxAge > 0) heldShut++;
            }
            prevAge = s.SpeechBoxAge;
            wasOpen = open;
        }
        Assert.True(movedOpen > LeastTypingFrames, $"it types while an eye is open ({movedOpen})");
        Assert.Equal(0, movedShut);
        Assert.True(heldShut > LeastHeldFrames, $"it holds while every eye is shut ({heldShut})");
    }

    [Fact]
    public void A_lost_eye_cuts_to_a_new_post_and_the_one_eye_phase_says_its_own_words()
    {
        const int JustSaid = 2;
        var (g, s) = Fresh();
        for (int f = 0; f < FirstWordsWithin && s.Text is null; f++) StepKeeping(g, s);
        Assert.Equal("I see every post.", s.Text);
        KillOpenEye(g, s);
        Assert.Equal("I was not looking anyway.", s.Text);
        Assert.Equal("SHUTTER", s.Header);
        Assert.True(s.SpeechBoxAge <= JustSaid, "the new box begins at once");
        KillOpenEye(g, s);
        Assert.Equal("Do not make me blink.", s.Text);
    }

    [Fact]
    public void A_post_waiting_when_an_eye_is_lost_is_said_in_place_of_its_own_line()
    {
        var (g, s) = Fresh();
        for (int f = 0; f < FirstWordsWithin && s.Text is null; f++) StepKeeping(g, s);
        g.OfferDark(new DarkPost(DarknessKind.Rude, 31, "you again"));
        KillOpenEye(g, s);
        Assert.Equal("you again", s.Text);
    }

    [Fact]
    public void The_turns_follow_the_cue()
    {
        var (g, s) = Fresh();
        Assert.Equal(Turn.Cut, Shutter.TurnFor(g, s, VoiceCue.Phase));
        Assert.Equal(Turn.Cut, Shutter.TurnFor(g, s, VoiceCue.PartLost));
        Assert.Equal(Turn.Cut, Shutter.TurnFor(g, s, VoiceCue.Dying));
        Assert.Equal(Turn.Cut, Shutter.TurnFor(g, s, VoiceCue.Leaving));
        Assert.Equal(Turn.Wait, Shutter.TurnFor(g, s, VoiceCue.ShipDied));
        Assert.Equal(Turn.Wait, Shutter.TurnFor(g, s, VoiceCue.Arrive));
        Assert.Equal(Turn.Wait, Shutter.TurnFor(g, s, VoiceCue.Idle));
        KillOpenEye(g, s);
        KillOpenEye(g, s);
        Assert.Equal(Turn.Skip, Shutter.TurnFor(g, s, VoiceCue.PartLost));   // the one-eye phase speaks instead
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_a_shutter_in_it()
    {
        const int RunFrames = 900;
        const ulong Seed = 11;
        string Run(ulong seed)
        {
            var g = new RaiderGame(seed) { SignedIn = true };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtBoss(ShutterStage);
            for (int f = 0; f < RunFrames; f++)
            {
                EnemyKit.BotFrame(g);
                g.Step();
            }
            return string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}:{g.Bullets.Count}";
        }
        Assert.Equal(Run(Seed), Run(Seed));
        Assert.Contains("Shutter", Run(Seed));
    }

    // ---------------------------------------------------------------- the fight

    /// <summary>The column nearest <paramref name="want"/> that the bolts in flight miss, with the ship walking there at its own speed.</summary>
    private static double? Dodge(RaiderGame g, double want)
    {
        var flying = g.Bullets.Where(b => !b.Friendly && b.VY > MinThreatSpeed && b.Y <= g.PY + BoltRowSlack && (g.PY - b.Y) / b.VY <= LookaheadFrames).ToList();
        if (flying.Count == 0) return want;
        // a column is safe if the ship, walking there at its own speed, is clear of every bolt when that bolt reaches the ship's row
        bool Safe(double c) => flying.All(b =>
        {
            double t = Math.Max(0, (g.PY - b.Y) / b.VY);
            for (double dt = -TimingSlack; dt <= TimingSlack; dt += TimingSlack)
            {
                double at = Math.Max(0, t + dt), ship = g.PX + Math.Clamp(c - g.PX, -Tuning.ShipSpeed * at, Tuning.ShipSpeed * at);
                if (Math.Abs(ship - (b.X + b.VX * at)) <= ShipClearance) return false;
            }
            return true;
        });
        for (int d = 0; d <= SearchWidth; d++)
            foreach (int sign in new[] { 1, -1 })
            {
                double c = Math.Clamp(want + sign * d, EdgeMargin, g.PW - EdgeMargin);
                if (Safe(c)) return c;
            }
        return null;
    }

    /// <summary>Where a player who has learnt the pattern stands: under the open eye, and once it shuts under the one the lid is slit on.</summary>
    private static double TrackTarget(Enemy boss, ref int opens, ref int last)
    {
        int now = OpenEye(boss), target = now;
        if (now >= 0 && now != last) { opens++; last = now; }
        if (target < 0)
        {
            var alive = Enumerable.Range(0, EyeCount).Where(i => Eye(boss, i).State != PartState.Dead).ToArray();
            if (alive.Length == EyeCount) target = ThreeEyePattern[opens % ThreeEyePattern.Length];
            else for (int k = 1; k <= EyeCount && target < 0; k++) if (alive.Contains((last + k + EyeCount) % EyeCount)) target = (last + k + EyeCount) % EyeCount;
        }
        return boss.X + Eye(boss, target).OX;
    }

    /// <summary>How one fight ended: the frames to the kill (or -1), the ship's deaths, and a line on the state the fight ended in.</summary>
    private readonly record struct FightResult(int Frames, int Deaths, string Note);

    private static FightResult Fight(ulong seed, int level)
    {
        const int BossArrivesWithin = 400;
        var g = new RaiderGame(seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(ShutterStage);
        EnemyKit.Clear(g);
        while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); EnemyKit.Clear(g); }
        for (int i = 0; i < BossArrivesWithin && g.Boss is null; i++) { g.Step(); g.Enemies.RemoveAll(e => e.Kind != EnemyKind.Shutter); }
        var boss = g.Boss!;
        int opens = 0, last = -1, f = 0;
        for (; f < FightFrameCap; f++)
        {
            EnemyKit.BotFrame(g, _ => Dodge(g, TrackTarget(boss, ref opens, ref last)));
            g.Step();
            g.Enemies.RemoveAll(e => !ReferenceEquals(e, boss));
            if (!g.Enemies.Contains(boss))
                return new(g.Kills > 0 ? f + 1 : -1, g.Deaths, $"gone at {f}: kills {g.Kills}, boss hp {boss.Hp:0.0}, leaving {boss.Leaving}, fight {g.FightFrames}");
        }
        return new(-1, g.Deaths, $"seed {seed}: boss hp {boss.Hp:0.0}/{boss.MaxHp:0.0}, deaths {g.Deaths}, ship hp {g.Hp}, weapon {g.WeaponLevel}, alive {g.Alive}, eyes {string.Join(",", boss.Parts!.Select(p => p.State + ":" + p.Hp.ToString("0")))}");
    }

    [Fact]
    public void A_bot_that_keeps_under_the_open_eye_and_dodges_beats_it_at_level_two_in_the_band()
    {
        const int Seeds = 6, WeaponLevel = 2;
        const double MedianFastestSeconds = 15.0, MedianSlowestSeconds = 27.0;
        var times = new List<int>();
        var deaths = new List<int>();
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            var fight = Fight(seed, WeaponLevel);
            Assert.True(fight.Frames > 0, $"seed {seed}: not dead inside {FightFrameCap} frames: {fight.Note}");
            times.Add(fight.Frames); deaths.Add(fight.Deaths);
        }
        times.Sort();
        double median = (times[Seeds / 2 - 1] + times[Seeds / 2]) / 2.0;
        double medianSeconds = median / Tuning.Fps;
        output.WriteLine($"shutter fight at level {WeaponLevel}, frames to kill: {string.Join(" ", times)}; median {median:0} ({medianSeconds:0.0} s); deaths {string.Join(" ", deaths)}");
        Assert.InRange(medianSeconds, MedianFastestSeconds, MedianSlowestSeconds);
    }
}
