using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The Warden: a mini-boss that shoots nothing and makes the three enemies nearest it proof against shots while it lives. These
/// tests give its machine (enter, hold, sway, leave), its safety, its pixels, its fit on the four fields, its voice, the shield rule
/// (exactly three, never a gunship or a boss, gone the frame it dies) and the fight (a bot at stage 2's weapon level).
/// </summary>
public sealed class WardenTests(ITestOutputHelper output)
{
    private static EnemyBehaviour Warden => RaiderGame.TableBehaviour(EnemyKind.Warden)!;
    /// <summary>The post a Warden is offered and then says.</summary>
    private const string WaitingPost = "grr you again";

    /// <summary>Decimal places two hit points or positions must agree to.</summary>
    private const int Places = 6;

    /// <summary>The hold row is this share of the field's height, but never higher than this row.</summary>
    private const double HoldShare = 0.14, HoldMin = 10;

    /// <summary>It settles by frame 120 and leaves after this many frames; the tests watch to just short of that, then give it up to the second count to be gone.</summary>
    private const int StayFrames = 600, WatchedFrames = 590, GoneWithin = 700, SettledFrame = 120;

    /// <summary>The fewest pixels it must sway from side to side while it holds.</summary>
    private const double MinSway = 4;

    /// <summary>The stage that brings it, the weapon level a player has by then, and the shorter level-one gun.</summary>
    private const int WardenStage = 2, StageWeaponLevel = 2, ShortGunLevel = 1;

    /// <summary>Its stats: the emitter's hit points (the warden's own) and the points the kill scores.</summary>
    private const int EmitterHp = 7, WardenScore = 700;

    /// <summary>The emitter is a target this many pixels wide; its centre lies this far below the warden's.</summary>
    private const int EmitterWidth = 5, EmitterOffset = 4;

    /// <summary>A shot this far above the warden's centre lands on the hull, which hangs 2 pixels above it.</summary>
    private const double HullProbeOffset = 3;

    /// <summary>How many enemies one warden shields, and how far from it they may be.</summary>
    private const int Shields = 3;
    private const double Reach = 30;

    /// <summary>An evade cooldown longer than any test runs, so a placed enemy never dodges a shot.</summary>
    private const int NoEvadeCooldown = 9999;

    /// <summary>Where the shield tests place a warden on the default field: mid-field, at its hold row.</summary>
    private const double WardenX = 60, WardenRow = 13;

    /// <summary>A row further down the default field, for the drawing and damage tests, where the hold row does not matter.</summary>
    private const double OpenRow = 30;

    /// <summary>A grunt this far to one side of a warden is inside its reach.</summary>
    private const double NeighbourDx = 10;

    /// <summary>A grunt inside the reach of a warden at (<see cref="WardenX"/>, <see cref="WardenRow"/>) and clear of its sprite, so a shot aimed at it meets nothing else.</summary>
    private const double ShieldedGruntX = 74, ShieldedGruntY = 22;

    /// <summary>A shot this strong kills the emitter in one hit; a shot this weak only chips it; a shot of this strength is aimed at a grunt.</summary>
    private const double KillingDamage = 50, ChipDamage = 3, GruntShotDamage = 5;

    /// <summary>Frames the voice tests wait for it to speak.</summary>
    private const int SpeakWithin = 300;

    private static double Hold(RaiderGame g) => Math.Max(HoldShare * g.PH, HoldMin);

    /// <summary>Steps one frame and sweeps away every enemy but the ones a test keeps (a filler wave, a wave the stage brings).</summary>
    private static void StepKeeping(RaiderGame g, params Enemy[] keep)
    {
        g.Step();
        g.Enemies.RemoveAll(e => !keep.Contains(e));
    }

    private static Enemy MakeEnemy(RaiderGame g, EnemyKind kind, double x, double y)
    {
        var e = EnemyKit.Place(g, kind, x, y);
        e.VY = 0; e.Amp = 0; e.EvadeCd = NoEvadeCooldown;
        return e;
    }

