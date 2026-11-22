using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>
/// The boss gate and the stage table. Every run is deterministic: one seed, scripted input.
/// A "quiet" step strips the enemy bolts and every enemy but the ones a test watches, so the ship lives through the long runs
/// and a test sees the gate alone.
/// </summary>
public class GateTests
{
    private const int MaxDieFrames = 400;   // a ship hit every frame dies well inside this
    private const int SlayFrames = 600;   // a boss of several vital parts or phases falls inside this many shots
    private const int BossEntryFrames = 90;   // the boss has come well into the field within this many frames
    private const int FieldCols = 120, FieldRows = 46;   // a desktop-sized screen
    private const ulong Seed = 5;
    private const double KillingDamage = 100000;   // more than any boss part has
    private const double MineSpawnX = 0.1;   // share of the field's width: a mine that keeps the field from being empty
    private const int HudRightGap = 1;   // the stage readout ends one cell short of the field's right edge
    private const string GunshipCue = "boss:gunship", WarningText = "WARNING";
    private const int FrameSlack = 2;   // a scheduled arrival or departure may land this many frames either side of its frame

    private static RaiderGame NewGame(int stage = 1, bool gates = true)
    {
        var game = new RaiderGame(Seed) { SignedIn = true, BossGates = gates };
        game.SetLayout(FieldLayout.Compute(FieldCols, FieldRows));
        if (stage > 1) game.StartAtStage(stage);
        return game;
    }

    /// <summary>A game two frames short of the last frame of stage 1: the next step reaches it and starts the gate.</summary>
    private static RaiderGame AtTheGate()
    {
        var game = NewGame();
        game.SetProgress(Tuning.StageFrames - 2);
        return game;
    }

    /// <summary>One step with the field kept quiet: no enemy bolts, and no enemy but the gate's boss and the lesser gunships.</summary>
    private static void StepQuiet(RaiderGame game, bool keepLessers = true)
    {
        game.Step();
        game.Bullets.RemoveAll(b => !b.Friendly);
        game.Enemies.RemoveAll(enemy => enemy != game.Boss && !(keepLessers && enemy.Lesser));
    }

    private static void UntilGate(RaiderGame game, GateState state)
    {
        const int MaxFramesToReachState = 3000;   // well past a full clearing and warning
        for (int i = 0; i < MaxFramesToReachState && game.Gate != state; i++) StepQuiet(game);
        Assert.Equal(state, game.Gate);
    }

    /// <summary>Puts an enemy bolt on the ship each frame until it has died once more.</summary>
    private static void Die(RaiderGame game)
    {
        int deaths = game.Deaths;
        for (int i = 0; i < MaxDieFrames && game.Deaths == deaths; i++)
        {
            if (game.Alive && game.Invuln == 0) game.Bullets.Add(new Bullet { X = game.PX, Y = game.PY, VX = 0, VY = 0, Friendly = false });
            StepQuiet(game);
        }
        Assert.Equal(deaths + 1, game.Deaths);
    }

    /// <summary>Lays a shot that kills the boss on the next step.</summary>
    private static void Slay(RaiderGame game)
    {
        var boss = game.Boss!;
        var vital = boss.Parts?.FirstOrDefault(p => p.Vital && p.State == PartState.Open) ?? boss.Parts?.FirstOrDefault(p => p.Vital && p.State != PartState.Dead);   // a boss made of parts is killed where it is vital and open
        EnemyKit.Shot(game, boss.X + (vital?.OX ?? 0), boss.Y + (vital?.OY ?? 0), KillingDamage);
    }

    /// <summary>The gunship has the gunship's hit points; a boss of parts has the vital parts of its first phase, which is some of its own total.</summary>
    private static void BossHpIsItsOwn(RaiderGame game)
    {
        var boss = game.Boss!;
        if (boss.Parts is null) Assert.Equal(Tuning.GunshipHp, boss.MaxHp);
        else Assert.InRange(boss.MaxHp, 1, game.BehaviourFor(boss.Kind).Hp);
    }

    private static List<string> AttachCueRecorder(RaiderGame game)
    {
        var cues = new List<string>();
        game.Music = new MusicDirector(cues.Add);
        return cues;
    }

    private static List<string> AttachSoundRecorder(RaiderGame game)
    {
        var played = new List<string>();
        game.Sfx = new SfxBus((sound, _, _, _) => played.Add(sound));
        return played;
    }

    private static string Read(Screen screen, int row, int col, int len) => new(screen.Ch.AsSpan(row * screen.Cols + col, len));

    // ---------------------------------------------------------------- the gate of stage 1

    [Fact]
    public void Progress_stops_on_the_last_frame_of_stage_one_and_the_gate_runs_on_the_stated_frames()
    {
        var game = AtTheGate();
        var cues = AttachCueRecorder(game);
        var sounds = AttachSoundRecorder(game);
        StepQuiet(game);
        Assert.Equal(Tuning.StageFrames - 1, game.Progress);
        Assert.Equal(GateState.Clearing, game.Gate);
        Assert.Equal(0, game.GateAge);
        Assert.DoesNotContain(cues, MusicCues.IsBoss);

        StepQuiet(game);                                       // the field is empty, so the clearing is over in a frame
        Assert.Equal(GateState.Warning, game.Gate);
        Assert.Equal(0, game.GateAge);
        Assert.Contains(GunshipCue, cues);
        Assert.Contains(SfxNames.Warn, sounds);

        const int MaxWarningFrames = 200;   // far more than the warning's stated length, so a warning that never ends fails below
        const int FightFramesWatched = 300;
        int warning = 0;
        while (game.Gate == GateState.Warning && warning < MaxWarningFrames) { warning++; StepQuiet(game); }
        Assert.Equal(Tuning.BossWarnFrames, warning);
        Assert.Equal(45, Tuning.BossWarnFrames);
        Assert.Equal(GateState.Fighting, game.Gate);
        Assert.Equal(0, game.FightFrames);
        Assert.NotNull(game.Boss);
        Assert.Contains(game.Boss, game.Enemies);
        Assert.Equal(EnemyKind.Gunship, game.Boss.Kind);
        Assert.False(game.Boss.Lesser);
        Assert.True(game.Boss.Y + game.Boss.Sprite.H / 2.0 <= 0, "the boss enters from above the field");
        Assert.Equal(Tuning.GunshipHp, game.Boss.MaxHp);

        for (int i = 0; i < FightFramesWatched; i++) StepQuiet(game);
        Assert.Equal(Tuning.StageFrames - 1, game.Progress);
        Assert.Equal(1, game.Stage);
        Assert.Equal(GateState.Fighting, game.Gate);
        Assert.Equal(FightFramesWatched, game.FightFrames);
        Assert.True(game.Boss.Y > 0, "and the boss has come in");
    }

