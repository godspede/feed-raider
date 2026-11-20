using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The music state machine: pure snapshots and a fake clock for the director, and the real sim for the wiring.
public class MusicTests
{
    private static readonly MusicSnapshot Quiet = new(null);

    private static (MusicDirector Dir, List<string> Cues) New()
    {
        var cues = new List<string>();
        return (new MusicDirector(cues.Add), cues);
    }

    /// <summary>Feeds the same snapshot every half second from <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
    private static void Hold(MusicDirector d, MusicSnapshot s, double from, double to)
    {
        for (double t = from; t <= to; t += 0.5) d.Update(s);
    }

    [Fact]
    public void First_update_cues_gameplay_and_an_unchanged_state_never_repeats()
    {
        var (d, cues) = New();
        Hold(d, Quiet, 0, 120);
        Assert.Equal(["gameplay"], cues);
    }

    [Fact]
    public void The_start_cue_is_the_page_s_job_then_gameplay_boss_gameplay_and_gameover_at_the_run_end()
    {
        var (d, cues) = New();
        Hold(d, Quiet, 0, 5);
        Hold(d, Quiet with { Boss = "gunship" }, 5.5, 20);
        Assert.Equal(["gameplay", "boss:gunship"], cues);
        Hold(d, Quiet, 20.5, 30);
        Assert.Equal(["gameplay", "boss:gunship", "gameplay"], cues);
        d.End();
        Hold(d, Quiet, 30.5, 60);                                       // nothing brings gameplay back into an ended run
        Assert.Equal(["gameplay", "boss:gunship", "gameplay", "gameover"], cues);
    }

    [Fact]
    public void A_boss_cue_names_the_boss_and_a_different_boss_cues_again()
    {
        var (d, cues) = New();
        Hold(d, Quiet, 0, 2);
        Hold(d, Quiet with { Boss = "gunship" }, 2.5, 8);
        Hold(d, Quiet with { Boss = "gunship" }, 8.5, 12);
        Hold(d, Quiet, 12.5, 15);
        Hold(d, Quiet with { Boss = "gunship" }, 15.5, 20);                 // a returning boss on a later lap cues its own key again
        Hold(d, Quiet with { Boss = "bulwark" }, 20.5, 25);
        Assert.Equal(["gameplay", "boss:gunship", "gameplay", "boss:gunship", "boss:bulwark"], cues);
    }

    [Fact]
    public void The_hint_names_the_stage_s_boss_once_and_again_only_when_the_name_changes()
    {
        var (d, cues) = New();
        Hold(d, Quiet with { Ahead = "gunship" }, 0, 5);
        Hold(d, Quiet with { Ahead = "gunship", Boss = "gunship" }, 5.5, 10);
        Hold(d, Quiet with { Ahead = "bulwark" }, 10.5, 15);
        Hold(d, Quiet with { Ahead = null }, 15.5, 20);                     // no hint, nothing said
        Assert.Equal(["gameplay", "ahead:boss:gunship", "boss:gunship", "gameplay", "ahead:boss:bulwark"], cues);
        d.End();
        d.Update(Quiet with { Ahead = "shutter" });                         // an ended run hints at nothing
        Assert.Equal("gameover", cues[^1]);
    }

    [Fact]
    public void The_hint_is_not_a_music_cue_so_it_leaves_the_current_one_alone()
    {
        var (d, cues) = New();
        Hold(d, Quiet, 0, 2);
        Hold(d, Quiet with { Ahead = "thread" }, 2.5, 4);
        Assert.Equal("gameplay", d.Current);
        Assert.Equal(["gameplay", "ahead:boss:thread"], cues);
    }

    // ---------------------------------------------------------------- the real game

    private static RaiderGame NewGame(out List<string> cues)
    {
        var list = cues = new List<string>();
        var g = new RaiderGame(5);
        g.SetLayout(FieldLayout.Compute(120, 46));
        g.Music = new MusicDirector(list.Add);
        return g;
    }

    [Fact]
    public void The_game_cues_gameplay_first_and_the_boss_when_a_boss_is_announced()
    {
        var g = NewGame(out var cues);
        for (int i = 0; i < 30; i++) g.Step();
        Assert.Equal("gameplay", cues[0]);
        g.StartAtBoss(1);   // the warning cues the boss track
        for (int i = 0; i < 600 && !cues.Contains("boss:gunship"); i++) g.Step();
        Assert.Contains("boss:gunship", cues);
        Assert.DoesNotContain(cues.Zip(cues.Skip(1)), p => p.First == p.Second);   // never the same cue twice in a row
    }

