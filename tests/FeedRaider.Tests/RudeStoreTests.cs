using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>The store the feed's rude posts wait in until a speaker takes them.</summary>
public class RudeStoreTests
{
    private const int Cols = 120, Rows = 46;

    private static RudePost Post(ulong seed, string? text = null) =>
        new(seed, text ?? "post " + seed, DarknessSource.Label, "rude", 0);

    private static RaiderGame NewGame(bool gates = false)
    {
        var g = new RaiderGame(7) { SignedIn = true, BossGates = gates };
        g.SetLayout(FieldLayout.Compute(Cols, Rows));
        return g;
    }

    // ---------------------------------------------------------------- the store alone

    [Fact]
    public void It_holds_its_cap_of_waiting_posts_and_only_one_more_drops_the_oldest()
    {
        var s = new RudeStore();
        for (ulong i = 1; i <= Tuning.RudeWaiting; i++) Assert.True(s.Add(Post(i)));
        Assert.Equal(Tuning.RudeWaiting, s.Waiting.Count);
        Assert.Equal(1UL, s.Waiting[0].Seed);
        Assert.True(s.Add(Post(Tuning.RudeWaiting + 1)));
        Assert.Equal(Tuning.RudeWaiting, s.Waiting.Count);
        Assert.Equal(2UL, s.Waiting[0].Seed);
        Assert.Equal((ulong)Tuning.RudeWaiting + 1, s.Waiting[^1].Seed);
        Assert.False(s.Holds(1));
    }

    [Fact]
    public void It_refuses_a_seed_it_holds()
    {
        var s = new RudeStore();
        Assert.True(s.Add(Post(9)));
        Assert.False(s.Add(Post(9, "the same again")));
        Assert.Single(s.Waiting);
        Assert.Equal("post 9", s.Waiting[0].Text);
    }

    [Fact]
    public void It_refuses_a_seed_it_has_seen_spoken()
    {
        var s = new RudeStore();
        s.Add(Post(9));
        var taken = s.Take(0);
        Assert.Equal(9UL, taken!.Seed);
        Assert.Empty(s.Waiting);
        Assert.True(s.Holds(9));
        Assert.False(s.Add(Post(9)));
        Assert.Empty(s.Waiting);
    }

    [Fact]
    public void It_remembers_the_seeds_of_the_last_sixty_four_taken_and_no_more()
    {
        var s = new RudeStore();
        for (ulong i = 1; i <= Tuning.RudeSeen + 1; i++) { s.Add(Post(i)); s.Take(0); }
        Assert.False(s.Holds(1));
        Assert.True(s.Holds(2));
        Assert.True(s.Holds(Tuning.RudeSeen + 1));
        Assert.True(s.Add(Post(1)));
        Assert.False(s.Add(Post(2)));
    }

    [Fact]
    public void Holds_is_true_for_a_post_waiting_or_spoken_and_false_for_a_stranger()
    {
        var s = new RudeStore();
        s.Add(Post(1)); s.Add(Post(2));
        s.Take(1);
        Assert.True(s.Holds(1));   // waiting
        Assert.True(s.Holds(2));   // spoken
        Assert.False(s.Holds(3));
    }

    [Fact]
    public void Take_removes_the_chosen_post_and_a_bad_index_takes_nothing()
    {
        var s = new RudeStore();
        s.Add(Post(1)); s.Add(Post(2)); s.Add(Post(3));
        Assert.Equal(2UL, s.Take(1)!.Seed);
        Assert.Equal([1UL, 3UL], s.Waiting.Select(p => p.Seed).ToArray());
        Assert.Null(s.Take(2));
        Assert.Null(s.Take(-1));
        Assert.Equal(2, s.Waiting.Count);
    }

