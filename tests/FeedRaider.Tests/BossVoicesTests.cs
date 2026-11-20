using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;
using static FeedRaider.Tests.DarknessSourcesTests;

namespace FeedRaider.Tests;

/// <summary>What a run hears once rude posts are known: every boss keeps saying them at its own pace, and a later speaker knows what an earlier one said.</summary>
public class BossVoicesTests
{
    /// <summary>The ids the three posts are offered under, each the same wherever that post is offered.</summary>
    private const ulong ShortId = 1, LoudId = 2, LongId = 3;
    /// <summary>Where a speaker arrives in a test: the field's middle, as a share of its width.</summary>
    private const double Middle = 0.5;

    private const string Short = "short one";
    private const string Loud = "A MIDDLE POST THAT SHOUTS A BIT!!";
    private static readonly string Long = "the long one " + string.Join(' ', Enumerable.Repeat("and more", 30));

    /// <summary>A signed-in run that has heard three posts and has none waiting.</summary>
    private static RaiderGame HeardThree(bool gates = true)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = gates };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.OfferDark(new DarkPost(DarknessKind.Rude, ShortId, Short, DarkSource.Label, Rude));
        g.OfferDark(new DarkPost(DarknessKind.Rude, LoudId, Loud, DarkSource.Hidden, null));
        g.OfferDark(new DarkPost(DarknessKind.Rude, LongId, Long, DarkSource.Label, Rude));
        while (g.Rude.Waiting.Count > 0) g.Rude.Take(0);
        return g;
    }

    /// <summary>Steps a frame and clears the hostile bullets, so the ship lives through the fight.</summary>
    private static void StepUnharmed(RaiderGame g)
    {
        g.Step();
        g.Bullets.RemoveAll(b => !b.Friendly);
    }

    /// <summary>Frames a test waits for a boss to come in and speak before it gives up.</summary>
    private const int ArriveLimit = 600;

    /// <summary>Seconds a boss may be silent beyond its pace: the box's closing frame and the turn's first, and the Shutter's wait for an eye to open.</summary>
    private const int SlackSeconds = 1;

    /// <summary>The fewest posts a five-minute fight shows, whichever boss: one every twenty-five seconds.</summary>
    private const int FewestPosts = 12;

    /// <summary>Each boss's pace: the seconds its box stays shut before the next post.</summary>
    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 10)]
    [InlineData(3, 3)]
    [InlineData(4, 8)]
    [InlineData(5, 6)]
    [InlineData(6, 5)]
    [InlineData(7, 4)]
    [InlineData(8, 6)]
    [InlineData(9, 6)]
    [InlineData(10, 6)]
    public void With_three_posts_known_and_none_waiting_a_five_minute_fight_keeps_showing_them_at_the_boss_pace(int stage, int paceSeconds)
    {
        var g = HeardThree();
        g.StartAtBoss(stage);
        int fought = 0, shut = 0, longestShut = 0, posts = 0;
        bool open = false;
        string? last = null;
        var shown = new HashSet<string>();
        for (int frame = 0; frame < Tuning.BossStayFrames + ArriveLimit && fought < Tuning.BossStayFrames; frame++)
        {
            StepUnharmed(g);
            if (g.Gate != GateState.Fighting || g.Boss is not { } boss) continue;
            fought++;
            bool speaking = EnemySpeechBox.IsOpen(boss);
            if (!speaking) longestShut = Math.Max(longestShut, ++shut);
            else
            {
                shut = 0;
                if (!open || boss.Text != last)
                {
                    Assert.NotEqual(last, boss.Text);   // never the same post twice running while another is known
                    posts++;
                    last = boss.Text;
                    shown.Add(boss.Text!);
                    Assert.Equal(boss.Text == Loud ? "HIDDEN REPLY" : "RUDE", boss.Header);
                }
            }
            open = speaking;
        }
        Assert.Equal(Tuning.BossStayFrames, fought);
        Assert.InRange(longestShut, 0, (paceSeconds + SlackSeconds) * Tuning.Fps);
        Assert.Equal([Loud, Short, Long], shown.Order().ToArray());
        Assert.InRange(posts, FewestPosts, int.MaxValue);
    }

    [Fact]
    public void The_default_pace_is_six_seconds_and_no_boss_is_slower_than_ten()
    {
        const int DefaultSeconds = 6, SlowestSeconds = 10;
        Assert.Equal(DefaultSeconds * Tuning.Fps, Tuning.VoiceIdleFrames);
        foreach (var kind in Enum.GetValues<EnemyKind>())
        {
            var g = EnemyKit.Game();
            var e = g.SpawnKind(kind, Middle);
            if (g.BehaviourFor(e.Kind) is not { Boss: true } behaviour) continue;
            Assert.InRange(behaviour.IdleFrames(e), 1, SlowestSeconds * Tuning.Fps);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void A_later_boss_arriving_with_nothing_waiting_opens_with_a_post_an_earlier_one_said(int stage)
    {
        var g = HeardThree();
        g.StartAtBoss(stage);
        Enemy? boss = null;
        for (int i = 0; i < ArriveLimit && (boss = g.Boss)?.Text is null; i++) StepUnharmed(g);
        Assert.NotNull(boss);
        Assert.Contains(boss.Text, new[] { Short, Loud, Long });
        Assert.NotNull(boss.Header);
        Assert.Empty(g.Rude.Waiting);
    }

    /// <summary>Frames a visitor is watched after it spoke, long enough for several idle cues to have come to a boss.</summary>
    private const int WatchFrames = 4 * Tuning.VoiceIdleFrames;

    [Theory]
    [InlineData(EnemyKind.Warden)]
    [InlineData(EnemyKind.Nest)]
    [InlineData(EnemyKind.Yoke)]
    [InlineData(EnemyKind.Chorus)]
    [InlineData(EnemyKind.Gunship)]
    public void A_mini_boss_or_a_lesser_gunship_with_nothing_waiting_says_one_heard_post_on_arriving_and_no_more(EnemyKind kind)
    {
        var g = HeardThree(gates: false);
        EnemyKit.Clear(g);
        var e = kind == EnemyKind.Gunship ? g.SpawnLesser() : g.SpawnKind(kind, Middle);
        for (int i = 0; i < ArriveLimit && e.Text is null; i++) StepUnharmed(g);
        string? said = e.Text;
        Assert.Contains(said, new[] { Short, Loud, Long });
        for (int i = 0; i < WatchFrames && g.Enemies.Contains(e); i++)
        {
            StepUnharmed(g);
            Assert.True(e.Text is null || e.Text == said, "a visitor says one thing");
        }
    }

    [Fact]
    public void Speakers_share_the_round_so_a_later_one_carries_on_where_the_last_stopped()
    {
        var g = HeardThree(gates: false);
        EnemyKit.Clear(g);
        var said = new List<string>();
        for (int visitor = 0; visitor < g.Rude.Heard.Count; visitor++)
        {
            var e = g.SpawnLesser();
            for (int i = 0; i < ArriveLimit && e.Text is null; i++) StepUnharmed(g);
            Assert.NotNull(e.Text);
            said.Add(e.Text);
            g.Enemies.Remove(e);
        }
        Assert.Equal([Short, Loud, Long], said);
    }

    /// <summary>The boss that pages a long post in the shortest pages.</summary>
    private const int SlowReader = 4;

    [Fact]
    public void The_slow_reader_pages_a_long_post_in_short_pages_and_still_shows_it_whole()
    {
        var g = HeardThree();
        g.StartAtBoss(SlowReader);
        Enemy? boss = null;
        for (int i = 0; i < ArriveLimit && (boss = g.Boss)?.SpeechLines is null; i++) StepUnharmed(g);
        Assert.NotNull(boss);
        Assert.Equal(Long, boss.Text);   // the longest first
        Assert.Equal(SpeechBoxText.MinPageLines, boss.PageLines);
        Assert.Equal(Long, string.Join(' ', boss.SpeechLines!));
        int pages = SpeechBoxText.Pages(boss.SpeechLines!, boss.PageLines);
        Assert.InRange(pages, 2, int.MaxValue);
        var counters = new HashSet<string>();
        var turned = new HashSet<int>();
        for (int i = 0; i < Tuning.BossStayFrames && boss.Text == Long && EnemySpeechBox.IsOpen(boss); i++)
        {
            turned.Add(SpeechBoxText.Progress(boss.SpeechLines!, boss.PageLines, boss.SpeechBoxAge).Page);
            if (boss.Voice?.Prefix is { } prefix) counters.Add(prefix);
            StepUnharmed(g);
        }
        Assert.Equal(pages, turned.Count);       // every page had its turn before the box shut
        Assert.Equal(Enumerable.Range(1, pages).Select(page => $"{page}/{pages}").ToArray(), counters.Order().ToArray());
    }

    /// <summary>Steps a fight for some frames and lists what the boss said, one entry each time its box opens or its words change.</summary>
    private static List<string> Openings(RaiderGame g, int frames)
    {
        var said = new List<string>();
        bool open = false;
        string? last = null;
        for (int frame = 0; frame < frames; frame++)
        {
            StepUnharmed(g);
            bool speaking = g.Boss is { } boss && EnemySpeechBox.IsOpen(boss);
            if (speaking && (!open || g.Boss!.Text != last)) said.Add(last = g.Boss!.Text!);
            open = speaking;
        }
        return said;
    }

    private const int SceneFrames = 150 * Tuning.Fps;
    /// <summary>Long enough for a boss to say what it has been given, at the slowest pace.</summary>
    private const int TakeUpFrames = 60 * Tuning.Fps;

    /// <summary>A signed-out run of the given boss, played with no input.</summary>
    private static RaiderGame SignedOutAt(int stage)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = false, BossGates = true };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        g.StartAtBoss(stage);
        return g;
    }

    [Fact]
    public void A_boss_on_a_feed_with_no_rude_post_says_its_own_line_once_then_speaks_each_post_as_soon_as_they_come()
    {
        var g = SignedOutAt(1);
        Assert.Equal(["Nobody asked. I answered."], Openings(g, SceneFrames));
        g.OfferDark(new DarkPost(DarknessKind.Rude, ShortId, Short, DarkSource.Hidden));
        g.OfferDark(new DarkPost(DarknessKind.Rude, LoudId, Loud, DarkSource.Hidden));
        g.OfferDark(new DarkPost(DarknessKind.Rude, LongId, Long, DarkSource.Label, Rude));
        var said = Openings(g, TakeUpFrames);
        Assert.Equal([Loud, Short, Long], said.Distinct().Order().ToArray());
    }

    [Fact]
    public void A_boss_that_knows_one_post_says_it_again_once_read_and_takes_up_the_next_when_it_comes()
    {
        const int FirstPostFrames = 98 * Tuning.Fps;
        var g = SignedOutAt(7);
        var before = Openings(g, FirstPostFrames);
        Assert.DoesNotContain(Short, before);
        g.OfferDark(new DarkPost(DarknessKind.Rude, ShortId, Short, DarkSource.Hidden));
        var one = Openings(g, SceneFrames - FirstPostFrames);
        Assert.All(one, text => Assert.Equal(Short, text));
        Assert.InRange(one.Count, 2, int.MaxValue);
        g.OfferDark(new DarkPost(DarknessKind.Rude, LoudId, Loud, DarkSource.Hidden));
        g.OfferDark(new DarkPost(DarknessKind.Rude, LongId, Long, DarkSource.Label, Rude));
        var three = Openings(g, TakeUpFrames);
        Assert.Equal([Loud, Short, Long], three.Distinct().Order().ToArray());
    }

    /// <summary>The hidden replies a run's first seconds bring, as the replay of the last half hour gave two runs on a quiet
    /// feed: some fourteen speakable, from a word to a few hundred characters.</summary>
    private const int ReplayPosts = 14;
    private static readonly int[] ReplayLengths = [20, 55, 110, 140, 205, 300];

    /// <summary>The fewest different posts a boss says in a fight of <see cref="SceneFrames"/>. The slowest, boss 4, pages long posts
    /// in short pages at an eight-second pace and says seven; the others say nine to fourteen.</summary>
    private const int FewestDifferent = 6;

    private const string ReplyMark = "reply ", Word = "words";

    private static string ReplayPost(int number) =>
        string.Concat($"{ReplyMark}{number}: ", string.Join(' ', Enumerable.Repeat(Word, ReplayLengths[number % ReplayLengths.Length] / (Word.Length + 1))));

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
    public void With_the_replay_fetched_one_post_at_a_time_from_the_start_a_boss_says_several_different_posts_in_a_fight(int stage)
    {
        var g = SignedOutAt(stage);
        int fetchFrames = (int)(DarknessFeed.DefaultPaceMs * Tuning.Fps / TimeSpan.MillisecondsPerSecond);
        var said = new List<string>();
        for (int number = 0; number < ReplayPosts; number++)
        {
            g.OfferDark(new DarkPost(DarknessKind.Rude, (ulong)number + 1, ReplayPost(number), DarkSource.Hidden));
            said.AddRange(Openings(g, fetchFrames));
        }
        said.AddRange(Openings(g, SceneFrames - ReplayPosts * fetchFrames));
        int different = said.Distinct().Count(text => text.StartsWith(ReplyMark, StringComparison.Ordinal));
        Assert.True(different >= FewestDifferent, $"boss {stage} said {different} different posts in {said.Count}");
    }
}
