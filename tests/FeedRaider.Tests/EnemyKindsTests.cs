using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The kinds the first stages unlock (mine, diver, splitter and its shards, turret) and the difficulty ramp. Deterministic:
// one seed, scripted inputs, no network.
public class EnemyKindsTests
{
    // the seed and multiplier of the FNV-1a-style hash that fingerprints a recorded run
    private const ulong HashSeed = 1469598103934665603UL, HashPrime = 1099511628211UL;

    /// <summary>The kinds that unlock one a stage from stage 1 (the mine halfway through it), and the stage the last of them unlocks at.</summary>
    private static readonly EnemyKind[] EarlyUnlockOrder = [EnemyKind.Mine, EnemyKind.Diver, EnemyKind.Splitter, EnemyKind.Turret];
    private const int LastEarlyUnlockStage = 4;

    /// <summary>The early unlocks and the shard, which no stage unlocks: it comes only from a splitter breaking.</summary>
    private static readonly EnemyKind[] EarlyUnlocksAndShard = [.. EarlyUnlockOrder, EnemyKind.Shard];

    /// <summary>The frames a mine is watched drifting through the ship's fire.</summary>
    private const int MineUnderFireFrames = 150;

    /// <summary>The frames a mine is given to drift down onto the ship.</summary>
    private const int MineReachesShipFrames = 600;

    /// <summary>The frames a diver is given to lock, wind up and charge into the ship.</summary>
    private const int DiverChargeFrames = 400;

    /// <summary>The frames a turret is given to fire all its bursts and climb back out.</summary>
    private const int TurretVisitFrames = 1500;

    /// <summary>The frames a diver or a turret under the ship's fire is given to die.</summary>
    private const int KilledByFireFrames = 300;

    /// <summary>The frames a weaving splitter, chased by the ship, is given to die.</summary>
    private const int SplitterChaseFrames = 1500;

    /// <summary>The frames two shards are given to fly off the field.</summary>
    private const int ShardsLeaveFrames = 300;

    /// <summary>The frames a splitter falls before the bomb, so it is on the field when the bomb goes off.</summary>
    private const int SplitterFallFrames = 30;

    /// <summary>A bound on the recorded stage-one run, past the frame it reaches the half of the stage at.</summary>
    private const int RecordedRunFrames = 700;

    /// <summary>A bound on the long run, enough for a sweeping ship to reach the last early unlock stage.</summary>
    private const int LongRunFrames = 90000;

    /// <summary>The frames a run with no one steering is given to lose its first life.</summary>
    private const int FirstDeathFrames = 30000;

    /// <summary>The frames a live wave offered at the cap stage is given to arrive whole.</summary>
    private const int LiveWaveArrivesFrames = 600;

    /// <summary>The seed of every game here; the recorded run's fingerprint holds only for this seed.</summary>
    private const ulong Seed = 5;

    /// <summary>The middle of the field's width, as the share of it an enemy arrives at.</summary>
    private const double MidField = 0.5;

    /// <summary>The stage a game starts on, the one after it, and the decimal places a position is compared to.</summary>
    private const int FirstStage = 1, SecondStage = FirstStage + 1, Places = 6;

    private const int StartingGunLevel = 1;

    /// <summary>A sweeping ship's two ways, left and right, taken in turn.</summary>
    private const int SweepWays = 2;

    /// <summary>What one hit takes off the ship's shield and hit points together, and what one kill and one death add to their counts.</summary>
    private const int OneHit = 1, OneKill = 1, OneDeath = 1;

    /// <summary>The frame of an event a run has not seen.</summary>
    private const int Never = -1;

    /// <summary>The wave seeds a test tries.</summary>
    private const ulong SeedsTried = 40;

    /// <summary>The progress of half a stage without kills.</summary>
    private const long HalfStageFrames = Tuning.StageFrames / 2;

    /// <summary>The last progress before the half of stage 1, where the mines start.</summary>
    private const long BeforeMines = HalfStageFrames - 1;

    /// <summary>The seed of the follower spam post, and the text of two spam posts.</summary>
    private const ulong SpamSeed = 3;
    private const string CryptoSpam = "free crypto giveaway dm me now please", FollowerSpam = "buy followers now cheap";

    private static RaiderGame NewGame(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows, bool gates = true)
    {
        var game = new RaiderGame(Seed) { SignedIn = true, BossGates = gates };
        game.SetLayout(FieldLayout.Compute(cols, rows));
        return game;
    }

    private static void Run(RaiderGame game, int frames) { for (int i = 0; i < frames; i++) game.Step(); }