    [Fact]
    public void Clearing_ends_after_the_stated_frames_when_the_field_is_never_empty_and_tells_what_is_left_to_leave()
    {
        var game = AtTheGate();
        StepQuiet(game);
        Assert.Equal(GateState.Clearing, game.Gate);
        var stay = game.SpawnKind(EnemyKind.Mine, MineSpawnX);
        const int MaxClearingFrames = 400;   // far more than the clearing's stated length, so a clearing that never ends fails below
        int clearing = 0;
        while (game.Gate == GateState.Clearing && clearing < MaxClearingFrames)
        {
            clearing++;
            game.Step();
            game.Bullets.RemoveAll(b => !b.Friendly);
            if (game.Gate == GateState.Clearing) game.Enemies.RemoveAll(enemy => enemy != stay);
        }
        Assert.Equal(Tuning.BossClearFrames, clearing);
        Assert.Equal(75, Tuning.BossClearFrames);
        Assert.Equal(GateState.Warning, game.Gate);
        Assert.True(stay.Leaving, "what was left is told to leave");
    }

    [Fact]
    public void The_warning_banner_reads_warning_over_the_name_of_the_boss_while_it_lasts_and_not_after()
    {
        var game = AtTheGate();
        StepQuiet(game); StepQuiet(game);
        Assert.Equal(GateState.Warning, game.Gate);
        const int BannerTopRow = 2, BannerBottomGap = 4;   // the banner sits below the HUD and clear of the bottom
        const int BannerLookFrame = 20;   // part way through the warning, once the banner is up
        var scr = new Screen(FieldCols, FieldRows);
        var lay = FieldLayout.Compute(FieldCols, FieldRows);
        var view = new RaiderView();
        bool Has(string text, int row) => Read(scr, row, lay.Col0, lay.PCols).Contains(text);
        int warnRow = -1;
        for (int i = 0; i < Tuning.BossWarnFrames; i++)
        {
            view.FitAndRender(game, scr);
            if (i == BannerLookFrame)
            {
                for (int row = 0; row < scr.Rows && warnRow < 0; row++) if (Has(WarningText, row)) warnRow = row;
                Assert.True(warnRow >= 0, "the banner is on the field");
                Assert.True(Has(game.BossName, warnRow + 1), "and the boss's name is under it");
                Assert.InRange(warnRow, BannerTopRow, lay.Rows - BannerBottomGap);
            }
            StepQuiet(game);
        }
        Assert.Equal("GUNSHIP", game.BossName);
        Assert.Equal(GateState.Fighting, game.Gate);
        view.FitAndRender(game, scr);
        for (int row = 0; row < scr.Rows; row++) Assert.False(Has(WarningText, row));
    }

    [Fact]
    public void The_hud_reads_boss_in_place_of_the_stage_while_a_boss_is_fought()
    {
        const string BossThree = "BOSS 3", StageOne = "STAGE 1", BossOne = "BOSS 1";
        Assert.Equal(BossThree, RaiderView.StageReadout(3, 7, boss: true));
        Assert.Equal(BossThree, RaiderView.StageReadout(3, 6, boss: true));
        Assert.Equal("B 3", RaiderView.StageReadout(3, 4, boss: true));
        Assert.Equal("3", RaiderView.StageReadout(3, 2, boss: true));
        Assert.Equal("STAGE 3", RaiderView.StageReadout(3, 7, boss: false));
        var game = AtTheGate();
        var lay = FieldLayout.Compute(FieldCols, FieldRows);
        var scr = new Screen(FieldCols, FieldRows);
        string ReadoutAtRight(string expected) => Read(scr, 0, lay.Col0 + lay.PCols - expected.Length - HudRightGap, expected.Length);
        new RaiderView().FitAndRender(game, scr);
        Assert.Equal(StageOne, ReadoutAtRight(StageOne));
        UntilGate(game, GateState.Fighting);
        new RaiderView().FitAndRender(game, scr);
        Assert.Equal(BossOne, ReadoutAtRight(BossOne));
    }

    // ---------------------------------------------------------------- what the gate holds back

    [Fact]
    public void No_filler_wave_and_no_lesser_gunship_launches_from_clearing_on_and_a_live_wave_launches_after_the_fight()
    {
        const int ThroughWarningIntoFight = 600;
        const int MaxFramesToLaunch = 300;
        var game = AtTheGate();
        StepQuiet(game);
        int waves = game.WavesSpawned;
        game.OfferDarkness(new DarknessPost(DarknessKind.Spam, 3, "buy followers now cheap"));
        for (int i = 0; i < ThroughWarningIntoFight; i++)
        {
            StepQuiet(game);
            Assert.Equal(waves, game.WavesSpawned);
        }
        Assert.Equal(GateState.Fighting, game.Gate);
        Assert.Equal(1, game.WavesQueued);
        Assert.DoesNotContain(game.Enemies, enemy => enemy.Lesser);

        Slay(game);
        StepQuiet(game);
        Assert.Equal(GateState.None, game.Gate);
        for (int i = 0; i < MaxFramesToLaunch && game.LiveWavesSpawned == 0; i++) game.Step();
        Assert.Equal(1, game.LiveWavesSpawned);            // queued through the fight, launched after it
        Assert.Equal(0, game.WavesQueued);
    }

    [Fact]
    public void The_waves_a_gate_holds_back_are_still_capped_at_the_queue_limit()
    {
        const int OfferedOverCap = 4;
        const int HeldFrames = 400;
        var game = AtTheGate();
        StepQuiet(game);
        for (int i = 0; i < Tuning.MaxPendingWaves + OfferedOverCap; i++) game.OfferDarkness(new DarknessPost(DarknessKind.Spam, (ulong)(10 + i), "post number " + i));
        for (int i = 0; i < HeldFrames; i++) StepQuiet(game);
        Assert.Equal(Tuning.MaxPendingWaves, game.WavesQueued);
    }

