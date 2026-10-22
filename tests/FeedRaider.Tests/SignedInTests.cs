using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The signed-in game: the Following timeline as the source, the like path from the game's own feed, B, and hold-to-like.
// The JS side (main.js -> atproto.js) is a fake: delegates that answer with the JSON the real calls give.
public class SignedInTests
{
    private const string Text = "this is a perfectly ordinary post with enough words in it";

    private static string Page(params (string Handle, bool Liked)[] items)
    {
        var rows = items.Select((it, i) =>
        {
            string viewer = it.Liked ? """{"like":"at://did:plc:me/app.bsky.feed.like/1"}""" : "{}";
            return $$$"""{"post":{"uri":"at://did:plc:a{{{i}}}/app.bsky.feed.post/{{{i}}}","cid":"cid{{{i}}}","author":{"did":"did:plc:a{{{i}}}","handle":"{{{it.Handle}}}"},"record":{"text":"{{{Text}}}"},"likeCount":5,"viewer":{{{viewer}}} }}""";
        });
        return "{\"feed\":[" + string.Join(",", rows) + "],\"cursor\":\"c1\"}";
    }

    private static RaiderGame NewGame()
    {
        var g = new RaiderGame(5) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(120, 46));
        return g;
    }

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    private static Wingman Arrived(RaiderGame g)
    {
        for (int i = 0; i < 400 && !g.Wings.Any(w => w.Age > Tuning.WingEnterFrames); i++) g.Step();
        return g.Wings.First(w => w.Age > Tuning.WingEnterFrames);
    }

    private static void MoveOnto(RaiderGame g, Wingman w)
    {
        for (int i = 0; i < 200; i++)
        {
            g.Input.Key(GameKey.Left, g.PX > w.X + 1); g.Input.Key(GameKey.Right, g.PX < w.X - 1);
            g.Input.Key(GameKey.Up, g.PY > w.Y + 1); g.Input.Key(GameKey.Down, g.PY < w.Y - 1);
            g.Step();
            if (Math.Abs(g.PX - w.X) < 3 && Math.Abs(g.PY - w.Y) < 3) break;
        }
        foreach (var k in Enum.GetValues<GameKey>()) g.Input.Key(k, false);
    }

    private sealed class Fixed(params FeedPost[] posts) : IFeedSource
    {
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) => Task.FromResult(new FeedPage(posts, null));
    }

    // ---------------------------------------------------------------- the source

    [Fact]
    public void Signed_in_the_default_source_is_the_timeline_and_the_feed_flags_still_pick_feeds()
    {
        IFeedSource timeline = new Fixed(), generator = new Fixed(), pub = new Fixed();
        var def = FeedPlan.Choose("", true, "me.test", timeline, generator, pub, _ => throw new InvalidOperationException())!;
        Assert.Same(timeline, def.Primary);
        Assert.Same(pub, def.Fallback);
        Assert.Same(timeline, FeedPlan.Choose(null, true, "me.test", timeline, generator, pub, _ => pub)!.Primary);
        Assert.Same(timeline, FeedPlan.Choose("following", true, "me.test", timeline, generator, pub, _ => pub)!.Primary);

        var disc = FeedPlan.Choose("discover", true, "me.test", timeline, generator, pub, _ => pub)!;
        Assert.Same(generator, disc.Primary);
        Assert.Equal(Feeds.Discover, disc.FeedUri);
        var fy = FeedPlan.Choose("foryou", true, "me.test", timeline, generator, pub, _ => pub)!;
        Assert.Same(generator, fy.Primary);
        Assert.Equal(Feeds.ForYou, fy.FeedUri);
        Assert.Null(FeedPlan.Choose("nonsense", true, "me.test", timeline, generator, pub, _ => pub));
    }

    [Fact]
    public void Signed_out_there_is_no_timeline()
    {
        IFeedSource timeline = new Fixed(), generator = new Fixed(), pub = new Fixed(), follows = new Fixed();
        var guest = FeedPlan.Choose("", false, "", timeline, generator, pub, _ => follows)!;
        Assert.Same(pub, guest.Primary);
        Assert.Null(guest.Fallback);
        Assert.Same(follows, FeedPlan.Choose("", false, "me.test", timeline, generator, pub, _ => follows)!.Primary);
    }

    [Fact]
    public async Task The_timeline_source_calls_the_timeline_bridge_not_the_feed_one()
    {
        var calls = new List<string>();
        var timeline = new JsFeedSource((uri, cursor, limit) => { calls.Add($"timeline:{cursor}:{limit}"); return Task.FromResult(Page(("a.test", false))); });
        var page = await timeline.GetPageAsync(Feeds.Discover, "cur", 30, default);
        Assert.Equal(["timeline:cur:30"], calls);
        Assert.Equal("a.test", page.Posts.Single().AuthorHandle);
    }

    [Fact]
    public async Task A_timeline_run_still_never_serves_the_same_author_twice_in_a_row()
    {
        var json = Page(("a.test", false), ("a.test", false), ("a.test", false), ("b.test", false), ("b.test", false));
        // one author per did in Page(); make the dids repeat by author instead
        json = json.Replace("did:plc:a1", "did:plc:a0").Replace("did:plc:a2", "did:plc:a0").Replace("did:plc:a4", "did:plc:a3");
        var game = new RaiderGame(1);
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, new JsFeedSource((_, _, _) => Task.FromResult(json)), null, Feeds.Discover, cts.Token);
        await Task.Delay(1500);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        var served = new List<FeedPost>();
        lock (game) while (game.Queue.TryDequeue(out var p)) served.Add(p);
        Assert.True(served.Count >= 2);
        for (int i = 1; i < served.Count; i++) Assert.NotEqual(served[i - 1].AuthorDid, served[i].AuthorDid);
    }

    // ---------------------------------------------------------------- why the feed failed, in words

    private sealed class Throws(Exception ex) : IFeedSource
    {
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) => throw ex;
    }

    [Fact]
    public async Task A_login_problem_switches_to_discover_at_once_and_says_why()
    {
        var game = new RaiderGame(1);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, new Throws(new Exception("Insufficient scope: rpc:app.bsky.feed.getTimeline")), new Fixed(), Feeds.Discover, cts.Token,
            onFallback: m => { said.Add(m); cts.Cancel(); }, sourceName: "your Following timeline");
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        var msg = Assert.Single(said);
        Assert.Contains("not allowed to read your Following timeline", msg);
        Assert.Contains("sign out and sign in again", msg);
        Assert.EndsWith("showing Discover", msg);
    }

    [Fact]
    public async Task A_flaky_source_says_so_at_the_first_failure_not_after_thirty_silent_seconds()
    {
        var game = new RaiderGame(1);
        var trouble = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, new Throws(new HttpRequestException("Failed to fetch")), new Fixed(), Feeds.Discover, cts.Token,
            backoffMs: 10, onTrouble: m => { trouble.Add(m); cts.Cancel(); }, sourceName: "your Following timeline");
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("could not reach Bluesky for your Following timeline; trying again", Assert.Single(trouble));
    }

    [Fact]
    public void Plain_words_for_like_failures()
    {
        Assert.Equal("your sign-in does not allow likes, sign out and sign in again", Plain.Like(new Exception("Insufficient scope")));
        Assert.Equal("you are signed out", Plain.Like(new Exception("not logged in")));
        Assert.Equal("could not reach Bluesky", Plain.Like(new Exception("TypeError: Failed to fetch")));
        Assert.Equal("too many likes, wait a moment", Plain.Like(new Exception("Rate limit exceeded")));
        Assert.Equal("boom", Plain.Like(new Exception("boom")));
    }

    [Fact]
    public void A_page_without_sign_in_is_an_auth_problem()
    {
        Assert.True(Plain.IsAuthProblem(new Exception("sign-in unavailable on this page")));
    }

    // ---------------------------------------------------------------- the like, end to end

    /// <summary>A game whose wingman came down the game's own feed (WingmenAsync over a fake JS feed), with a fake JS like.</summary>
    private static async Task<(RaiderGame Game, Wingman Wing, List<(string Uri, string Cid)> Sent, Func<Task> Like)> LiveGame(Func<string, string, Task<string>> authLike, params (string, bool)[] feed)
    {
        var game = NewGame();
        var sent = new List<(string, string)>();
        var actions = new JsActions((uri, cid) => { sent.Add((uri, cid)); return authLike(uri, cid); });
        var json = Page(feed);
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, new JsFeedSource((_, _, _) => Task.FromResult(json)), null, Feeds.Discover, cts.Token);
        for (int i = 0; i < 100; i++) { lock (game) if (game.Queue.Count > 0) break; await Task.Delay(50); }
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        var pending = new List<Task>();
        game.OnLike = post => pending.Add(RaiderFeeds.LikeAsync(game, actions, post));
        var wing = Arrived(game);
        return (game, wing, sent, () => Task.WhenAll(pending));
    }

    [Fact]
    public async Task E_on_a_post_from_the_games_feed_reaches_like_create_and_confirms_with_the_author()
    {
        var (g, w, sent, done) = await LiveGame((uri, cid) => Task.FromResult($"{{\"uri\":\"at://did:plc:me/app.bsky.feed.like/9\",\"cid\":\"lc\"}}"), ("ann.test", false));
        MoveOnto(g, w);
        g.Input.Like(); g.Step();
        await done();
        Assert.Equal([(w.Post.Uri, w.Post.Cid)], sent);
        Assert.Equal("liked @ann.test", g.Toast);
        Assert.Contains(g.Popups, p => p.Text.Contains('\u2665'));   // \u2665 heart suit
    }

    [Fact]
    public async Task A_failed_like_toasts_the_reason_and_can_be_tried_again()
    {
        int n = 0;
        var (g, w, sent, done) = await LiveGame((_, _) => ++n == 1 ? throw new Exception("Insufficient scope") : Task.FromResult("{\"uri\":\"u\",\"cid\":\"c\"}"), ("ann.test", false));
        MoveOnto(g, w);
        g.Input.Like(); g.Step();
        await done();
        Assert.StartsWith("like failed: your sign-in does not allow likes", g.Toast);
        MoveOnto(g, w); g.Input.Like(); g.Step();
        await done();
        Assert.Equal(2, sent.Count);
        Assert.Equal("liked @ann.test", g.Toast);
    }

    [Fact]
    public async Task A_post_is_never_liked_twice()
    {
        var (g, w, sent, done) = await LiveGame((_, _) => Task.FromResult("{\"uri\":\"u\",\"cid\":\"c\"}"), ("ann.test", false));
        MoveOnto(g, w);
        for (int i = 0; i < 4; i++) { g.Input.Like(); g.Step(); }
        await done();
        Assert.Single(sent);
    }

    [Fact]
    public async Task A_post_the_player_already_liked_is_not_offered_or_sent_again()
    {
        var (g, w, sent, done) = await LiveGame((_, _) => Task.FromResult("{\"uri\":\"u\",\"cid\":\"c\"}"), ("ann.test", true));
        Assert.True(w.Post.AlreadyLiked);
        MoveOnto(g, w);
        g.Input.Like(); g.Step();
        Run(g, Tuning.LikeHoldFrames + 2);
        await done();
        Assert.Empty(sent);
        Assert.Null(g.Prompt);
    }

    // ---------------------------------------------------------------- B

    [Fact]
    public void B_bombs_and_the_other_keys_still_work()
    {
        var g = NewGame();
        int bombs = g.Bombs;
        HostKeys.Apply(g.Input, "KeyB", true);
        g.Step();
        HostKeys.Apply(g.Input, "KeyB", false);
        Assert.Equal(bombs - 1, g.Bombs);
        Assert.NotEmpty(g.Rings);

        HostKeys.Apply(g.Input, "Space", true);
        Assert.True(g.Input.Firing);
        HostKeys.Apply(g.Input, "Space", false);
        Assert.False(g.Input.Firing);
        HostKeys.Apply(g.Input, "KeyF", true);
        Assert.True(g.Input.AutoToggle);
        HostKeys.Apply(g.Input, "KeyE", true);
        Assert.True(g.Input.LikeLatch > 0);
    }

    // ---------------------------------------------------------------- liking takes intent

    private static (RaiderGame G, Wingman W, List<FeedPost> Liked, (double Col, double Row) Cell) Staged()
    {
        var g = NewGame();
        var liked = new List<FeedPost>();
        g.OnLike = liked.Add;
        g.OfferPost(new FeedPost("at://did:plc:t1/app.bsky.feed.post/1", "cid1", "did:plc:t1", "t1.test", Text, 10, 0, 0, null, null));
        var w = Arrived(g);
        return (g, w, liked, (g.Layout.Col0 + w.X / 2, w.Y / 2));
    }

    [Fact]
    public void A_tap_or_click_never_likes()
    {
        var (g, w, liked, cell) = Staged();
        g.Input.Tap(cell.Col, cell.Row);                       // the host also sends hold start and end around a quick click
        g.Input.HoldStart(cell.Col, cell.Row); g.Step(); g.Input.HoldEnd();
        Run(g, Tuning.LikeHoldFrames * 2);
        Assert.Empty(liked);
        Assert.False(w.Liked);
    }

    [Fact]
    public void Holding_on_a_wingman_for_450_ms_likes_it_and_shows_the_charge()
    {
        var (g, w, liked, cell) = Staged();
        Assert.True(Tuning.LikeHoldMs == 450);
        g.Input.HoldStart(cell.Col, cell.Row);
        Run(g, Tuning.LikeHoldFrames / 2);
        Assert.Same(w, g.HoldTarget);
        Assert.InRange(g.HoldProgress, 0.3, 0.7);
        Assert.Empty(liked);
        Run(g, Tuning.LikeHoldFrames);
        Assert.Single(liked);
        Run(g, Tuning.LikeHoldFrames * 3);                     // the press is still down: no second like
        Assert.Single(liked);
        Assert.True(Tuning.LikeHoldFrames * 1000.0 / Tuning.Fps >= 450);
    }

    [Fact]
    public void Letting_go_before_the_charge_fills_cancels()
    {
        var (g, w, liked, cell) = Staged();
        g.Input.HoldStart(cell.Col, cell.Row);
        Run(g, Tuning.LikeHoldFrames - 2);
        g.Input.HoldEnd();
        Run(g, Tuning.LikeHoldFrames * 2);
        Assert.Empty(liked);
        Assert.Null(g.HoldTarget);
        Assert.Equal(0, g.HoldProgress);
        // and a fresh hold starts from empty
        g.Input.HoldStart(cell.Col, cell.Row);
        Run(g, Tuning.LikeHoldFrames - 2);
        Assert.Empty(liked);
    }

    [Fact]
    public void A_hold_away_from_every_wingman_charges_nothing()
    {
        var (g, w, liked, cell) = Staged();
        g.Input.HoldStart(g.Layout.Col0 + 1, 3);
        Run(g, Tuning.LikeHoldFrames * 2);
        Assert.Empty(liked);
        Assert.Null(g.HoldTarget);
    }
}