    /// <summary>Clears everything but the kinds under test, and every bolt but a turret's, so a test sees one threat alone.</summary>
    private static void KeepOnly(RaiderGame game, params EnemyKind[] kinds)
    {
        game.Enemies.RemoveAll(enemy => !kinds.Contains(enemy.Kind));
        game.Bullets.RemoveAll(bullet => !bullet.Friendly && !bullet.Aimed);
    }

    private static void StepKeepingOnly(RaiderGame game, params EnemyKind[] kinds) { game.Step(); KeepOnly(game, kinds); }

    private static double ShipXFraction(RaiderGame game) => game.PX / game.PW;

    /// <summary>How the screen names a stage, in the HUD and on the banner that announces it.</summary>
    private static string StageBanner(int stage) => $"STAGE {stage}";

    private static string RowText(Screen screen, int row) => new(Enumerable.Range(0, screen.Cols).Select(column => screen.CharAt(column, row)).ToArray());

    // ---------------------------------------------------------------- each kind

    [Fact]
    public void Each_early_unlock_and_the_shard_enters_from_above_the_field_so_nothing_can_hit_on_the_frame_it_appears()
    {
        var game = NewGame();
        foreach (var kind in EarlyUnlocksAndShard)
        {
            var enemy = game.SpawnKind(kind, MidField);
            Assert.True(EnemySpeechBox.Shown(enemy).Bottom <= 0, $"{kind} starts above the field");
            Assert.Equal(Enemy.WindupNotBegun, enemy.Windup);
        }
        Assert.False(game.SpawnKind(EnemyKind.Mine, MidField).Armed);
        Assert.False(game.SpawnKind(EnemyKind.Shard, MidField).Armed);
    }

    [Fact]
    public void A_mine_drifts_down_shrugs_off_shots_and_only_a_bomb_clears_it()
    {
        const int LeastPings = 10;
        var game = NewGame();
        var mine = game.SpawnKind(EnemyKind.Mine, ShipXFraction(game));
        game.Input.Key(GameKey.Fire, true);
        int kills = game.Kills, pings = 0;
        for (int i = 0; i < MineUnderFireFrames; i++)
        {
            double lastY = mine.Y;
            StepKeepingOnly(game, EnemyKind.Mine);
            Assert.Equal(lastY + Tuning.MineDrift, mine.Y, Places);   // no knock-back: firing cannot hold it off
            if (mine.Flash > 0) pings++;
        }
        Assert.True(pings > LeastPings, "shots reached the mine");
        Assert.Contains(mine, game.Enemies);
        Assert.Equal(mine.MaxHp, mine.Hp);
        Assert.Equal(kills, game.Kills);

        game.Input.Key(GameKey.Fire, false);
        game.Input.Bomb();
        StepKeepingOnly(game, EnemyKind.Mine);
        Assert.DoesNotContain(mine, game.Enemies);
        Assert.Equal(kills + OneKill, game.Kills);
    }

    [Fact]
    public void An_armed_mine_that_meets_the_ship_costs_a_hit_goes_off_and_scores_nothing()
    {
        var game = NewGame();
        var mine = game.SpawnKind(EnemyKind.Mine, ShipXFraction(game));
        int hp = game.Hp + game.Shield;                      // the shield takes the first hit, then the hit point
        long score = game.Score;
        for (int i = 0; i < MineReachesShipFrames && game.Hp + game.Shield == hp; i++)
        {
            bool armed = mine.Armed;
            StepKeepingOnly(game, EnemyKind.Mine);
            if (game.Hp + game.Shield < hp) Assert.True(armed, "only an armed mine hurts");
        }
        Assert.Equal(hp - OneHit, game.Hp + game.Shield);
        Assert.DoesNotContain(mine, game.Enemies);
        Assert.Equal(score, game.Score);
    }

    [Fact]
    public void A_diver_falls_winds_up_on_the_spot_it_locked_then_charges_it()
    {
        const double LeastFall = 10;
        var game = NewGame();
        var diver = game.SpawnKind(EnemyKind.Diver, ShipXFraction(game));
        int hp = game.Hp + game.Shield, windup = 0, firstWindupFrame = Never, hitFrame = Never;
        double startY = diver.Y, lockedX = double.NaN, shipX = double.NaN;
        for (int frame = 0; frame < DiverChargeFrames && hitFrame == Never; frame++)
        {
            StepKeepingOnly(game, EnemyKind.Diver);
            if (diver.Windup > 0 && !diver.Dashing)
            {
                if (windup++ == 0) { firstWindupFrame = frame; lockedX = diver.AimX; shipX = game.PX; }
            }
            if (game.Hp + game.Shield < hp) hitFrame = frame;
        }
        Assert.True(diver.Y > startY + LeastFall, "it moved down the field");
        Assert.Equal(Tuning.DiverWindupFrames, windup);
        Assert.Equal(shipX, lockedX);
        Assert.True(hitFrame > firstWindupFrame + Tuning.DiverWindupFrames, $"hit at {hitFrame}, wind-up from {firstWindupFrame}");
        Assert.True(diver.Dashing);
    }

