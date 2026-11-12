using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The fire cadence and the sidestep: fewer, slower player shots; enemies that slip a lined-up shot,
// but only after a visible tell.
public class EvasionTests
{
    private static RaiderGame NewGame()
    {
        var g = new RaiderGame(7) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(120, 46));
        return g;
    }

    private static Enemy Place(RaiderGame g, EnemyKind kind, double x, double y)
    {
        g.Enemies.Clear();
        var sprite = kind == EnemyKind.Gunship ? Art.Gunship : kind == EnemyKind.Carrier ? Art.Carrier : kind == EnemyKind.Mine ? Art.Mine : Art.Grunt;
        var e = new Enemy { Kind = kind, Sprite = sprite, X = x, BaseX = x, Y = y, Hp = 4, MaxHp = 4, Windup = -1 };
        g.Enemies.Add(e);
        return e;
    }

    /// <summary>A step with nothing else on the field: waves are cleared and the ship is out of the way.</summary>
    private static void Step(RaiderGame g, Enemy keep)
    {
        g.Enemies.RemoveAll(x => x != keep);
        g.Step();
    }

    private static Bullet Shot(RaiderGame g, double x, double y)
    {
        var b = new Bullet { X = x, Y = y, VY = -Tuning.PlayerBulletSpeed, Dmg = 1, Friendly = true };
        g.Bullets.Add(b);
        return b;
    }

    [Fact]
    public void Fire_cadence_is_slow_and_a_held_trigger_makes_one_volley_per_interval()
    {
        Assert.True(Tuning.FireInterval >= 6);
        Assert.True(Tuning.FireIntervalMax >= 5 && Tuning.FireIntervalMax <= Tuning.FireInterval);
        Assert.True(Tuning.PlayerBulletSpeed < 3.2);
        var g = NewGame();
        g.Input.Key(GameKey.Fire, true);
        int volleys = 0;
        var seen = new HashSet<Bullet>();
        for (int i = 0; i < 70; i++)
        {
            g.Enemies.Clear();
            g.Step();
            if (g.Bullets.Where(b => b.Friendly).Count(seen.Add) > 0) volleys++;   // a frame that made new bullets is a volley
        }
        Assert.Equal(70 / Tuning.FireInterval, volleys);
    }

    [Fact]
    public void More_streams_add_little_damage_per_second_and_the_top_gun_is_not_a_wall_of_bullets()
    {
        double Dps(int lvl) => RaiderGame.WeaponPattern(lvl).Sum(s => s.Dmg) / RaiderGame.FireIntervalFor(lvl) * Tuning.Fps;
        Assert.True(Dps(4) <= Dps(1) * 1.8, $"five streams do {Dps(4):0.0}/s against {Dps(1):0.0}/s for one");
        Assert.True(Dps(2) <= Dps(1) * 1.3);
        Assert.True(Dps(1) <= 10);   // one stream: 2 damage every 7 frames, about 8.6 a second
        // the outer streams are weak and wide: they chip, and a mover slips them
        var l4 = RaiderGame.WeaponPattern(4);
        Assert.True(l4.Where(s => Math.Abs(s.Angle) > 15).All(s => s.Dmg <= 0.5));
        Assert.True(l4.Max(s => Math.Abs(s.Angle)) >= 20);
    }

    [Fact]
    public void A_lined_up_shot_makes_a_grunt_shiver_first_and_it_does_not_move_during_the_tell()
    {
        var g = NewGame();
        var e = Place(g, EnemyKind.Grunt, 40, 30);
        double x0 = e.BaseX;
        Shot(g, 41, 30 + 30);
        Step(g, e);
        Assert.True(e.EvadeWind > 0, "it saw the shot");
        for (int i = 1; i < Tuning.EvadeWindupFrames - 1; i++)
        {
            Step(g, e);
            Assert.Equal(x0, e.BaseX, 6);                         // no instant dodge: the base position holds during the tell
        }
    }

    [Fact]
    public void The_grunt_slides_away_from_the_shot_and_slips_it()
    {
        var g = NewGame();
        var e = Place(g, EnemyKind.Grunt, 40, 30);
        Shot(g, 41, 30 + 40);                                     // slightly to the right of it: it goes left
        for (int i = 0; i < 40; i++) Step(g, e);
        Assert.True(e.BaseX < 40 - e.Sprite.W / 2.0, $"slid to {e.BaseX}");
        Assert.Equal(4, e.Hp);                                    // the shot flew past
        Assert.True(e.EvadeCd > 0);
    }

    [Fact]
    public void A_shot_already_on_top_of_it_still_lands_and_a_shot_off_to_the_side_is_ignored()
    {
        var g = NewGame();
        var e = Place(g, EnemyKind.Grunt, 40, 30);
        Shot(g, 40, 30 + e.Sprite.H / 2.0 + 6);                   // 6 px from its bottom: 2 frames away, less than the tell
        for (int i = 0; i < 6; i++) Step(g, e);
        Assert.True(e.Hp < 4, "too close to dodge");
        var f = Place(g, EnemyKind.Grunt, 40, 30);
        Shot(g, 40 + f.Sprite.W / 2.0 + Tuning.EvadeLane + 3, 30 + 30);
        Step(g, f);
        Assert.False(f.Evading);
    }

    [Fact]
    public void It_cannot_sidestep_again_until_its_cooldown_ends_and_the_cooldown_shortens_with_the_stage()
    {
        Assert.True(Ramp.EvadeCooldown(10) < Ramp.EvadeCooldown(1));
        Assert.Equal(Tuning.EvadeCooldownStart, Ramp.EvadeCooldown(1));
        Assert.Equal(Tuning.EvadeCooldownCap, Ramp.EvadeCooldown(Tuning.StageCap));
        var g = NewGame();
        var e = Place(g, EnemyKind.Grunt, 40, 20);
        Shot(g, 40, 20 + 30);
        for (int i = 0; i < Tuning.EvadeWindupFrames + Tuning.EvadeSlideFrames + 2; i++) Step(g, e);
        Assert.False(e.Evading);
        Shot(g, e.X, e.Y + 30);
        Step(g, e);
        Assert.False(e.Evading, "still cooling down");
    }

    [Fact]
    public void Only_the_evasive_kinds_dodge_and_nothing_dodges_before_it_is_on_screen()
    {
        foreach (var kind in new[] { EnemyKind.Mine, EnemyKind.Gunship, EnemyKind.Turret })
        {
            var g = NewGame();
            var e = Place(g, kind, 40, 24);
            Shot(g, 40, 24 + 30);
            Step(g, e);
            Assert.False(e.Evading, kind.ToString());
        }
        foreach (var kind in new[] { EnemyKind.Grunt, EnemyKind.Carrier, EnemyKind.Splitter })
        {
            var g = NewGame();
            var e = Place(g, kind, 40, 24);
            Shot(g, 40, 24 + 30);
            Step(g, e);
            Assert.True(e.Evading, kind.ToString());
        }
        var h = NewGame();
        var above = Place(h, EnemyKind.Grunt, 40, -4);
        Shot(h, 40, 30);
        Step(h, above);
        Assert.False(above.Evading);
    }

    [Fact]
    public void A_sidestep_never_takes_an_enemy_off_the_edge_of_the_field()
    {
        var g = NewGame();
        double nearLeftEdge = Art.Grunt.W / 2.0 + 1;
        var e = Place(g, EnemyKind.Grunt, nearLeftEdge, 30);
        Shot(g, e.X + 1, 30 + 30);                                // right of it, so it wants to go left: into the wall
        for (int i = 0; i < 40; i++) Step(g, e);
        Assert.InRange(e.BaseX, e.Sprite.W / 2.0, g.PW - e.Sprite.W / 2.0);
        Assert.Contains(e, g.Enemies);
    }

    [Fact]
    public void The_enemy_kinds_fall_and_drift_at_least_as_fast_as_their_floor_speeds()
    {
        Assert.True(Tuning.EnemyFallMin >= 0.7);
        Assert.True(Tuning.DiverFall > 0.75 && Tuning.DiverDashSpeed > 2.4 && Tuning.SplitterFall > 0.4 && Tuning.MineDrift > 0.32);
        var g = NewGame();
        g.Step();
        for (int i = 0; i < 40; i++)
        {
            var e = g.SpawnKind(EnemyKind.Grunt, 0.5);
            Assert.InRange(e.VY, Tuning.EnemyFallMin, Tuning.EnemyFallMin + Tuning.EnemyFallJitter);
        }
    }
}