    [Fact]
    public void The_game_keeps_its_music_when_the_ship_dies_and_reports_the_best_score()
    {
        var g = NewGame(out var cues);
        var saved = new List<int>();
        g.OnBest = saved.Add;
        g.BestToBeat = 0;
        g.Input.Key(GameKey.Fire, true);
        for (int i = 0; i < 2000 && g.Deaths == 0; i++) g.Step();   // sitting still, the filler waves hit the ship
        Assert.True(g.Deaths > 0);
        Assert.Equal(["gameplay"], cues.Where(c => !MusicCues.IsAhead(c)));        // the hint is no state change
        Assert.NotEmpty(saved);
        Assert.True(saved[^1] > 0, "a best score was saved, not nothing");
    }

    [Fact]
    public void A_killed_live_wave_counts_as_cleared_and_cues_nothing()
    {
        var g = NewGame(out var cues);
        ClearALiveWave(g);
        Assert.Equal(1, g.LiveWavesCleared);
        Assert.DoesNotContain("victory", cues);
        Assert.All(cues, c => Assert.Contains(c, new[] { "gameplay", "boss:gunship", "gameover", "ahead:boss:gunship" }));
    }

    [Fact]
    public void A_cleared_wave_and_a_death_cause_no_cue()
    {
        var g = NewGame(out var cues);
        ClearALiveWave(g);
        Assert.Equal(1, g.LiveWavesCleared);
        g.Input.Key(GameKey.Fire, false);
        int deaths = g.Deaths;
        Die(g, static g => g.Step());
        Assert.Equal(deaths + 1, g.Deaths);
        Assert.Equal(["gameplay"], cues.Where(c => !MusicCues.IsAhead(c)));
    }

    [Fact]
    public void A_death_changes_no_music_in_gameplay_or_in_a_boss_fight()
    {
        var g = NewGame(out var cues);
        for (int i = 0; i < 5; i++) g.Step();
        Die(g, static g => g.Step());
        Assert.Equal(["gameplay"], cues.Where(c => !MusicCues.IsAhead(c)));
        for (int i = 0; i < Tuning.RespawnFrames && !g.Alive; i++) g.Step();
        g.StartAtBoss(1);
        for (int i = 0; i < Tuning.BossWarnFrames + 3 && g.Gate != GateState.Fighting; i++) StepWithoutThreats(g);
        Assert.Equal(GateState.Fighting, g.Gate);
        Die(g, StepWithoutThreats);
        Assert.Equal(2, g.Deaths);
        Assert.Equal(GateState.Fighting, g.Gate);
        Assert.Equal(["gameplay", "boss:gunship"], cues.Where(c => !MusicCues.IsAhead(c)));
        g.Music!.End();
        g.Music.End();
        Assert.Equal(["gameplay", "boss:gunship", "gameover"], cues.Where(c => !MusicCues.IsAhead(c)));
    }

    /// <summary>Fires and follows the lowest enemy until one dark post's wave is killed.</summary>
    private static void ClearALiveWave(RaiderGame g)
    {
        g.OfferDark(new DarkPost(DarknessKind.Spam, 3, "buy followers now cheap"));
        g.Input.Key(GameKey.Fire, true);
        for (int i = 0; i < 9000 && g.LiveWavesCleared == 0; i++)
        {
            var t = g.Enemies.Where(e => e.Hp > 0).OrderByDescending(e => e.Y).FirstOrDefault();   // enemies sidestep a lone lane of fire: follow one
            g.Input.Key(GameKey.Left, t is not null && t.X < g.PX - 2); g.Input.Key(GameKey.Right, t is not null && t.X > g.PX + 2);
            g.Step();
        }
    }