    [Fact]
    public void A_turret_settles_shows_every_burst_before_it_fires_it_and_pulls_out()
    {
        const int SteerAsideFrames = 10;
        var game = NewGame();
        game.Input.Key(GameKey.Left, true);
        Run(game, SteerAsideFrames);                      // the ship sits off to one side, so the burst flies at an angle
        game.Input.Key(GameKey.Left, false);
        KeepOnly(game);
        var turret = game.SpawnKind(EnemyKind.Turret, MidField);
        int windupRun = 0, bursts = 0, aimedSeen = 0;
        bool inBurst = false;
        for (int frame = 0; frame < TurretVisitFrames && game.Enemies.Contains(turret); frame++)
        {
            int before = game.Bullets.Count(bullet => bullet.Aimed);
            StepKeepingOnly(game, EnemyKind.Turret);
            int fired = game.Bullets.Count(bullet => bullet.Aimed) - before;
            if (turret.Windup > 0) windupRun++;
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
            if (turret.Burst == 0 && turret.Windup <= 0 && inBurst) { inBurst = false; windupRun = 0; }
        }
        Assert.Equal(Tuning.TurretVolleys, bursts);
        Assert.Equal(Tuning.TurretVolleys * Tuning.TurretBurst, aimedSeen);
        Assert.DoesNotContain(turret, game.Enemies);
    }

    [Fact]
    public void A_diver_and_a_turret_die_to_fire_and_score_their_own_points()
    {
        foreach (var kind in new[] { EnemyKind.Diver, EnemyKind.Turret })
        {
            var game = NewGame();
            var enemy = game.SpawnKind(kind, ShipXFraction(game));
            game.Input.Key(GameKey.Fire, true);
            for (int i = 0; i < KilledByFireFrames && game.Enemies.Contains(enemy); i++) StepKeepingOnly(game, kind);
            Assert.DoesNotContain(enemy, game.Enemies);
            Assert.Equal(OneKill, game.Kills);
            Assert.True(game.Score >= RaiderGame.TableBehaviour(kind).Score, $"{kind} scored {game.Score}");
        }
    }

    [Fact]
    public void A_splitter_killed_bursts_into_two_shards_that_cannot_hit_at_once()
    {
        const double UnderSlack = 2;
        const int ShardsPerSplitter = 2;
        var game = NewGame();
        var splitter = game.SpawnKind(EnemyKind.Splitter, ShipXFraction(game));
        game.Input.Key(GameKey.Fire, true);
        for (int i = 0; i < SplitterChaseFrames && game.Enemies.Contains(splitter); i++)
        {
            game.Input.Key(GameKey.Left, splitter.X < game.PX - UnderSlack); game.Input.Key(GameKey.Right, splitter.X > game.PX + UnderSlack);   // a splitter weaves and sidesteps: stay under it
            StepKeepingOnly(game, EnemyKind.Splitter, EnemyKind.Shard);
        }
        Assert.DoesNotContain(splitter, game.Enemies);
        var shards = game.Enemies.Where(enemy => enemy.Kind == EnemyKind.Shard).ToList();
        Assert.Equal(ShardsPerSplitter, shards.Count);
        Assert.All(shards, shard => Assert.False(shard.Armed));
        Assert.True(shards[0].VX < 0 && shards[1].VX > 0, "they fan out to both sides");
        game.Input.Key(GameKey.Fire, false);
        for (int i = 0; i < ShardsLeaveFrames && game.Enemies.Count > 0; i++) StepKeepingOnly(game, EnemyKind.Shard);
        Assert.Empty(game.Enemies);                       // they fly off the field (or into the ship) and are gone
    }

    [Fact]
    public void A_splitter_cleared_by_a_bomb_leaves_no_shards()
    {
        var game = NewGame();
        var splitter = game.SpawnKind(EnemyKind.Splitter, MidField);
        for (int i = 0; i < SplitterFallFrames; i++) StepKeepingOnly(game, EnemyKind.Splitter, EnemyKind.Shard);
        game.Input.Bomb();
        StepKeepingOnly(game, EnemyKind.Splitter, EnemyKind.Shard);
        Assert.DoesNotContain(splitter, game.Enemies);
        Assert.DoesNotContain(game.Enemies, enemy => enemy.Kind == EnemyKind.Shard);
    }

    // ---------------------------------------------------------------- the ramp

