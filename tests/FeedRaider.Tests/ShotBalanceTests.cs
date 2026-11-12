using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests;

/// <summary>
/// A headless bot run of the sim (no renderer) that measures how the shot streams change the late game. A naive bot holds fire
/// and slides under the lowest enemy. For each weapon level (1, 2, 4 and 5 streams) it reports, over every kill, the median
/// share of the enemy's sprite that was on screen when it died and the median frames it lived, plus how long the bot lasts.
/// Run it with: dotnet test FeedRaider.slnx --filter ShotBalance --logger "console;verbosity=detailed"
/// </summary>
public class ShotBalanceTests(ITestOutputHelper output)
{
    public readonly record struct Result(int Level, int Streams, int Kills, double MedianVisible, double MedianAge, double FirstDeathFrames, double Score, double FullShare);

    private const int Frames = 5400;   // three minutes
    private const double SteerDeadZone = 1.5;   // the bot holds still while the target is within this many cells
    private static readonly ulong[] Seeds = [1, 2, 3, 4, 5, 6];

    /// <summary>Share of the sprite's rectangle inside the field, 0..1.</summary>
    public static double Visible(Enemy e, int pw, int ph)
    {
        double w = e.Sprite.W, h = e.Sprite.H;
        double ox = Math.Min(e.X + w / 2, pw) - Math.Max(e.X - w / 2, 0);
        double oy = Math.Min(e.Y + h / 2, ph) - Math.Max(e.Y - h / 2, 0);
        return Math.Clamp(ox / w, 0, 1) * Math.Clamp(oy / h, 0, 1);
    }

    /// <summary>Sorts <paramref name="v"/> in place and returns its upper middle element (NaN when empty).</summary>
    private static double UpperMedian(List<double> v) { if (v.Count == 0) return double.NaN; v.Sort(); return v[v.Count / 2]; }

    public static Result Measure(int level, bool sniper = false)
    {
        var vis = new List<double>(); var age = new List<double>();
        double firstDeath = 0, score = 0; int kills = 0;
        foreach (var seed in Seeds)
        {
            var g = new RaiderGame(seed);
            g.SetLayout(FieldLayout.Compute(120, 46));
            int deaths0 = 0; bool died = false;
            for (int f = 0; f < Frames; f++)
            {
                while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Power, g.PX, g.PY); g.Step(); }
                Enemy? t = null;
                foreach (var e in g.Enemies)
                    if (e.Hp > 0 && e.Kind != EnemyKind.Mine && (t is null || (sniper ? e.Y < t.Y : e.Y > t.Y))) t = e;
                double dx = t is null ? 0 : t.X - g.PX;
                g.Input.Key(GameKey.Left, dx < -SteerDeadZone); g.Input.Key(GameKey.Right, dx > SteerDeadZone);
                g.Input.Key(GameKey.Fire, true);
                var before = g.Enemies.ToArray(); int k0 = g.Kills;
                g.Step();
                if (g.Kills > k0)
                    foreach (var e in before)
                        if (e.Hp <= 0 && !g.Enemies.Contains(e)) { vis.Add(Visible(e, g.PW, g.PH)); age.Add(e.Age); kills++; }
                if (!died && g.Deaths > deaths0) { died = true; firstDeath += f; }
            }
            if (!died) firstDeath += Frames;
            score += g.Score;
        }
        return new Result(level, RaiderGame.WeaponPattern(level).Length, kills, UpperMedian(vis), UpperMedian(age), firstDeath / Seeds.Length, score / Seeds.Length, vis.Count(v => v >= 0.999) / (double)Math.Max(1, vis.Count));
    }

    [Fact]
    public void ShotBalance_report()
    {
        foreach (bool sniper in new[] { false, true })
        {
            output.WriteLine(sniper ? "SNIPER bot (shoots the newest enemy)" : "NAIVE bot (shoots the lowest enemy)");
            output.WriteLine("level streams kills medianVisible shareFullyVisible medianAgeFrames meanFramesToFirstDeath meanScore");
            var rs = new[] { 1, 2, 3, 4 }.Select(l => Measure(l, sniper)).ToArray();
            foreach (var r in rs)
                output.WriteLine($"{r.Level} {r.Streams} {r.Kills} {r.MedianVisible:0.00} {r.FullShare:0.00} {r.MedianAge:0} {r.FirstDeathFrames:0} {r.Score:0}");
            Assert.All(rs, r => Assert.True(r.Kills > 0));
            // the late game is no shooting gallery: kills land on screen, and a heavy gun does not outlive a light one
            Assert.All(rs, r => Assert.Equal(1.0, r.MedianVisible));
            Assert.All(rs, r => Assert.True(r.FullShare >= 0.95, $"level {r.Level}: {r.FullShare:0.00} fully visible"));
            Assert.True(rs[3].MedianAge >= 70, $"max streams: median kill at {rs[3].MedianAge} frames");
            Assert.True(rs[3].Kills <= rs[0].Kills * 1.25, "five streams must not out-kill one by much");
        }
    }
}