    [Fact]
    public void The_last_eight_spoken_are_the_echoes_newest_first()
    {
        var s = new RudeStore();
        Assert.Empty(s.Echoes);
        for (ulong i = 1; i <= Tuning.RudeEchoes + 3; i++) { s.Add(Post(i)); s.Take(0); }
        Assert.Equal(Tuning.RudeEchoes, s.Echoes.Count);
        Assert.Equal([11UL, 10, 9, 8, 7, 6, 5, 4], s.Echoes.Select(p => p.Seed).ToArray());
        Assert.Equal("post 11", s.Echoes[0].Text);
    }

    // ---------------------------------------------------------------- the heard list and recall

    [Fact]
    public void It_keeps_the_last_thirty_two_heard_in_the_order_said_and_a_thirty_third_drops_the_oldest()
    {
        Assert.Equal(32, Tuning.RudeHeard);
        var s = new RudeStore();
        for (ulong i = 1; i <= Tuning.RudeHeard; i++) { s.Add(Post(i)); s.Take(0); }
        Assert.Equal(Tuning.RudeHeard, s.Heard.Count);
        Assert.Equal(1UL, s.Heard[0].Seed);
        s.Add(Post(Tuning.RudeHeard + 1)); s.Take(0);
        Assert.Equal(Tuning.RudeHeard, s.Heard.Count);
        Assert.Equal(2UL, s.Heard[0].Seed);
        Assert.Equal((ulong)Tuning.RudeHeard + 1, s.Heard[^1].Seed);
    }

    private static RudeStore Heard(int posts)
    {
        var s = new RudeStore();
        for (ulong i = 1; i <= (ulong)posts; i++) { s.Add(Post(i)); s.Take(0); }
        return s;
    }

    private static int Oldest(IReadOnlyList<RudePost> offered) => 0;

    private static int Newest(IReadOnlyList<RudePost> offered) => offered.Count - 1;

    [Fact]
    public void Recall_goes_round_every_heard_post_before_it_says_one_again()
    {
        var s = Heard(3);
        // the three were said as they were taken, so the first round is spent: a fresh one starts, and a second after it
        ulong[] twoRounds = [1, 2, 3, 1, 2, 3];
        Assert.Equal(twoRounds, twoRounds.Select(_ => s.Recall(Oldest, null)!.Seed).ToArray());
        Assert.Empty(s.Waiting);
        Assert.Equal(3, s.Heard.Count);
    }

    [Fact]
    public void Recall_picks_by_the_taste_it_is_given_and_holds_a_wild_index_to_the_list()
    {
        const int Wild = 99;
        var s = Heard(3);
        Assert.Equal(3UL, s.Recall(Newest, null)!.Seed);
        Assert.Equal(2UL, s.Recall(_ => Wild, null)!.Seed);
        Assert.Equal(1UL, s.Recall(_ => -Wild, null)!.Seed);
    }

    [Fact]
    public void Recall_never_hands_out_the_post_on_show_and_gives_nothing_when_no_other_is_known()
    {
        var s = Heard(2);
        Assert.Equal(2UL, s.Recall(Oldest, "post 1")!.Seed);
        Assert.Equal(1UL, s.Recall(Oldest, "post 2")!.Seed);   // post 1 is the only one left in the round
        Assert.Equal(2UL, s.Recall(Oldest, "post 1")!.Seed);   // the round starts over without the one on show
        var one = Heard(1);
        Assert.Null(one.Recall(Oldest, "post 1"));
        Assert.Equal(1UL, one.Recall(Oldest, null)!.Seed);
        Assert.Null(new RudeStore().Recall(Oldest, null));
    }

    [Fact]
    public void A_post_taken_fresh_counts_as_said_in_the_round()
    {
        var s = Heard(2);
        Assert.Equal(1UL, s.Recall(Oldest, null)!.Seed);
        s.Add(Post(3)); s.Take(0);
        Assert.Equal(2UL, s.Recall(Oldest, null)!.Seed);
        Assert.Equal(1UL, s.Recall(Oldest, null)!.Seed);   // 3 was said when it was taken, so the round ended with 2
    }