    // ---------------------------------------------------------------- the end of a fight

    [Fact]
    public void Killing_the_boss_sets_progress_to_the_start_of_stage_two_scores_drops_and_shows_the_stage_banner()
    {
        var game = AtTheGate();
        const int FightFramesBeforeKill = 120;
        UntilGate(game, GateState.Fighting);
        for (int i = 0; i < FightFramesBeforeKill; i++) StepQuiet(game);
        int score = game.Score, kills = game.Kills;
        var cues = AttachCueRecorder(game);
        var sounds = AttachSoundRecorder(game);
        Slay(game);
        StepQuiet(game);
        Assert.Equal(GateState.None, game.Gate);
        Assert.Null(game.Boss);
        Assert.Equal(Ramp.StageStart(2), game.Progress);
        Assert.Equal(2, game.Stage);
        Assert.Equal(1, game.BossesBeaten);
        Assert.Equal(score + Tuning.ScoreGunship, game.Score);
        Assert.Equal(kills + 1, game.Kills);
        Assert.NotEmpty(game.Pickups);
        Assert.Equal(2, game.StageShown);
        Assert.True(game.StageBannerLeft > 0);
        Assert.Contains(SfxNames.BossDown, sounds);
        while (game.HitStop > 0) StepQuiet(game);   // the kill froze the game for a moment
        StepQuiet(game);
        Assert.Equal(Ramp.StageStart(2) + 1, game.Progress);
        Assert.Equal(MusicCues.Gameplay, cues.Last(c => !MusicCues.IsAhead(c)));
    }

    [Fact]
    public void A_boss_is_still_there_after_8999_frames_of_fighting_and_leaves_at_9000_with_no_score_and_no_drop()
    {
        Assert.Equal(9000, Tuning.BossStayFrames);
        var game = AtTheGate();
        UntilGate(game, GateState.Fighting);
        var boss = game.Boss!;
        int score = game.Score;
        for (int i = 0; i < Tuning.BossStayFrames - 1; i++) StepQuiet(game);
        Assert.Equal(Tuning.BossStayFrames - 1, game.FightFrames);
        Assert.Equal(GateState.Fighting, game.Gate);
        Assert.Contains(boss, game.Enemies);
        Assert.False(boss.Leaving);
        StepQuiet(game);
        Assert.Equal(GateState.None, game.Gate);
        Assert.Equal(2, game.Stage);
        Assert.Equal(Ramp.StageStart(2), game.Progress);
        Assert.Equal(1, game.BossesBeaten);
        Assert.True(boss.Leaving);
        Assert.Equal(score, game.Score);
        Assert.Empty(game.Pickups);
        const int FlyOffFrames = 400;
        for (int i = 0; i < FlyOffFrames; i++) StepQuiet(game, keepLessers: false);
        Assert.DoesNotContain(boss, game.Enemies);
        Assert.Equal(score, game.Score);
        Assert.Empty(game.Pickups);
    }

    [Fact]
    public void Two_deaths_during_the_fight_do_not_move_the_frame_the_boss_leaves()
    {
        const int FramesBetweenDeaths = 500;
        const int MaxFramesToLeave = 20000;   // more than the boss's stay with every death's frames added
        long LeftAt(int deaths)
        {
            var game = AtTheGate();
            UntilGate(game, GateState.Fighting);
            for (int death = 0; death < deaths; death++) { for (int i = 0; i < FramesBetweenDeaths; i++) StepQuiet(game); Die(game); }
            for (int i = 0; i < MaxFramesToLeave && game.Gate != GateState.None; i++) StepQuiet(game);
            Assert.Equal(GateState.None, game.Gate);
            Assert.Equal(deaths, game.Deaths);
            return game.Frame;
        }
        long none = LeftAt(0);
        Assert.Equal(none, LeftAt(2));
    }

    [Fact]
    public void A_death_during_the_fight_leaves_progress_and_the_bosses_hit_points_as_they_were()
    {
        const int FightFramesBeforeHit = 200;
        const double WoundingDamage = 40;   // less than the gunship's hit points
        const int RespawnFrames = 100;
        var game = AtTheGate();
        UntilGate(game, GateState.Fighting);
        for (int i = 0; i < FightFramesBeforeHit; i++) StepQuiet(game);
        EnemyKit.Shot(game, game.Boss!.X, game.Boss.Y, WoundingDamage);
        StepQuiet(game);
        double hp = game.Boss.Hp;
        Assert.True(hp < game.Boss.MaxHp);
        long progress = game.Progress;
        Die(game);
        Assert.Equal(progress, game.Progress);
        Assert.Equal(hp, game.Boss.Hp);
        Assert.Equal(GateState.Fighting, game.Gate);
        for (int i = 0; i < RespawnFrames; i++) StepQuiet(game);
        Assert.True(game.Alive);
        Assert.Equal(progress, game.Progress);
        Assert.Equal(hp, game.Boss.Hp);
    }

    [Fact]
    public void A_death_during_clearing_applies_the_usual_setback_and_cancels_the_gate()
    {
        var game = AtTheGate();
        StepQuiet(game);
        Assert.Equal(GateState.Clearing, game.Gate);
        var stay = game.SpawnKind(EnemyKind.Mine, MineSpawnX);     // the field is not empty, so the clearing waits
        long before = game.Progress;
        int deaths = game.Deaths;
        const int MaxFramesToDie = 100;   // a ship hit every frame dies well inside this, inside the clearing too
        for (int i = 0; i < MaxFramesToDie && game.Deaths == deaths; i++)
        {
            if (game.Alive && game.Invuln == 0) game.Bullets.Add(new Bullet { X = game.PX, Y = game.PY, VX = 0, VY = 0, Friendly = false });
            game.Step();
            game.Bullets.RemoveAll(b => !b.Friendly);
            game.Enemies.RemoveAll(enemy => enemy != stay);
        }
        Assert.Equal(deaths + 1, game.Deaths);
        Assert.Equal(GateState.None, game.Gate);
        Assert.Equal(Ramp.AfterDeath(before), game.Progress);
        Assert.True(game.Progress < before);
        Assert.Equal(0, game.BossesBeaten);
        Assert.Null(game.Boss);
    }

