using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The music state machine: pure snapshots and a fake clock for the director, and the real sim for the wiring.
public class MusicTests
{
    private static readonly MusicSnapshot Quiet = new(0, false, 3, 0, 0, 0, 0);

    private static (MusicDirector Dir, List<string> Cues) New()
    {
        var cues = new List<string>();
        return (new MusicDirector(cues.Add), cues);
    }

    /// <summary>Feeds the same snapshot every half second from <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
    private static void Hold(MusicDirector d, MusicSnapshot s, double from, double to)
    {
        for (double t = from; t <= to; t += 0.5) d.Update(s, t);
    }

    [Fact]
    public void First_update_cues_gameplay_and_an_unchanged_state_never_repeats()
    {
        var (d, cues) = New();
        Hold(d, Quiet, 0, 120);
        Assert.Equal(["gameplay"], cues);
    }

    [Fact]
    public void Menu_is_the_page_s_job_then_gameplay_boss_gameplay_and_gameover_at_the_run_end()
    {
        var (d, cues) = New();
        Hold(d, Quiet, 0, 5);
        Hold(d, Quiet with { Boss = true }, 5.5, 20);
        Assert.Equal(["gameplay", "boss"], cues);
        Hold(d, Quiet, 20.5, 30);
        Assert.Equal(["gameplay", "boss", "gameplay"], cues);
        d.End();
        Hold(d, Quiet, 30.5, 60);                                       // nothing brings gameplay back into an ended run
        Assert.Equal(["gameplay", "boss", "gameplay", "gameover"], cues);
    }

    [Fact]
    public void A_death_changes_no_music_in_gameplay_or_in_a_boss_fight()
    {
        var (d, cues) = New();
        Hold(d, Quiet, 0, 5);
        Hold(d, Quiet with { Deaths = 1 }, 5.5, 20);
        Assert.Equal(["gameplay"], cues);
        Hold(d, Quiet with { Deaths = 1, Boss = true }, 20.5, 25);
        Hold(d, Quiet with { Deaths = 2, Boss = true }, 25.5, 40);
        Assert.Equal(["gameplay", "boss"], cues);
        d.End();
        d.End();
        Assert.Equal(["gameplay", "boss", "gameover"], cues);
    }

    [Fact]
    public void Short_events_cause_no_cue()
    {
        var (d, cues) = New();
        Hold(d, Quiet, 0, 5);
        Hold(d, Quiet with { Wings = 1 }, 5.5, 15);                    // an ally arrives
        Hold(d, Quiet with { Wings = 1, Hp = 2 }, 15.5, 25);           // a life is lost, the ship lives on
        Hold(d, Quiet with { Wings = 1, Hp = 1, LiveWavesCleared = 1 }, 25.5, 35);   // a wave is cleared
        Hold(d, Quiet with { Wings = 0, Hp = 1, LiveWavesCleared = 1, Score = 900, Record = 500 }, 35.5, 60);   // a record falls
        Assert.Equal(["gameplay"], cues);
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
    public void The_game_cues_gameplay_first_and_the_boss_when_a_gunship_arrives()
    {
        var g = NewGame(out var cues);
        for (int i = 0; i < 30; i++) g.Step();
        Assert.Equal("gameplay", cues[0]);
        g.OfferDark(new DarkPost(DarknessKind.Rude, 4, "some rude words for the gunship"));
        for (int i = 0; i < 600 && !cues.Contains("boss"); i++) g.Step();
        Assert.Contains("boss", cues);
        Assert.DoesNotContain(cues.Zip(cues.Skip(1)), p => p.First == p.Second);   // never the same cue twice in a row
    }

    [Fact]
    public void The_game_keeps_its_music_when_the_player_dies_and_reports_the_best_score()
    {
        var g = NewGame(out var cues);
        var saved = new List<int>();
        g.OnBest = saved.Add;
        g.RecordToBeat = 0;
        g.Input.Key(GameKey.Fire, true);
        for (int i = 0; i < 2000 && g.Deaths == 0; i++) g.Step();   // sitting still, the patrols hit the player
        Assert.True(g.Deaths > 0);
        Assert.Equal(["gameplay"], cues);
        Assert.True(saved.Count == 0 || saved[^1] > 0);
    }

    [Fact]
    public void A_shot_down_live_wave_counts_as_cleared_and_cues_nothing()
    {
        var g = NewGame(out var cues);
        g.OfferDark(new DarkPost(DarknessKind.Spam, 3, "buy followers now cheap"));
        g.Input.Key(GameKey.Fire, true);
        for (int i = 0; i < 3000 && g.LiveWavesCleared == 0; i++) g.Step();
        Assert.Equal(1, g.LiveWavesCleared);
        Assert.DoesNotContain("victory", cues);
        Assert.All(cues, c => Assert.Contains(c, new[] { "gameplay", "boss", "gameover" }));
    }
}