    /// <summary>Lands a bolt on the ship each time it can be hurt, stepping with <paramref name="step"/>, until it dies once.</summary>
    private static void Die(RaiderGame g, Action<RaiderGame> step)
    {
        int deaths = g.Deaths;
        for (int i = 0; i < Tuning.ShipHp * (Tuning.InvulnFrames + 1) && g.Deaths == deaths; i++)
        {
            if (g.Alive && g.Invuln == 0) g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY, VX = 0, VY = 0, Friendly = false });
            step(g);
        }
    }

    // ---------------------------------------------------------------- bosses by stage

    /// <summary>A game in which every new kind is scripted, so a stage's own boss fights.</summary>
    private static RaiderGame ScriptedGame(out List<string> cues, out List<string> sounds)
    {
        var g = NewGame(out cues);
        g.BehaviourOverride = k => k >= EnemyKind.Gnat ? new ScriptBehaviour(k) { IsBoss = Array.IndexOf(BossKinds, k) >= 0 } : null;
        var played = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(n));
        sounds = played;
        return g;
    }

    private static readonly EnemyKind[] BossKinds =
    [
        EnemyKind.Bulwark, EnemyKind.Shutter, EnemyKind.Thread, EnemyKind.Spotlight, EnemyKind.Brood,
        EnemyKind.Schism, EnemyKind.PileOn, EnemyKind.Mirror, EnemyKind.Doomscroll,
    ];

    private static readonly string[] BossKeys = ["gunship", "bulwark", "shutter", "thread", "spotlight", "brood", "schism", "pileon", "mirror", "doomscroll"];

    private static void StepWithoutThreats(RaiderGame g)
    {
        g.Step();
        g.Bullets.RemoveAll(b => !b.Friendly);
        g.Enemies.RemoveAll(e => e != g.Boss && !e.Lesser);
    }

    private static int BossCues(List<string> cues) => cues.Count(c => c.StartsWith("boss:", StringComparison.Ordinal));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(20)]
    [InlineData(21)]
    public void Each_stage_s_warning_cues_that_stage_s_boss_once_and_gameplay_when_the_fight_ends_in_a_kill(int stage)
    {
        var g = ScriptedGame(out var cues, out _);
        for (int i = 0; i < 5; i++) g.Step();
        g.StartAtBoss(stage);
        string key = BossKeys[Ramp.BossNumber(stage) - 1];
        Assert.Equal(GateState.Warning, g.Gate);
        for (int i = 0; i < Tuning.BossWarnFrames + 3 && g.Gate != GateState.Fighting; i++) StepWithoutThreats(g);
        Assert.Equal(GateState.Fighting, g.Gate);
        for (int i = 0; i < 30; i++) StepWithoutThreats(g);
        Assert.Equal(1, cues.Count(c => c == "boss:" + key));
        Assert.Equal(1, BossCues(cues));
        Assert.Equal("boss:" + key, g.Music!.Current);
        EnemyKit.Shot(g, g.Boss!.X, g.Boss.Y, 100000);
        for (int i = 0; i < 5; i++) StepWithoutThreats(g);
        Assert.Equal(GateState.None, g.Gate);
        Assert.Equal("gameplay", g.Music.Current);
        Assert.Equal(1, BossCues(cues));
    }

    [Fact]
    public void The_boss_cue_comes_with_the_warning_banner_not_when_the_boss_appears()
    {
        var g = ScriptedGame(out var cues, out var sounds);
        for (int i = 0; i < 5; i++) g.Step();
        g.StartAtBoss(2);
        g.Step();
        Assert.Equal(GateState.Warning, g.Gate);
        Assert.Null(g.Boss);
        Assert.Equal("boss:bulwark", g.Music!.Current);
        Assert.Contains(SfxNames.Warn, sounds);
    }

    [Fact]
    public void A_boss_that_leaves_ends_the_boss_music_too()
    {
        var g = ScriptedGame(out var cues, out _);
        for (int i = 0; i < 5; i++) g.Step();
        g.StartAtBoss(3);
        for (int i = 0; i < Tuning.BossWarnFrames + Tuning.BossStayFrames + 10 && g.BossesBeaten < 3; i++) StepWithoutThreats(g);
        Assert.Equal(3, g.BossesBeaten);
        StepWithoutThreats(g);
        Assert.Equal("gameplay", g.Music!.Current);
        Assert.Equal(["boss:shutter"], cues.Where(c => c.StartsWith("boss:", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void A_death_in_the_warning_cancels_the_boss_music_and_the_next_warning_cues_it_again()
    {
        var g = ScriptedGame(out var cues, out _);
        for (int i = 0; i < 5; i++) g.Step();
        // wound the ship to its last hit point first: the pause after a hit is as long as the warning, so two hits cannot both land inside it
        for (int i = 0; i < 400 && (g.Hp > 1 || g.Invuln > 0); i++)
        {
            if (g.Hp > 1 && g.Invuln == 0) g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY, VX = 0, VY = 0, Friendly = false });
            g.Step();
        }
        Assert.True(g.Alive);
        g.StartAtBoss(4);
        g.Step();
        Assert.Equal("boss:thread", g.Music!.Current);
        for (int i = 0; i < 400 && g.Gate == GateState.Warning; i++)
        {
            if (g.Alive && g.Invuln == 0) g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY, VX = 0, VY = 0, Friendly = false });
            g.Step();
        }
        Assert.NotEqual(GateState.Warning, g.Gate);
        g.Step();
        Assert.Equal("gameplay", g.Music.Current);
        g.StartAtBoss(4);
        g.Step();
        Assert.Equal("boss:thread", g.Music.Current);
        Assert.Equal(2, cues.Count(c => c == "boss:thread"));
    }

    [Fact]
    public void The_hint_names_the_boss_of_the_stage_that_begins_and_again_when_the_next_stage_begins()
    {
        var g = ScriptedGame(out var cues, out _);
        for (int i = 0; i < 5; i++) g.Step();
        Assert.Equal(["gameplay", "ahead:boss:gunship"], cues);
        g.StartAtBoss(1);
        for (int i = 0; i < Tuning.BossWarnFrames + 3 && g.Gate != GateState.Fighting; i++) StepWithoutThreats(g);
        Assert.Equal(GateState.Fighting, g.Gate);
        Assert.DoesNotContain("ahead:boss:bulwark", cues);                  // stage 1 is still on
        for (int i = 0; i < 30; i++) StepWithoutThreats(g);
        EnemyKit.Shot(g, g.Boss!.X, g.Boss.Y, 100000);
        for (int i = 0; i < 5; i++) StepWithoutThreats(g);
        Assert.Equal(2, g.Stage);
        Assert.Equal(1, cues.Count(c => c == "ahead:boss:bulwark"));
        Assert.True(cues.IndexOf("ahead:boss:bulwark") > cues.IndexOf("boss:gunship"));
    }

    [Fact]
    public void With_gates_off_there_is_no_hint_and_a_gunship_on_the_field_still_cues_the_gunship_key()
    {
        var cues = new List<string>();
        var g = new RaiderGame(5) { BossGates = false };
        g.SetLayout(FieldLayout.Compute(120, 46));
        g.Music = new MusicDirector(cues.Add);
        for (int i = 0; i < 10; i++) g.Step();
        var ship = g.SpawnKind(EnemyKind.Gunship, 0.5);
        ship.Y = 5;
        g.Step();
        Assert.Equal(["gameplay", "boss:gunship"], cues);
    }

    // ---------------------------------------------------------------- the sting for a visitor

    [Theory]
    [InlineData(EnemyKind.Warden)]
    [InlineData(EnemyKind.Nest)]
    [InlineData(EnemyKind.Yoke)]
    [InlineData(EnemyKind.Chorus)]
    public void A_mini_boss_arrives_to_the_sting_and_the_music_does_not_move(EnemyKind kind)
    {
        var g = ScriptedGame(out var cues, out var sounds);
        for (int i = 0; i < 5; i++) g.Step();
        var before = cues.ToArray();
        g.SpawnKind(kind, 0.5);
        g.Step();
        Assert.Equal(1, sounds.Count(n => n == SfxNames.Sting));
        Assert.Equal(before, cues.ToArray());
        Assert.Equal("gameplay", g.Music!.Current);
    }

    [Fact]
    public void A_lesser_gunship_arrives_to_the_sting_and_the_music_does_not_move()
    {
        var g = ScriptedGame(out var cues, out var sounds);
        for (int i = 0; i < 5; i++) g.Step();
        var before = cues.ToArray();
        g.SpawnLesser();
        for (int i = 0; i < 10; i++) g.Step();
        Assert.Equal(1, sounds.Count(n => n == SfxNames.Sting));
        Assert.Equal(before, cues.ToArray());
    }

    [Fact]
    public void An_ordinary_arrival_and_the_boss_itself_make_no_sting()
    {
        var g = ScriptedGame(out _, out var sounds);
        for (int i = 0; i < 5; i++) g.Step();
        g.SpawnKind(EnemyKind.Grunt, 0.3);
        g.SpawnKind(EnemyKind.Gnat, 0.6);
        g.StartAtBoss(1);
        for (int i = 0; i < Tuning.BossWarnFrames + 5; i++) StepWithoutThreats(g);
        Assert.NotNull(g.Boss);
        Assert.DoesNotContain(SfxNames.Sting, sounds);
    }

    [Fact]
    public void The_sting_does_not_use_the_random_stream_so_a_run_is_the_same_with_or_without_a_bus()
    {
        static (long, int, int) Run(bool bus)
        {
            var g = new RaiderGame(9);
            g.SetLayout(FieldLayout.Compute(120, 46));
            if (bus) g.Sfx = new SfxBus((_, _, _, _) => { });
            for (int i = 0; i < 20; i++) g.Step();
            g.SpawnLesser();
            g.SpawnKind(EnemyKind.Gnat, 0.4);
            for (int i = 0; i < 300; i++) g.Step();
            return (g.Frame, g.Enemies.Count, g.Score);
        }
        Assert.Equal(Run(false), Run(true));
    }
}
