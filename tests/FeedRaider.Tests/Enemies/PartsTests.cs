using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The shared machinery every enemy stands on: parts, the enemy table, and the hooks a kind uses so a new kind never edits
/// <c>RaiderGame.cs</c>. Every test builds its enemy by hand (<see cref="RaiderGame.SpawnParts"/>) or scripts a kind
/// (<see cref="ScriptBehaviour"/>), so none depends on a real kind existing. Nothing here counts hearts or shield hits.
/// </summary>
public class PartsTests
{
    /// <summary>The hit points of the owner of <see cref="EnemyKit.ScriptedThreeParts"/>: its two pods', the only vital parts.</summary>
    private const double OwnerHp = 2 * EnemyKit.PodHp;

    /// <summary>The side of a pod's art in pixels.</summary>
    private const int PodSide = 2 * EnemyKit.PodHalf + 1;

    /// <summary>A shot that breaks a part with hit points to spare, and one far heavier than any enemy.</summary>
    private const double HeavyShot = 25, KillingShot = 9999;

    /// <summary>A row well onto the field, below where an enemy comes in.</summary>
    private const double OnFieldY = 40;

    /// <summary>A spot well inside the field where a placed enemy sits whole, and a column in its left half.</summary>
    private const double SpotX = 60, SpotY = 30, LeftX = 30;

    /// <summary>The decimal places hit points and positions are compared to.</summary>
    private const int Places = 6;

    /// <summary>The names <see cref="EnemyKit.ScriptedThreeParts"/> gives its two pods and its shell.</summary>
    private const string PodLeftName = "pod-l", PodRightName = "pod-r", ShellName = "shell";

    /// <summary>The names of the two parts a scripted kind adds as it arrives.</summary>
    private const string PartA = "a", PartB = "b";

    /// <summary>The share of the field's width an enemy arrives at: its middle.</summary>
    private const double MidField = 0.5;

    private const int TwoShots = 2;

    /// <summary>The left pod, the right pod and the shell of an enemy <see cref="EnemyKit.ScriptedThreeParts"/> made.</summary>
    internal static Part PodL(Enemy enemy) => enemy.Parts!.Single(part => part.Name == PodLeftName);
    internal static Part PodR(Enemy enemy) => enemy.Parts!.Single(part => part.Name == PodRightName);
    internal static Part Shell(Enemy enemy) => enemy.Parts!.Single(part => part.Name == ShellName);

    /// <summary>Drops a shot exactly on a part's centre and steps the game one frame, so the shot lands.</summary>
    private static void ShootAtAndStep(RaiderGame game, Enemy enemy, Part part, double damage = EnemyKit.PlainShotDamage)
    {
        EnemyKit.Shot(game, enemy.X + part.OX, enemy.Y + part.OY, damage);
        EnemyKit.StepSweeping(game);
    }

    private static bool HasWhiteSpark(RaiderGame game) => game.Particles.Any(spark => spark.Kind == ParticleKind.Spark && spark.Colour == Palette.White);

    // ---------------------------------------------------------------- shots

