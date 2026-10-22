using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The newer enemy kinds and the difficulty ramp. Deterministic: one seed, scripted inputs, no network.
public class EnemyKindsTests
{
    private static readonly EnemyKind[] Newer = [EnemyKind.Mine, EnemyKind.Diver, EnemyKind.Splitter, EnemyKind.Shard, EnemyKind.Turret];

    private static RaiderGame NewGame(int cols = 120, int rows = 46)
    {
        var g = new RaiderGame(5) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        return g;
    }

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    /// <summary>Clears everything but the kinds under test, and every enemy bullet but a turret's, so a test sees one threat alone.</summary>
    private static void Keep(RaiderGame g, params EnemyKind[] kinds)
    {
        g.Enemies.RemoveAll(e => Array.IndexOf(kinds, e.Kind) < 0);
        g.Bullets.RemoveAll(b => !b.Friendly && !b.Aimed);
    }

    private static void Step(RaiderGame g, params EnemyKind[] kinds) { g.Step(); Keep(g, kinds); }

    private static double Above(RaiderGame g) => g.PX / g.PW;

    private static string Row(Screen s, int r) => new(Enumerable.Range(0, s.Cols).Select(c => s.CharAt(c, r)).ToArray());

    // ---------------------------------------------------------------- each kind

    [Fact]
    public void Every_kind_enters_from_above_the_field_so_nothing_can_hit_on_the_frame_it_appears()
    {
        var g = NewGame();
        foreach (var k in Newer)
        {
            var e = g.SpawnKind(k, 0.5);
            Assert.True(e.Y + e.Sprite.H / 2.0 <= 0, $"{k} starts above the field");
            Assert.Equal(-1, e.Windup);
        }
        Assert.False(g.SpawnKind(EnemyKind.Mine, 0.5).Armed);
        Assert.False(g.SpawnKind(EnemyKind.Shard, 0.5).Armed);
    }

    [Fact]
    public void A_mine_drifts_down_shrugs_off_bullets_and_only_a_bomb_clears_it()
    {
        var g = NewGame();
        var mine = g.SpawnKind(EnemyKind.Mine, Above(g));
        g.Input.Key(GameKey.Fire, true);
        int kills = g.Kills, pings = 0;
        for (int i = 0; i < 150; i++)
        {
            double y = mine.Y;
            Step(g, EnemyKind.Mine);
            Assert.Equal(y + Tuning.MineDrift, mine.Y, 6);   // no knock-back: firing cannot hold it off
            if (mine.Flash > 0) pings++;
        }
        Assert.True(pings > 10, "bullets reached the mine");
        Assert.Contains(mine, g.Enemies);
        Assert.Equal(mine.MaxHp, mine.Hp);
        Assert.Equal(kills, g.Kills);

        g.Input.Key(GameKey.Fire, false);
        g.Input.Bomb();
        Step(g, EnemyKind.Mine);
        Assert.DoesNotContain(mine, g.Enemies);
        Assert.Equal(kills + 1, g.Kills);
    }

    [Fact]
    public void An_armed_mine_that_meets_the_ship_costs_a_heart_goes_off_and_scores_nothing()
    {
        var g = NewGame();
        var mine = g.SpawnKind(EnemyKind.Mine, Above(g));
        int hp = g.Hp;
        long score = g.Score;
        for (int i = 0; i < 600 && g.Hp == hp; i++)
        {
            bool armed = mine.Armed;
            Step(g, EnemyKind.Mine);
            if (g.Hp < hp) Assert.True(armed, "only an armed mine hurts");
        }
        Assert.Equal(hp - 1, g.Hp);
        Assert.DoesNotContain(mine, g.Enemies);
        Assert.Equal(score, g.Score);
    }

    [Fact]
    public void A_diver_falls_winds_up_on_the_spot_it_locked_then_charges_it()
    {
        var g = NewGame();
        var d = g.SpawnKind(EnemyKind.Diver, Above(g));
        int hp = g.Hp, windup = 0, firstWindupFrame = -1, hitFrame = -1;
        double y0 = d.Y, lockedX = double.NaN, shipX = double.NaN;
        for (int f = 0; f < 400 && hitFrame < 0; f++)
        {
            Step(g, EnemyKind.Diver);
            if (d.Windup > 0 && !d.Dashing)
            {
                if (windup++ == 0) { firstWindupFrame = f; lockedX = d.AimX; shipX = g.PX; }
            }
            if (g.Hp < hp) hitFrame = f;
        }
        Assert.True(d.Y > y0 + 10, "it moved down the field");
        Assert.Equal(Tuning.DiverWindupFrames, windup);
        Assert.Equal(shipX, lockedX);
        Assert.True(hitFrame > firstWindupFrame + Tuning.DiverWindupFrames, $"hit at {hitFrame}, wind-up from {firstWindupFrame}");
        Assert.True(d.Dashing);
    }