    [Fact]
    public void Every_lever_moves_one_way_and_stops_at_its_cap()
    {
        const int StagesPastCap = 5, CapPlaces = 9;
        const int WellPastCap = 7, FarPastCap = 1000;
        const long HugeProgress = long.MaxValue, NegativeProgress = -5;
        for (int stage = FirstStage; stage < Tuning.StageCap + StagesPastCap; stage++)
        {
            int next = stage + 1;
            Assert.True(Ramp.FillerGap(next) <= Ramp.FillerGap(stage));
            Assert.True(Ramp.FillerExtra(next) >= Ramp.FillerExtra(stage));
            Assert.True(Ramp.HpMul(next) >= Ramp.HpMul(stage));
            Assert.True(Ramp.BoltSpeedMul(next) >= Ramp.BoltSpeedMul(stage));
            Assert.True(Ramp.FireGapMul(next) <= Ramp.FireGapMul(stage));
            Assert.True(Ramp.Specials(next) >= Ramp.Specials(stage));
            Assert.True(Enum.GetValues<EnemyKind>().Count(kind => Ramp.Unlocked(kind, next)) >= Enum.GetValues<EnemyKind>().Count(kind => Ramp.Unlocked(kind, stage)));
        }
        foreach (int stage in new[] { Tuning.StageCap, Tuning.StageCap + WellPastCap, FarPastCap })
        {
            Assert.Equal(Tuning.FillerGapCap, Ramp.FillerGap(stage));
            Assert.Equal(Tuning.FillerExtraCap, Ramp.FillerExtra(stage));
            Assert.Equal(Tuning.HpMulCap, Ramp.HpMul(stage), CapPlaces);
            Assert.Equal(Tuning.BoltSpeedMulCap, Ramp.BoltSpeedMul(stage), CapPlaces);
            Assert.Equal(Tuning.FireGapMulCap, Ramp.FireGapMul(stage), CapPlaces);
            Assert.Equal(Tuning.SpecialsCap, Ramp.Specials(stage));
        }
        Assert.Equal(Tuning.StageCap, Ramp.StageAt(HugeProgress));
        Assert.Equal(FirstStage, Ramp.StageAt(NegativeProgress));
    }

    [Fact]
    public void Stage_one_is_the_base_difficulty_with_every_lever_at_its_starting_value()
    {
        const ulong StageOneSeeds = 30;
        const double Unscaled = 1.0, AtStageStart = 0;
        const int NoneExtra = 0;
        Assert.Equal(Tuning.FillerGapFrames, Ramp.FillerGap(FirstStage));
        Assert.Equal(NoneExtra, Ramp.FillerExtra(FirstStage));
        Assert.Equal(Unscaled, Ramp.HpMul(FirstStage));
        Assert.Equal(Unscaled, Ramp.BoltSpeedMul(FirstStage));
        Assert.Equal(Unscaled, Ramp.FireGapMul(FirstStage));
        Assert.Equal(NoneExtra, Ramp.Specials(FirstStage, AtStageStart));
        Assert.Empty(Ramp.Introduced(FirstStage));
        for (ulong seed = 0; seed < StageOneSeeds; seed++)
        {
            Assert.Equal(WaveSpawner.Filler(seed).Enemies, WaveSpawner.Filler(seed, FirstStage).Enemies);
            var spam = new DarknessPost(DarknessKind.Spam, seed, CryptoSpam);
            Assert.Equal(WaveSpawner.FromPost(spam).Enemies, WaveSpawner.FromPost(spam, Tuning.LiveSpecialsFromStage - 1).Enemies);   // live waves keep their shape until LiveSpecialsFromStage
        }
    }

    [Fact]
    public void A_stage_one_run_plays_frame_for_frame_as_the_recorded_run()
    {
        // the same seed and inputs as a recorded stage-one run, which never meets the unlocked kinds: same score, kills, death, waves and positions
        const int TurnFrames = 60, SpamFrame = 100, RudeFrame = 400;
        const ulong RudeSeed = 4;
        const double HashXScale = 100, HashYScale = 7919;
        var game = NewGame();
        game.Input.Key(GameKey.Fire, true);
        ulong hash = HashSeed;
        for (int frame = 0; frame < RecordedRunFrames && game.Progress < BeforeMines; frame++)
        {
            bool goLeft = frame / TurnFrames % SweepWays == 0;
            game.Input.Key(GameKey.Left, goLeft);
            game.Input.Key(GameKey.Right, !goLeft);
            if (frame == SpamFrame) game.OfferDarkness(new DarknessPost(DarknessKind.Spam, SpamSeed, FollowerSpam));
            if (frame == RudeFrame) game.OfferDarkness(new DarknessPost(DarknessKind.Rude, RudeSeed, "some rude words for the gunship"));
            game.Step();
            Assert.Equal(FirstStage, game.Stage);
            Assert.DoesNotContain(game.Enemies, enemy => enemy.Kind >= EnemyKind.Mine);
            foreach (var enemy in game.Enemies) hash = (hash ^ (ulong)(long)Math.Round(enemy.X * HashXScale + enemy.Y * HashYScale)) * HashPrime;
            hash = (hash ^ (ulong)game.Bullets.Count) * HashPrime;
        }
        // a fingerprint of the stage-one run: two deaths among stage 1's four fillers up, one live wave, and the run's last frame
        // reached short of half the stage
        Assert.Equal("700/2082/24/2/7/1/3/8/82b6432e9e22c8ce",
            $"{game.Frame}/{game.Score}/{game.Kills}/{game.Deaths}/{game.WavesSpawned}/{game.LiveWavesSpawned}/{game.Enemies.Count}/{game.Bullets.Count}/{hash:x16}");
    }