    // ---------------------------------------------------------------- every stage's gate, and the gate switched off

    [Fact]
    public void Each_stages_gate_brings_its_boss_and_beating_it_opens_the_next_stage()
    {
        for (int stage = 1; stage <= Tuning.StageCap; stage++)
        {
            var expected = Ramp.Boss(stage);
            var game = NewGame(stage);
            game.StartAtBoss(stage);
            Assert.Equal(GateState.Warning, game.Gate);
            Assert.Equal(stage - 1, game.BossesBeaten);
            UntilGate(game, GateState.Fighting);
            Assert.Equal(expected, game.Boss!.Kind);
            Assert.False(game.Boss.Lesser);
            BossHpIsItsOwn(game);        // written per boss, never scaled by the stage
            for (int i = 0; i < BossEntryFrames; i++) StepQuiet(game);
            for (int i = 0; i < SlayFrames && game.Gate == GateState.Fighting; i++) { Slay(game); StepQuiet(game); }   // a boss of several vital parts falls part by part
            Assert.Equal(GateState.None, game.Gate);
            Assert.Equal(stage, game.BossesBeaten);
            Assert.Equal((long)stage * Tuning.StageFrames, game.Progress);
            Assert.Equal(Math.Min(stage + 1, Tuning.StageCap), game.Stage);
        }
    }

    [Fact]
    public void With_gates_off_the_game_is_todays_game_no_gate_no_lesser_gunship_and_progress_runs_on()
    {
        foreach (int stage in new[] { 1, 2, 7, 10 })
        {
            var game = NewGame(stage, gates: false);
            long start = game.Progress;
            const int PastTheGateFrame = 200;
            int frames = Tuning.StageFrames + PastTheGateFrame;
            for (int i = 0; i < frames; i++)
            {
                game.Step();
                game.Bullets.RemoveAll(b => !b.Friendly);
                game.Enemies.Clear();
                Assert.Equal(GateState.None, game.Gate);
            }
            Assert.Equal(0, game.Deaths);
            Assert.Equal(start + frames, game.Progress);
            Assert.Null(game.Boss);
        }
        var seen = NewGame(7, gates: false);
        for (int i = 0; i < Tuning.StageFrames; i++)
        {
            seen.Step();
            seen.Bullets.RemoveAll(b => !b.Friendly);
            Assert.DoesNotContain(seen.Enemies, enemy => enemy.Lesser);     // and the waves stay on, so the field is not swept here
        }
    }

    [Fact]
    public void With_gates_off_a_rude_post_brings_no_gunship_and_no_boss_music_but_a_gunship_still_cues_it()
    {
        const int WatchFrames = 600;
        const double OnFieldY = 40;
        var game = NewGame(gates: false);
        var cues = AttachCueRecorder(game);
        game.OfferDarkness(new DarknessPost(DarknessKind.Rude, 4, "some rude words for the gunship"));
        for (int i = 0; i < WatchFrames; i++) { game.Step(); Assert.DoesNotContain(game.Enemies, enemy => enemy.Kind == EnemyKind.Gunship); }
        Assert.DoesNotContain(cues, MusicCues.IsBoss);
        var ship = game.SpawnLesser();
        ship.Y = OnFieldY;
        for (int i = 0; i < WatchFrames && !cues.Contains(GunshipCue); i++) game.Step();
        Assert.Contains(GunshipCue, cues);
    }

    // ---------------------------------------------------------------- lesser gunships

    private static List<(long Progress, Enemy Ship)> Arrivals(RaiderGame game, int frames)
    {
        var seen = new List<(long, Enemy)>();
        var known = new HashSet<Enemy>(game.Enemies.Where(enemy => enemy.Lesser));   // only the ones that arrive from here on
        for (int i = 0; i < frames && game.Gate == GateState.None; i++)
        {
            StepQuiet(game);
            foreach (var enemy in game.Enemies.Where(enemy => enemy.Lesser && known.Add(enemy))) seen.Add((game.Progress - Ramp.StageStart(game.Stage), enemy));
        }
        return seen;
    }

    [Fact]
    public void The_lesser_schedule_is_the_stated_table()
    {
        foreach (int stage in new[] { 1, 2, 3 }) Assert.Equal([825], Visits(stage));
        foreach (int stage in new[] { 4, 5, 6 }) Assert.Equal([500, 1000], Visits(stage));
        foreach (int stage in new[] { 7, 8 }) Assert.Equal([375, 750, 1125], Visits(stage));
        Assert.Equal((0, 600, 2), (Ramp.Lessers(9).Visits, Ramp.Lessers(9).Every, Ramp.Lessers(9).MaxAlive));
        Assert.Equal((0, 420, 2), (Ramp.Lessers(10).Visits, Ramp.Lessers(10).Every, Ramp.Lessers(10).MaxAlive));
        Assert.Equal(Ramp.Lessers(10), Ramp.Lessers(37));
        static int[] Visits(int stage) => Enumerable.Range(0, Ramp.Lessers(stage).Visits).Select(Ramp.Lessers(stage).At).ToArray();
    }

    [Fact]
    public void A_lesser_gunship_arrives_at_its_scheduled_progress_once_a_stage()
    {
        foreach (var (stage, at) in new[] { (1, new[] { 825 }), (4, new[] { 500, 1000 }), (7, new[] { 375, 750, 1125 }) })
        {
            var game = NewGame(stage);
            var arrivals = Arrivals(game, Tuning.StageFrames * 2);
            Assert.Equal(GateState.Clearing, game.Gate);
            Assert.Equal(at.Length, arrivals.Count);
            for (int i = 0; i < at.Length; i++) Assert.InRange(arrivals[i].Progress, at[i] - FrameSlack, at[i] + FrameSlack);
        }
    }

    [Fact]
    public void A_death_does_not_bring_a_visit_round_again()
    {
        const int FirstVisit = 500, SecondVisit = 1000;   // stage 4's visits
        const int BetweenTheVisits = 900;
        const int VisitStage = 4;
        var game = NewGame(VisitStage);
        var first = Arrivals(game, BetweenTheVisits);
        Assert.Single(first);
        Die(game);
        Assert.True(game.Progress - Ramp.StageStart(VisitStage) < FirstVisit);
        var more = Arrivals(game, Tuning.StageFrames);
        Assert.Single(more);
        Assert.InRange(more[0].Progress, SecondVisit - FrameSlack, SecondVisit + FrameSlack);
    }