    [Fact]
    public void A_turret_settles_shows_every_burst_before_it_fires_it_and_pulls_out()
    {
        var g = NewGame();
        g.Input.Key(GameKey.Left, true);
        Run(g, 10);                                    // the ship sits off to one side, so the burst flies at an angle
        g.Input.Key(GameKey.Left, false);
        Keep(g);
        var t = g.SpawnKind(EnemyKind.Turret, 0.5);
        int windupRun = 0, bursts = 0, aimedSeen = 0;
        bool inBurst = false;
        for (int f = 0; f < 1500 && g.Enemies.Contains(t); f++)
        {
            int before = g.Bullets.Count(b => b.Aimed);
            Step(g, EnemyKind.Turret);
            int fired = g.Bullets.Count(b => b.Aimed) - before;
            if (t.Windup > 0) windupRun++;
            if (fired > 0)
            {
                aimedSeen += fired;
                if (!inBurst)
                {
                    Assert.True(windupRun >= Tuning.TurretWindupFrames - 1, $"burst {bursts + 1} followed only {windupRun} frames of wind-up");
                    bursts++;
                    inBurst = true;
                }
            }
            if (t.Burst == 0 && t.Windup <= 0 && inBurst) { inBurst = false; windupRun = 0; }
        }
        Assert.Equal(Tuning.TurretVolleys, bursts);
        Assert.Equal(Tuning.TurretVolleys * Tuning.TurretBurst, aimedSeen);
        Assert.DoesNotContain(t, g.Enemies);           // it climbed back out
    }

    [Fact]
    public void A_diver_and_a_turret_die_to_fire_and_score_their_own_points()
    {
        foreach (var k in new[] { EnemyKind.Diver, EnemyKind.Turret })
        {
            var g = NewGame();
            var e = g.SpawnKind(k, Above(g));
            g.Input.Key(GameKey.Fire, true);
            for (int i = 0; i < 300 && g.Enemies.Contains(e); i++) Step(g, k);
            Assert.DoesNotContain(e, g.Enemies);
            Assert.Equal(1, g.Kills);
            Assert.True(g.Score >= RaiderGame.ScoreFor(k), $"{k} scored {g.Score}");
        }
    }

    [Fact]
    public void A_splitter_shot_down_bursts_into_two_shards_that_cannot_hit_at_once()
    {
        var g = NewGame();
        var s = g.SpawnKind(EnemyKind.Splitter, Above(g));
        g.Input.Key(GameKey.Fire, true);
        for (int i = 0; i < 300 && g.Enemies.Contains(s); i++) Step(g, EnemyKind.Splitter, EnemyKind.Shard);
        Assert.DoesNotContain(s, g.Enemies);
        var shards = g.Enemies.Where(e => e.Kind == EnemyKind.Shard).ToList();
        Assert.Equal(2, shards.Count);
        Assert.All(shards, sh => Assert.False(sh.Armed));
        Assert.True(shards[0].VX < 0 && shards[1].VX > 0, "they fan out to both sides");
        g.Input.Key(GameKey.Fire, false);
        for (int i = 0; i < 300 && g.Enemies.Count > 0; i++) Step(g, EnemyKind.Shard);
        Assert.Empty(g.Enemies);                       // they fly off the field (or into the ship) and are gone
    }

    [Fact]
    public void A_splitter_cleared_by_a_bomb_leaves_no_shards()
    {
        var g = NewGame();
        var s = g.SpawnKind(EnemyKind.Splitter, 0.5);
        for (int i = 0; i < 30; i++) Step(g, EnemyKind.Splitter, EnemyKind.Shard);
        g.Input.Bomb();
        Step(g, EnemyKind.Splitter, EnemyKind.Shard);
        Assert.DoesNotContain(s, g.Enemies);
        Assert.DoesNotContain(g.Enemies, e => e.Kind == EnemyKind.Shard);
    }

    // ---------------------------------------------------------------- the ramp