    [Fact]
    public void Kinds_unlock_one_a_stage_in_the_stated_order()
    {
        const double WholeStage = 1;
        Assert.Equal(FirstStage, Ramp.UnlockStage(EarlyUnlockOrder[0]));
        Assert.Equal(EnemyKind.Mine, Ramp.MidStageKind(FirstStage));
        for (int stage = SecondStage; stage <= LastEarlyUnlockStage; stage++)
        {
            Assert.Equal(stage, Ramp.UnlockStage(EarlyUnlockOrder[stage - FirstStage]));
            Assert.NotEmpty(Ramp.Introduced(stage));
            Assert.All(Ramp.Introduced(stage), kind => Assert.Contains(kind, Ramp.Unlocks(stage)));   // a stage's banner names only its own
        }
        for (int stage = FirstStage; stage <= Tuning.StageCap; stage++)
            for (ulong seed = 0; seed < SeedsTried; seed++)
            {
                var wave = WaveSpawner.Filler(seed, stage, (int)seed, WholeStage);
                Assert.All(wave.Enemies, spawn => Assert.True(Ramp.Unlocked(spawn.Kind, stage), $"{spawn.Kind} at stage {stage}"));
                if (stage > FirstStage && Ramp.Unlocks(stage) is { Length: > 0 } own)
                    Assert.Contains(wave.Enemies, spawn => spawn.Kind == own[(int)(seed % (ulong)own.Length)]);   // a new kind shows up at once, in turn
            }
    }

    [Fact]
    public void A_long_run_meets_the_kinds_in_order_and_never_before_their_stage()
    {
        const double LookAhead = 30, BoltLane = 8, BodyBelow = 4, BodyMargin = 6, EdgeMargin = 8, HalfStage = 0.5;
        const int SweepFrames = 45;
        var game = NewGame(gates: false);   // the ramp alone: a boss gate would hold the run at the end of stage 1
        game.Input.Key(GameKey.Fire, true);
        var firstStage = new Dictionary<EnemyKind, int>();
        for (int frame = 0; frame < LongRunFrames && game.Stage <= LastEarlyUnlockStage; frame++)
        {
            // sweep, but slip away from anything falling at the ship: the faster, evasive enemies kill a blind sweeper before the late stages
            var threat = game.Bullets.Where(bullet => !bullet.Friendly && bullet.Y < game.PY && game.PY - bullet.Y < LookAhead && Math.Abs(bullet.X - game.PX) < BoltLane).OrderByDescending(bullet => bullet.Y).FirstOrDefault();
            var body = game.Enemies.Where(enemy => enemy.Y < game.PY + BodyBelow && game.PY - enemy.Y < LookAhead && Math.Abs(enemy.X - game.PX) < enemy.Sprite.W / 2.0 + BodyMargin).OrderByDescending(enemy => enemy.Y).FirstOrDefault();
            bool left = threat is not null ? threat.X >= game.PX : body is not null ? body.X >= game.PX : frame / SweepFrames % SweepWays == 0;
            if (game.PX < EdgeMargin) left = false; else if (game.PX > game.PW - EdgeMargin) left = true;
            game.Input.Key(GameKey.Left, left);
            game.Input.Key(GameKey.Right, !left);
            game.Step();
            foreach (var enemy in game.Enemies)
            {
                var kind = enemy.Kind == EnemyKind.Shard ? EnemyKind.Splitter : enemy.Kind;   // a shard is a splitter's, and unlocks with it
                Assert.True(Ramp.Unlocked(kind, game.Stage), $"{kind} at stage {game.Stage}");
                if (kind == EnemyKind.Mine && !firstStage.ContainsKey(kind)) Assert.True(game.Stage > FirstStage || game.Deaths > 0 || Ramp.StageShare(game.Progress) >= HalfStage, "no mine before half of stage 1");
                firstStage.TryAdd(kind, game.Stage);
            }
        }
        foreach (var kind in EarlyUnlockOrder) Assert.Equal(Ramp.UnlockStage(kind), firstStage[kind]);
    }

