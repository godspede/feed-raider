using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The fire cadence and the sidestep: a slow trigger and slow ship shots; enemies that slip a lined-up shot,
// but only after a visible tell.
public class EvasionTests
{
    /// <summary>The seed of every game here.</summary>
    private const ulong Seed = 7;

    /// <summary>The frames the held trigger is watched for.</summary>
    private const int WatchFrames = 70;

    /// <summary>The decimal places a position is compared to.</summary>
    private const int Places = 6;

    /// <summary>The hit points of an enemy <see cref="PlaceAlone"/> puts on the field.</summary>
    private const int PlacedHp = 4;

    /// <summary>The column and row an enemy is placed on, well inside the field.</summary>
    private const double EnemyX = 40, EnemyY = 30;

    /// <summary>How far below an enemy a lined-up shot starts: far enough that the whole tell passes before it arrives.</summary>
    private const double ShotBelow = 30;

    /// <summary>How far right of an enemy's centre a shot flies so that the enemy slides left.</summary>
    private const double OffCentre = 1;

    /// <summary>The frames a sidestep is watched for: the tell, the slide and the shot flying past.</summary>
    private const int SidestepFrames = 40;

    private static RaiderGame NewGame()
    {
        var g = new RaiderGame(Seed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        return g;
    }

    private static Enemy PlaceAlone(RaiderGame g, EnemyKind kind, double x, double y)
    {
        g.Enemies.Clear();
        var sprite = kind switch
        {
            EnemyKind.Gunship => Art.Gunship,
            EnemyKind.Carrier => Art.Carrier,
            EnemyKind.Mine => Art.Mine,
            _ => Art.Grunt,
        };
        var e = new Enemy { Kind = kind, Sprite = sprite, X = x, BaseX = x, Y = y, Hp = PlacedHp, MaxHp = PlacedHp, Windup = Enemy.WindupNotBegun };
        g.Enemies.Add(e);
        return e;
    }

    /// <summary>Removes every enemy but <paramref name="keep"/>, then steps, so no other enemy acts in that step.</summary>
    private static void StepKeeping(RaiderGame g, Enemy keep)
    {
        g.Enemies.RemoveAll(e => e != keep);
        g.Step();
    }

    /// <summary>The damage of the ship shot <see cref="AddShot"/> puts on the field: too little to kill a placed enemy.</summary>
    private const double WeakShotDamage = 1;

    /// <summary>Puts one weak ship shot at (<paramref name="x"/>, <paramref name="y"/>), flying up.</summary>
    private static void AddShot(RaiderGame g, double x, double y) =>
        g.Bullets.Add(new Bullet { X = x, Y = y, VY = -Tuning.ShipShotSpeed, Dmg = WeakShotDamage, Friendly = true });

    [Fact]
    public void Fire_cadence_is_slow_and_a_held_trigger_makes_one_volley_per_interval()
    {
        // the first weapon's trigger stays slow so that aim and timing matter: at most five volleys a second, and no kind past
        // ten, where a gun chatters rather than streams
        const int MaxVolleysPerSecond = 5, QuickestKindVolleysPerSecond = 10;
        // pixels a frame: a shot slow enough that a moving enemy can slip it
        const double ShotSpeedCeiling = 3.2;
        Assert.True(Tuning.FireInterval >= Tuning.Fps / MaxVolleysPerSecond);
        Assert.All(Weapons.Kinds, kind => Assert.True(kind.Interval >= Tuning.Fps / QuickestKindVolleysPerSecond, kind.Name));
        Assert.True(Tuning.ShipShotSpeed < ShotSpeedCeiling);
        var g = NewGame();
        g.Input.Key(GameKey.Fire, true);
        int volleys = 0;
        var seen = new HashSet<Bullet>();
        for (int i = 0; i < WatchFrames; i++)
        {
            g.Enemies.Clear();
            g.Step();
            int before = seen.Count;
            seen.UnionWith(g.Bullets.Where(b => b.Friendly));
            if (seen.Count > before) volleys++;
        }
        Assert.Equal(WatchFrames / Tuning.FireInterval, volleys);
    }

    [Fact]
    public void The_first_weapon_stays_slow_and_the_spread_is_a_fan_whose_outer_shots_only_chip()
    {
        const int FirstLevel = 1;
        // one stream does at most this much damage a second, so dodging beats firepower at the start of a run
        const double MaxOneStreamDps = 10;
        // degrees off straight up: a shot wider than the first angle is an outer one, which does at most the damage given,
        // and the widest flies at least the last angle out
        const double OuterAngle = 15, MaxOuterDmg = 0.5, MinWidestAngle = 20;
        double firstDps = Weapons.At(FirstLevel).Shots.Sum(s => s.Dmg) / Weapons.At(FirstLevel).Interval * Tuning.Fps;
        Assert.True(firstDps <= MaxOneStreamDps, $"the first weapon does {firstDps:0.0}/s");
        var spread = Weapons.Kinds.Single(k => k.Look == ShotLook.Spread).Shots;
        Assert.True(spread.Where(s => Math.Abs(s.Angle) > OuterAngle).All(s => s.Dmg <= MaxOuterDmg));
        Assert.True(spread.Max(s => Math.Abs(s.Angle)) >= MinWidestAngle);
    }

    [Fact]
    public void A_lined_up_shot_makes_a_grunt_shiver_first_and_it_does_not_move_during_the_tell()
    {
        var g = NewGame();
        var e = PlaceAlone(g, EnemyKind.Grunt, EnemyX, EnemyY);
        double startX = e.BaseX;
        AddShot(g, EnemyX + OffCentre, EnemyY + ShotBelow);
        StepKeeping(g, e);
        Assert.True(e.EvadeWind > 0, "it saw the shot");
        for (int i = 1; i < Tuning.EvadeWindupFrames - 1; i++)
        {
            StepKeeping(g, e);
            Assert.Equal(startX, e.BaseX, Places);
        }
    }

    [Fact]
    public void The_grunt_slides_away_from_the_shot_and_slips_it()
    {
        const double FarShotBelow = 40;
        var g = NewGame();
        var e = PlaceAlone(g, EnemyKind.Grunt, EnemyX, EnemyY);
        AddShot(g, EnemyX + OffCentre, EnemyY + FarShotBelow);       // slightly to the right of it: it goes left
        for (int i = 0; i < SidestepFrames; i++) StepKeeping(g, e);
        Assert.True(e.BaseX < EnemyX - e.Sprite.W / 2.0, $"slid to {e.BaseX}");
        Assert.Equal(PlacedHp, e.Hp);                             // the shot flew past
        Assert.True(e.EvadeCd > 0);
    }

    [Fact]
    public void A_shot_already_on_top_of_it_still_lands_and_a_shot_off_to_the_side_is_ignored()
    {
        const double CloseGap = 6, PastLane = 3;
        const int LandFrames = 6;
        var g = NewGame();
        var e = PlaceAlone(g, EnemyKind.Grunt, EnemyX, EnemyY);
        AddShot(g, EnemyX, EnemyY + e.Sprite.H / 2.0 + CloseGap);   // just under its bottom: 2 frames away, less than the tell
        for (int i = 0; i < LandFrames; i++) StepKeeping(g, e);
        Assert.True(e.Hp < PlacedHp, "too close to dodge");
        var wide = PlaceAlone(g, EnemyKind.Grunt, EnemyX, EnemyY);
        AddShot(g, EnemyX + wide.Sprite.W / 2.0 + Tuning.EvadeLane + PastLane, EnemyY + ShotBelow);
        StepKeeping(g, wide);
        Assert.False(wide.Evading);
    }

    [Fact]
    public void It_cannot_sidestep_again_until_its_cooldown_ends_and_the_cooldown_shortens_with_the_stage()
    {
        const int FirstStage = 1, SettleFrames = 2;
        const double Row = 20;
        Assert.True(Ramp.EvadeCooldown(Tuning.StageCap) < Ramp.EvadeCooldown(FirstStage));
        Assert.Equal(Tuning.EvadeCooldownStart, Ramp.EvadeCooldown(FirstStage));
        Assert.Equal(Tuning.EvadeCooldownCap, Ramp.EvadeCooldown(Tuning.StageCap));
        var g = NewGame();
        var e = PlaceAlone(g, EnemyKind.Grunt, EnemyX, Row);
        AddShot(g, EnemyX, Row + ShotBelow);
        for (int i = 0; i < Tuning.EvadeWindupFrames + Tuning.EvadeSlideFrames + SettleFrames; i++) StepKeeping(g, e);
        Assert.False(e.Evading);
        AddShot(g, e.X, e.Y + ShotBelow);
        StepKeeping(g, e);
        Assert.False(e.Evading, "still cooling down");
    }

    [Fact]
    public void Only_the_evasive_kinds_dodge_and_nothing_dodges_before_it_is_on_screen()
    {
        const double Row = 24, AboveField = -4;
        foreach (var kind in new[] { EnemyKind.Mine, EnemyKind.Gunship, EnemyKind.Turret })
        {
            var g = NewGame();
            var e = PlaceAlone(g, kind, EnemyX, Row);
            AddShot(g, EnemyX, Row + ShotBelow);
            StepKeeping(g, e);
            Assert.False(e.Evading, kind.ToString());
        }
        foreach (var kind in new[] { EnemyKind.Grunt, EnemyKind.Carrier, EnemyKind.Splitter })
        {
            var g = NewGame();
            var e = PlaceAlone(g, kind, EnemyX, Row);
            AddShot(g, EnemyX, Row + ShotBelow);
            StepKeeping(g, e);
            Assert.True(e.Evading, kind.ToString());
        }
        var offScreen = NewGame();
        var above = PlaceAlone(offScreen, EnemyKind.Grunt, EnemyX, AboveField);
        AddShot(offScreen, EnemyX, EnemyY);
        StepKeeping(offScreen, above);
        Assert.False(above.Evading);
    }

    [Fact]
    public void A_sidestep_never_takes_an_enemy_off_the_edge_of_the_field()
    {
        const double EdgeGap = 1;
        var g = NewGame();
        double nearLeftEdge = Art.Grunt.W / 2.0 + EdgeGap;
        var e = PlaceAlone(g, EnemyKind.Grunt, nearLeftEdge, EnemyY);
        AddShot(g, e.X + OffCentre, EnemyY + ShotBelow);             // right of it, so it wants to go left: into the wall
        for (int i = 0; i < SidestepFrames; i++) StepKeeping(g, e);
        Assert.InRange(e.BaseX, e.Sprite.W / 2.0, g.PW - e.Sprite.W / 2.0);
        Assert.Contains(e, g.Enemies);
    }

    [Fact]
    public void The_enemy_kinds_fall_and_drift_at_least_as_fast_as_their_floor_speeds()
    {
        const int Arrivals = 40;
        const double MidField = 0.5;
        // px a frame: the slowest each kind may move and still press the ship; a diver falls faster than a grunt
        const double FallFloor = 0.7, DashFloor = 2.4, SplitterFallFloor = 0.4, MineDriftFloor = 0.32;
        Assert.True(Tuning.EnemyFallMin >= FallFloor);
        Assert.True(Tuning.DiverFall > Tuning.EnemyFallMin && Tuning.DiverDashSpeed > DashFloor && Tuning.SplitterFall > SplitterFallFloor && Tuning.MineDrift > MineDriftFloor);
        var g = NewGame();
        g.Step();
        for (int i = 0; i < Arrivals; i++)
        {
            var e = g.SpawnKind(EnemyKind.Grunt, MidField);
            Assert.InRange(e.VY, Tuning.EnemyFallMin, Tuning.EnemyFallMin + Tuning.EnemyFallJitter);
        }
    }
}