    [Fact]
    public void Every_lever_moves_one_way_and_stops_at_its_cap()
    {
        for (int s = 1; s < Tuning.StageCap + 5; s++)
        {
            Assert.True(Ramp.FillerGap(s + 1) <= Ramp.FillerGap(s));
            Assert.True(Ramp.FillerExtra(s + 1) >= Ramp.FillerExtra(s));
            Assert.True(Ramp.HpMul(s + 1) >= Ramp.HpMul(s));
            Assert.True(Ramp.BulletSpeedMul(s + 1) >= Ramp.BulletSpeedMul(s));
            Assert.True(Ramp.FireGapMul(s + 1) <= Ramp.FireGapMul(s));
            Assert.True(Ramp.Specials(s + 1) >= Ramp.Specials(s));
            Assert.True(Ramp.UnlockedCount(s + 1) >= Ramp.UnlockedCount(s));
        }
        foreach (int s in new[] { Tuning.StageCap, Tuning.StageCap + 7, 1000 })
        {
            Assert.Equal(Tuning.FillerGapCap, Ramp.FillerGap(s));
            Assert.Equal(Tuning.FillerExtraCap, Ramp.FillerExtra(s));
            Assert.Equal(Tuning.HpMulCap, Ramp.HpMul(s), 9);
            Assert.Equal(Tuning.BulletSpeedMulCap, Ramp.BulletSpeedMul(s), 9);
            Assert.Equal(Tuning.FireGapMulCap, Ramp.FireGapMul(s), 9);
            Assert.Equal(Tuning.SpecialsCap, Ramp.Specials(s));
        }
        Assert.Equal(Tuning.StageCap, Ramp.StageAt(long.MaxValue / 2));
        Assert.Equal(1, Ramp.StageAt(-5));
    }

    [Fact]
    public void Stage_one_is_the_old_game_lever_for_lever()
    {
        Assert.Equal(Tuning.FillerGapFrames, Ramp.FillerGap(1));
        Assert.Equal(0, Ramp.FillerExtra(1));
        Assert.Equal(1.0, Ramp.HpMul(1));
        Assert.Equal(1.0, Ramp.BulletSpeedMul(1));
        Assert.Equal(1.0, Ramp.FireGapMul(1));
        Assert.Equal(0, Ramp.Specials(1));
        Assert.Null(Ramp.Introduces(1));
        for (ulong seed = 0; seed < 30; seed++)
        {
            Assert.Equal(WaveSpawner.Filler(seed).Enemies, WaveSpawner.Filler(seed, 1).Enemies);
            var p = new DarkPost(DarknessKind.Spam, seed, "free crypto giveaway dm me now please");
            Assert.Equal(WaveSpawner.FromPost(p).Enemies, WaveSpawner.FromPost(p, 2).Enemies);   // live waves keep their shape until LiveSpecialsFromStage
        }
    }

    [Fact]
    public void A_stage_one_run_plays_frame_for_frame_as_it_did_before_the_ramp()
    {
        // the same seed and inputs as a run recorded before the newer kinds existed: same score, kills, death, waves and positions
        var g = NewGame();
        g.Input.Key(GameKey.Fire, true);
        ulong h = 1469598103934665603UL;
        for (int f = 0; f < 900; f++)
        {
            g.Input.Key(GameKey.Left, f / 60 % 2 == 0);
            g.Input.Key(GameKey.Right, f / 60 % 2 == 1);
            if (f == 100) g.OfferDark(new DarkPost(DarknessKind.Spam, 3, "buy followers now cheap"));
            if (f == 400) g.OfferDark(new DarkPost(DarknessKind.Rude, 4, "some rude words for the gunship"));
            g.Step();
            Assert.Equal(1, g.Stage);
            Assert.DoesNotContain(g.Enemies, e => e.Kind >= EnemyKind.Mine);
            foreach (var e in g.Enemies) h = (h ^ (ulong)(long)Math.Round(e.X * 100 + e.Y * 7919)) * 1099511628211UL;
            h = (h ^ (ulong)g.Bullets.Count) * 1099511628211UL;
        }
        Assert.Equal("4300/16/1/7/2/8/17/8debd48e319d9a18",
            $"{g.Score}/{g.Kills}/{g.Deaths}/{g.WavesSpawned}/{g.LiveWavesSpawned}/{g.Enemies.Count}/{g.Bullets.Count}/{h:x16}");
    }