    [Fact]
    public void A_rude_post_spawns_nothing_and_waits_in_the_store()
    {
        var g = NewGame();
        var post = new DarknessPost(DarknessKind.Rude, 11, "you are all idiots", DarknessSource.Label, "rude");
        Assert.Empty(WaveSpawner.FromPost(post, 1).Enemies);
        g.OfferDarkness(post);
        Assert.Single(g.Rude.Waiting);
        Assert.Equal("you are all idiots", g.Rude.Waiting[0].Text);
        Assert.Equal(11UL, g.Rude.Waiting[0].Seed);
        for (int i = 0; i < 1500; i++)
        {
            EnemyKit.StepSweeping(g);
            Assert.DoesNotContain(g.Enemies, e => e.Text is not null || e.Kind == EnemyKind.Gunship);
        }
        Assert.Single(g.Rude.Waiting);   // nobody to take it
    }

    [Fact]
    public void A_spam_post_still_launches_its_wave_and_never_reaches_the_store()
    {
        var g = NewGame();
        g.OfferDarkness(new DarknessPost(DarknessKind.Spam, 12, "buy my coin", DarknessSource.Label, "spam"));
        Assert.Empty(g.Rude.Waiting);
        bool carried = false;
        for (int i = 0; i < 1500 && !carried; i++) { g.Step(); carried = g.Enemies.Any(e => e.Text == "buy my coin"); }
        Assert.True(carried);
    }

    [Fact]
    public void A_demo_rude_post_takes_the_same_road()
    {
        var g = NewGame();
        g.OfferDarkness(new DarknessPost(DarknessKind.Rude, 13, "demo words", DarknessSource.Demo));
        Assert.Single(g.Rude.Waiting);
        Assert.Equal(DarknessSource.Demo, g.Rude.Waiting[0].Source);
    }

    [Fact]
    public void The_stored_text_is_cleaned_like_the_gunships_was()
    {
        var g = NewGame();
        g.OfferDarkness(new DarknessPost(DarknessKind.Rude, 14, "  tabs\tand   gaps  ", DarknessSource.Label, "rude"));
        Assert.Single(g.Rude.Waiting);
        Assert.Equal(DarknessText.Clean("  tabs\tand   gaps  ", DarknessText.MaxPostChars), g.Rude.Waiting[0].Text);
    }

    [Fact]
    public void A_post_offered_twice_is_stored_once()
    {
        var g = NewGame();
        var post = new DarknessPost(DarknessKind.Rude, 15, "say it once", DarknessSource.Label, "rude");
        g.OfferDarkness(post); g.OfferDarkness(post);
        Assert.Single(g.Rude.Waiting);
    }

    [Fact]
    public void Nothing_is_lost_on_a_stage_change_a_death_or_a_gate()
    {
        var g = NewGame(gates: true);
        for (ulong i = 1; i <= 5; i++) g.OfferDarkness(new DarknessPost(DarknessKind.Rude, i, "post " + i, DarknessSource.Label, "rude"));
        g.StartAtStage(3);
        EnemyKit.StepSweeping(g, 5);
        Assert.Equal(5, g.Rude.Waiting.Count);

        int deaths = g.Deaths;
        for (int i = 0; i < 400 && g.Deaths == deaths; i++)
        {
            if (g.Alive && g.Invuln == 0) g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY, VX = 0, VY = 0, Friendly = false });
            g.Step();
            g.Enemies.Clear();   // no speaker ever gets to arrive
        }
        Assert.Equal(deaths + 1, g.Deaths);
        Assert.Equal(5, g.Rude.Waiting.Count);

        g.StartAtBoss(2);   // the warning of a boss: no boss is on the field yet, so nobody speaks
        Assert.Equal(GateState.Warning, g.Gate);
        EnemyKit.StepSweeping(g, 5);
        Assert.Equal(5, g.Rude.Waiting.Count);
    }
}