    [Fact]
    public void Time_alive_and_kills_climb_the_stages_and_a_banner_announces_each()
    {
        const int ClockFrames = 10, StagesOfWaiting = 2;
        var game = NewGame(gates: false);   // with the gate on, stage 2 waits for the stage 1 boss (GateTests)
        Assert.Equal(FirstStage, game.Stage);
        long startProgress = game.Progress;
        Run(game, ClockFrames);
        Assert.Equal(startProgress + ClockFrames, game.Progress);
        game.SpawnKind(EnemyKind.Diver, ShipXFraction(game));
        game.Input.Key(GameKey.Fire, true);
        int kills = game.Kills;
        long before = game.Progress;
        for (int i = 0; i < KilledByFireFrames && game.Kills == kills; i++) game.Step();
        Assert.True(game.Progress - before >= Tuning.StageKillFrames, "a kill is worth progress");
        game.Input.Key(GameKey.Fire, false);
        for (int i = 0; i < Tuning.StageFrames * StagesOfWaiting && game.Stage < SecondStage; i++) StepKeepingOnly(game);   // nothing on screen: the ship survives
        Assert.Equal(SecondStage, game.Stage);
        Assert.Equal(SecondStage, game.StageShown);
        Assert.True(game.StageBannerLeft > 0);
    }

    [Fact]
    public void The_mines_hint_shows_once_when_stage_one_is_half_over_and_never_when_a_run_starts_later()
    {
        const int PastBannerFrames = 5, BeforeStageEndFrames = 5, LaterStage = 3, LaterRunFrames = 5;
        var game = NewGame(gates: false);
        Assert.Equal(EnemyKind.Mine, Ramp.MidStageKind(FirstStage));
        Assert.Null(game.KindHint);
        while (game.Progress < BeforeMines) { StepKeepingOnly(game); Assert.Null(game.KindHint); }
        StepKeepingOnly(game);
        StepKeepingOnly(game);
        Assert.Equal(EnemyKind.Mine, game.KindHint);
        Assert.True(game.KindHintLeft is > 0 and <= Tuning.StageBannerFrames);
        var view = new RaiderView();
        var screen = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        view.FitAndRender(game, screen);
        string all = string.Join("\n", Enumerable.Range(0, screen.Rows).Select(row => RowText(screen, row)));
        Assert.Contains(Ramp.HintFor(EnemyKind.Mine)!, all);
        Assert.DoesNotContain(StageBanner(SecondStage), all);

        Run(game, Tuning.StageBannerFrames + PastBannerFrames);   // it goes away and does not come back
        Assert.Null(game.KindHint);
        Assert.Equal(0, game.KindHintLeft);
        while (game.Progress < Tuning.StageFrames - BeforeStageEndFrames) { StepKeepingOnly(game); Assert.Null(game.KindHint); }

        var later = NewGame(gates: false);
        later.StartAtStage(LaterStage);
        Run(later, LaterRunFrames);
        Assert.Null(later.KindHint);
    }

    [Fact]
    public void The_mines_hint_shows_on_the_frame_the_stage_share_reaches_the_mines_unlock_share()
    {
        var game = NewGame(gates: false);
        for (int i = 0; i < Tuning.StageFrames && game.KindHint is null; i++) StepKeepingOnly(game);
        Assert.Equal(EnemyKind.Mine, game.KindHint);
        Assert.Equal(Ramp.UnlockShare(EnemyKind.Mine), Ramp.StageShare(game.Progress));
    }

    [Fact]
    public void Dying_keeps_the_stage_and_only_loses_the_way_into_the_next()
    {
        const int DeathStage = 4, SetbackStage = 3, ShortOfRespawnFrames = 2, ProgressStep = 37;
        var game = NewGame();
        game.StartAtStage(DeathStage);
        Assert.Equal(DeathStage, game.Stage);
        int stage = 0;
        long before = 0;
        for (int i = 0; i < FirstDeathFrames && game.Deaths == 0; i++)
        {
            stage = game.Stage;
            before = game.Progress;
            game.Step();
        }
        const long ProgressPerFrame = 1;
        const int StagesWalked = Tuning.StageCap + 1;
        Assert.Equal(OneDeath, game.Deaths);
        Assert.Equal(stage, game.Stage);
        Assert.Equal(Ramp.AfterDeath(before + ProgressPerFrame), game.Progress);
        long after = game.Progress;
        Run(game, Tuning.RespawnFrames - ShortOfRespawnFrames);   // the clock stands still while the ship is down
        Assert.Equal(after, game.Progress);

        long start = Ramp.StageStart(SetbackStage);
        Assert.Equal(start + HalfStageFrames - (long)(HalfStageFrames * Tuning.DeathSetback), Ramp.AfterDeath(start + HalfStageFrames));
        Assert.Equal(start, Ramp.AfterDeath(start));
        for (long progress = 0; progress < Tuning.StageFrames * StagesWalked; progress += ProgressStep)
        {
            Assert.Equal(Ramp.StageAt(progress), Ramp.StageAt(Ramp.AfterDeath(progress)));
            Assert.True(Ramp.AfterDeath(progress) <= progress);
        }
    }