    [Fact]
    public void Stage_nine_sends_one_every_600_frames_and_stage_ten_one_every_420_never_more_than_two_alive()
    {
        const int NineEvery = 600, TenEvery = 420;
        const int ShortOfTheGate = 20;
        const int NineStage = 9, TenStage = 10, MostAlive = 2;
        // Lesser gunships that never leave: the cap is what stops them.
        static int MostAliveWhenHeld(int stage)
        {
            var held = NewGame(stage);
            int most = 0;
            for (int i = 0; i < Tuning.StageFrames - ShortOfTheGate && held.Gate == GateState.None; i++)
            {
                StepQuiet(held);
                foreach (var lesser in held.Enemies.Where(enemy => enemy.Lesser)) lesser.Age = 0;
                most = Math.Max(most, held.Enemies.Count(enemy => enemy.Lesser));
            }
            return most;
        }

        var nine = Arrivals(NewGame(NineStage), Tuning.StageFrames);
        Assert.True(nine.Count >= 2, $"stage nine sees at least two visits ({nine.Count})");
        Assert.InRange(nine[0].Progress, NineEvery - FrameSlack, NineEvery + FrameSlack);
        for (int i = 1; i < nine.Count; i++) Assert.True(nine[i].Progress - nine[i - 1].Progress >= NineEvery - FrameSlack);
        Assert.Equal(MostAlive, MostAliveWhenHeld(NineStage));

        var ten = Arrivals(NewGame(TenStage), Tuning.StageFrames);
        Assert.True(ten.Count >= nine.Count, $"stage ten sends no fewer than stage nine ({ten.Count} against {nine.Count})");
        Assert.InRange(ten[0].Progress, TenEvery - FrameSlack, TenEvery + FrameSlack);
        Assert.Equal(MostAlive, MostAliveWhenHeld(TenStage));
    }

    [Fact]
    public void A_lesser_gunship_has_40_hit_points_at_any_stage_stays_20_seconds_and_does_not_cue_boss_music()
    {
        Assert.Equal(40, Tuning.LesserHp);
        Assert.Equal(600, Tuning.LesserStayFrames);
        const int MaxFramesToArrive = 2000;   // past the first visit of every stage tried
        const int FramesToComeIn = 200;
        foreach (int stage in new[] { 4, 5, 8, 10 })
        {
            var game = NewGame(stage);
            var cues = AttachCueRecorder(game);
            Enemy? lesser = null;
            for (int i = 0; i < MaxFramesToArrive && lesser is null; i++) { StepQuiet(game); lesser = game.Enemies.FirstOrDefault(enemy => enemy.Lesser); }
            Assert.NotNull(lesser);
            Assert.Equal(EnemyKind.Gunship, lesser.Kind);
            Assert.True(lesser.Lesser);
            Assert.Equal(Tuning.LesserHp, lesser.Hp);
            Assert.Equal(Tuning.LesserHp, lesser.MaxHp);
            for (int i = 0; i < FramesToComeIn && game.Gate == GateState.None; i++) StepQuiet(game);
            Assert.True(lesser.Y > 0);
            Assert.DoesNotContain(cues, MusicCues.IsBoss);
            while (!lesser.Leaving && game.Gate == GateState.None) StepQuiet(game);
            Assert.InRange(lesser.Age, Tuning.LesserStayFrames, Tuning.LesserStayFrames + FrameSlack);
            Assert.DoesNotContain(cues, MusicCues.IsBoss);
        }
    }

    [Fact]
    public void A_lesser_gunship_scores_1500_and_drops_about_half_the_time_and_moves_no_gate()
    {
        const int Kills = 60;
        const double HoverX = 30, HoverY = 20;
        const double OverkillDamage = 1000;   // far more than a lesser gunship's hit points
        Assert.Equal(1500, Tuning.ScoreLesser);
        var game = NewGame(3);
        int drops = 0;
        for (int i = 0; i < Kills; i++)
        {
            game.Pickups.Clear();
            while (game.HitStop > 0) game.Step();   // the last kill froze the game
            game.Enemies.Clear();
            var lesser = game.SpawnLesser();
            lesser.Y = HoverY; lesser.X = HoverX; lesser.BaseX = HoverX; lesser.Amp = 0;
            int score = game.Score;
            EnemyKit.Shot(game, lesser.X, lesser.Y, OverkillDamage);
            game.Step();
            game.Bullets.RemoveAll(b => !b.Friendly);
            game.Enemies.Clear();
            Assert.Equal(score + Tuning.ScoreLesser, game.Score);
            if (game.Pickups.Count > 0) drops++;
        }
        Assert.InRange(drops, Kills / 4, Kills * 3 / 4);
        Assert.Equal(GateState.None, game.Gate);
        Assert.Equal(2, game.BossesBeaten);
    }

    [Fact]
    public void A_lesser_visit_that_falls_due_during_a_gate_is_dropped_not_queued()
    {
        const int GateFramesWatched = 400, AfterFightWatched = 300;
        var game = NewGame(2);
        game.StartAtBoss(2);                                // the gate is open and the visit's point is already behind
        for (int i = 0; i < GateFramesWatched; i++)
        {
            StepQuiet(game);
            Assert.DoesNotContain(game.Enemies, enemy => enemy.Lesser);
        }
        Slay(game);
        StepQuiet(game);
        for (int i = 0; i < AfterFightWatched; i++) { StepQuiet(game); Assert.DoesNotContain(game.Enemies, enemy => enemy.Lesser); }   // stage 3's visit is half a stage away
    }

    // ---------------------------------------------------------------- after boss 10

