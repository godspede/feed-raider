using FeedRaider.Engine.Raider;
using Xunit;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The attacks that are warned of by a white muzzle alone: the muzzle is white in the frames before the bolt, and the bolt comes
/// on the frame its own clock always put it.
/// </summary>
public class TellTests
{
    /// <summary>Frames each case is watched for: several volleys of the slowest of them.</summary>
    private const int WatchFrames = 600;

    /// <summary>What one frame showed: whether every muzzle asked about was warning white, and the hostile bolts that first appeared in it.</summary>
    private sealed record Seen(bool Tell, IReadOnlyList<Bullet> Bolts);

    /// <summary>Steps a game with one enemy in it, the ship kept alive and still, and reads the muzzles off the field after each frame.</summary>
    private static List<Seen> Watch(RaiderGame g, Enemy e, Func<IEnumerable<(int X, int Y)>> muzzles, Action<int>? eachFrame = null)
    {
        var view = new RaiderView(); var scr = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        var known = new HashSet<Bullet>();
        var seen = new List<Seen>();
        for (int i = 0; i < WatchFrames && g.Enemies.Contains(e); i++)
        {
            ColourRuleTests.KeepShipAlive(g);
            g.Step();
            g.Enemies.RemoveAll(x => !ReferenceEquals(x, e));
            var bolts = g.Bullets.Where(b => !b.Friendly && known.Add(b)).ToList();
            var f = ColourRuleTests.DrawEnemiesOnly(g, view, scr);
            var at = muzzles().ToList();
            seen.Add(new Seen(at.Count > 0 && at.All(m => (f.Get(m.X, m.Y) & EnemyKit.RgbMask) == Palette.Tell), bolts));
            eachFrame?.Invoke(i);
        }
        return seen;
    }

    /// <summary>Every volley that had room for a whole warning before it had the muzzle white for exactly <paramref name="tellFrames"/> frames.</summary>
    private static void AssertWhiteBeforeEachVolley(List<Seen> seen, IReadOnlyList<int> volleyFrames, int tellFrames)
    {
        var judged = volleyFrames.Where(s => s > tellFrames).ToList();
        Assert.NotEmpty(judged);
        foreach (int s in judged)
        {
            for (int back = 1; back <= tellFrames; back++) Assert.True(seen[s - back].Tell, $"volley at frame {s}: no white {back} frames before");
            Assert.False(seen[s - tellFrames - 1].Tell, $"volley at frame {s}: white earlier than {tellFrames} frames before");
        }
    }