    private static Part PartOf(Enemy e, string name) => e.Parts!.Single(p => p.Name == name);

    /// <summary>Clears every shield, runs the warden's update once, and returns the enemies it then shields.</summary>
    private static List<Enemy> UpdateAndGetShielded(RaiderGame g, Enemy warden)
    {
        foreach (var e in g.Enemies) e.ShieldedBy = null;
        Warden.Update(g, warden);
        return g.Enemies.Where(e => e.ShieldedBy == warden).ToList();
    }

    // ---------------------------------------------------------------- the kind

    [Fact]
    public void It_has_its_numbers()
    {
        Assert.Equal(EnemyKind.Warden, Warden.Kind);
        Assert.Equal("WARDEN", Warden.Name);
        Assert.Equal(WardenScore, Warden.Score);
        Assert.False(Warden.Boss);
        Assert.True(Warden.Name.Length <= EnemySpeechBox.MaxLabel);
    }

    [Fact]
    public void It_is_a_hull_of_armour_over_an_open_seven_point_emitter()
    {
        var g = EnemyKit.Game();
        var w = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        Assert.Equal(["hull", "emitter"], w.Parts!.Select(p => p.Name));
        var hull = PartOf(w, "hull"); var emitter = PartOf(w, "emitter");
        Assert.Equal(PartState.Armoured, hull.State);
        Assert.False(hull.Vital);
        Assert.Equal(PartState.Open, emitter.State);
        Assert.True(emitter.Vital);
        Assert.Equal(EmitterHp, emitter.Hp);
        Assert.Equal(EmitterHp, w.Hp);
        Assert.Equal(EmitterHp, w.MaxHp);
        Assert.Equal(EmitterWidth, emitter.HW * 2 + 1);
        Assert.Equal(EmitterOffset, emitter.OY);
        Assert.True(emitter.OY > hull.OY, "the emitter hangs under the hull, so from below it is the first thing a shot meets");
    }

    [Fact]
    public void Its_hit_points_follow_the_ramp_like_any_other_kind()
    {
        var g = EnemyKit.Game();
        g.StartAtStage(WardenStage);
        var w = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        Assert.Equal(EmitterHp * Ramp.HpMul(WardenStage), PartOf(w, "emitter").Hp, Places);
        Assert.Equal(PartOf(w, "emitter").Hp, w.Hp, Places);
    }

    [Fact]
    public void Stage_two_has_it_in_its_unlocks()
    {
        Assert.Contains(EnemyKind.Warden, Ramp.Unlocks(WardenStage));
    }

    // ---------------------------------------------------------------- the machine

    [Theory]
    [InlineData(30, 16)]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void It_enters_holds_and_sways_then_leaves_after_six_hundred_frames(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var w = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        Assert.True(w.Y < 0, "it enters from above");
        double minX = double.MaxValue, maxX = double.MinValue, floor = EnemyKit.BossFloor(g.PH);
        for (int f = 1; f <= WatchedFrames; f++)
        {
            StepKeeping(g, w);
            Assert.Contains(w, g.Enemies);
            if (f >= SettledFrame) { Assert.Equal(Hold(g), w.Y, Places); minX = Math.Min(minX, w.X); maxX = Math.Max(maxX, w.X); }
            Assert.True(EnemyKit.InsideWidth(g, w), $"frame {f}: x {w.X} outside {g.PW}");
            Assert.True(EnemyKit.LowestPixel(w) <= floor, $"frame {f}: lowest pixel {EnemyKit.LowestPixel(w)} under the floor {floor}");
            Assert.True(EnemyKit.Extent(w).T >= 0 || w.Y < Hold(g), $"frame {f}: the top is above the field once it has settled");
        }
        Assert.True(maxX - minX >= MinSway, $"it sways: {minX:0.0} to {maxX:0.0}");
        Assert.False(w.Leaving);
        for (int f = WatchedFrames + 1; f <= GoneWithin && g.Enemies.Contains(w); f++) StepKeeping(g, w);
        Assert.DoesNotContain(w, g.Enemies);
        Assert.Equal(0, g.Kills);
    }

