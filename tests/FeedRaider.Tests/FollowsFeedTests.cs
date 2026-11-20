using System.Net;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class FollowsFeedTests
{
    private const int DoneSeconds = 10;
    private const int GameSeed = 1, PageLimit = 30, FastBackoffMs = 10;
    private const int LoadAttempts = 3, PostsPerAuthor = 3;   // the source's tries at the follows, and the posts one author adds to a page
    private const string AnyFeedUri = "";   // the sources under test ignore the feed URI
    private const string FollowsCall = "getFollows", EmptyJson = "{}", NetworkFailure = "Failed to fetch";

    private static string HandleOf(string did) => did["did:plc:".Length..] + ".test";

    private static string ActorOf(string url) => Uri.UnescapeDataString(url.Split("actor=")[1].Split('&')[0]);

    private sealed class Handler(Func<string, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string url = req.RequestUri!.ToString();
            lock (Urls) Urls.Add(url);
            var (code, body) = reply(url);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    private static string Post(string did, string handle, string rkey, bool repost = false)
    {
        string reason = repost ? ",\"reason\":{\"$type\":\"app.bsky.feed.defs#reasonRepost\",\"by\":{\"did\":\"did:plc:alice111\"}}" : "";
        return $$$"""{"post":{"uri":"at://{{{did}}}/app.bsky.feed.post/{{{rkey}}}","cid":"c{{{rkey}}}","author":{"did":"{{{did}}}","handle":"{{{handle}}}"},"record":{"text":"t {{{rkey}}}"}}{{{reason}}} }""";
    }

    private static string FollowsJson(params string[] dids) =>
        "{\"follows\":[" + string.Join(",", dids.Select(d => $"{{\"did\":\"{d}\",\"handle\":\"{HandleOf(d)}\"}}")) + "]}";

    private static string FeedJson(IEnumerable<string> posts) => "{\"feed\":[" + string.Join(",", posts) + "]}";

    private static Handler Net(params string[] follows) => new(url =>
    {
        if (url.Contains(FollowsCall)) return (HttpStatusCode.OK, FollowsJson(follows));
        string actor = ActorOf(url);
        // every author feed has one original post and one repost of a stranger
        return (HttpStatusCode.OK, FeedJson([Post(actor, HandleOf(actor), "orig"), Post("did:plc:stranger", "stranger.test", "rp", repost: true)]));
    });

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public void ParsePage_drops_reposts_unless_asked_to_keep_them()
    {
        string json = JetstreamParserTests.Fixture("feed-page.json");
        Assert.Equal(FeedMapper.ParsePage(json).Posts.Count, FeedMapper.ParsePage(json, skipReposts: true).Posts.Count);
        Assert.True(FeedMapper.ParsePage(json, skipReposts: true).Posts.Count < FeedMapper.ParsePage(json, skipReposts: false).Posts.Count);
    }

    [Fact]
    public async Task Reposts_are_dropped_and_only_followed_authors_remain()
    {
        var src = new FollowsFeedSource(new HttpClient(Net("did:plc:alice111")), "me.test", NoDelay);
        var page = await src.GetPageAsync(AnyFeedUri, null, PageLimit, default);
        var post = Assert.Single(page.Posts);
        Assert.Equal("did:plc:alice111", post.AuthorDid);
    }

    [Fact]
    public async Task One_page_interleaves_across_the_follows()
    {
        const int PostsOnAuthorPage = 12;   // more than one page takes, so the next page has leftovers
        string[] follows = ["did:plc:alice111", "did:plc:bob22222", "did:plc:carol333"];
        string Many(string actor) => FeedJson(Enumerable.Range(1, PostsOnAuthorPage).Select(i => Post(actor, HandleOf(actor), "p" + i)));
        var h = new Handler(url => url.Contains(FollowsCall)
            ? (HttpStatusCode.OK, FollowsJson(follows))
            : (HttpStatusCode.OK, Many(ActorOf(url))));
        var src = new FollowsFeedSource(new HttpClient(h), "me.test", NoDelay);
        var posts = (await src.GetPageAsync(AnyFeedUri, null, PageLimit, default)).Posts;
        Assert.Equal(follows.Length * PostsPerAuthor, posts.Count);
        Assert.Equal(follows, posts.Take(follows.Length).Select(p => p.AuthorDid));
        Assert.Equal(follows.Length, posts.Select(p => p.AuthorDid).Distinct().Count());
        // the next page reuses the leftovers: no new author requests
        int before = h.Urls.Count;
        Assert.Equal(follows.Length * PostsPerAuthor, (await src.GetPageAsync(AnyFeedUri, null, PageLimit, default)).Posts.Count);
        Assert.Equal(before, h.Urls.Count);
    }

    [Fact]
    public async Task Rotation_covers_all_follows_in_batches()
    {
        var src = new FollowsFeedSource(new HttpClient(Net("did:plc:a1", "did:plc:a2", "did:plc:a3", "did:plc:a4")), "me.test", NoDelay, width: 2);
        var first = (await src.GetPageAsync(AnyFeedUri, null, PageLimit, default)).Posts.Select(p => p.AuthorDid).ToList();
        var second = (await src.GetPageAsync(AnyFeedUri, null, PageLimit, default)).Posts.Select(p => p.AuthorDid).ToList();
        Assert.Equal(["did:plc:a1", "did:plc:a2"], first);
        Assert.Equal(["did:plc:a3", "did:plc:a4"], second);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_handle_Bluesky_rejects_or_does_not_know_is_asked_for_once_then_reported(HttpStatusCode status)
    {
        var h = new Handler(_ => (status, EmptyJson));
        var src = new FollowsFeedSource(new HttpClient(h), "@Typo", NoDelay);
        var ex = await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync(AnyFeedUri, null, PageLimit, default));
        Assert.Equal("Could not find @Typo on Bluesky.", ex.Message);
        Assert.Single(h.Urls);
        await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync(AnyFeedUri, null, PageLimit, default));
        Assert.Single(h.Urls); // the failure is final, no new requests per page
    }

    [Fact]
    public async Task A_server_error_for_the_follows_is_tried_three_times_with_doubling_waits()
    {
        var h = new Handler(_ => (HttpStatusCode.InternalServerError, EmptyJson));
        var waits = new List<TimeSpan>();
        var src = new FollowsFeedSource(new HttpClient(h), "busy.test", (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        var ex = await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync(AnyFeedUri, null, PageLimit, default));
        Assert.Equal("Bluesky could not load who @busy.test follows just now.", ex.Message);
        Assert.Equal(LoadAttempts, h.Urls.Count);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], waits);
    }

    [Fact]
    public async Task A_rejected_handle_says_one_plain_sentence_and_nothing_of_the_server_body()
    {
        const string body = "{\"error\":\"InvalidRequest\",\"message\":\"Invalid app.bsky.graph.getFollows params: Invalid AT identifier\"}";
        var src = new FollowsFeedSource(new HttpClient(new Handler(_ => (HttpStatusCode.BadRequest, body))), "not a handle", NoDelay);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(new RaiderGame(GameSeed), src, new Fixed(), AnyFeedUri, cts.Token, onFallback: m => { said.Add(m); cts.Cancel(); });
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
        Assert.Equal("Could not find @not a handle on Bluesky. Showing Discover instead.", Assert.Single(said));
    }

    [Fact]
    public async Task A_network_failure_does_not_blame_the_handle_and_keeps_the_cause_inside()
    {
        var src = new FollowsFeedSource(new HttpClient(new ThrowingHandler()), "bsky.app", NoDelay);
        var ex = await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync(AnyFeedUri, null, PageLimit, default));
        Assert.Equal("Could not reach Bluesky to load who @bsky.app follows.", ex.Message);
        Assert.Contains(NetworkFailure, ex.InnerException!.Message);
    }

    [Fact]
    public void An_unrecognised_feed_failure_never_shows_the_raw_message()
    {
        Assert.Equal("could not load the feed", Plain.FeedFailureText(new InvalidOperationException("500 {\"error\":\"boom\"}"), "the feed"));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) => throw new HttpRequestException(NetworkFailure);
    }

    [Fact]
    public async Task A_follows_body_that_is_not_json_is_tried_three_times_then_reported_as_unreadable()
    {
        var h = new Handler(_ => (HttpStatusCode.OK, "<html>bad gateway</html>"));
        var src = new FollowsFeedSource(new HttpClient(h), "bsky.app", NoDelay);
        var ex = await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync(AnyFeedUri, null, PageLimit, default));
        Assert.Equal("Bluesky's list of who @bsky.app follows could not be read.", ex.Message);
        Assert.Equal(LoadAttempts, h.Urls.Count);
    }

    private sealed class BrokenHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException(BrokenCode);
        }
    }

    private const string BrokenCode = "a bug in the game, not a failure of Bluesky";

    [Fact]
    public async Task A_bug_while_loading_the_follows_escapes_as_itself_and_is_not_retried()
    {
        var h = new BrokenHandler();
        var src = new FollowsFeedSource(new HttpClient(h), "bsky.app", NoDelay);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => src.GetPageAsync(AnyFeedUri, null, PageLimit, default));
        Assert.Equal(BrokenCode, ex.Message);
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task Zero_follows_is_reported()
    {
        var src = new FollowsFeedSource(new HttpClient(Net()), "lonely.test", NoDelay);
        var ex = await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync(AnyFeedUri, null, PageLimit, default));
        Assert.Equal("@lonely.test follows nobody the game can show.", ex.Message);
    }

    private sealed class Fixed(params FeedPost[] posts) : IFeedSource
    {
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) => Task.FromResult(new FeedPage(posts, null));
    }

    [Fact]
    public async Task Posts_report_the_fallback_to_Discover()
    {
        var failing = new FollowsFeedSource(new HttpClient(new Handler(_ => (HttpStatusCode.InternalServerError, EmptyJson))), "x.test", NoDelay);
        var discover = Parsed("did:plc:stranger", "d1");
        var game = new RaiderGame(GameSeed);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, failing, new Fixed(discover), AnyFeedUri, cts.Token, onFallback: m => { lock (said) said.Add(m); cts.Cancel(); });
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
        var msg = Assert.Single(said);
        Assert.Equal("Bluesky could not load who @x.test follows just now. Showing Discover instead.", msg);
    }

    private sealed class Flaky(int failures, IFeedSource inner, Exception? error = null) : IFeedSource
    {
        public int Calls;
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) =>
            ++Calls <= failures ? throw (error ?? new HttpRequestException("timeout")) : inner.GetPageAsync(feedUri, cursor, limit, ct);
    }

    private sealed class Counting(params FeedPost[] posts) : IFeedSource
    {
        public int Calls;
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) { Calls++; return Task.FromResult(new FeedPage(posts, null)); }
    }

    private static FeedPost Parsed(string did, string rkey) => FeedMapper.ParsePage(FeedJson([Post(did, HandleOf(did), rkey)])).Posts[0];

    [Fact]
    public async Task Transient_primary_errors_are_retried_and_never_fill_from_the_fallback()
    {
        var mine = Parsed("did:plc:alice111", "mine-with-enough-text");
        var primary = new Flaky(2, new Fixed(mine));
        var fallback = new Counting(Parsed("did:plc:stranger", "s1"));
        var game = new RaiderGame(GameSeed);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, primary, fallback, AnyFeedUri, cts.Token, onFallback: said.Add, backoffMs: FastBackoffMs);
        await Wait.UntilAsync(() => { lock (game) return game.Posts.Count > 0; }, "a post after the retries");
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
        Assert.Equal(0, fallback.Calls);
        Assert.Empty(said);
        lock (game) { Assert.True(game.Posts.TryDequeue(out var p, out _)); Assert.Equal("did:plc:alice111", p.AuthorDid); }
    }

    [Fact]
    public async Task Definite_failure_switches_with_the_message_and_is_not_retried()
    {
        var primary = new Flaky(int.MaxValue, new Fixed(), new FollowsUnavailableException("Could not find @x.test on Bluesky."));
        var game = new RaiderGame(GameSeed);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, primary, new Fixed(Parsed("did:plc:stranger", "d1")), AnyFeedUri, cts.Token, onFallback: m => { said.Add(m); cts.Cancel(); });
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
        Assert.Equal("Could not find @x.test on Bluesky. Showing Discover instead.", Assert.Single(said));
        Assert.Equal(1, primary.Calls);
    }

    [Fact]
    public async Task Transient_failure_for_long_enough_with_nothing_buffered_switches_with_a_message()
    {
        var primary = new Flaky(int.MaxValue, new Fixed());
        var game = new RaiderGame(GameSeed);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, primary, new Fixed(), AnyFeedUri, cts.Token, onFallback: m => { said.Add(m); cts.Cancel(); }, backoffMs: FastBackoffMs, failoverMs: 100);
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
        Assert.Equal("could not reach Bluesky for the feed; showing Discover", Assert.Single(said));
    }

    [Fact]
    public async Task One_follows_429_still_yields_the_others()
    {
        var h = new Handler(url =>
        {
            if (url.Contains(FollowsCall)) return (HttpStatusCode.OK, FollowsJson("did:plc:alice111", "did:plc:bob22222", "did:plc:carol333"));
            string actor = ActorOf(url);
            return actor == "did:plc:bob22222" ? (HttpStatusCode.TooManyRequests, EmptyJson)
                : (HttpStatusCode.OK, FeedJson([Post(actor, HandleOf(actor), "orig")]));
        });
        var src = new FollowsFeedSource(new HttpClient(h), "me.test", NoDelay);
        var page = await src.GetPageAsync(AnyFeedUri, null, PageLimit, default);
        Assert.Equal(["did:plc:alice111", "did:plc:carol333"], page.Posts.Select(p => p.AuthorDid));
        // the limited follow sits out and the source pauses: the next call makes no new author requests
        int before = h.Urls.Count;
        Assert.Empty((await src.GetPageAsync(AnyFeedUri, null, PageLimit, default)).Posts);
        Assert.Equal(before, h.Urls.Count);
    }
}