    [Fact]
    public void The_toughness_and_density_numbers_are_the_played_pace()
    {
        Assert.Equal((2, 4, 3, 4, 5), (Tuning.GruntHp, Tuning.CarrierHp, Tuning.DiverHp, Tuning.SplitterHp, Tuning.TurretHp));
        Assert.Equal(1.5, Tuning.HpMulCap);
        Assert.Equal((90, 40), (Tuning.GunshipHp, Tuning.LesserHp));
        Assert.Equal((70, 45), (Tuning.FillerGapFrames, Tuning.FillerGapCap));
        Assert.Equal(5, Tuning.FillerBase);
        Assert.Equal((75, 45), (Tuning.BossClearFrames, Tuning.BossWarnFrames));
        Assert.Equal(3, Tuning.SpecialsCap);
        Assert.Equal(6, Tuning.ThreeGroupsFromStage);
    }

    [Fact]
    public void The_starting_gun_kills_a_grunt_in_one_hit_at_stage_one_and_a_carrier_in_two()
    {
        const int CarrierHits = 2;
        const double OnFieldY = 30;
        var game = NewGame(gates: false);
        Assert.Equal(FirstStage, game.Stage);
        Assert.Equal(StartingGunLevel, game.WeaponLevel);
        game.Input.Key(GameKey.Fire, true);
        game.Step();
        var shot = game.Bullets.First(bullet => bullet.Friendly);
        var grunt = game.SpawnKind(EnemyKind.Grunt, MidField);
        var carrier = game.SpawnKind(EnemyKind.Carrier, MidField);
        Assert.True(shot.Dmg >= grunt.MaxHp, $"{shot.Dmg} against {grunt.MaxHp}");
        Assert.True(shot.Dmg < carrier.MaxHp && shot.Dmg * CarrierHits >= carrier.MaxHp, $"{shot.Dmg} against {carrier.MaxHp}");
        game.Enemies.Clear(); game.Bullets.Clear();
        var target = game.SpawnKind(EnemyKind.Grunt, MidField); target.Y = OnFieldY;
        game.Bullets.Add(new Bullet { X = target.X, Y = target.Y + Tuning.ShipShotSpeed, VY = -Tuning.ShipShotSpeed, Dmg = shot.Dmg, Friendly = true });
        game.Step();
        Assert.DoesNotContain(target, game.Enemies);
    }

    [Fact]
    public void Enemies_grow_tougher_at_later_stages()
    {
        var early = NewGame();
        var late = NewGame();
        late.StartAtStage(Tuning.StageCap);
        Assert.Equal(Tuning.TurretHp, early.SpawnKind(EnemyKind.Turret, MidField).MaxHp);
        Assert.Equal(Tuning.TurretHp * Tuning.HpMulCap, late.SpawnKind(EnemyKind.Turret, MidField).MaxHp, Places);
        Assert.Equal(RaiderGame.TableBehaviour(EnemyKind.Mine).Hp, late.SpawnKind(EnemyKind.Mine, MidField).MaxHp);
        Assert.Equal(Tuning.ShardHp, late.SpawnKind(EnemyKind.Shard, MidField).MaxHp);
    }

    // ---------------------------------------------------------------- live waves, HUD and speech box

    [Fact]
    public void A_late_live_wave_keeps_its_carrier_text_and_badges()
    {
        for (ulong seed = 1; seed < SeedsTried; seed++)
        {
            var wave = WaveSpawner.FromPost(new DarknessPost(DarknessKind.Spam, seed, CryptoSpam), Tuning.StageCap);
            Assert.Single(wave.Enemies, spawn => spawn.Kind == EnemyKind.Carrier);
            Assert.Single(wave.Enemies, spawn => spawn.Text is not null);
            Assert.DoesNotContain(wave.Enemies, spawn => spawn.Kind == EnemyKind.Mine);   // a live wave can always be shot clear
        }
        const int LiveWaveSize = 5;
        var game = NewGame();
        game.StartAtStage(Tuning.StageCap);
        game.OfferDarkness(new DarknessPost(DarknessKind.Spam, SpamSeed, FollowerSpam));
        for (int i = 0; i < LiveWaveArrivesFrames && game.Enemies.Count(enemy => enemy.Live) < LiveWaveSize; i++) game.Step();
        var live = game.Enemies.Where(enemy => enemy.Live).ToList();
        Assert.Contains(live, enemy => enemy.Kind >= EnemyKind.Mine);
        Assert.All(live, enemy => Assert.NotNull(enemy.Header));
        Assert.Single(live, enemy => enemy.Kind == EnemyKind.Carrier);
    }