    [Fact]
    public void It_stops_shielding_once_it_is_leaving()
    {
        var g = EnemyKit.Game();
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
        var grunt = MakeEnemy(g, EnemyKind.Grunt, WardenX + NeighbourDx, WardenRow);
        Assert.Contains(grunt, UpdateAndGetShielded(g, w));
        w.Age = StayFrames + 1; w.Leaving = true;
        Assert.Empty(UpdateAndGetShielded(g, w));
    }

    // ---------------------------------------------------------------- safe

    [Theory]
    [InlineData(30, 16)]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void It_shoots_nothing_and_a_ship_standing_still_lives(int cols, int rows)
    {
        var g = EnemyKit.Game(cols, rows);
        var w = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        int hostile = 0;
        for (int f = 0; f < GoneWithin; f++)
        {
            StepKeeping(g, w);
            hostile += g.Bullets.Count(b => !b.Friendly);
            Assert.False(Warden.Touches(g, w));
        }
        Assert.Equal(0, hostile);
        Assert.Equal(0, g.Deaths);
        Assert.Equal(Tuning.ShipHp, g.Hp);
    }

    // ---------------------------------------------------------------- pixels

    [Fact]
    public void It_is_drawn_with_a_gold_eye_in_a_blue_emitter_under_a_plum_hull()
    {
        var g = EnemyKit.Game();
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, OpenRow);
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int x0 = (int)Math.Round(w.X - w.Sprite.W / 2.0), y0 = (int)Math.Round(w.Y - w.Sprite.H / 2.0);
        Assert.Equal(SpriteW, w.Sprite.W);
        Assert.Equal(SpriteH, w.Sprite.H);
        Assert.Equal(Palette.Gold, f.Get(x0 + MidCol, y0 + EyeRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Shield, f.Get(x0 + MidCol - 1, y0 + EyeRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Shield, f.Get(x0 + MidCol, y0 + EyeRow - 1) & EnemyKit.RgbMask);
        Assert.Equal(Art.DarkPal['+'], f.Get(x0 + VioletCol, y0 + VioletRow) & EnemyKit.RgbMask);
        Assert.Equal(Art.DarkPal['o'], f.Get(x0 + MidCol, y0 + FillRow) & EnemyKit.RgbMask);
        Assert.Equal(Palette.Plum, f.Get(x0 + RimCol, y0) & EnemyKit.RgbMask);
    }

    /// <summary>The drawing is 13 by 12; its middle column holds the gold eye on the emitter's row 9, a violet stripe starts at column 3 of row 2,
    /// the husk's fill shows in the middle of row 3, and the plum rim of the top row starts at column 2.</summary>
    private const int SpriteW = 13, SpriteH = 12, MidCol = SpriteW / 2, EyeRow = 9, VioletCol = 3, VioletRow = 2, FillRow = 3, RimCol = 2;

    [Fact]
    public void The_parts_are_drawn_exactly_over_the_body_for_every_whole_pixel_position()
    {
        // an odd column adds half a pixel, so four columns and four rows draw the 13-wide sprite at both of its roundings
        const int FirstX = 40, FirstY = 20, Steps = 4;
        var g = EnemyKit.Game();
        for (int x = FirstX; x < FirstX + Steps; x++)
            for (int y = FirstY; y < FirstY + Steps; y++)
            {
                EnemyKit.Clear(g);
                var w = MakeEnemy(g, EnemyKind.Warden, x + 0.5 * (x % 2), y);
                var withParts = EnemyKit.ClearPickupsAndDraw(g);
                var snapshot = (uint[])withParts.P.Clone();
                w.Parts!.Clear();
                var bodyOnly = EnemyKit.ClearPickupsAndDraw(g);
                Assert.True(snapshot.SequenceEqual(bodyOnly.P), $"at ({w.X}, {w.Y}) the parts disagree with the body");
            }
    }

    [Fact]
    public void A_blue_thread_joins_it_to_a_shielded_enemy_and_only_to_one()
    {
        var g = EnemyKit.Game();
        // the grunt sits level with the emitter, a gap wide enough for a dotted thread; the far one is out of reach
        const double ThreadWardenX = 40, ThreadGap = 22, FarX = 100, GruntRow = OpenRow + EmitterOffset;
        const int MinThreadDots = 3;
        var w = MakeEnemy(g, EnemyKind.Warden, ThreadWardenX, OpenRow);
        var grunt = MakeEnemy(g, EnemyKind.Grunt, ThreadWardenX + ThreadGap, GruntRow);
        // before any frame the warden has shielded no one: nothing blue between the two
        Assert.Equal(0, ShieldPixelsBetween(EnemyKit.ClearPickupsAndDraw(g), w, grunt));
        StepKeeping(g, w, grunt);
        grunt.X = grunt.BaseX = ThreadWardenX + ThreadGap; grunt.Y = GruntRow; w.X = w.BaseX = ThreadWardenX; w.Y = OpenRow;
        Assert.True(ShieldPixelsBetween(EnemyKit.ClearPickupsAndDraw(g), w, grunt) >= MinThreadDots, "a dotted blue thread runs to the shielded grunt");
        EnemyKit.Clear(g);
        var w2 = MakeEnemy(g, EnemyKind.Warden, ThreadWardenX, OpenRow);
        var far = MakeEnemy(g, EnemyKind.Grunt, FarX, GruntRow);
        StepKeeping(g, w2, far);
        Assert.Equal(0, ShieldPixelsBetween(EnemyKit.ClearPickupsAndDraw(g), w2, far));
    }

    /// <summary>Rows searched above and below the target's middle for thread pixels.</summary>
    private const int ThreadBand = 8;

    /// <summary>Counts the shield-blue pixels in the gap between the warden's right edge and the target's left edge.</summary>
    private static int ShieldPixelsBetween(Surface f, Enemy w, Enemy t)
    {
        int n = 0;
        var wx = EnemyKit.Extent(w);
        for (int x = (int)Math.Ceiling(wx.R) + 1; x < (int)(t.X - t.Sprite.W / 2.0) - 1; x++)
            for (int y = (int)t.Y - ThreadBand; y <= (int)t.Y + ThreadBand; y++)
                if ((f.Get(x, y) & EnemyKit.RgbMask) == Palette.Shield) n++;
        return n;
    }

    // ---------------------------------------------------------------- voice

    [Fact]
    public void It_says_a_waiting_post_on_arriving()
    {
        var g = EnemyKit.Game();
        g.OfferDarkness(new DarknessPost(DarknessKind.Rude, 1, WaitingPost));
        var w = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        for (int i = 0; i < SpeakWithin && w.Text is null; i++) g.Step();   // it speaks once it is whole on the field
        Assert.Equal(WaitingPost, w.Text);
        Assert.True(EnemySpeechBox.Speaks(w));
    }

    [Fact]
    public void With_no_post_waiting_it_says_its_own_line_under_its_name()
    {
        var g = EnemyKit.Game();
        var w = g.SpawnKind(EnemyKind.Warden, EnemyKit.MidWidth);
        for (int i = 0; i < SpeakWithin && w.Text is null; i++) g.Step();   // it speaks once it is whole on the field
        Assert.Equal("They are with me.", w.Text);
        Assert.Equal("WARDEN", w.Header);
    }

    // ---------------------------------------------------------------- who is shielded

    [Fact]
    public void Exactly_the_three_nearest_are_shielded_never_a_gunship_a_boss_a_mine_or_another_warden()
    {
        var g = EnemyKit.Game();
        var script = new ScriptBehaviour(EnemyKind.Bulwark) { IsBoss = true };
        EnemyKit.OverrideBehaviour(g, script);
        // every kind it must pass over sits nearer than the nearest it may take, so a wrong pick shows
        const double GunshipDx = 3, BossDx = 4, OtherWardenDx = 6, MineDx = 6;
        // the four it may take, nearest first: the fourth is in reach but one too many
        const double FirstDx = 8, SecondDx = 12, ThirdDy = 16, FourthDx = 20;
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
        var gunship = MakeEnemy(g, EnemyKind.Gunship, WardenX + GunshipDx, WardenRow);
        var lesser = MakeEnemy(g, EnemyKind.Gunship, WardenX - GunshipDx, WardenRow); lesser.Lesser = true;
        var boss = MakeEnemy(g, EnemyKind.Bulwark, WardenX + BossDx, WardenRow);
        var other = MakeEnemy(g, EnemyKind.Warden, WardenX - OtherWardenDx, WardenRow);
        var mine = MakeEnemy(g, EnemyKind.Mine, WardenX + MineDx, WardenRow);
        var a = MakeEnemy(g, EnemyKind.Grunt, WardenX + FirstDx, WardenRow);
        var b = MakeEnemy(g, EnemyKind.Carrier, WardenX - SecondDx, WardenRow);
        var c = MakeEnemy(g, EnemyKind.Splitter, WardenX, WardenRow + ThirdDy);
        var d = MakeEnemy(g, EnemyKind.Diver, WardenX + FourthDx, WardenRow);
        var farther = MakeEnemy(g, EnemyKind.Grunt, WardenX + Reach - 1, WardenRow);
        var beyond = MakeEnemy(g, EnemyKind.Grunt, WardenX + Reach + 1, WardenRow);
        var shielded = UpdateAndGetShielded(g, w);
        Assert.Equal(Shields, shielded.Count);
        Assert.Equivalent(new[] { a, b, c }, shielded);
        foreach (var x in new[] { w, gunship, lesser, boss, other, mine, d, farther, beyond }) Assert.Null(x.ShieldedBy);
    }

    [Fact]
    public void With_only_two_in_reach_only_two_are_shielded_and_with_none_nothing_is()
    {
        var g = EnemyKit.Game();
        const double OutOfReachDx = 40;
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
        Assert.Empty(UpdateAndGetShielded(g, w));
        MakeEnemy(g, EnemyKind.Grunt, WardenX + NeighbourDx, WardenRow);
        MakeEnemy(g, EnemyKind.Grunt, WardenX - NeighbourDx, WardenRow);
        MakeEnemy(g, EnemyKind.Grunt, WardenX + OutOfReachDx, WardenRow);
        Assert.Equal(2, UpdateAndGetShielded(g, w).Count);
    }

    [Fact]
    public void The_nearest_are_taken_by_distance_and_a_tie_goes_to_the_older()
    {
        var g = EnemyKit.Game();
        const double CloserDx = 4;
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
        var l1 = MakeEnemy(g, EnemyKind.Grunt, WardenX - NeighbourDx, WardenRow);
        var r1 = MakeEnemy(g, EnemyKind.Grunt, WardenX + NeighbourDx, WardenRow);
        var l2 = MakeEnemy(g, EnemyKind.Grunt, WardenX - NeighbourDx, WardenRow);   // ties with l1 and r1
        var closer = MakeEnemy(g, EnemyKind.Grunt, WardenX + CloserDx, WardenRow);
        var shielded = UpdateAndGetShielded(g, w);
        Assert.Equivalent(new[] { closer, l1, r1 }, shielded);
        Assert.Null(l2.ShieldedBy);
    }

    [Fact]
    public void It_does_not_shield_an_enemy_that_has_not_come_onto_the_field_or_is_dead()
    {
        var g = EnemyKit.Game();
        // the two it must pass over sit nearer than the one it takes
        const double NearDx = 6, AboveField = -12, OkDy = 11;
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
        var above = MakeEnemy(g, EnemyKind.Grunt, WardenX + NearDx, WardenRow); above.Y = AboveField;
        var dead = MakeEnemy(g, EnemyKind.Grunt, WardenX - NearDx, WardenRow); dead.Hp = 0;
        var ok = MakeEnemy(g, EnemyKind.Grunt, WardenX, WardenRow + OkDy);
        Assert.Equivalent(new[] { ok }, UpdateAndGetShielded(g, w));
    }

    [Fact]
    public void A_shielded_enemy_shrugs_shots_with_a_ping_and_takes_nothing()
    {
        var g = EnemyKit.Game();
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
        var grunt = MakeEnemy(g, EnemyKind.Grunt, ShieldedGruntX, ShieldedGruntY);
        double hp = grunt.Hp;
        EnemyKit.Shot(g, grunt.X, grunt.Y, GruntShotDamage);
        StepKeeping(g, w, grunt);
        Assert.Equal(hp, grunt.Hp);
        Assert.Contains(SfxNames.Ping, played);
        Assert.DoesNotContain(g.Bullets, b => b.Friendly && Math.Abs(b.X - grunt.X) < 1 && Math.Abs(b.Y - grunt.Y) < 1);
    }

    [Fact]
    public void An_enemy_out_of_its_reach_is_hurt_as_ever()
    {
        var g = EnemyKit.Game();
        // the two sit more than twice the reach apart
        const double LeftWardenX = 20, FarGruntX = 90;
        var w = MakeEnemy(g, EnemyKind.Warden, LeftWardenX, WardenRow);
        var grunt = MakeEnemy(g, EnemyKind.Grunt, FarGruntX, ShieldedGruntY);
        double hp = grunt.Hp;
        EnemyKit.Shot(g, grunt.X, grunt.Y, GruntShotDamage);
        StepKeeping(g, w, grunt);
        Assert.True(grunt.Hp < hp);
    }

    [Fact]
    public void The_hull_stops_a_shot_and_the_emitter_takes_it()
    {
        var g = EnemyKit.Game();
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, OpenRow);
        var emitter = PartOf(w, "emitter");
        EnemyKit.Shot(g, w.X, w.Y - HullProbeOffset, ChipDamage);
        StepKeeping(g, w);
        Assert.Equal(EmitterHp, emitter.Hp);
        Assert.Equal(EmitterHp, w.Hp);
        EnemyKit.Shot(g, w.X, w.Y + EmitterOffset, ChipDamage);
        StepKeeping(g, w);
        Assert.Equal(EmitterHp - ChipDamage, emitter.Hp, Places);
        Assert.Equal(EmitterHp - ChipDamage, w.Hp, Places);
        Assert.Contains(w, g.Enemies);
    }