    [Fact]
    public void After_boss_ten_the_next_boss_is_boss_one_with_fifteen_percent_more_hit_points()
    {
        const double FirstLapHpMul = 1.15, SecondLapHpMul = 1.30, MostHpMul = 2.0;
        const int FirstLapStage = Tuning.StageCap + 1, FirstLapEnd = 2 * Tuning.StageCap, SecondLapStage = FirstLapEnd + 1;
        const int FarLapStage = 200, ShortOfTheGate = 2, HpMulDigits = 9;
        Assert.Equal(10, Tuning.StageCap);
        var one = Ramp.LapBoss(Ramp.GateFrame(FirstLapStage));        // the last frame of the first stage past stage 10
        Assert.Equal(1, one.Number);
        Assert.Equal(FirstLapHpMul, one.HpMul, HpMulDigits);
        var ten = Ramp.LapBoss(Ramp.GateFrame(FirstLapEnd));
        Assert.Equal(Tuning.StageCap, ten.Number);
        Assert.Equal(FirstLapHpMul, ten.HpMul, HpMulDigits);
        var again = Ramp.LapBoss(Ramp.GateFrame(SecondLapStage));
        Assert.Equal(1, again.Number);
        Assert.Equal(SecondLapHpMul, again.HpMul, HpMulDigits);
        Assert.Equal(MostHpMul, Ramp.LapBoss(Ramp.GateFrame(FarLapStage)).HpMul, HpMulDigits);

        var game = NewGame(Tuning.StageCap);
        game.StartAtBoss(Tuning.StageCap);
        UntilGate(game, GateState.Fighting);
        BossHpIsItsOwn(game);
        for (int i = 0; i < BossEntryFrames; i++) StepQuiet(game);
        for (int i = 0; i < SlayFrames && game.Gate == GateState.Fighting; i++) { Slay(game); StepQuiet(game); }   // a boss of several phases falls phase by phase
        Assert.Equal(Tuning.StageCap, game.BossesBeaten);
        Assert.Equal(Tuning.StageFrames * (long)Tuning.StageCap, game.Progress);
        Assert.Equal(Tuning.StageCap, game.Stage);
        game.SetProgress(Ramp.GateFrame(FirstLapStage) - ShortOfTheGate);
        StepQuiet(game);
        UntilGate(game, GateState.Fighting);
        Assert.Equal(Tuning.GunshipHp * FirstLapHpMul, game.Boss!.MaxHp, 6);
        Assert.Equal(Ramp.GateFrame(FirstLapStage), game.Progress);
    }

    [Fact]
    public void A_death_in_the_laps_after_stage_ten_gives_back_half_of_the_way_into_the_lap_stage_only()
    {
        const int LapIn = 400, StageIn = 200;   // frames into the stage when the ship dies
        const long LapStageStart = Tuning.StageFrames * 12L;   // the start of a stage in the laps
        const int InTheRunStage = 3;
        long progress = LapStageStart + LapIn;
        Assert.Equal(LapStageStart + LapIn / 2, Ramp.AfterDeath(progress));
        Assert.Equal(Ramp.StageStart(InTheRunStage) + StageIn / 2, Ramp.AfterDeath(Ramp.StageStart(InTheRunStage) + StageIn));
    }

    // ---------------------------------------------------------------- the unlock table

    [Fact]
    public void The_unlock_table_gives_each_stage_its_kinds()
    {
        Assert.Equal([EnemyKind.Mine], Ramp.Unlocks(1));
        Assert.Equal([EnemyKind.Diver, EnemyKind.Warden], Ramp.Unlocks(2));
        Assert.Equal([EnemyKind.Splitter, EnemyKind.Sower], Ramp.Unlocks(3));
        Assert.Equal([EnemyKind.Turret, EnemyKind.Flanker], Ramp.Unlocks(4));
        Assert.Equal([EnemyKind.Lobber], Ramp.Unlocks(5));
        Assert.Equal([EnemyKind.Nest], Ramp.Unlocks(6));
        Assert.Equal([EnemyKind.Yoke, EnemyKind.Lurk], Ramp.Unlocks(7));
        Assert.Equal([EnemyKind.Chorus], Ramp.Unlocks(8));
        Assert.Equal([EnemyKind.Glint], Ramp.Unlocks(9));
        Assert.Equal([EnemyKind.Fuse], Ramp.Unlocks(10));
        const int FirstPairedStage = 2, LastPairedStage = 4, KindsInAPair = 2;
        Assert.Empty(Ramp.Unlocks(Tuning.StageCap + 1));
        for (int stage = FirstPairedStage; stage <= LastPairedStage; stage++) Assert.Equal(KindsInAPair, Ramp.Unlocks(stage).Length);
        var all = Enumerable.Range(1, Tuning.StageCap).SelectMany(s => Ramp.Unlocks(s)).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.NotNull(RaiderGame.TableBehaviour(EnemyKind.Chorus));
        Assert.NotNull(RaiderGame.TableBehaviour(EnemyKind.Glint));
        Assert.Equal(EnemyKind.Chorus, RaiderGame.TableBehaviour(EnemyKind.Chorus)!.Kind);
        Assert.Equal(EnemyKind.Glint, RaiderGame.TableBehaviour(EnemyKind.Glint)!.Kind);
        Assert.Equal(2, Ramp.UnlockStage(EnemyKind.Warden));
        Assert.Equal(2, Ramp.UnlockStage(EnemyKind.Diver));
        Assert.Equal(3, Ramp.UnlockStage(EnemyKind.Splitter));
        Assert.Equal(4, Ramp.UnlockStage(EnemyKind.Turret));
        Assert.Equal(1, Ramp.UnlockStage(EnemyKind.Mine));
        Assert.Equal(5, Ramp.UnlockStage(EnemyKind.Lobber));
        Assert.Equal(8, Ramp.UnlockStage(EnemyKind.Chorus));
        Assert.True(Ramp.Unlocked(EnemyKind.Turret, 4));
        Assert.False(Ramp.Unlocked(EnemyKind.Turret, 3));
        Assert.True(Ramp.Unlocked(EnemyKind.Diver, 2));
        Assert.False(Ramp.Unlocked(EnemyKind.Diver, 1));
    }

    [Fact]
    public void Stage_twos_own_unlocks_are_a_diver_and_a_warden()
    {
        Assert.Equal([EnemyKind.Diver, EnemyKind.Warden], Ramp.Unlocks(2));
        Assert.Equal([EnemyKind.Diver, EnemyKind.Warden], Ramp.Unlocks(2, 0));   // both come with the stage
    }