    [Fact]
    public void The_hud_shows_the_stage_on_phone_and_desktop_fields()
    {
        const int PhoneCols = 48, PhoneRows = 60, DesktopCols = 130, HudRow = 0;
        foreach (var (cols, rows) in new[] { (PhoneCols, PhoneRows), (DesktopCols, EnemyKit.BigRows) })
        {
            var game = NewGame(cols, rows);
            var view = new RaiderView();
            var screen = new Screen(cols, rows);
            view.FitAndRender(game, screen);
            Assert.Contains(StageBanner(FirstStage), RowText(screen, HudRow));
            game.StartAtStage(Tuning.StageCap);
            game.Step();
            view.FitAndRender(game, screen);
            Assert.Contains(StageBanner(Tuning.StageCap), RowText(screen, HudRow));
        }
        const int ShownStage = 3, NarrowRoom = 5, NarrowestRoom = 2;
        Assert.Equal($"ST {ShownStage}", RaiderView.StageReadout(ShownStage, NarrowRoom));
        Assert.Equal($"{ShownStage}", RaiderView.StageReadout(ShownStage, NarrowestRoom));
    }

    [Fact]
    public void A_diver_in_its_windup_draws_a_dotted_aim_line_to_its_locked_spot()
    {
        // the diver's sprite is 16 tall, so the line starts at y 28 and runs to y 80 in dots three pixels apart, at 28, 31, 34 and so on, and
        // two pixel rows to a cell, so the dots land on screen rows 14, 15, 17, 18 ... and the cross at the end on row 40
        int[] dots = [14, 15, 17, 18, 20, 21, 23, 24, 26, 27, 29, 30, 32, 33, 35, 36, 38, 39, 40];
        Assert.Equal(dots, LimeRows(EnemyKind.Diver));
    }

    [Fact]
    public void A_diver_draws_no_aim_line_before_its_windup_or_once_it_dashes()
    {
        // the sprite's own lime sits on rows 9 to 11; nothing below it
        int[] spriteLimeRows = [9, 10, 11];
        Assert.Equal(spriteLimeRows, LimeRows(EnemyKind.Diver, windup: 0));
        Assert.Equal(spriteLimeRows, LimeRows(EnemyKind.Diver, dashing: true));
    }

    [Fact]
    public void A_turret_in_its_windup_draws_no_aim_line()
    {
        Assert.Empty(LimeRows(EnemyKind.Turret));
    }

    /// <summary>The wind-up frames left on an enemy <see cref="LimeRows"/> draws, unless a test gives its own.</summary>
    private const int AimWindupFrames = 10;

    /// <summary>An enemy of <paramref name="kind"/> winding up at (<c>EnemyX</c>, <c>EnemyY</c>) with its aim locked straight down to
    /// <c>AimY</c>, rendered; the screen rows that carry the diver's lime in its column.</summary>
    private static List<int> LimeRows(EnemyKind kind, int windup = AimWindupFrames, bool dashing = false)
    {
        const int GlintOffFrame = 3, EnemyX = 40, EnemyY = 20, AimY = 80;
        var game = NewGame();
        if (dashing) Run(game, GlintOffFrame);   // on this frame the dashing diver's glint is off, so its lime shows
        game.Enemies.Clear(); game.Bullets.Clear(); game.Particles.Clear(); game.Rings.Clear();
        var enemy = game.SpawnKind(kind, EnemyX / game.PW);
        enemy.X = EnemyX; enemy.Y = EnemyY; enemy.Windup = windup; enemy.Dashing = dashing; enemy.Grace = 0; enemy.AimX = EnemyX; enemy.AimY = AimY;
        var screen = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        new RaiderView().FitAndRender(game, screen);
        uint lime = Art.DiverLime;
        int column = game.Layout.Col0 + EnemyX / CardField.PixelsPerCell;
        return Enumerable.Range(0, screen.Rows).Where(row => screen.Fg[row * screen.Cols + column] == lime || screen.Bg[row * screen.Cols + column] == lime).ToList();
    }
}