    [Fact]
    public void Killing_the_emitter_kills_the_warden_for_seven_hundred()
    {
        var g = EnemyKit.Game();
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, OpenRow);
        int before = g.Score;
        EnemyKit.Shot(g, w.X, w.Y + EmitterOffset, KillingDamage);
        StepKeeping(g, w);
        Assert.DoesNotContain(w, g.Enemies);
        Assert.Equal(1, g.Kills);
        Assert.Equal(before + WardenScore, g.Score);
    }

    [Fact]
    public void They_are_hurt_again_the_frame_it_dies()
    {
        double GruntDamageTaken(bool killIt)
        {
            var g = EnemyKit.Game();
            var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
            var grunt = MakeEnemy(g, EnemyKind.Grunt, ShieldedGruntX, ShieldedGruntY);
            if (killIt) EnemyKit.Shot(g, w.X, w.Y + EmitterOffset, KillingDamage);   // the first shot of the frame kills it
            EnemyKit.Shot(g, grunt.X, grunt.Y, GruntShotDamage);             // the second lands on the grunt in the same frame
            double hp = grunt.Hp;
            StepKeeping(g, w, grunt);
            Assert.Equal(killIt, !g.Enemies.Contains(w) || w.Hp <= 0);
            return hp - grunt.Hp;
        }
        Assert.Equal(0, GruntDamageTaken(false));
        Assert.Equal(GruntShotDamage, GruntDamageTaken(true), Places);
    }

    [Fact]
    public void The_frame_after_it_dies_nothing_is_shielded()
    {
        var g = EnemyKit.Game();
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
        var grunt = MakeEnemy(g, EnemyKind.Grunt, ShieldedGruntX, ShieldedGruntY);
        EnemyKit.Shot(g, w.X, w.Y + EmitterOffset, KillingDamage);
        StepKeeping(g, w, grunt);
        double hp = grunt.Hp;
        EnemyKit.Shot(g, grunt.X, grunt.Y, GruntShotDamage);
        StepKeeping(g, grunt);
        Assert.Equal(hp - GruntShotDamage, grunt.Hp, Places);
        Assert.Null(grunt.ShieldedBy);
    }

    [Fact]
    public void A_bomb_ignores_the_shield_and_hurts_the_open_emitter_but_not_the_hull()
    {
        var g = EnemyKit.Game();
        var w = MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow);
        var grunt = MakeEnemy(g, EnemyKind.Grunt, ShieldedGruntX, ShieldedGruntY);
        var hull = PartOf(w, "hull");
        g.Input.BombPressed = true;
        StepKeeping(g, w, grunt);
        Assert.True(!g.Enemies.Contains(grunt) || grunt.Hp < 1, "a bomb does not care about the shield");
        Assert.DoesNotContain(w, g.Enemies);
        Assert.Equal(hull.MaxHp, hull.Hp);
        Assert.Equal(PartState.Armoured, hull.State);
    }

    // ---------------------------------------------------------------- a shielded diver

    [Fact]
    public void A_shielded_diver_winds_up_for_the_same_thirty_frames_and_a_ship_that_steps_aside_lives()
    {
        // the diver sits inside the warden's reach; the watch is long enough for a whole wind-up, and the count may start a frame or two late
        const double DiverDx = 6;
        const int WatchFrames = 120, WindupSlack = 2;
        int Windup(bool warden)
        {
            var g = EnemyKit.Game();
            Enemy? w = warden ? MakeEnemy(g, EnemyKind.Warden, WardenX, WardenRow) : null;
            var diver = MakeEnemy(g, EnemyKind.Diver, WardenX + DiverDx, g.PH * Tuning.DiverHoldRow);
            diver.Windup = -1;
            g.Input.Key(GameKey.Left, false);
            int start = -1, end = -1;
            for (int f = 0; f < WatchFrames; f++)
            {
                g.Step();
                g.Enemies.RemoveAll(e => e != diver && e != w);
                if (start < 0 && diver.Windup > 0) start = f;
                if (start >= 0 && end < 0 && (diver.Dashing || diver.Windup <= 0)) end = f;
                if (diver.Dashing) break;
            }
            return end - start;
        }
        Assert.Equal(Windup(false), Windup(true));
        Assert.True(Windup(true) >= Tuning.DiverWindupFrames - WindupSlack);
    }

    // ---------------------------------------------------------------- same seed, same run

    [Fact]
    public void The_same_seed_gives_the_same_run_with_wardens_in_it()
    {
        // a seed whose stage-two run brings a warden inside the frames watched
        const ulong RunSeed = 11;
        const int RunFrames = 900;
        (string, bool) Run(ulong seed)
        {
            var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
            g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
            g.StartAtStage(WardenStage);
            bool seen = false;
            for (int f = 0; f < RunFrames; f++)
            {
                EnemyKit.BotFrame(g);
                g.Step();
                seen |= g.Enemies.Any(e => e.Kind == EnemyKind.Warden);
            }
            var state = string.Join("|", g.Enemies.Select(e => $"{e.Kind}:{e.X:0.000}:{e.Y:0.000}:{e.Hp:0.000}")) + $"#{g.Score}:{g.Kills}:{g.Deaths}:{g.PX:0.0}";
            return (state, seen);
        }
        var (a, seenA) = Run(RunSeed);
        var (b, _) = Run(RunSeed);
        Assert.Equal(a, b);
        Assert.True(seenA, $"stage {WardenStage} brings a warden within {RunFrames} frames");
    }

    // ---------------------------------------------------------------- the fight

    /// <summary>Each seed brings the warden in at one of ten slots across the middle 60% of the field, starting 20% in; the stride spreads neighbouring seeds apart.</summary>
    private const double SpawnLeft = 0.2, SpawnSpan = 0.6;
    private const ulong SpawnSlots = 10, SeedStride = 7;

    /// <summary>The escort's offsets from the warden, how far below its hold row they fly, and how near an edge one may be pushed.</summary>
    private static readonly double[] EscortDx = [-14, 14, 20];
    private const double EscortBelow = 10, EscortEdge = 6;

    /// <summary>How many seeds each fight test runs.</summary>
    private const ulong EscortedSeeds = 9, LoneSeeds = 5;

    /// <summary>Frames a bot that aims at the emitter needs to kill a warden at stage 2 with the given weapon level, or -1 if it does not.</summary>
    private static int Fight(ulong seed, int level, bool withEscort)
    {
        var g = new RaiderGame(seed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtStage(WardenStage);
        EnemyKit.Clear(g);
        while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Weapon, g.PX, g.PY); g.Step(); EnemyKit.Clear(g); }
        var w = g.SpawnKind(EnemyKind.Warden, SpawnLeft + SpawnSpan * ((seed * SeedStride) % SpawnSlots) / (double)SpawnSlots);
        var keep = new List<Enemy> { w };
        var escort = new List<(Enemy E, double Dx)>();
        if (withEscort)
            foreach (var dx in EscortDx)
            {
                var e = EnemyKit.Place(g, EnemyKind.Grunt, w.X + dx, Hold(g) + EscortBelow);
                e.VY = 0; e.EvadeCd = NoEvadeCooldown;
                keep.Add(e); escort.Add((e, dx));
            }
        for (int f = 0; f < StayFrames; f++)
        {
            // the escort flies along with the warden, to either side: the bot aims at the emitter, and a shielded grunt in the lane would take its shots
            foreach (var (e, dx) in escort) { e.X = e.BaseX = Math.Clamp(w.X + dx, EscortEdge, g.PW - EscortEdge); }
            EnemyKit.BotFrame(g);
            g.Step();
            g.Enemies.RemoveAll(e => !keep.Contains(e));
            if (!g.Enemies.Contains(w)) return g.Kills > 0 ? f + 1 : -1;
        }
        return -1;
    }

    [Fact]
    public void A_bot_that_aims_at_the_emitter_kills_it_inside_its_six_hundred_frames_at_stage_twos_weapon_level()
    {
        var times = new List<int>();
        for (ulong seed = 1; seed <= EscortedSeeds; seed++)
        {
            int t = Fight(seed, StageWeaponLevel, withEscort: true);
            Assert.True(t > 0, $"seed {seed}: not dead inside {StayFrames} frames");
            times.Add(t);
        }
        times.Sort();
        output.WriteLine($"warden fight at level {StageWeaponLevel}, frames to kill: {string.Join(" ", times)}; median {times[times.Count / 2]} ({times[times.Count / 2] / Tuning.Fps:0.0} s)");
        Assert.True(times[times.Count / 2] < StayFrames);
    }

    [Fact]
    public void The_fight_at_the_short_level_one_gun_still_ends_inside_the_window()
    {
        var times = new List<int>();
        for (ulong seed = 1; seed <= LoneSeeds; seed++)
        {
            int t = Fight(seed, ShortGunLevel, withEscort: false);
            Assert.True(t > 0, $"seed {seed}: not dead inside {StayFrames} frames");
            times.Add(t);
        }
        output.WriteLine($"warden fight at level {ShortGunLevel}, frames to kill: {string.Join(" ", times.Order())}");
    }
}
