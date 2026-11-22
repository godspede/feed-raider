using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The signed-in game: the Following timeline as the source, the like path from the game's own feed, B, and the like key.
// The JS side (main.js -> atproto.js) is a fake: delegates that answer with the JSON the real calls give.
public class SignedInTests
{
    private const string Text = "this is a perfectly ordinary post with enough words in it";
    private const string Me = "me.test";
    /// <summary>The handles of two authors whose posts a test page carries.</summary>
    private const string AuthorHandle = "a.test", OtherAuthorHandle = "b.test";
    private const string Following = "your Following timeline";
    private const string NoTimelineScope = "Insufficient scope: rpc:app.bsky.feed.getTimeline";
    /// <summary>The address of a post the game has never held.</summary>
    private const string GonePost = "at://did:plc:gone/app.bsky.feed.post/1";
    /// <summary>The feed flag left empty, which picks the default source.</summary>
    private const string DefaultFeed = "";
    /// <summary>What the browser's fetch says when it cannot reach the server, and the toast a like that fails that way leaves.</summary>
    private const string FetchFailed = "Failed to fetch", LikeFailedToast = "Like failed: could not reach Bluesky.";
    /// <summary>How long a loader run gets to finish once it is cancelled.</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    private static string Page(params string[] handles)
    {
        var rows = handles.Select((handle, i) =>
            $$$"""{"post":{"uri":"at://did:plc:a{{{i}}}/app.bsky.feed.post/{{{i}}}","cid":"cid{{{i}}}","author":{"did":"did:plc:a{{{i}}}","handle":"{{{handle}}}"},"record":{"text":"{{{Text}}}"},"likeCount":5,"viewer":{} }}""");
        return "{\"feed\":[" + string.Join(",", rows) + "],\"cursor\":\"c1\"}";
    }

