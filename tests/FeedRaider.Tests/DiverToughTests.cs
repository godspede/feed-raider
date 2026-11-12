using FeedRaider.Engine.Raider;
using Xunit.Abstractions;

namespace FeedRaider.Tests;

/// <summary>
/// The diver's charge has to be a threat. A headless bot stands still and holds fire (the worst case for the diver: it charges
/// straight down the stream) while divers wind up above it and dash. We report, per weapon level (1, 2 and 4 = one, two and
/// five streams), the share of dashes that reach the player's row alive and the mean frames they stay alive from the dash start.
/// Run: dotnet test FeedRaider.slnx --filter DiverTough --logger "console;verbosity=detailed"
/// </summary>
public class DiverToughTests(ITestOutputHelper output)
{
    public readonly record struct Result(int Level, int Dashes, double Reached, double MeanAlive, double KillBeforeDash);

    private static RaiderGame NewGame(ulong seed, int stage)
    {
        var g = new RaiderGame(seed) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(120, 46));
        if (stage > 1) g.StartAtStage(stage);
        return g;
    }

    public static Result Measure(int level, int stage = 1, int divers = 40)
    {
        int dashes = 0, reached = 0, killedEarly = 0; double alive = 0;
        for (ulong seed = 1; seed <= (ulong)divers; seed++)
        {
            var g = NewGame(seed, stage);
            while (g.WeaponLevel < level) { g.SpawnPickup(PickupKind.Power, g.PX, g.PY); g.Step(); }
            g.Enemies.Clear();
            // vary the fire phase and where the diver comes from (up to 8 px off the ship's column), so the dashes are not all one shot
            for (ulong w = 0; w < seed % 7; w++) { g.Input.Key(GameKey.Fire, true); g.Step(); g.Enemies.Clear(); }
            var d = g.SpawnKind(EnemyKind.Diver, 0.5);
            d.BaseX = d.X = g.PX + (seed % 9 - 4) * 2;
            int dashStart = -1, hp0 = g.Hp; bool wasDashing = false, done = false;
            for (int f = 0; f < 900 && !done; f++)
            {
                g.Enemies.RemoveAll(x => x != d);
                g.Input.Key(GameKey.Fire, true);
                g.Step();
                if (d.Dashing && !wasDashing) { wasDashing = true; dashStart = f; dashes++; }
                if (wasDashing && (g.Hp < hp0 || g.Deaths > 0)) { reached++; alive += f - dashStart; done = true; }   // it rammed the ship
                else if (d.Hp <= 0 || !g.Enemies.Contains(d))
                {
                    if (!wasDashing) killedEarly++;
                    else alive += f - dashStart;
                    done = true;
                }
            }
        }
        return new Result(level, dashes, reached / (double)Math.Max(1, dashes), alive / Math.Max(1, dashes), killedEarly / (double)divers);
    }

    [Fact]
    public void DiverTough_report()
    {
        foreach (int stage in new[] { 1, 10 })
        {
            output.WriteLine($"stage {stage}: level dashes reachedAlive meanFramesAliveInDash killedBeforeDash");
            foreach (int l in new[] { 1, 2, 4 })
            {
                var r = Measure(l, stage);
                output.WriteLine($"  {r.Level} {r.Dashes} {r.Reached:0.00} {r.MeanAlive:0.0} {r.KillBeforeDash:0.00}");
                Assert.True(r.Dashes > 0, $"stage {stage} level {l}: no diver dashed, so there is nothing to report");
            }
        }
    }

    private static Enemy Dasher(RaiderGame g, bool dashing)
    {
        g.Enemies.Clear();
        var d = g.SpawnKind(EnemyKind.Diver, 0.5);
        d.Y = 30; d.Dashing = dashing; d.Windup = dashing ? 0 : -1;
        return d;
    }

    [Fact]
    public void A_diver_has_four_hp_and_is_worth_more_than_a_mine_and_less_than_a_turret()
    {
        Assert.Equal(4, Tuning.DiverHp);
        Assert.Equal(4.0, RaiderGame.BaseHp(EnemyKind.Diver));
        Assert.Equal(400, Tuning.ScoreDiver);
        Assert.Equal(400, RaiderGame.ScoreFor(EnemyKind.Diver));
        Assert.True(Tuning.ScoreDiver > Tuning.ScoreMine && Tuning.ScoreDiver < Tuning.ScoreTurret);
    }

    [Fact]
    public void A_dashing_diver_takes_a_third_of_a_bullets_damage_and_is_not_shoved_but_a_winding_one_takes_it_all()
    {
        var g = NewGame(1, 1);
        var d = Dasher(g, true);
        g.Bullets.Add(new Bullet { X = d.X, Y = d.Y, VY = -2.8, Dmg = 2, Friendly = true });
        g.Step();
        Assert.Equal(4 - 2 * Tuning.DiverDashArmour, d.Hp, 6);
        Assert.Equal(0, d.KY);
        var w = Dasher(g, false);
        g.Bullets.Add(new Bullet { X = w.X, Y = w.Y, VY = -2.8, Dmg = 2, Friendly = true });
        g.Step();
        Assert.Equal(2, w.Hp, 6);
    }

    [Fact]
    public void A_bomb_ignores_the_dash_armour()
    {
        var g = NewGame(1, 1);
        var d = Dasher(g, true);
        g.Input.Bomb();
        g.Step();
        Assert.DoesNotContain(d, g.Enemies);
    }

    [Fact]
    public void Sustained_fire_still_kills_a_diver_before_its_dash_and_a_stream_does_not_stop_the_charge()
    {
        var r = Measure(1);
        Assert.True(r.KillBeforeDash > 0.2, $"{r.KillBeforeDash:0.00} died before the dash");
        foreach (int level in new[] { 1, 2, 4 })
        {
            var m = Measure(level);
            Assert.True(m.Dashes >= 10);
            Assert.True(m.Reached >= 0.85, $"level {level}: {m.Reached:0.00} reached the ship's row");
            Assert.True(m.MeanAlive >= 17, $"level {level}: alive {m.MeanAlive:0.0} frames");
        }
    }

    [Fact]
    public void A_diver_that_is_shot_before_its_dash_dies_to_two_hits_of_the_starting_gun()
    {
        var g = NewGame(1, 1);
        var d = Dasher(g, false);
        for (int i = 0; i < 2; i++) g.Bullets.Add(new Bullet { X = d.X, Y = d.Y, VY = -2.8, Dmg = 2, Friendly = true });
        g.Step();
        Assert.DoesNotContain(d, g.Enemies);
    }
}