    [Fact]
    public void The_mine_unlocks_half_way_through_stage_one_and_unlocks_and_unlocked_answer_by_progress()
    {
        long half = Tuning.StageFrames / 2;
        static EnemyKind[] UnlocksAt(long progress) => Ramp.Unlocks(Ramp.StageAt(progress), Ramp.StageShare(progress));
        static bool UnlockedAt(EnemyKind kind, long progress) => Ramp.Unlocked(kind, Ramp.StageAt(progress), Ramp.StageShare(progress));
        Assert.Equal(0.5, Ramp.UnlockShare(EnemyKind.Mine));
        Assert.Equal(0, Ramp.UnlockShare(EnemyKind.Diver));
        Assert.Empty(UnlocksAt(0));
        Assert.Empty(UnlocksAt(half - 1));
        Assert.Equal([EnemyKind.Mine], UnlocksAt(half));
        Assert.Equal([EnemyKind.Mine], UnlocksAt(Tuning.StageFrames - 1));
        Assert.Equal([EnemyKind.Diver, EnemyKind.Warden], UnlocksAt(Tuning.StageFrames));
        Assert.False(UnlockedAt(EnemyKind.Mine, 0));
        Assert.False(UnlockedAt(EnemyKind.Mine, half - 1));
        Assert.True(UnlockedAt(EnemyKind.Mine, half));
        Assert.True(UnlockedAt(EnemyKind.Mine, Ramp.StageStart(2)));
        Assert.False(UnlockedAt(EnemyKind.Diver, Tuning.StageFrames - 1));
        Assert.True(UnlockedAt(EnemyKind.Diver, Tuning.StageFrames));
        Assert.True(UnlockedAt(EnemyKind.Grunt, 0));
        Assert.Equal(0.5, Ramp.StageShare(half));
        Assert.Equal(0, Ramp.StageShare(Tuning.StageFrames));
        Assert.Equal(1, Ramp.StageShare(Ramp.StageStart(Tuning.StageCap) + Tuning.StageFrames * 3L));   // past the cap nothing more unlocks
        Assert.Equal(EnemyKind.Mine, Ramp.MidStageKind(1));
        Assert.Null(Ramp.MidStageKind(2));
        Assert.Equal("mines: fly around them", Ramp.HintFor(EnemyKind.Mine));
    }

    [Fact]
    public void Groups_per_filler_wave_are_none_then_one_in_stage_one_two_from_stage_two_and_three_from_stage_six()
    {
        Assert.Equal(0, Ramp.Specials(1, 0));
        Assert.Equal(0, Ramp.Specials(1, 0.49));
        Assert.Equal(1, Ramp.Specials(1, 0.5));
        Assert.Equal(1, Ramp.Specials(1, 0.99));
        const int TwoGroups = 2, StagesPastTheCap = 3;
        for (int stage = 2; stage < Tuning.ThreeGroupsFromStage; stage++) Assert.Equal(TwoGroups, Ramp.Specials(stage, 0));
        for (int stage = Tuning.ThreeGroupsFromStage; stage <= Tuning.StageCap + StagesPastTheCap; stage++) Assert.Equal(Tuning.SpecialsCap, Ramp.Specials(stage, 0));
        Assert.Equal(Tuning.SpecialsCap, Ramp.Specials(Tuning.StageCap));
    }

    [Fact]
    public void The_newcomer_of_a_stage_is_the_newest_of_its_kinds()
    {
        Assert.Empty(Ramp.Introduced(1));   // the mines have their own hint, part way through
        for (int stage = 2; stage <= Tuning.StageCap; stage++)
            Assert.Equal(Ramp.Unlocks(stage, 0), Ramp.Introduced(stage));   // every kind a stage starts with has a banner line
        Assert.Equal(EnemyKind.Warden, Ramp.Introduced(2)[^1]);   // the newest unlock of stage 2
        Assert.Equal("wardens shield others: kill one first", Ramp.HintFor(Ramp.Introduced(2)[^1]));
    }

    [Fact]
    public void Group_zero_is_a_stages_own_new_kinds_turn_and_turn_about()
    {
        const ulong AnySeed = 99;   // group zero takes its turn from the wave, not the seed
        const int WavesTried = 8;
        static bool All(EnemyKind _) => true;
        for (int stage = 1; stage <= Tuning.StageCap; stage++)
        {
            var own = Ramp.Unlocks(stage);
            for (int wave = 0; wave < WavesTried; wave++)
                Assert.Equal(own[wave % own.Length], WaveSpawner.PickKind(AnySeed, 0, stage, shootable: false, wave: wave, available: All, share: 1));
        }
        // a live wave never gets a mine, so it takes the other of its stage
        Assert.Equal(EnemyKind.Diver, WaveSpawner.PickKind(AnySeed, 0, 2, shootable: true, wave: 0, available: All));
        Assert.Equal(EnemyKind.Warden, WaveSpawner.PickKind(AnySeed, 0, 2, shootable: true, wave: 1, available: All));
        // with only one of the two available it is that one every time
        Assert.Equal(EnemyKind.Diver, WaveSpawner.PickKind(AnySeed, 0, 2, shootable: false, wave: 3, available: k => k != EnemyKind.Warden));
        Assert.Equal(EnemyKind.Warden, WaveSpawner.PickKind(AnySeed, 0, 2, shootable: false, wave: 3, available: k => k != EnemyKind.Diver));
        // in stage one the mine is the only unlock, and only after the half
        Assert.Null(WaveSpawner.PickKind(AnySeed, 0, 1, shootable: false, wave: 0, available: All, share: 0.49));
        Assert.Equal(EnemyKind.Mine, WaveSpawner.PickKind(AnySeed, 0, 1, shootable: false, wave: 0, available: All, share: 0.5));
    }