    private static RaiderGame NewGame()
    {
        var g = new RaiderGame(5) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(PressRig.ScrCols, PressRig.ScrRows));
        return g;
    }

    /// <summary>A source whose every page is empty.</summary>
    private sealed class EmptySource : IFeedSource
    {
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) => Task.FromResult(new FeedPage([], null));
    }

    // ---------------------------------------------------------------- the source

    [Fact]
    public void Signed_in_the_default_source_is_the_timeline_and_the_feed_flags_still_pick_feeds()
    {
        IFeedSource timeline = new EmptySource(), generator = new EmptySource(), pub = new EmptySource();
        var def = FeedPlan.Choose(DefaultFeed, true, Me, timeline, generator, pub, _ => throw new InvalidOperationException())!;
        Assert.Same(timeline, def.Primary);
        Assert.Same(pub, def.Fallback);
        Assert.Same(timeline, FeedPlan.Choose(null, true, Me, timeline, generator, pub, _ => pub)!.Primary);
        Assert.Same(timeline, FeedPlan.Choose("following", true, Me, timeline, generator, pub, _ => pub)!.Primary);

        var disc = FeedPlan.Choose("discover", true, Me, timeline, generator, pub, _ => pub)!;
        Assert.Same(generator, disc.Primary);
        Assert.Equal(Feeds.Discover, disc.FeedUri);
        var fy = FeedPlan.Choose("foryou", true, Me, timeline, generator, pub, _ => pub)!;
        Assert.Same(generator, fy.Primary);
        Assert.Equal(Feeds.ForYou, fy.FeedUri);
        Assert.Null(FeedPlan.Choose("nonsense", true, Me, timeline, generator, pub, _ => pub));
    }

    [Fact]
    public void Signed_out_there_is_no_timeline()
    {
        IFeedSource timeline = new EmptySource(), generator = new EmptySource(), pub = new EmptySource(), follows = new EmptySource();
        var guest = FeedPlan.Choose(DefaultFeed, false, string.Empty, timeline, generator, pub, _ => follows)!;
        Assert.Same(pub, guest.Primary);
        Assert.Null(guest.Fallback);
        Assert.Same(follows, FeedPlan.Choose(DefaultFeed, false, Me, timeline, generator, pub, _ => follows)!.Primary);
    }

    [Fact]
    public async Task The_timeline_source_calls_the_timeline_bridge_not_the_feed_one()
    {
        var calls = new List<string>();
        const int pageSize = 30;
        var timeline = new JsFeedSource((uri, cursor, limit) => { calls.Add($"timeline:{cursor}:{limit}"); return Task.FromResult(Page(AuthorHandle)); });
        var page = await timeline.GetPageAsync(Feeds.Discover, "cur", pageSize, default);
        Assert.Equal([$"timeline:cur:{pageSize}"], calls);
        Assert.Equal(AuthorHandle, page.Posts.Single().AuthorHandle);
    }

    [Fact]
    public async Task A_timeline_run_still_never_serves_the_same_author_twice_in_a_row()
    {
        const string firstDid = "did:plc:a0", secondDid = "did:plc:a3";
        var json = Page(AuthorHandle, AuthorHandle, AuthorHandle, OtherAuthorHandle, OtherAuthorHandle);
        // Page() gives every post its own did, but one author is one did
        json = json.Replace("did:plc:a1", firstDid).Replace("did:plc:a2", firstDid).Replace("did:plc:a4", secondDid);
        var game = new RaiderGame(1);
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, new JsFeedSource((_, _, _) => Task.FromResult(json)), null, Feeds.Discover, cts.Token);
        await Wait.UntilAsync(() => { lock (game) return game.Posts.Count >= 2; }, "two posts in the queue");
        cts.Cancel();
        await run.WaitAsync(RunTimeout);
        var served = new List<FeedPost>();
        lock (game) while (game.Posts.TryDequeue(out var p, out _)) served.Add(p);
        Assert.True(served.Count >= 2);
        for (int i = 1; i < served.Count; i++) Assert.NotEqual(served[i - 1].AuthorDid, served[i].AuthorDid);
    }

    // ---------------------------------------------------------------- why the feed failed, in words

    private sealed class Throws(Exception ex) : IFeedSource
    {
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) => throw ex;
    }

    [Fact]
    public async Task A_sign_in_problem_switches_to_discover_at_once_and_says_why()
    {
        var game = new RaiderGame(1);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, new Throws(new Exception(NoTimelineScope)), new EmptySource(), Feeds.Discover, cts.Token,
            onFallback: m => { said.Add(m); cts.Cancel(); }, sourceName: Following);
        await run.WaitAsync(RunTimeout);
        var msg = Assert.Single(said);
        Assert.Contains("not allowed to read " + Following, msg);
        Assert.Contains("sign out and sign in again", msg);
        Assert.EndsWith("showing Discover", msg);
    }

    [Fact]
    public async Task A_flaky_source_says_so_at_the_first_failure_not_after_thirty_silent_seconds()
    {
        var game = new RaiderGame(1);
        var trouble = new List<string>();
        using var cts = new CancellationTokenSource();
        const int quickBackoffMs = 10;
        var run = RaiderFeeds.PostsAsync(game, new Throws(new HttpRequestException(FetchFailed)), new EmptySource(), Feeds.Discover, cts.Token,
            backoffMs: quickBackoffMs, onTrouble: m => { trouble.Add(m); cts.Cancel(); }, sourceName: Following);
        await run.WaitAsync(RunTimeout);
        Assert.Equal($"could not reach Bluesky for {Following}; trying again", Assert.Single(trouble));
    }

    [Fact]
    public async Task A_failing_fallback_says_so_too_instead_of_retrying_in_silence()
    {
        var game = new RaiderGame(1);
        var trouble = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, new Throws(new Exception(NoTimelineScope)),
            new Throws(new HttpRequestException(FetchFailed)), Feeds.Discover, cts.Token,
            onTrouble: m => { trouble.Add(m); cts.Cancel(); }, sourceName: Following);
        await run.WaitAsync(RunTimeout);
        Assert.Equal("could not reach Bluesky for Discover; trying again", Assert.Single(trouble));
    }

    [Fact]
    public async Task The_loader_says_it_is_loading_then_that_the_feed_is_empty_once()
    {
        var game = new RaiderGame(1);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, new EmptySource(), null, Feeds.Discover, cts.Token, onStatus: m => { lock (said) said.Add(m); });
        await Wait.UntilAsync(() => { lock (said) return said.Count >= 2; }, "the loader to say the feed is empty");
        cts.Cancel();
        await run.WaitAsync(RunTimeout);
        Assert.Equal([FeedStatus.Loading, FeedStatus.Empty], said);
        Assert.Equal("loading your feed…", FeedStatus.Loading);
        Assert.Equal("no posts to show", FeedStatus.Empty);
    }

    [Fact]
    public void Every_toast_is_passed_on_to_the_host()
    {
        var game = new RaiderGame(1);
        var said = new List<string>();
        game.Said = said.Add;
        const string toast = "sign in to like";
        game.Say(toast);
        Assert.Equal([toast], said);
        Assert.Equal(toast, game.Toast);
    }

    [Fact]
    public void A_page_without_sign_in_is_an_auth_problem()
    {
        Assert.True(Plain.IsAuthProblem(new Exception("sign-in unavailable on this page")));
    }

    // ---------------------------------------------------------------- the like, end to end

    private sealed class FakeActions : IPostActions
    {
        public List<string> Calls { get; } = [];
        public TaskCompletionSource<PostRef> Answer { get; } = new();
        /// <summary>How a like of the post at <paramref name="uri"/> is written down in <see cref="Calls"/>.</summary>
        public static string LikeCall(string uri) => "like " + uri;
        public Task<PostRef> LikeAsync(PostRef post, CancellationToken ct) { Calls.Add(LikeCall(post.Uri)); return Answer.Task; }
        public Task<PostRef> RepostAsync(PostRef post, CancellationToken ct) { Calls.Add("repost " + post.Uri); return Answer.Task; }
        public Task DeleteAsync(string recordUri, CancellationToken ct) { Calls.Add("delete " + recordUri); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Signed_in_a_like_key_asks_for_one_like_write_and_the_heart_is_pending_until_the_answer_then_lit_with_the_records_address_kept()
    {
        var (g, c, _) = PressRig.Scene();
        var actions = new FakeActions();
        var writes = new List<Task>();
        g.OnLike = p => writes.Add(RaiderFeeds.WriteAsync(g, actions, p, PostAction.Like));
        PressRig.RestOn(g, c, PostAction.Like, Tuning.Fps);
        Assert.Empty(actions.Calls);
        PressRig.Press(g, c, PostAction.Like);
        Assert.Equal([FakeActions.LikeCall(c.Post.Uri)], actions.Calls);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        Assert.Null(c.Marks.LikeUri);
        PressRig.RestOn(g, c, PostAction.Like, Tuning.Fps);
        PressRig.Press(g, c, PostAction.Like);   // pressed again while it waits: no second write
        Assert.Single(actions.Calls);

        const string likeRecord = "at://did:plc:me/app.bsky.feed.like/3kx";
        actions.Answer.SetResult(new PostRef(likeRecord, "cidl"));
        await Assert.Single(writes);
        Assert.Equal(ButtonState.Lit, c.Marks.Like);
        Assert.Equal(likeRecord, c.Marks.LikeUri);
        PressRig.RestOn(g, c, PostAction.Like, Tuning.Fps);
        Assert.Single(actions.Calls);
    }

    [Fact]
    public async Task A_failed_write_puts_the_heart_back_plays_nolike_says_why_in_plain_words_and_takes_no_reward_back()
    {
        var (g, c, played) = PressRig.Scene();
        var actions = new FakeActions();
        var writes = new List<Task>();
        var said = new List<string>();
        g.Said = said.Add;
        g.OnLike = p => writes.Add(RaiderFeeds.WriteAsync(g, actions, p, PostAction.Like));
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Press(g, c, PostAction.Like);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        Assert.Equal(LikePressTests.CountAfter, CardPainter.CountText(c, PostAction.Like));
        played.Clear();

        actions.Answer.SetException(new Exception($"TypeError: {FetchFailed}"));
        await Assert.Single(writes);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        Assert.Null(c.Marks.LikeUri);
        Assert.Equal(LikePressTests.CountBefore, CardPainter.CountText(c, PostAction.Like));
        Assert.Equal([SfxNames.NoLike], played.Select(p => p.Name));
        Assert.Equal(LikeFailedToast, said[^1]);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.True(c.Marks.Paid);
    }

    [Fact]
    public void An_answer_for_an_unknown_post_only_toasts_the_failure_and_does_not_throw()
    {
        var g = NewGame();
        g.WriteConfirmed(GonePost, PostAction.Like, "at://did:plc:me/app.bsky.feed.like/1");
        g.WriteFailed(GonePost, PostAction.Like, "could not reach Bluesky");
        Assert.Equal(LikeFailedToast, g.Toast);
    }

    // ---------------------------------------------------------------- B

    [Fact]
    public void B_bombs_and_the_other_keys_still_work()
    {
        var g = NewGame();
        int bombs = g.Bombs;
        const string bombKey = "KeyB", fireKey = "Space";
        HostKeys.Apply(g.Input, bombKey, true);
        g.Step();
        HostKeys.Apply(g.Input, bombKey, false);
        Assert.Equal(bombs - 1, g.Bombs);
        Assert.NotEmpty(g.Rings);

        HostKeys.Apply(g.Input, fireKey, true);
        Assert.True(g.Input.Firing);
        HostKeys.Apply(g.Input, fireKey, false);
        Assert.False(g.Input.Firing);
        HostKeys.Apply(g.Input, "KeyF", true);
        Assert.True(g.Input.AutoToggle);
        HostKeys.Apply(g.Input, "KeyE", true);
        Assert.True(g.Input.LogLikeLatch > 0);
        HostKeys.Apply(g.Input, "KeyZ", true);
        Assert.True(g.Input.ShipLikeLatch > 0);
        HostKeys.Apply(g.Input, "KeyX", true);
        Assert.True(g.Input.ShipRepostLatch > 0);
    }
}
