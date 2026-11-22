using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;
using Xunit.Abstractions;

namespace FeedRaider.Tests;

/// <summary>
/// The weapon ramp, measured the way a player meets it: the ship holds fire at a still dummy and the test counts what lands.
/// Each pickup is a step up the ramp that a player can feel but that never turns a fight into a walk-over.
/// </summary>
public class WeaponRampTests(ITestOutputHelper output)
{
    /// <summary>A grunt-sized dummy: as wide as a grunt in pixels, and how far ahead of the ship it holds.</summary>
    private const int DummyWidth = 24, DummyHeight = 8, DummyAhead = 50;
    /// <summary>A dummy as wide as a boss, which catches the shots a fan sends wide of a grunt.</summary>
    private const int WideDummyWidth = 64;
    /// <summary>Hit points no weapon gets through in a test, so the dummy never falls.</summary>
    private const double EndlessHp = 1e9;
    /// <summary>How long the ship fires at the dummy, counted from the first hit.</summary>
    private const int FireSeconds = 10;
    /// <summary>Frames to wait for the first hit, and for a single volley's last missile to land.</summary>
    private const int FirstHitWithin = 90, VolleyLandsWithin = 90;
    /// <summary>Frames of held fire over which the gap between volleys is read.</summary>
    private const int CadenceFrames = 60;
    private const uint DummyColour = 0x808080;

    private static readonly int[] Levels = Enumerable.Range(1, Tuning.MaxWeaponLevel).ToArray();

    /// <summary>A fresh game with the ship at <paramref name="level"/> and one still dummy straight ahead of it.</summary>
    private static (RaiderGame Game, Enemy Dummy) Range(int level, int width = DummyWidth, double hp = EndlessHp)
    {
        var game = EnemyKit.Game();
        while (game.WeaponLevel < level) game.DebugStepWeapon();
        var still = new ScriptBehaviour(EnemyKind.Gnat);
        double x = game.PX, y = game.PY - DummyAhead;
        still.OnUpdate = (_, e) => { e.X = e.BaseX = x; e.Y = y; e.KX = e.KY = 0; };
        EnemyKit.OverrideBehaviour(game, still);
        var dummy = EnemyKit.Place(game, EnemyKind.Gnat, x, y);
        dummy.Sprite = EnemyKit.Solid(width, DummyHeight, DummyColour);
        dummy.Hp = dummy.MaxHp = hp;
        return (game, dummy);
    }

    /// <summary>One frame with only the dummy and the ship's own shots on the field.</summary>
    private static void Step(RaiderGame game, Enemy dummy)
    {
        game.Step();
        game.Enemies.RemoveAll(e => e != dummy);
        game.Bullets.RemoveAll(b => !b.Friendly);
        game.Pickups.Clear();
    }

    /// <summary>Damage a second at the dummy under a held trigger, over <see cref="FireSeconds"/> from the first hit.</summary>
    private static double Dps(int level, int width = DummyWidth)
    {
        var (game, dummy) = Range(level, width);
        game.Input.Key(GameKey.Fire, true);
        for (int f = 0; f < FirstHitWithin && dummy.Hp >= EndlessHp; f++) Step(game, dummy);
        Assert.True(dummy.Hp < EndlessHp, $"level {level} never hit the dummy");
        for (int f = 1; f < FireSeconds * Tuning.Fps; f++) Step(game, dummy);
        return (EndlessHp - dummy.Hp) / FireSeconds;
    }

    /// <summary>All the damage one tap of the trigger does to the dummy, once every shot of the volley has landed.</summary>
    private static double VolleyDamage(int level)
    {
        var (game, dummy) = Range(level);
        game.Input.Key(GameKey.Fire, true);
        Step(game, dummy);
        game.Input.Key(GameKey.Fire, false);
        for (int f = 0; f < VolleyLandsWithin; f++) Step(game, dummy);
        return EndlessHp - dummy.Hp;
    }

    /// <summary>The ship's shots fired over a held trigger, as (frame, shots) for each frame that fired.</summary>
    private static List<(int Frame, List<Bullet> Shots)> Volleys(int level, Action<RaiderGame>? setup = null)
    {
        var (game, dummy) = Range(level);
        setup?.Invoke(game);
        game.Input.Key(GameKey.Fire, true);
        var seen = new HashSet<Bullet>();
        var volleys = new List<(int, List<Bullet>)>();
        for (int f = 0; f < CadenceFrames; f++)
        {
            Step(game, dummy);
            var fresh = game.Bullets.Where(b => b.Friendly && seen.Add(b)).ToList();
            if (fresh.Count > 0) volleys.Add((f, fresh));
        }
        return volleys;
    }