    [Fact]
    public void Kinds_unlock_one_a_stage_in_the_stated_order()
    {
        Assert.Equal([EnemyKind.Mine, EnemyKind.Diver, EnemyKind.Splitter, EnemyKind.Turret], Ramp.UnlockOrder);
        for (int s = 2; s <= Ramp.LastUnlockStage; s++)
        {
            Assert.Equal(Ramp.UnlockOrder[s - 2], Ramp.Introduces(s));
            Assert.Equal(s, Ramp.UnlockStage(Ramp.UnlockOrder[s - 2]));
            Assert.NotNull(Ramp.Hint(s));
        }
        for (int s = 1; s <= Tuning.StageCap; s++)
            for (ulong seed = 0; seed < 40; seed++)
            {
                var w = WaveSpawner.Filler(seed, s);
                Assert.All(w.Enemies, e => Assert.True(Ramp.Unlocked(e.Kind, s), $"{e.Kind} at stage {s}"));
                if (Ramp.Introduces(s) is { } k) Assert.Contains(w.Enemies, e => e.Kind == k);   // the new kind shows up at once
            }
    }

    [Fact]
    public void A_long_run_meets_the_kinds_in_order_and_never_before_their_stage()
    {
        var g = NewGame();
        g.Input.Key(GameKey.Fire, true);
        var firstStage = new Dictionary<EnemyKind, int>();
        for (int f = 0; f < 30000 && g.Stage <= Ramp.LastUnlockStage; f++)
        {
            g.Input.Key(GameKey.Left, f / 45 % 2 == 0);
            g.Input.Key(GameKey.Right, f / 45 % 2 == 1);
            g.Step();
            foreach (var e in g.Enemies)
            {
                var k = e.Kind == EnemyKind.Shard ? EnemyKind.Splitter : e.Kind;
                Assert.True(Ramp.Unlocked(k, g.Stage), $"{k} at stage {g.Stage}");
                firstStage.TryAdd(k, g.Stage);
            }
        }
        foreach (var k in Ramp.UnlockOrder) Assert.Equal(Ramp.UnlockStage(k), firstStage[k]);
    }

    [Fact]
    public void Time_alive_and_kills_climb_the_stages_and_a_banner_announces_each()
    {
        var g = NewGame();
        Assert.Equal(1, g.Stage);
        long p0 = g.Progress;
        Run(g, 10);
        Assert.Equal(p0 + 10, g.Progress);
        var e = g.SpawnKind(EnemyKind.Diver, Above(g));
        g.Input.Key(GameKey.Fire, true);
        int kills = g.Kills;
        long before = g.Progress;
        for (int i = 0; i < 300 && g.Kills == kills; i++) g.Step();
        Assert.True(g.Progress - before >= Tuning.StageKillFrames, "a kill is worth progress");
        g.Input.Key(GameKey.Fire, false);
        for (int i = 0; i < Tuning.StageFrames * 2 && g.Stage < 2; i++) Step(g);   // nothing on screen: the ship survives
        Assert.Equal(2, g.Stage);
        Assert.Equal(2, g.StageShown);
        Assert.True(g.StageBannerLeft > 0);
    }

    [Fact]
    public void Dying_keeps_the_stage_and_only_loses_the_way_into_the_next()
    {
        var g = NewGame();
        g.StartAtStage(4);
        Assert.Equal(4, g.Stage);
        int stage = 0;
        long before = 0;
        for (int i = 0; i < 30000 && g.Deaths == 0; i++)
        {
            stage = g.Stage;
            before = g.Progress;
            g.Step();
        }
        Assert.Equal(1, g.Deaths);
        Assert.Equal(stage, g.Stage);
        Assert.Equal(Ramp.AfterDeath(before + 1), g.Progress);
        long after = g.Progress;
        Run(g, Tuning.RespawnFrames - 2);              // the clock stands still while the ship is down
        Assert.Equal(after, g.Progress);

        long start = Ramp.StageStart(3), half = Tuning.StageFrames / 2;
        Assert.Equal(start + half - (long)(half * Tuning.DeathSetback), Ramp.AfterDeath(start + half));
        Assert.Equal(start, Ramp.AfterDeath(start));
        for (long p = 0; p < Tuning.StageFrames * (Tuning.StageCap + 1); p += 37)
        {
            Assert.Equal(Ramp.StageAt(p), Ramp.StageAt(Ramp.AfterDeath(p)));
            Assert.True(Ramp.AfterDeath(p) <= p);
        }
    }

