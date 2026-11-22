using FeedRaider.Engine;
using TUImation;

namespace FeedRaider.Tests;

public class DarknessSourcesTests
{
    private const string Owner = "did:plc:owneroowneroowneroowner1";
    private const string Replier = "did:plc:replierreplierreplier222";
    private const string Spammer = "did:plc:spammerspammerspammer222";
    private const int RoomW = 100, RoomRows = 40;
    private const long FrameMs = 250, LabelWatchMs = 12000, HiddenWatchMs = 3000;
    internal const string Spam = "spam", Rude = "rude", Porn = "porn";
    private const string Create = "create";
    private static string Reply(int number) => $"at://{Replier}/app.bsky.feed.post/3kr{number}";
    private const string GateCreated = "threadgate-create.json", GateUpdated = "threadgate-update.json", AuthorFeed = "author-feed.json";

    private static List<string> Feed(ThreadgateDiffer differ, string fixture)
    {
        Assert.True(ThreadgateDiffer.TryParse(JetstreamParserTests.Fixture(fixture), out var did, out var rkey, out var operation, out var hidden));
        return differ.Apply(did, rkey, operation, hidden);
    }

    internal static Room RoomOfFigures() =>
        new(Enumerable.Range(0, 6).Select(number => new Follow($"did:plc:figure{number}aaaaaaaaaaaaa", $"figure{number}.test")));

    /// <summary>Everything the room draws, a frame at a time, until <paramref name="untilMs"/>.</summary>
    internal static string Watch(Room room, long untilMs)
    {
        var drawn = new System.Text.StringBuilder();
        for (long ms = 0; ms < untilMs; ms += FrameMs) drawn.Append(room.AdvanceAndRender(RoomW, RoomRows, ms));
        return drawn.ToString();
    }

    // ---- threadgate diffing ------------------------------------------------------------------

    [Fact]
    public void A_new_gate_reports_all_its_hidden_replies()
    {
        var differ = new ThreadgateDiffer();
        Assert.Equal([Reply(1), Reply(2)], Feed(differ, GateCreated));
    }

    [Fact]
    public void An_updated_gate_reports_only_what_it_added()
    {
        var differ = new ThreadgateDiffer();
        Feed(differ, GateCreated);
        // reply 1 removed (unhidden), reply 2 kept, reply 3 added
        Assert.Equal([Reply(3)], Feed(differ, GateUpdated));
        Assert.Empty(Feed(differ, GateUpdated));   // replaying the same version adds nothing
    }

    [Fact]
    public void A_deleted_gate_is_forgotten_so_a_recreate_is_all_new()
    {
        var differ = new ThreadgateDiffer();
        Feed(differ, GateCreated);
        Assert.Empty(Feed(differ, "threadgate-delete.json"));
        Assert.Equal(0, differ.Tracked);
        Assert.Equal([Reply(1), Reply(2)], Feed(differ, GateCreated));
    }

    [Fact]
    public void Gates_are_independent_and_other_messages_are_ignored()
    {
        var differ = new ThreadgateDiffer();
        Assert.Equal([Reply(1)], differ.Apply(Owner, "a", Create, [Reply(1)]));
        Assert.Equal([Reply(1)], differ.Apply(Owner, "b", Create, [Reply(1)]));
        Assert.False(ThreadgateDiffer.TryParse(JetstreamParserTests.Fixture("post.json"), out _, out _, out _, out _));
        Assert.False(ThreadgateDiffer.TryParse("not json", out _, out _, out _, out _));
        Assert.False(ThreadgateDiffer.TryParse(JetstreamParserTests.Fixture("identity.json"), out _, out _, out _, out _));
    }

    [Fact]
    public void The_stream_url_is_network_wide_and_only_threadgates()
    {
        Assert.Contains("wantedCollections=app.bsky.feed.threadgate", ThreadgateSource.Endpoint);
        Assert.DoesNotContain("wantedDids", ThreadgateSource.Endpoint);
    }