    [Fact]
    public void Every_further_group_draws_from_every_kind_unlocked_so_far()
    {
        const int SeedsTried = 600, StageOneSeedsTried = 50;
        const double PastTheHalf = 0.7;
        static bool All(EnemyKind _) => true;
        foreach (int stage in new[] { 2, 3, 6, 10 })
        {
            var unlocked = Enumerable.Range(1, stage).SelectMany(s => Ramp.Unlocks(s)).ToHashSet();
            var seen = new HashSet<EnemyKind>();
            for (ulong seed = 0; seed < SeedsTried; seed++)
                for (int group = 1; group < Tuning.SpecialsCap; group++)
                    if (WaveSpawner.PickKind(seed, group, stage, shootable: false, wave: 0, available: All, share: 1) is { } k) seen.Add(k);
            Assert.True(unlocked.SetEquals(seen), $"stage {stage}: drew {string.Join(",", seen)} of {string.Join(",", unlocked)}");
        }
        // stage 2: the diver and the warden of the stage itself and the mine of stage 1
        var two = Enumerable.Range(0, SeedsTried).Select(seed => WaveSpawner.PickKind((ulong)seed, 1, 2, shootable: false, available: All)).Select(k => k!.Value).Distinct().OrderBy(k => k).ToArray();
        Assert.Equal([EnemyKind.Mine, EnemyKind.Diver, EnemyKind.Warden], two);
        Assert.DoesNotContain(Enumerable.Range(0, SeedsTried).Select(seed => WaveSpawner.PickKind((ulong)seed, 1, 2, shootable: false, available: k => k != EnemyKind.Warden)), k => k == EnemyKind.Warden);
        // stage one's pool is its mine alone, once it has unlocked
        Assert.All(Enumerable.Range(0, StageOneSeedsTried), seed => Assert.Equal(EnemyKind.Mine, WaveSpawner.PickKind((ulong)seed, 1, 1, shootable: false, available: All, share: PastTheHalf)));
    }

    [Fact]
    public void A_stage_with_no_kind_of_its_own_draws_every_group_from_the_pool_and_the_newest_two_stages_count_double()
    {
        const int SeedsTried = 4000, LiveSeedsTried = 300;
        const double DoubleLow = 1.6, DoubleHigh = 2.5;   // the bounds on a double share over this many seeds
        const int PoolStage = 6, OldKinds = 4;   // a stage whose own kind the filter below leaves out, and the kinds it lets through
        static bool Old(EnemyKind k) => k is EnemyKind.Mine or EnemyKind.Diver or EnemyKind.Splitter or EnemyKind.Turret;
        var counts = new Dictionary<EnemyKind, int>();
        for (ulong seed = 0; seed < SeedsTried; seed++)
            if (WaveSpawner.PickKind(seed, 0, PoolStage, shootable: false, wave: 0, available: Old) is { } k) counts[k] = counts.GetValueOrDefault(k) + 1;
        Assert.Equal(OldKinds, counts.Count);
        Assert.InRange(counts[EnemyKind.Splitter] / (double)counts[EnemyKind.Mine], DoubleLow, DoubleHigh);   // stages 3 and 4, against 1 and 2
        Assert.InRange(counts[EnemyKind.Turret] / (double)counts[EnemyKind.Diver], DoubleLow, DoubleHigh);
        Assert.Null(WaveSpawner.PickKind(1, 0, 2, shootable: false, wave: 0, available: k => k == EnemyKind.Grunt));   // nothing to pick
        Assert.DoesNotContain(Enumerable.Range(0, LiveSeedsTried).Select(s => WaveSpawner.PickKind((ulong)s, 0, PoolStage, shootable: true, wave: 0, available: Old)), k => k == EnemyKind.Mine);
    }

    [Fact]
    public void A_filler_wave_in_stage_one_has_no_mine_before_half_the_stage_and_can_have_one_after()
    {
        const int SeedsTried = 80, WavesTried = 4;
        for (ulong seed = 0; seed < SeedsTried; seed++)
            for (int wave = 0; wave < WavesTried; wave++)
            {
                Assert.DoesNotContain(WaveSpawner.Filler(seed, 1, wave, 0).Enemies, spawn => spawn.Kind == EnemyKind.Mine);
                Assert.DoesNotContain(WaveSpawner.Filler(seed, 1, wave, 0.49).Enemies, spawn => spawn.Kind == EnemyKind.Mine);
                Assert.Equal(Tuning.MinesPerSpecial, WaveSpawner.Filler(seed, 1, wave, 0.5).Enemies.Count(spawn => spawn.Kind == EnemyKind.Mine));
                Assert.Equal(Tuning.MinesPerSpecial, WaveSpawner.Filler(seed, 1, wave, 0.9).Enemies.Count(spawn => spawn.Kind == EnemyKind.Mine));
            }
    }

    [Fact]
    public void A_filler_wave_has_two_groups_from_stage_two_three_from_stage_six_and_five_to_nine_grunts_at_stage_one()
    {
        const int SeedsPerStage = 40, StageOneSeeds = 60;
        const int FewestGrunts = 5, MostGrunts = 9, TwoGroups = 2;
        Assert.Equal(FewestGrunts, Tuning.FillerBase);
        Assert.Equal(MostGrunts, Tuning.FillerBase + Tuning.FillerGruntsSpread - 1);   // the draw falls below the spread
        for (int stage = 2; stage <= Tuning.StageCap; stage++)
            for (ulong seed = 0; seed < SeedsPerStage; seed++)
            {
                var wave = WaveSpawner.Filler(seed, stage, (int)seed);
                int groups = Ramp.Specials(stage, 0);
                Assert.Equal(stage < Tuning.ThreeGroupsFromStage ? TwoGroups : Tuning.SpecialsCap, groups);
                Assert.True(wave.Enemies.Count(spawn => spawn.Kind != EnemyKind.Grunt) >= groups, $"stage {stage} seed {seed}");
            }
        for (ulong seed = 0; seed < StageOneSeeds; seed++)
            Assert.InRange(WaveSpawner.Filler(seed, 1).Enemies.Count(spawn => spawn.Kind == EnemyKind.Grunt), FewestGrunts, MostGrunts);
    }

    [Fact]
    public void A_filler_wave_takes_its_turn_from_the_wave_number_and_the_default_is_the_first_wave()
    {
        const ulong AnySeed = 7;
        const int AnyStage = 3;
        var first = WaveSpawner.Filler(AnySeed, AnyStage, wave: 0).Enemies;
        Assert.Equal(first.Select(spawn => spawn.Kind), WaveSpawner.Filler(AnySeed, AnyStage, wave: 0).Enemies.Select(spawn => spawn.Kind));
        Assert.Equal(WaveSpawner.Filler(AnySeed, AnyStage).Enemies.Select(spawn => spawn.Kind), first.Select(spawn => spawn.Kind));
        var next = WaveSpawner.Filler(AnySeed, AnyStage, wave: 1).Enemies;   // the same seed, so only the turn differs
        Assert.NotEqual(first.Select(spawn => spawn.Kind), next.Select(spawn => spawn.Kind));
    }
}