    private static IEnumerable<(int X, int Y)> GunshipMuzzle(Enemy e)
    {
        const int halfWidth = 1;
        int y = (int)Math.Round(e.Y - e.Sprite.H / 2.0) + e.Sprite.H;
        for (int dx = -halfWidth; dx <= halfWidth; dx++) yield return ((int)Math.Round(e.X) + dx, y);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_gunship_s_muzzle_is_white_for_the_twelve_frames_before_each_volley_and_the_volleys_keep_their_gap(bool lesser)
    {
        var g = EnemyKit.Game();
        var e = lesser ? g.SpawnLesser() : g.SpawnKind(EnemyKind.Gunship, 0.5);
        var seen = Watch(g, e, () => GunshipMuzzle(e));
        var volleys = Enumerable.Range(0, seen.Count).Where(i => seen[i].Bolts.Count > 0).ToList();
        AssertWhiteBeforeEachVolley(seen, volleys, Tuning.GunshipTellFrames);
        Assert.All(volleys.Zip(volleys.Skip(1), (a, b) => b - a), gap => Assert.Equal(Tuning.GunshipVolleyFrames, gap));
    }

    /// <summary>The white a muzzle shows before a battery volley, a car's drop and the ice fan: twelve frames, as the engine names it for each.</summary>
    private const int TellFrames = 12;

    [Fact]
    public void A_spotlight_battery_s_muzzle_is_white_for_the_twelve_frames_before_each_volley_and_the_volleys_keep_their_frames()
    {
        const int firstVolleyAge = 20, secondVolleyAge = 50;
        var g = EnemyKit.Game();
        var e = g.SpawnKind(EnemyKind.Spotlight, 0.5);
        var ages = new List<int>();
        IEnumerable<(int X, int Y)> Muzzles() => e.Parts!.Where(p => p.Name.StartsWith("battery", StringComparison.Ordinal) && p.State != PartState.Dead)
            .Select(p => ((int)Math.Round(e.X + p.OX), (int)Math.Round(e.Y + p.OY + p.HH)));
        var seen = Watch(g, e, Muzzles, _ => ages.Add(e.ModeAge));
        var volleys = Enumerable.Range(0, seen.Count).Where(i => seen[i].Bolts.Count > 0).ToList();
        AssertWhiteBeforeEachVolley(seen, volleys, TellFrames);
        Assert.All(volleys, v => Assert.Contains(ages[v], new[] { firstVolleyAge, secondVolleyAge }));
    }

    [Fact]
    public void The_schism_s_ice_muzzle_is_white_for_the_twelve_frames_before_each_fan_and_the_fans_keep_their_gap()
    {
        const int fanWidth = 5, fanEvery = 70, muzzleDrop = 6;
        var g = EnemyKit.Game();
        var e = g.SpawnKind(EnemyKind.Schism, 0.5);
        var ice = e.Parts!.Single(p => p.Name == "ice");
        var seen = Watch(g, e, () => [((int)Math.Round(e.X + ice.OX), (int)Math.Round(e.Y + ice.OY + muzzleDrop))]);
        var fans = Enumerable.Range(0, seen.Count).Where(i => seen[i].Bolts.Count >= fanWidth).ToList();
        AssertWhiteBeforeEachVolley(seen, fans, TellFrames);
        Assert.All(fans.Zip(fans.Skip(1), (a, b) => b - a), gap => Assert.Equal(fanEvery, gap));
    }

    [Fact]
    public void A_thread_car_shows_white_under_it_for_the_twelve_frames_before_it_drops_and_drops_on_the_frame_it_comes_over_the_ship()
    {
        const double dropBelow = 4, dropReach = 2;
        var g = EnemyKit.Game();
        var e = g.SpawnKind(EnemyKind.Thread, 0.5);
        var view = new RaiderView(); var scr = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        var known = new HashSet<Bullet>();
        var white = new List<Dictionary<Part, bool>>(); var across = new List<Dictionary<Part, double>>();
        int drops = 0;
        for (int i = 0; i < WatchFrames && g.Enemies.Contains(e); i++)
        {
            ColourRuleTests.KeepShipAlive(g);
            EnemyKit.StepSweeping(g);
            var f = ColourRuleTests.DrawEnemiesOnly(g, view, scr);
            var cars = e.Parts!.Skip(1).ToList();
            white.Add(cars.ToDictionary(p => p, p => (f.Get((int)Math.Round(e.X + p.OX), (int)Math.Round(e.Y + p.OY + dropBelow)) & EnemyKit.RgbMask) == Palette.Tell));
            across.Add(cars.ToDictionary(p => p, p => Math.Abs(e.X + p.OX - g.PX)));
            foreach (var b in g.Bullets.Where(b => !b.Friendly && known.Add(b)))
            {
                var car = cars.MinBy(p => Math.Abs(e.X + p.OX - b.X))!;
                Assert.True(across[i][car] <= dropReach, $"frame {i}: the car that dropped is {across[i][car]} from the ship across");
                Assert.True(across[i - 1][car] > dropReach, $"frame {i}: the car was over the ship a frame sooner and held its bolt");
                for (int back = 1; back <= TellFrames; back++) Assert.True(white[i - back][car], $"drop at frame {i}: no white {back} frames before");
                drops++;
            }
        }
        Assert.True(drops > 0, "no car dropped a bolt");
    }
}