    private static async IAsyncEnumerable<T> Of<T>(params T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    [Fact]
    public async Task Hidden_replies_become_anonymous_rude_posts()
    {
        var stats = new DarknessStats();
        var posts = new List<DarknessPost>();
        var fetched = new List<string>();
        await DarknessFeed.RunHiddenAsync(Of(Reply(1), Reply(1), "at://did:plc:x/app.bsky.actor.profile/self", Reply(2)),
            (uri, _) => { fetched.Add(uri); return Task.FromResult<string?>("go away @someone.example.social https://t.example/x you muppet"); },
            posts.Add, stats, CancellationToken.None, paceMs: 1);

        Assert.Equal([Reply(1), Reply(2)], fetched);   // duplicate and non-post dropped before any fetch
        Assert.Equal((2, 2), (stats.HiddenNew, stats.HiddenFetched));
        Assert.Equal(2, posts.Count);
        Assert.All(posts, post =>
        {
            Assert.Equal((DarknessKind.Rude, DarknessSource.Hidden), (post.Kind, post.Source));
            Assert.Equal("go away you muppet", post.Text);
            Assert.DoesNotContain(Replier, post.ToString());
        });
    }

    // ---- account-level labels ----------------------------------------------------------------

    [Fact]
    public void Account_spam_passes_and_nothing_else_on_an_account_does()
    {
        Assert.Equal(DarknessKind.Spam, DarknessFilter.AcceptAccount(new LabelEvent(Spammer, Spam, false, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent(Spammer, Spam, Neg: true, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent(Spammer, Rude, false, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent(Spammer, Porn, false, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent($"at://{Spammer}/app.bsky.feed.post/3k", Spam, false, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent("did:plc:short", Spam, false, 1)));
    }

    [Fact]
    public void Latest_post_parser_skips_reposts_and_takes_text_only()
    {
        Assert.StartsWith("win free crypto", PostTextFetcher.ParseAuthorFeed(JetstreamParserTests.Fixture(AuthorFeed), signedOut: false));
        Assert.Null(PostTextFetcher.ParseAuthorFeed("{\"feed\":[]}", signedOut: false));
    }

    [Fact]
    public async Task An_account_spam_label_becomes_a_spam_post_with_the_latest_text_only()
    {
        var stats = new DarknessStats();
        var posts = new List<DarknessPost>();
        var fetched = new List<string>();
        await DarknessFeed.RunAsync(
            Of(new LabelEvent(Spammer, Spam, false, 1), new LabelEvent(Spammer, Spam, Neg: true, 2), new LabelEvent(Spammer, Porn, false, 3)),
            (subject, _) => { fetched.Add(subject); return Task.FromResult(PostTextFetcher.ParseAuthorFeed(JetstreamParserTests.Fixture(AuthorFeed), signedOut: false)); },
            posts.Add, stats, CancellationToken.None, paceMs: 1);

        Assert.Equal([Spammer], fetched);
        Assert.Equal((3, 1, 1), (stats.LabelsSeen, stats.AccountLabels, stats.PostsFetched));
        var post = Assert.Single(posts);
        Assert.Equal((DarknessKind.Spam, DarknessSource.Label, "win free crypto now"), (post.Kind, post.Source, post.Text));

        var room = RoomOfFigures();
        room.Darkness.Offer(post);
        string everything = Watch(room, LabelWatchMs);
        Assert.True(room.Darkness.SpawnedFromLabels > 0);
        Assert.Equal(0, room.Darkness.SpawnedFromHidden);
        Assert.Contains("crypto", everything);
        foreach (var leak in new[] { "spammerspammer", "spammy", "McSpam", "cdn.bsky", "links.test", "Repost of someone" })
            Assert.DoesNotContain(leak, everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Spawns_are_counted_by_source()
    {
        var room = RoomOfFigures();
        room.Darkness.Offer(new DarknessPost(DarknessKind.Rude, 1, "grr", DarknessSource.Hidden));
        Watch(room, HiddenWatchMs);
        Assert.Equal(1, room.Darkness.SpawnedFromHidden);
        Assert.Equal(0, room.Darkness.SpawnedFromLabels);
        Assert.Equal(DarknessSource.Hidden, room.Darkness.Enemies[0].Source);
    }

    // ---- layers ------------------------------------------------------------------------------

    private static readonly Rgb FigureRed = new(200, 10, 10), EnemyGreen = new(10, 200, 10);

    private static Canvas Block(int side, Rgb colour)
    {
        var canvas = new Canvas(side, side);
        canvas.FillRect(0, 0, side, side, colour);
        return canvas;
    }

    [Fact]
    public void Enemies_draw_above_figures_whatever_the_call_order()
    {
        const int Side = 6, FrameW = 20;
        var figure = Block(Side, FigureRed);
        var enemy = Block(Side, EnemyGreen);
        var figureFirst = new Frame(FrameW, Side);
        figureFirst.Blit(figure, 4, 2); figureFirst.BlitTop(enemy, 6, 4);
        var enemyFirst = new Frame(FrameW, Side);
        enemyFirst.BlitTop(enemy, 6, 4); enemyFirst.Blit(figure, 4, 2);
        string ansi = figureFirst.ToAnsi();
        Assert.Equal(ansi, enemyFirst.ToAnsi());
        Assert.Contains($"38;2;{EnemyGreen.R};{EnemyGreen.G};{EnemyGreen.B}", ansi);   // the enemy shows where they overlap
    }

    [Fact]
    public void Enemy_text_is_never_hidden_by_a_figure_or_his_label()
    {
        const string Words = "buy followers";
        var frame = new Frame(40, 8);
        frame.Whisper(Words, 20, 4);
        frame.Blit(Block(12, FigureRed), 10, 0);   // a figure standing right over the text
        frame.Label("someone", 20, 3);
        string plain = System.Text.RegularExpressions.Regex.Replace(frame.ToAnsi(), "\x1b\\[[0-9;]*[A-Za-z]", "");
        Assert.Contains(Words, plain);
    }

    [Fact]
    public void Spam_motes_are_big_enough_to_read()
    {
        Assert.True(DarknessEnemySprites.MoteW >= 9 && DarknessEnemySprites.MoteH >= 6);
    }
}