    /// <summary>Frames between volleys under a held trigger.</summary>
    private static int Interval(int level)
    {
        var frames = Volleys(level).Select(v => v.Frame).ToArray();
        var gaps = frames.Zip(frames.Skip(1), (a, b) => b - a).Distinct().ToArray();
        Assert.Single(gaps);
        return gaps[0];
    }

    [Fact]
    public void Each_level_is_a_modest_step_over_the_last()
    {
        // a pickup is a step a player feels, never a jump that makes the last one look broken
        const double LeastStep = 1.10, MostStep = 1.35;
        var dps = Levels.Select(level => Dps(level)).ToArray();
        for (int i = 0; i < dps.Length; i++) output.WriteLine($"level {Levels[i]} {Weapons.At(Levels[i]).Name}: {dps[i]:0.00}/s" + (i > 0 ? $" x{dps[i] / dps[i - 1]:0.000}" : ""));
        for (int i = 1; i < dps.Length; i++)
            Assert.InRange(dps[i] / dps[i - 1], LeastStep, MostStep);
    }

    [Fact]
    public void No_level_is_weaker_than_the_one_below_on_a_target_as_wide_as_a_boss()
    {
        var dps = Levels.Select(level => Dps(level, WideDummyWidth)).ToArray();
        for (int i = 0; i < dps.Length; i++) output.WriteLine($"level {Levels[i]}: {dps[i]:0.00}/s on a wide target");
        for (int i = 1; i < dps.Length; i++)
            Assert.True(dps[i] > dps[i - 1], $"level {Levels[i]} does {dps[i]:0.00}/s, under level {Levels[i - 1]}'s {dps[i - 1]:0.00}/s");
    }

    [Fact]
    public void Each_level_is_a_different_kind_of_shot()
    {
        // what a player sees of a kind: how its shots look, how many fly at once, how quickly they come and whether any home
        var signatures = Levels.Select(level =>
        {
            var volleys = Volleys(level);
            var shots = volleys.SelectMany(v => v.Shots).ToArray();
            string looks = string.Join("+", shots.Select(b => b.Look).Distinct().Order());
            return $"{looks} x{volleys.Max(v => v.Shots.Count)} every {Interval(level)} homing {shots.Any(b => b.Homing)}";
        }).ToArray();
        for (int i = 0; i < signatures.Length; i++) output.WriteLine($"level {Levels[i]}: {signatures[i]}");
        Assert.Equal(signatures.Length, signatures.Distinct().Count());
        Assert.Equal(Levels.Length, Levels.Select(level => Weapons.At(level).Name).Distinct().Count());
    }

    [Fact]
    public void Every_level_sounds_different()
    {
        var sounds = Levels.Select(level =>
        {
            var played = new HashSet<string>();
            Volleys(level, game => game.Sfx = new SfxBus((name, _, _, _) => played.Add(name)));
            return played.Single(name => name.StartsWith(SfxNames.Shot, StringComparison.Ordinal));
        }).ToArray();
        Assert.Equal(sounds.Length, sounds.Distinct().Count());
    }

    [Fact]
    public void Grunt_kill_steps_stay_modest()
    {
        // a stage where the grunts are as tough as they start, two stages where they have toughened, and the cap
        int[] stages = [1, 2, 5, Tuning.StageCap];
        // a pickup may cut the time to kill a grunt by at most this much, so the grunts never turn to paper all at once; and the
        // time never grows past the level below's by more than the last, a small dip a player cannot feel
        const double MostFaster = 1.4, MostSlower = 1.5;
        var volley = Levels.Select(VolleyDamage).ToArray();
        var interval = Levels.Select(Interval).ToArray();
        foreach (int stage in stages)
        {
            var game = EnemyKit.Game();
            PressRig.Force(game, nameof(RaiderGame.Progress), Ramp.StageStart(stage));
            double hp = game.SpawnKind(EnemyKind.Grunt, EnemyKit.MidWidth).Hp;
            // the volleys a grunt takes, times the frames between them: how quickly a stream of grunts falls
            var frames = Levels.Select(level => Math.Ceiling(hp / volley[level - 1] - VolleyRounding) * interval[level - 1]).ToArray();
            output.WriteLine($"stage {stage}, grunt {hp:0.000} hp: frames a kill " + string.Join(" ", frames));
            for (int i = 1; i < frames.Length; i++)
            {
                Assert.True(frames[i - 1] / frames[i] <= MostFaster, $"stage {stage}: level {Levels[i]} kills in {frames[i]} frames against {frames[i - 1]}");
                Assert.True(frames[i] / frames[i - 1] <= MostSlower, $"stage {stage}: level {Levels[i]} kills in {frames[i]} frames against {frames[i - 1]}");
            }
        }
    }

    /// <summary>A volley that lands a hair short of a grunt's hit points by float error still kills it in that many volleys.</summary>
    private const double VolleyRounding = 1e-9;
}