    [Fact]
    public void A_shot_in_an_open_parts_box_damages_that_part_only()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        ShootAtAndStep(game, enemy, PodL(enemy));
        Assert.Equal(EnemyKit.PodHp - EnemyKit.PlainShotDamage, PodL(enemy).Hp, Places);
        Assert.Equal(EnemyKit.PodHp, PodR(enemy).Hp, Places);
        Assert.Equal(EnemyKit.ShellHp, Shell(enemy).Hp, Places);
        Assert.DoesNotContain(game.Bullets, bullet => bullet.Friendly);
        Assert.True(PodL(enemy).Flash > 0);
        Assert.Equal(0, PodR(enemy).Flash);
    }

    [Fact]
    public void A_shot_in_an_armoured_parts_box_stops_with_a_spark_and_damages_nothing()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        ShootAtAndStep(game, enemy, Shell(enemy), HeavyShot);
        Assert.Empty(game.Bullets);
        Assert.True(HasWhiteSpark(game));
        Assert.All(enemy.Parts!, part => Assert.Equal(part.MaxHp, part.Hp, Places));
        Assert.Equal(OwnerHp, enemy.Hp, Places);
    }

    [Fact]
    public void A_shot_that_meets_no_part_flies_on()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const double BelowEveryBox = 24, BesideEveryBox = 30;
        EnemyKit.Shot(game, enemy.X, enemy.Y + BelowEveryBox);
        EnemyKit.Shot(game, enemy.X + BesideEveryBox, enemy.Y);
        EnemyKit.StepSweeping(game);
        Assert.Equal(TwoShots, game.Bullets.Count(bullet => bullet.Friendly));
        Assert.All(enemy.Parts!, part => Assert.Equal(part.MaxHp, part.Hp, Places));
    }

    [Fact]
    public void A_shot_is_tested_against_the_last_part_first()
    {
        var game = EnemyKit.Game();
        const int HalfSide = 6;
        const double Hp = 20;
        var under = EnemyKit.MakePart("under", 0, 0, HalfSide, HalfSide, Hp);
        var over = EnemyKit.MakePart("over", 0, 0, HalfSide, HalfSide, Hp);
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark));   // a stand-in kind: the real Bulwark would run its own machine on these parts
        var enemy = game.SpawnParts(EnemyKind.Bulwark, SpotX, OnFieldY, under, over);
        ShootAtAndStep(game, enemy, under);
        Assert.Equal(Hp, under.Hp, Places);
        Assert.Equal(Hp - EnemyKit.PlainShotDamage, over.Hp, Places);
    }

    [Fact]
    public void The_kind_hears_every_part_hit_and_whether_the_shot_was_stopped()
    {
        var game = EnemyKit.Game();
        var hits = new List<(string Part, bool Stopped)>();
        var behaviour = new ScriptBehaviour(EnemyKind.Bulwark) { OnPartHit = (_, _, part, _, stopped) => hits.Add((part.Name, stopped)) };
        EnemyKit.OverrideBehaviour(game, behaviour);
        var enemy = EnemyKit.ScriptedThreeParts(game);
        ShootAtAndStep(game, enemy, Shell(enemy));
        ShootAtAndStep(game, enemy, PodL(enemy));
        Assert.Equal([(ShellName, true), (PodLeftName, false)], hits);
    }

    [Fact]
    public void Parts_take_no_knock_back_and_the_owner_never_sidesteps()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const int Volleys = 6, FramesBetween = 3;
        const double Aside = 10, Above = 40, Below = 4;
        double startX = enemy.X, startY = enemy.Y;
        for (int volley = 0; volley < Volleys; volley++) { EnemyKit.Shot(game, enemy.X - Aside, enemy.Y - Above); EnemyKit.Shot(game, enemy.X + Aside, enemy.Y + Below); EnemyKit.StepSweeping(game, FramesBetween); }
        Assert.Equal(0, enemy.KY, Places);
        Assert.Equal(0, enemy.KX, Places);
        Assert.False(enemy.Evading);
        Assert.Equal(startX, enemy.X, Places);
        Assert.Equal(startY, enemy.Y, Places);
    }

    // ---------------------------------------------------------------- hit points and death

    [Fact]
    public void The_owners_hp_is_the_sum_of_its_vital_parts()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const double Hit = 15;
        Assert.Equal(OwnerHp, enemy.Hp, Places);
        Assert.Equal(OwnerHp, enemy.MaxHp, Places);
        ShootAtAndStep(game, enemy, PodR(enemy), Hit);
        Assert.Equal(OwnerHp - Hit, enemy.Hp, Places);
        Assert.Equal(OwnerHp, enemy.MaxHp, Places);
    }

    [Fact]
    public void The_owner_dies_when_the_last_vital_part_does_and_not_before()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        ShootAtAndStep(game, enemy, PodL(enemy), EnemyKit.PodHp);
        Assert.Equal(PartState.Dead, PodL(enemy).State);
        Assert.Contains(enemy, game.Enemies);
        Assert.Equal(0, game.Kills);
        ShootAtAndStep(game, enemy, PodR(enemy), EnemyKit.PodHp);
        Assert.DoesNotContain(enemy, game.Enemies);
        Assert.Equal(1, game.Kills);
    }

    [Fact]
    public void An_armoured_part_that_is_not_vital_does_not_keep_the_owner_alive()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const double UnderOneShot = 5;
        PodL(enemy).Hp = UnderOneShot; PodR(enemy).Hp = UnderOneShot;
        ShootAtAndStep(game, enemy, PodL(enemy)); ShootAtAndStep(game, enemy, PodR(enemy));
        Assert.DoesNotContain(enemy, game.Enemies);
        Assert.Equal(EnemyKit.ShellHp, Shell(enemy).Hp, Places);
    }

    [Fact]
    public void A_part_at_zero_becomes_dead_scores_and_tells_the_kind()
    {
        var game = EnemyKit.Game();
        var down = new List<string>();
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { OnPartDown = (_, _, part) => down.Add(part.Name) });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        int before = game.Score;
        const int MoreFrames = 4;
        ShootAtAndStep(game, enemy, PodL(enemy), EnemyKit.PodHp);
        Assert.Equal(PartState.Dead, PodL(enemy).State);
        Assert.Equal(before + EnemyKit.PodLeftScore, game.Score);
        Assert.Equal([PodLeftName], down);
        Assert.Contains(game.Popups, popup => popup.Text == "+" + EnemyKit.PodLeftScore);
        Assert.NotEmpty(game.Rings);
        EnemyKit.StepSweeping(game, MoreFrames);
        Assert.Equal([PodLeftName], down);
    }

    [Fact]
    public void The_kind_hears_when_the_owner_goes_down()
    {
        var game = EnemyKit.Game();
        Enemy? went = null;
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { OnDown = (_, enemy) => went = enemy });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        ShootAtAndStep(game, enemy, PodL(enemy), EnemyKit.PodHp); ShootAtAndStep(game, enemy, PodR(enemy), EnemyKit.PodHp);
        Assert.Same(enemy, went);
    }

    [Fact]
    public void A_dead_part_with_a_wreck_still_stops_shots_and_one_without_lets_them_through()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);                      // pod-l has a wreck, pod-r has none
        const double Scratch = 5, SameColumn = 0.5;
        ShootAtAndStep(game, enemy, PodL(enemy), EnemyKit.PodHp); ShootAtAndStep(game, enemy, PodR(enemy), Scratch);
        enemy.Parts![0].Vital = true;                          // the shell keeps the owner alive while both pods are down
        PodR(enemy).Hp = 0; PodR(enemy).State = PartState.Dead;
        EnemyKit.Shot(game, enemy.X + PodL(enemy).OX, enemy.Y + PodL(enemy).OY);
        EnemyKit.Shot(game, enemy.X + PodR(enemy).OX, enemy.Y + PodR(enemy).OY);
        EnemyKit.StepSweeping(game);
        double leftPodX = enemy.X + PodL(enemy).OX, rightPodX = enemy.X + PodR(enemy).OX;   // the ship's own shots fly elsewhere, so only these two columns are looked at
        Assert.DoesNotContain(game.Bullets, bullet => bullet.Friendly && Math.Abs(bullet.X - leftPodX) < SameColumn);
        Assert.Single(game.Bullets, bullet => bullet.Friendly && Math.Abs(bullet.X - rightPodX) < SameColumn);
        Assert.Equal(0, PodL(enemy).Hp, Places);
    }

    // ---------------------------------------------------------------- bombs

    [Fact]
    public void A_bomb_takes_twelve_from_each_open_part_and_nothing_from_an_armoured_one()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        game.Input.Bomb();
        EnemyKit.StepSweeping(game);
        Assert.Equal(EnemyKit.PodHp - Tuning.BombPartDamage, PodL(enemy).Hp, Places);
        Assert.Equal(EnemyKit.PodHp - Tuning.BombPartDamage, PodR(enemy).Hp, Places);
        Assert.Equal(EnemyKit.ShellHp, Shell(enemy).Hp, Places);
        Assert.Equal(12, Tuning.BombPartDamage);
    }

    [Fact]
    public void A_bomb_clears_enemy_bolts_and_skips_an_untouchable_enemy()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        var hidden = EnemyKit.ScriptedThreeParts(game); hidden.Untouchable = true;
        const double NearTopLeft = 5;
        game.Bullets.Add(new Bullet { X = NearTopLeft, Y = NearTopLeft, VY = 1 });
        game.Input.Bomb();
        EnemyKit.StepSweeping(game);
        Assert.DoesNotContain(game.Bullets, bullet => !bullet.Friendly);
        Assert.Equal(EnemyKit.PodHp - Tuning.BombPartDamage, PodL(enemy).Hp, Places);
        Assert.Equal(EnemyKit.PodHp, PodL(hidden).Hp, Places);
    }

    // ---------------------------------------------------------------- shielded and untouchable

    [Fact]
    public void A_shielded_enemy_shrugs_off_shots_but_not_bombs()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { OnUpdate = (_, x) => x.ShieldedBy = x });
        EnemyKit.StepSweeping(game);                                    // the kind sets it in its update
        EnemyKit.Shot(game, enemy.X + PodL(enemy).OX, enemy.Y + PodL(enemy).OY);
        EnemyKit.StepSweeping(game);
        Assert.Equal(EnemyKit.PodHp, PodL(enemy).Hp, Places);
        Assert.Empty(game.Bullets);
        Assert.True(HasWhiteSpark(game));
        game.Input.Bomb();
        EnemyKit.StepSweeping(game);
        Assert.Equal(EnemyKit.PodHp - Tuning.BombPartDamage, PodL(enemy).Hp, Places);
    }

    [Fact]
    public void The_shield_is_cleared_every_frame_so_a_warden_that_stops_shielding_frees_its_charge()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        enemy.ShieldedBy = enemy;
        EnemyKit.StepSweeping(game);
        Assert.Null(enemy.ShieldedBy);
        ShootAtAndStep(game, enemy, PodL(enemy));
        Assert.Equal(EnemyKit.PodHp - EnemyKit.PlainShotDamage, PodL(enemy).Hp, Places);
    }

    [Fact]
    public void An_untouchable_enemy_is_passed_through_by_shots_and_by_the_ship()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        enemy.Untouchable = true;
        EnemyKit.Shot(game, enemy.X + PodL(enemy).OX, enemy.Y + PodL(enemy).OY);
        EnemyKit.StepSweeping(game);
        Assert.Single(game.Bullets, bullet => bullet.Friendly);
        Assert.Equal(EnemyKit.PodHp, PodL(enemy).Hp, Places);
        enemy.Y = game.PY - PodL(enemy).OY; enemy.X = game.PX - PodL(enemy).OX;
        EnemyKit.StepSweeping(game);
        Assert.Equal(0, game.Invuln);
    }

    [Fact]
    public void A_plain_enemy_without_parts_can_be_shielded()
    {
        var game = EnemyKit.Game();
        var grunt = EnemyKit.Place(game, EnemyKind.Grunt, SpotX, SpotY);
        grunt.ShieldedBy = grunt;
        double hp = grunt.Hp;
        EnemyKit.Shot(game, grunt.X, grunt.Y);
        game.Step();
        Assert.Equal(hp, grunt.Hp, Places);
    }

    // ---------------------------------------------------------------- the ship

    [Fact]
    public void The_ship_is_hurt_touching_a_living_parts_box_and_not_a_dead_ones()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        enemy.X = game.PX - PodR(enemy).OX; enemy.Y = game.PY - PodR(enemy).OY;
        PodR(enemy).Hp = 0; PodR(enemy).State = PartState.Dead;
        const uint WreckGrey = 0x040404;
        PodR(enemy).Wreck = EnemyKit.Solid(PodSide, PodSide, WreckGrey);
        EnemyKit.StepSweeping(game);
        Assert.Equal(0, game.Invuln);                         // a wreck is scenery
        PodR(enemy).Hp = EnemyKit.PodHp; PodR(enemy).State = PartState.Open;
        EnemyKit.StepSweeping(game);
        Assert.True(game.Invuln > 0);
    }

    [Fact]
    public void The_ship_is_hurt_touching_an_armoured_part_too()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        enemy.X = game.PX - Shell(enemy).OX; enemy.Y = game.PY - Shell(enemy).OY;
        EnemyKit.StepSweeping(game);
        Assert.True(game.Invuln > 0);
    }

    [Fact]
    public void A_beam_the_kind_reports_through_touches_hurts_the_ship_only_inside_it()
    {
        var game = EnemyKit.Game();
        const double LeftColumn = 20, BeamHalfWidth = 3;
        double col = LeftColumn;
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { OnTouches = (hookGame, _) => Math.Abs(hookGame.PX - col) <= BeamHalfWidth });
        EnemyKit.ScriptedThreeParts(game);
        EnemyKit.StepSweeping(game);
        Assert.Equal(0, game.Invuln);                         // the ship stands at the middle, well right of the beam
        col = game.PX;
        EnemyKit.StepSweeping(game);
        Assert.True(game.Invuln > 0);
    }

    [Fact]
    public void An_unarmed_owner_cannot_touch_the_ship()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        enemy.X = game.PX - PodL(enemy).OX; enemy.Y = game.PY - PodL(enemy).OY;
        const int LongGrace = 1000;
        enemy.Grace = LongGrace;
        EnemyKit.StepSweeping(game);
        Assert.Equal(0, game.Invuln);
    }

    // ---------------------------------------------------------------- pixels

    private static (int X0, int Y0) ArtCorner(Enemy enemy, Part part, Sprite art) =>
        ((int)Math.Round(enemy.X + part.OX - art.W / 2.0), (int)Math.Round(enemy.Y + part.OY - art.H / 2.0));

    private static uint CentrePixel(Surface frame, Enemy enemy, Part part, Sprite art)
    {
        var (x0, y0) = ArtCorner(enemy, part, art);
        return frame.Get(x0 + art.W / 2, y0 + art.H / 2);
    }

    [Fact]
    public void Each_part_is_drawn_at_its_offset_in_the_art_for_its_state()
    {
        var game = EnemyKit.Game();
        const uint OpenRed = 0xaa0011;
        var open = EnemyKit.Solid(PodSide, PodSide, OpenRed);
        var enemy = EnemyKit.ScriptedThreeParts(game);
        PodR(enemy).OpenArt = open;
        Assert.Equal(EnemyKit.PodLeftColour, CentrePixel(EnemyKit.ClearPickupsAndDraw(game), enemy, PodL(enemy), PodL(enemy).Closed));
        Assert.Equal(EnemyKit.ShellColour, CentrePixel(EnemyKit.ClearPickupsAndDraw(game), enemy, Shell(enemy), Shell(enemy).Closed));
        Assert.Equal(OpenRed, CentrePixel(EnemyKit.ClearPickupsAndDraw(game), enemy, PodR(enemy), open));
        PodR(enemy).State = PartState.Armoured;
        Assert.Equal(EnemyKit.PodRightColour, CentrePixel(EnemyKit.ClearPickupsAndDraw(game), enemy, PodR(enemy), PodR(enemy).Closed));
        PodL(enemy).State = PartState.Dead;
        Assert.Equal(EnemyKit.PodWreckColour, CentrePixel(EnemyKit.ClearPickupsAndDraw(game), enemy, PodL(enemy), PodL(enemy).Wreck!));
        var (x0, y0) = ArtCorner(enemy, PodL(enemy), PodL(enemy).Wreck!);
        Assert.Equal(EnemyKit.PodWreckColour, EnemyKit.ClearPickupsAndDraw(game).Get(x0, y0));
    }

    [Fact]
    public void A_dead_part_with_no_wreck_draws_nothing()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        PodR(enemy).State = PartState.Dead;
        Assert.NotEqual(EnemyKit.PodRightColour, CentrePixel(EnemyKit.ClearPickupsAndDraw(game), enemy, PodR(enemy), PodR(enemy).Closed));
    }

    [Fact]
    public void A_part_flashes_on_its_own_and_the_rest_do_not()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        PodL(enemy).Flash = Tuning.FlashFrames;
        var frame = EnemyKit.ClearPickupsAndDraw(game);
        Assert.Equal(Surface.Mix(EnemyKit.PodLeftColour, Palette.White, RaiderView.FlashWhite), CentrePixel(frame, enemy, PodL(enemy), PodL(enemy).Closed));
        Assert.Equal(EnemyKit.PodRightColour, CentrePixel(frame, enemy, PodR(enemy), PodR(enemy).Closed));
    }

    [Fact]
    public void A_parts_flash_counts_down_while_the_game_runs()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        ShootAtAndStep(game, enemy, PodL(enemy));
        const int Frames = 2;
        int flashBefore = PodL(enemy).Flash;
        EnemyKit.StepSweeping(game, Frames);
        Assert.Equal(flashBefore - Frames, PodL(enemy).Flash);
    }

    private static (int Left, int Top, int Right) DrawnExtent(Enemy enemy)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue;
        foreach (var part in enemy.Parts!)
        {
            var art = RaiderView.PartArt(part);
            if (art is null) continue;
            var (x0, y0) = ArtCorner(enemy, part, art);
            left = Math.Min(left, x0); top = Math.Min(top, y0); right = Math.Max(right, x0 + art.W);
        }
        return (left, top, right);
    }

    [Fact]
    public void A_bosss_health_bar_is_filled_by_hp_over_max_hp_on_the_bar_row_above_its_highest_pixel()
    {
        var game = EnemyKit.Game();
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { IsBoss = true });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        Assert.NotEqual(Palette.Plum, EnemyKit.ClearPickupsAndDraw(game).Get(DrawnExtent(enemy).Left, RaiderView.BarRow(DrawnExtent(enemy).Top)));   // full hit points: no bar
        ShootAtAndStep(game, enemy, PodL(enemy));
        ShootAtAndStep(game, enemy, PodR(enemy));
        var (left, top, right) = DrawnExtent(enemy);
        var frame = EnemyKit.ClearPickupsAndDraw(game);
        int width = right - left, filled = (int)(width * enemy.Hp / enemy.MaxHp);
        Assert.Equal(OwnerHp - TwoShots * EnemyKit.PlainShotDamage, enemy.Hp, Places);
        for (int x = 0; x < width; x++) Assert.Equal(x < filled ? Palette.Crimson : Palette.Plum, frame.Get(left + x, RaiderView.BarRow(top)));
    }

    [Fact]
    public void The_bar_follows_hp_as_it_falls()
    {
        var game = EnemyKit.Game();
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { IsBoss = true });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const int Shots = 3;
        const double SmallShot = 5;
        int prev = int.MaxValue;
        for (int shot = 0; shot < Shots; shot++)
        {
            ShootAtAndStep(game, enemy, PodL(enemy), SmallShot);
            var (left, top, right) = DrawnExtent(enemy);
            var frame = EnemyKit.ClearPickupsAndDraw(game);
            int crimson = Enumerable.Range(left, right - left).Count(x => frame.Get(x, RaiderView.BarRow(top)) == Palette.Crimson);
            Assert.Equal((int)((right - left) * enemy.Hp / enemy.MaxHp), crimson);
            Assert.True(crimson < prev);
            prev = crimson;
        }
    }

    [Fact]
    public void The_gunships_health_bar_is_on_the_same_bar_row()
    {
        var game = EnemyKit.Game();
        var gunship = EnemyKit.Place(game, EnemyKind.Gunship, SpotX, SpotY);
        gunship.Hp = gunship.MaxHp / 2;
        var frame = EnemyKit.ClearPickupsAndDraw(game);
        int width = gunship.Sprite.W, x0 = (int)Math.Round(gunship.X - width / 2.0), y0 = RaiderView.BarRow((int)Math.Round(gunship.Y - gunship.Sprite.H / 2.0));
        Assert.Equal(Palette.Crimson, frame.Get(x0, y0));
        Assert.Equal(Palette.Plum, frame.Get(x0 + width - 1, y0));
    }

    [Fact]
    public void A_kinds_own_drawing_runs_after_its_sprite_with_a_canvas_over_the_field()
    {
        var game = EnemyKit.Game();
        const uint Marker = 0xfe12ab;
        const int Above = 30;
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { OnDraw = (canvas, enemy) => canvas.Set((int)enemy.X, (int)enemy.Y - Above, Marker) });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        Assert.Equal(Marker, EnemyKit.ClearPickupsAndDraw(game).Get((int)enemy.X, (int)enemy.Y - Above));
    }

    // ---------------------------------------------------------------- speech

    [Fact]
    public void Say_replaces_a_boxs_text_and_restarts_it()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const int PartRead = 20;
        enemy.Header = "OLD"; enemy.Text = "first words of the box"; enemy.Y = OnFieldY;
        EnemySpeechBox.Update(enemy, game.Layout.PCols, game.Layout.Rows);
        enemy.SpeechBoxAge = PartRead;
        Assert.NotNull(enemy.SpeechLines);
        const string NewHeader = "NEW", NewLine = "second line";
        EnemySpeechBox.Say(enemy, NewHeader, NewLine);
        Assert.Equal(NewHeader, enemy.Header);
        Assert.Equal(NewLine, enemy.Text);
        Assert.Equal(0, enemy.SpeechBoxAge);
        Assert.Null(enemy.SpeechLines);
        EnemySpeechBox.Update(enemy, game.Layout.PCols, game.Layout.Rows);
        EnemySpeechBox.Update(enemy, game.Layout.PCols, game.Layout.Rows);
        Assert.Equal([NewLine], enemy.SpeechLines!);
        Assert.True(EnemySpeechBox.IsOpen(enemy));
    }

    [Fact]
    public void Say_works_on_an_enemy_that_had_no_box()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        Assert.False(EnemySpeechBox.Speaks(enemy));
        EnemySpeechBox.Say(enemy, RaiderGame.TableBehaviour(enemy.Kind)!.Name, "hello");
        Assert.True(EnemySpeechBox.Speaks(enemy));
    }

    /// <summary>Wraps what the enemy says and reads it to the last page, so the next turn is not held behind it.</summary>
    private static void ReadOut(RaiderGame game, Enemy enemy)
    {
        EnemySpeechBox.Update(enemy, game.Layout.PCols, game.Layout.Rows);
        enemy.SpeechBoxAge = EnemySpeechBox.Frames(enemy);
    }

    [Fact]
    public void A_boss_takes_a_waiting_rude_post_on_arriving_and_the_next_one_on_a_part_loss()
    {
        const string SecondPost = "second post of the dark", ThirdPost = "third post of the dark", RudeLabel = "rude-ish";
        var game = EnemyKit.Game();
        game.OfferDarkness(new DarknessPost(DarknessKind.Spam, 1, "first post of the dark", DarknessSource.Label, "scam"));
        game.OfferDarkness(new DarknessPost(DarknessKind.Rude, 2, SecondPost, DarknessSource.Label, RudeLabel));
        game.OfferDarkness(new DarknessPost(DarknessKind.Rude, 3, ThirdPost, DarknessSource.Label, RudeLabel));
        const double WeakHp = 5, StrongHp = 50;   // one shot breaks the first and not the second
        const int HalfSide = 4, Apart = 12;
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark)
        {
            IsBoss = true,
            OnSpawn = (_, enemy) => { enemy.Parts = [EnemyKit.MakePart(PartA, 0, 0, HalfSide, HalfSide, WeakHp), EnemyKit.MakePart(PartB, Apart, 0, HalfSide, HalfSide, StrongHp)]; },
        });
        var boss = game.SpawnKind(EnemyKind.Bulwark, MidField);
        Assert.Null(boss.Text);
        boss.Y = OnFieldY; boss.X = SpotX;
        EnemyKit.StepSweeping(game);
        Assert.Equal("RUDE-ISH", boss.Header);
        Assert.Equal(SecondPost, boss.Text);
        ReadOut(game, boss);
        EnemyKit.Shot(game, boss.X, boss.Y);
        EnemyKit.StepSweeping(game);
        Assert.Equal(PartState.Dead, boss.Parts![0].State);
        Assert.Equal(ThirdPost, boss.Text);
        Assert.Equal(0, boss.SpeechBoxAge);
    }

    [Fact]
    public void A_kind_raises_Phase_for_the_next_voice_and_never_repeats_the_last()
    {
        var game = EnemyKit.Game();
        const int Posts = 3, Cues = 2 * Posts;
        for (ulong id = 1; id <= Posts; id++) game.OfferDarkness(new DarknessPost(DarknessKind.Rude, id, "post " + id, DarknessSource.Label, "gamma"));
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { IsBoss = true });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        var seen = new List<string?>();
        for (int cue = 0; cue < Cues; cue++) { game.Cue(enemy, VoiceCue.Phase); seen.Add(enemy.Text); ReadOut(game, enemy); }
        for (int next = 1; next < seen.Count; next++) Assert.NotEqual(seen[next - 1], seen[next]);
        Assert.Equal(Posts, seen.Distinct().Count());
    }

    [Fact]
    public void With_no_post_to_hand_an_enemy_keeps_its_own_words()
    {
        var game = EnemyKit.Game();
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { IsBoss = true });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const string StockLine = "stock line";
        EnemySpeechBox.Say(enemy, RaiderGame.TableBehaviour(enemy.Kind)!.Name, StockLine);
        game.Cue(enemy, VoiceCue.Phase);
        Assert.Equal(StockLine, enemy.Text);
    }

    // ---------------------------------------------------------------- the table

    [Fact]
    public void Every_new_kind_follows_turret_in_order_and_has_a_table_entry_with_a_short_capital_name()
    {
        var game = EnemyKit.Game();
        EnemyKind[] expected =
        [
            EnemyKind.Gnat, EnemyKind.Brick, EnemyKind.Flanker, EnemyKind.Warden, EnemyKind.Lobber, EnemyKind.Nest, EnemyKind.Sower,
            EnemyKind.Yoke, EnemyKind.Lurk, EnemyKind.Fuse, EnemyKind.Bulwark, EnemyKind.Shutter, EnemyKind.Thread, EnemyKind.Spotlight,
            EnemyKind.Brood, EnemyKind.Schism, EnemyKind.PileOn, EnemyKind.Mirror, EnemyKind.Doomscroll, EnemyKind.Chorus, EnemyKind.Glint,
        ];
        Assert.Equal(expected, EnemyKit.NewKinds);
        Assert.Equal((int)EnemyKind.Turret + 1, (int)EnemyKind.Gnat);
        for (int next = 1; next < expected.Length; next++) Assert.Equal((int)expected[next - 1] + 1, (int)expected[next]);
        foreach (var kind in expected)
        {
            var behaviour = game.BehaviourFor(kind);
            Assert.Equal(kind, behaviour.Kind);
            Assert.False(string.IsNullOrWhiteSpace(behaviour.Name));
            Assert.True(behaviour.Name.Length <= EnemySpeechBox.MaxLabel, kind.ToString());
            Assert.Equal(behaviour.Name.ToUpperInvariant(), behaviour.Name);
        }
    }

    [Fact]
    public void Every_kind_is_in_the_table_and_a_stub_spawns_like_any_kind()
    {
        const int RunFrames = 30;
        var game = EnemyKit.Game();
        foreach (var kind in Enum.GetValues<EnemyKind>())
            Assert.Equal(kind, game.BehaviourFor(kind).Kind);
        foreach (var kind in EnemyKit.NewKinds)
        {
            EnemyKit.Clear(game);
            var enemy = game.SpawnKind(kind, MidField);
            Assert.Equal(kind, enemy.Kind);
            if (!game.BehaviourFor(kind).Boss && kind is not (EnemyKind.Chorus or EnemyKind.Lurk or EnemyKind.Doomscroll)) Assert.Same(game.BehaviourFor(kind).Body, enemy.Sprite);   // a big boss, the made Doomscroll and the chorus draw a body fitted to the field they came in on; the lurk dresses its own
            EnemyKit.StepSweeping(game, RunFrames);
            Assert.Contains(enemy, game.Enemies);
        }
    }

    [Fact]
    public void A_kind_that_adds_parts_in_spawn_gets_hp_from_them_and_the_boss_flag_makes_it_big()
    {
        var game = EnemyKit.Game();
        const double AHp = 7, BHp = 9, AlmostDead = 0.1;
        const int HalfSide = 4, Apart = 10;
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark)
        {
            IsBoss = true,
            OnSpawn = (_, enemy) => enemy.Parts = [EnemyKit.MakePart(PartA, 0, 0, HalfSide, HalfSide, AHp), EnemyKit.MakePart(PartB, Apart, 0, HalfSide, HalfSide, BHp)],
        });
        var enemy = game.SpawnKind(EnemyKind.Bulwark, MidField);
        Assert.Equal(AHp + BHp, enemy.Hp, Places);
        Assert.Equal(AHp + BHp, enemy.MaxHp, Places);
        enemy.Y = OnFieldY;
        EnemyKit.StepSweeping(game);
        Assert.Equal(AHp + BHp, enemy.Hp, Places);
        int booms = 0;
        game.Sfx = new SfxBus((name, _, _, _) => { if (name == SfxNames.Boom) booms++; });
        enemy.Parts![0].Hp = AlmostDead; enemy.Parts[1].Hp = AlmostDead;
        ShootAtAndStep(game, enemy, enemy.Parts[0]); ShootAtAndStep(game, enemy, enemy.Parts[1]);
        Assert.DoesNotContain(enemy, game.Enemies);
        Assert.Equal(1, booms);                            // a boss dies like a gunship
    }

    // ---------------------------------------------------------------- hooks a kind leans on

    [Fact]
    public void Hook_a_part_that_moves_on_its_own_is_hit_where_it_is_now()
    {
        var game = EnemyKit.Game();
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { OnUpdate = (_, enemy) => PodL(enemy).OX += 1 });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const int Frames = 8;
        double was = enemy.X + PodL(enemy).OX;
        EnemyKit.StepSweeping(game, Frames);
        double now = enemy.X + PodL(enemy).OX;
        Assert.Equal(was + Frames, now, Places);
        EnemyKit.Shot(game, was, enemy.Y + PodL(enemy).OY);             // where it stood: beside the box after the move
        EnemyKit.Shot(game, now + 1, enemy.Y + PodL(enemy).OY);         // where it stands on the frame the shot lands, a pixel on
        EnemyKit.StepSweeping(game);
        Assert.Equal(EnemyKit.PodHp - EnemyKit.PlainShotDamage, PodL(enemy).Hp, Places);
        Assert.Single(game.Bullets, bullet => bullet.Friendly);
        Assert.Equal(was, game.Bullets.Single(bullet => bullet.Friendly).X, Places);
    }

    [Fact]
    public void Hook_a_part_hittable_only_in_one_state_follows_the_kinds_timer()
    {
        const int Spell = 10, OpensAfter = 3;
        var game = EnemyKit.Game();
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark)
        {
            OnUpdate = (_, enemy) =>
            {
                var pod = PodL(enemy);
                if (--pod.Timer <= 0) { pod.State = pod.State == PartState.Open ? PartState.Armoured : PartState.Open; pod.Timer = Spell; }
            },
        });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        PodL(enemy).State = PartState.Armoured; PodL(enemy).Timer = OpensAfter;
        ShootAtAndStep(game, enemy, PodL(enemy));
        Assert.Equal(EnemyKit.PodHp, PodL(enemy).Hp, Places);
        EnemyKit.StepSweeping(game, OpensAfter - 1);
        Assert.Equal(PartState.Open, PodL(enemy).State);
        ShootAtAndStep(game, enemy, PodL(enemy));
        Assert.Equal(EnemyKit.PodHp - EnemyKit.PlainShotDamage, PodL(enemy).Hp, Places);
    }

    [Fact]
    public void Hook_a_boss_spawns_adds_even_in_the_middle_of_the_update_loop()
    {
        var game = EnemyKit.Game();
        const double Left = 10, Below = 8;
        Enemy? add = null;
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark)
        {
            OnUpdate = (hookGame, enemy) => { if (add is null) add = hookGame.SpawnAdd(EnemyKind.Gnat, enemy.X - Left, enemy.Y + Below, enemy); },
        });
        var boss = EnemyKit.ScriptedThreeParts(game);
        EnemyKit.StepSweeping(game);                                    // does not throw, though Enemies is being walked
        Assert.NotNull(add);
        Assert.Contains(add!, game.Enemies);
        Assert.Same(boss, add!.Parent);
        Assert.False(add.Lesser);
        Assert.Equal(boss.X - Left, add.X, Places);
        Assert.Equal(boss.Y + Below, add.Y, Places);
        Assert.Equal(1, game.Enemies.Count(enemy => enemy.Kind == EnemyKind.Gnat));
        EnemyKit.StepSweeping(game);
        Assert.Single(game.Enemies, enemy => enemy.Kind == EnemyKind.Gnat);   // nothing duplicated it (the real gnat flies, so its place is not asserted)
    }

    [Fact]
    public void Hook_an_add_born_when_a_part_goes_down_appears_too()
    {
        var game = EnemyKit.Game();
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { OnPartDown = (hookGame, enemy, part) => hookGame.SpawnAdd(EnemyKind.Gnat, enemy.X + part.OX, enemy.Y + part.OY, enemy) });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        ShootAtAndStep(game, enemy, PodL(enemy), EnemyKit.PodHp);
        EnemyKit.StepSweeping(game);
        Assert.Single(game.Enemies, other => other.Kind == EnemyKind.Gnat);
        Assert.Contains(enemy, game.Enemies);
    }

    [Fact]
    public void Hook_an_add_is_a_whole_enemy_that_can_be_shot_and_scores()
    {
        var game = EnemyKit.Game();
        var add = game.SpawnAdd(EnemyKind.Gnat, LeftX, SpotY);
        EnemyKit.StepSweeping(game);
        Assert.Null(add.Parent);
        Assert.False(add.Lesser);
        Assert.True(add.Hp > 0);
        EnemyKit.Shot(game, add.X, add.Y, KillingShot);
        EnemyKit.StepSweeping(game);
        Assert.DoesNotContain(add, game.Enemies);
        Assert.Equal(1, game.Kills);
    }

    [Fact]
    public void Hook_a_shot_sent_back_at_the_ship_leaves_the_part_unhurt_and_hurts_the_ship()
    {
        var game = EnemyKit.Game();
        EnemyKit.OverrideBehaviour(game, new ScriptBehaviour(EnemyKind.Bulwark) { OnPartHit = (hookGame, _, part, bullet, stopped) => { if (stopped) hookGame.Reflect(bullet); } });
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const double AboveShip = 30;
        const int MaxFrames = 60;
        enemy.X = game.PX; enemy.Y = game.PY - AboveShip;
        var shell = Shell(enemy);
        EnemyKit.Shot(game, enemy.X, enemy.Y + shell.OY, HeavyShot);
        EnemyKit.StepSweeping(game);
        var bullet = Assert.Single(game.Bullets);
        Assert.False(bullet.Friendly);
        Assert.True(bullet.Reflected);
        Assert.True(bullet.VY > 0);
        Assert.Equal(EnemyKit.ShellHp, shell.Hp, Places);
        for (int frame = 0; frame < MaxFrames && game.Invuln == 0; frame++) EnemyKit.StepSweeping(game);
        Assert.True(game.Invuln > 0);
    }

    [Fact]
    public void Hook_a_reflected_shot_turns_hostile_and_is_aimed_at_the_ship_from_where_it_is()
    {
        const double FromX = 20, FromY = 30, RisingSpeed = -3, AnyDamage = 5;
        var game = EnemyKit.Game();
        var bullet = new Bullet { X = FromX, Y = FromY, VY = RisingSpeed, Dmg = AnyDamage, Friendly = true };
        game.Bullets.Add(bullet);
        game.Reflect(bullet);
        Assert.False(bullet.Friendly);
        Assert.True(bullet.Reflected);
        double dx = game.PX - FromX, dy = game.PY - FromY, distance = Math.Sqrt(dx * dx + dy * dy);
        Assert.Equal(dx / distance, bullet.VX / Math.Sqrt(bullet.VX * bullet.VX + bullet.VY * bullet.VY), Places);
        Assert.Equal(dy / distance, bullet.VY / Math.Sqrt(bullet.VX * bullet.VX + bullet.VY * bullet.VY), Places);
        Assert.Equal(Tuning.EnemyBoltSpeed, Math.Sqrt(bullet.VX * bullet.VX + bullet.VY * bullet.VY), Places);
    }

    [Fact]
    public void Hook_a_full_width_wall_of_bricks_has_no_part_ceiling_and_is_hit_brick_by_brick_on_every_field()
    {
        const int Spacing = 3, AtLeastBricks = 20, ManyParts = 200, BrickHalfW = 1, BrickHalfH = 3;
        const double BrickHp = 8, Chip = 3, WallY = 20;
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
        {
            var game = EnemyKit.Game(cols, rows);
            int bricksAcross = game.PW / Spacing;
            double midX = game.PW / 2.0;
            var bricks = Enumerable.Range(0, bricksAcross).Select(column => EnemyKit.MakePart("brick" + column, column * Spacing + Spacing / 2.0 - midX, 0, BrickHalfW, BrickHalfH, BrickHp, vital: column % 2 == 0)).ToArray();
            var wall = game.SpawnParts(EnemyKind.Brick, midX, WallY, bricks);
            Assert.True(wall.Parts!.Count >= AtLeastBricks, $"{cols}x{rows}");
            Assert.True(EnemyKit.InsideWidth(game, wall));
            int hit = bricksAcross / 2;
            ShootAtAndStep(game, wall, wall.Parts[hit], Chip);
            Assert.Equal(BrickHp - Chip, wall.Parts[hit].Hp, Places);
            Assert.Equal(1, wall.Parts.Count(part => part.Hp < part.MaxHp));
        }
        const double HugeX = 64, TinyHp = 1;
        const int TinyHalf = 1;
        var big = EnemyKit.Game();
        var many = Enumerable.Range(0, ManyParts).Select(slot => EnemyKit.MakePart("m" + slot, slot - ManyParts / 2, 0, TinyHalf, TinyHalf, TinyHp)).ToArray();
        var huge = big.SpawnParts(EnemyKind.Brick, HugeX, SpotY, many);
        Assert.Equal(ManyParts, huge.Parts!.Count);
        Assert.Equal(ManyParts * TinyHp, huge.Hp, Places);            // every part made by MakePart is vital
    }

    [Fact]
    public void Hook_a_size_the_kind_chooses_from_the_field_is_never_clipped_by_the_machinery()
    {
        const int SideMargins = 8, LeastHeight = 20, HeightShare = 3;
        const double BodyHp = 30, TopMargin = 6;
        const uint BodyGrey = 0x303030;
        const int Inset = 1;
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
        {
            var game = EnemyKit.Game(cols, rows);
            int bodyW = game.PW - SideMargins, bodyH = Math.Max(LeastHeight, game.PH / HeightShare);   // most of the field, so any clip by the machinery would show
            var body = EnemyKit.MakePart("body", 0, 0, bodyW / 2, bodyH / 2, BodyHp, closed: EnemyKit.Solid(bodyW, bodyH, BodyGrey));
            var enemy = game.SpawnParts(EnemyKind.Yoke, game.PW / 2.0, TopMargin + bodyH / 2.0, body);   // the yoke leaves a body it is given alone
            Assert.True(EnemyKit.InsideWidth(game, enemy));
            var frame = EnemyKit.ClearPickupsAndDraw(game, scr: new Screen(cols, rows));   // drawn before any hit, so no flash is on the art
            uint middle = frame.Get((int)Math.Round(enemy.X), (int)Math.Round(enemy.Y)), leftEdge = frame.Get((int)Math.Round(enemy.X - bodyW / 2.0) + Inset, (int)Math.Round(enemy.Y));
            double bottom = enemy.Y + bodyH / 2.0;
            EnemyKit.Shot(game, enemy.X + bodyW / 2.0 - Inset, bottom - Inset);   // the far lower corner of the box
            EnemyKit.Shot(game, enemy.X - bodyW / 2.0 + Inset, enemy.Y - bodyH / 2.0 + Inset);
            EnemyKit.StepSweeping(game);
            Assert.Equal(BodyHp - TwoShots * EnemyKit.PlainShotDamage, body.Hp, Places);
            Assert.Equal(BodyGrey, middle);
            Assert.Equal(BodyGrey, leftEdge);
        }
    }

    [Fact]
    public void Hook_phases_that_change_the_silhouette_swap_parts_art_and_hp_together()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const int HalfW = 20, HalfH = 4;
        const double WideHp = 60;
        const uint WideColour = 0x445566;
        var wide = EnemyKit.MakePart("wide", 0, 0, HalfW, HalfH, WideHp, closed: EnemyKit.Solid(2 * HalfW + 1, 2 * HalfH + 1, WideColour));
        game.SetParts(enemy, wide);
        Assert.Single(enemy.Parts!);
        Assert.Equal(WideHp, enemy.Hp, Places);
        Assert.Equal(WideHp, enemy.MaxHp, Places);
        EnemyKit.Shot(game, enemy.X + HalfW - 1, enemy.Y);              // in the new box, outside every old one
        EnemyKit.StepSweeping(game);
        Assert.Equal(WideHp - EnemyKit.PlainShotDamage, wide.Hp, Places);
        EnemyKit.StepSweeping(game, Tuning.FlashFrames + 1);   // let the hit flash fade, so the plain art shows
        Assert.Equal(WideColour, CentrePixel(EnemyKit.ClearPickupsAndDraw(game), enemy, wide, wide.Closed));
        Assert.Equal(WideColour, EnemyKit.ClearPickupsAndDraw(game).Get((int)Math.Round(enemy.X) + HalfW - 1, (int)Math.Round(enemy.Y)));
        Assert.Equal(WideHp - EnemyKit.PlainShotDamage, enemy.Hp, Places);
    }

    [Fact]
    public void An_enemy_with_a_body_sprite_shows_it_under_its_parts()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.Place(game, EnemyKind.Gnat, LeftX, SpotY);
        var frame = EnemyKit.ClearPickupsAndDraw(game);
        int x0 = (int)Math.Round(enemy.X - enemy.Sprite.W / 2.0), y0 = (int)Math.Round(enemy.Y - enemy.Sprite.H / 2.0);
        int shown = 0;
        for (int at = 0; at < enemy.Sprite.Px.Length; at++)
            if (enemy.Sprite.Px[at] != 0) { Assert.Equal(enemy.Sprite.Px[at] & EnemyKit.RgbMask, frame.Get(x0 + at % enemy.Sprite.W, y0 + at / enemy.Sprite.W)); shown++; }
        Assert.True(shown > 0);
    }

    [Fact]
    public void Hook_a_dead_part_can_be_revived_and_counts_again()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const double Revived = 15;
        ShootAtAndStep(game, enemy, PodL(enemy), EnemyKit.PodHp);
        Assert.Equal(OwnerHp - EnemyKit.PodHp, enemy.Hp, Places);
        game.Revive(enemy, PodL(enemy), Revived);
        Assert.Equal(PartState.Open, PodL(enemy).State);
        Assert.Equal(Revived, PodL(enemy).Hp, Places);
        EnemyKit.StepSweeping(game);
        Assert.Equal(OwnerHp - EnemyKit.PodHp + Revived, enemy.Hp, Places);
        ShootAtAndStep(game, enemy, PodL(enemy), Revived);
        Assert.Equal(PartState.Dead, PodL(enemy).State);
    }

    [Fact]
    public void Hook_the_kinds_private_state_and_mode_live_on_the_enemy()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        enemy.Kit = new List<int> { 1, 2 };
        const int Frames = 5, SomeMode = 2, NextMode = 3;
        enemy.Mode = SomeMode;
        EnemyKit.StepSweeping(game, Frames);
        Assert.Equal(SomeMode, enemy.Mode);
        Assert.Equal(Frames, enemy.ModeAge);
        enemy.ModeAge = 0; enemy.Mode = NextMode;
        EnemyKit.StepSweeping(game);
        Assert.Equal(1, enemy.ModeAge);
        Assert.IsType<List<int>>(enemy.Kit);
    }

    [Fact]
    public void An_enemy_with_parts_that_leaves_the_field_is_removed_by_its_own_size()
    {
        var game = EnemyKit.Game();
        var enemy = EnemyKit.ScriptedThreeParts(game);
        const double BelowTheField = 40;
        enemy.Y = game.PH + BelowTheField;
        EnemyKit.StepSweeping(game);
        Assert.DoesNotContain(enemy, game.Enemies);
    }

    [Fact]
    public void A_kind_is_found_through_the_override_and_the_table_is_used_without_one()
    {
        var game = EnemyKit.Game();
        Assert.Null(game.BehaviourOverride);
        var table = game.BehaviourFor(EnemyKind.Bulwark);
        var script = new ScriptBehaviour(EnemyKind.Bulwark);
        EnemyKit.OverrideBehaviour(game, script);
        Assert.Same(script, game.BehaviourFor(EnemyKind.Bulwark));
        Assert.Same(game.BehaviourFor(EnemyKind.Gnat), RaiderGame.TableBehaviour(EnemyKind.Gnat));
        game.BehaviourOverride = null;
        Assert.Same(table, game.BehaviourFor(EnemyKind.Bulwark));
    }
}