    [Fact]
    public void Enemies_grow_tougher_and_fire_faster_bullets_at_later_stages()
    {
        var early = NewGame();
        var late = NewGame();
        late.StartAtStage(Tuning.StageCap);
        Assert.Equal(Tuning.TurretHp, early.SpawnKind(EnemyKind.Turret, 0.5).MaxHp);
        Assert.Equal(Tuning.TurretHp * Tuning.HpMulCap, late.SpawnKind(EnemyKind.Turret, 0.5).MaxHp, 6);
        Assert.Equal(1, late.SpawnKind(EnemyKind.Mine, 0.5).MaxHp);
        Assert.Equal(Tuning.ShardHp, late.SpawnKind(EnemyKind.Shard, 0.5).MaxHp);
    }

    // ---------------------------------------------------------------- live waves, HUD and comm

    [Fact]
    public void A_late_live_wave_keeps_its_carrier_text_and_badges()
    {
        for (ulong seed = 1; seed < 40; seed++)
        {
            var w = WaveSpawner.FromPost(new DarkPost(DarknessKind.Spam, seed, "free crypto giveaway dm me now please"), Tuning.StageCap);
            Assert.Equal(1, w.Enemies.Count(e => e.Kind == EnemyKind.Carrier));
            Assert.Equal(1, w.Enemies.Count(e => e.Text is not null));
            Assert.DoesNotContain(w.Enemies, e => e.Kind == EnemyKind.Mine);   // a live wave can always be shot clear
        }
        var g = NewGame();
        g.StartAtStage(Tuning.StageCap);
        g.OfferDark(new DarkPost(DarknessKind.Spam, 3, "buy followers now cheap"));
        for (int i = 0; i < 600 && g.Enemies.Count(e => e.Live) < 5; i++) g.Step();
        var live = g.Enemies.Where(e => e.Live).ToList();
        Assert.Contains(live, e => e.Kind >= EnemyKind.Mine);
        Assert.All(live, e => Assert.NotNull(e.Badge));
        Assert.Single(live, e => e.Kind == EnemyKind.Carrier);
    }

    [Fact]
    public void The_hud_shows_the_stage_on_phone_and_desktop_fields()
    {
        foreach (var (cols, rows) in new[] { (48, 60), (130, 46) })
        {
            var g = NewGame(cols, rows);
            var view = new RaiderView();
            var scr = new Screen(cols, rows);
            view.Render(g, scr);
            Assert.Contains("STAGE 1", Row(scr, 0));
            g.StartAtStage(Tuning.StageCap);
            g.Step();
            view.Render(g, scr);
            Assert.Contains("STAGE 10", Row(scr, 0));
        }
        Assert.Equal("ST 3", RaiderView.StageReadout(3, 5));
        Assert.Equal("3", RaiderView.StageReadout(3, 2));
    }

    [Fact]
    public void The_stage_banner_and_new_enemies_never_cover_the_wingmans_comm()
    {
        var g = new RaiderGame(5) { LoggedIn = true, MaxWings = 1 };
        g.SetLayout(FieldLayout.Compute(48, 60));
        g.OfferPost(new FeedPost("at://did:plc:t1/app.bsky.feed.post/1", "cid1", "did:plc:t1", "t1.example.com",
            "this is a perfectly ordinary post with enough words in it to run well past three short lines of comm text", 10, 0, 0, null, null));
        for (int i = 0; i < 3000 && g.Comms.Speaker is null; i++) g.Step();
        var sp = g.Comms.Speaker!;
        Run(g, WingComms.RevealFrames(sp.CommChars) + 1);
        g.StartAtStage(5);                             // the banner is up
        foreach (var k in Newer) { var e = g.SpawnKind(k, sp.X / g.PW); e.Y = sp.Y + 12; e.Grace = 0; }
        var t = g.SpawnKind(EnemyKind.Turret, sp.X / g.PW);
        t.Y = sp.Y; t.Windup = 10; t.AimX = sp.X; t.AimY = sp.Y + 14;   // a reticle right under the comm
        g.Step();
        Assert.True(g.StageBannerLeft > 0);
        var view = new RaiderView();
        var scr = new Screen(48, 60);
        view.Render(g, scr);
        string all = string.Join("\n", Enumerable.Range(0, scr.Rows).Select(r => Row(scr, r)));
        Assert.Contains(sp.HandleTag, all);
        foreach (var line in g.Comms.Speaker!.CommLines) Assert.Contains(line, all);
        Assert.Contains("STAGE 5", all);
    }
}
