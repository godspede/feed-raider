using System.Net;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class FollowsFeedTests
{
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
        "{\"follows\":[" + string.Join(",", dids.Select(d => $"{{\"did\":\"{d}\",\"handle\":\"{d[8..]}.test\"}}")) + "]}";

    private static Handler Net(params string[] follows) => new(url =>
    {
        if (url.Contains("getFollows")) return (HttpStatusCode.OK, FollowsJson(follows));
        string actor = Uri.UnescapeDataString(url.Split("actor=")[1].Split('&')[0]);
        // every author feed has one original post and one repost of a stranger
        return (HttpStatusCode.OK, "{\"feed\":[" + Post(actor, actor[8..] + ".test", "orig") + "," + Post("did:plc:stranger", "stranger.test", "rp", repost: true) + "]}");
    });

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public void ParsePage_can_drop_reposts()
    {
        string json = JetstreamParserTests.Fixture("feed-page.json");
        Assert.True(FeedMapper.ParsePage(json, skipReposts: true).Posts.Count < FeedMapper.ParsePage(json).Posts.Count);
    }

    [Fact]
    public async Task Reposts_are_dropped_and_only_followed_authors_remain()
    {
        var src = new FollowsFeedSource(new HttpClient(Net("did:plc:alice111")), "me.test", NoDelay);
        var page = await src.GetPageAsync("", null, 30, default);
        var post = Assert.Single(page.Posts);
        Assert.Equal("did:plc:alice111", post.AuthorDid);
    }

    [Fact]
    public async Task One_page_interleaves_across_the_follows()
    {
        string Many(string actor) => "{\"feed\":[" + string.Join(",", Enumerable.Range(1, 12).Select(i => Post(actor, actor[8..] + ".test", "p" + i))) + "]}";
        var h = new Handler(url => url.Contains("getFollows")
            ? (HttpStatusCode.OK, FollowsJson("did:plc:alice111", "did:plc:bob22222", "did:plc:carol333"))
            : (HttpStatusCode.OK, Many(Uri.UnescapeDataString(url.Split("actor=")[1].Split('&')[0]))));
        var src = new FollowsFeedSource(new HttpClient(h), "me.test", NoDelay);
        var posts = (await src.GetPageAsync("", null, 30, default)).Posts;
        Assert.Equal(9, posts.Count);
        Assert.Equal(["did:plc:alice111", "did:plc:bob22222", "did:plc:carol333"], posts.Take(3).Select(p => p.AuthorDid));
        Assert.Equal(3, posts.Select(p => p.AuthorDid).Distinct().Count());
        // the next page reuses the leftovers: no new author requests
        int before = h.Urls.Count;
        Assert.Equal(9, (await src.GetPageAsync("", null, 30, default)).Posts.Count);
        Assert.Equal(before, h.Urls.Count);
    }

    [Fact]
    public async Task Rotation_covers_all_follows_in_batches()
    {
        var src = new FollowsFeedSource(new HttpClient(Net("did:plc:a1", "did:plc:a2", "did:plc:a3", "did:plc:a4")), "me.test", NoDelay, width: 2);
        var first = (await src.GetPageAsync("", null, 30, default)).Posts.Select(p => p.AuthorDid).ToList();
        var second = (await src.GetPageAsync("", null, 30, default)).Posts.Select(p => p.AuthorDid).ToList();
        Assert.Equal(["did:plc:a1", "did:plc:a2"], first);
        Assert.Equal(["did:plc:a3", "did:plc:a4"], second);
    }

    [Fact]
    public async Task Follows_failure_is_retried_then_reported()
    {
        var h = new Handler(_ => (HttpStatusCode.BadRequest, "{}"));
        var src = new FollowsFeedSource(new HttpClient(h), "@Typo", NoDelay);
        var ex = await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync("", null, 30, default));
        Assert.Contains("couldn't load @Typo's follows", ex.Message);
        Assert.Equal(3, h.Urls.Count);
        await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync("", null, 30, default));
        Assert.Equal(3, h.Urls.Count); // the failure is final, no new requests per page
    }

    [Fact]
    public async Task Zero_follows_is_reported()
    {
        var src = new FollowsFeedSource(new HttpClient(Net()), "lonely.test", NoDelay);
        var ex = await Assert.ThrowsAsync<FollowsUnavailableException>(() => src.GetPageAsync("", null, 30, default));
        Assert.Contains("follows nobody", ex.Message);
    }

    private sealed class Fixed(params FeedPost[] posts) : IFeedSource
    {
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) => Task.FromResult(new FeedPage(posts, null));
    }

    [Fact]
    public async Task Wingmen_report_the_fallback_to_Discover()
    {
        var failing = new FollowsFeedSource(new HttpClient(new Handler(_ => (HttpStatusCode.InternalServerError, ""))), "x.test", NoDelay);
        var discover = FeedMapper.ParsePage("{\"feed\":[" + Post("did:plc:stranger", "s.test", "d1") + "]}").Posts[0];
        var game = new RaiderGame(1);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, failing, new Fixed(discover), "", cts.Token, onFallback: m => { lock (said) said.Add(m); cts.Cancel(); });
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        var msg = Assert.Single(said);
        Assert.StartsWith("couldn't load @x.test's follows", msg);
        Assert.EndsWith("showing Discover", msg);
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

    private static FeedPost Parsed(string did, string rkey) => FeedMapper.ParsePage("{\"feed\":[" + Post(did, did[8..] + ".test", rkey) + "]}").Posts[0];

    [Fact]
    public async Task Transient_primary_errors_are_retried_and_never_fill_from_the_fallback()
    {
        var mine = Parsed("did:plc:alice111", "mine-with-enough-text");
        var primary = new Flaky(2, new Fixed(mine));
        var fallback = new Counting(Parsed("did:plc:stranger", "s1"));
        var game = new RaiderGame(1);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, primary, fallback, "", cts.Token, onFallback: said.Add, backoffMs: 10);
        await Task.Delay(1500);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, fallback.Calls);
        Assert.Empty(said);
        lock (game) { Assert.True(game.Queue.TryDequeue(out var p)); Assert.Equal("did:plc:alice111", p.AuthorDid); }
    }

    [Fact]
    public async Task Definite_failure_switches_with_the_message_and_never_asks_again_for_the_primary()
    {
        var primary = new Flaky(int.MaxValue, new Fixed(), new FollowsUnavailableException("couldn't load @x.test's follows (boom)"));
        var game = new RaiderGame(1);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, primary, new Fixed(Parsed("did:plc:stranger", "d1")), "", cts.Token, onFallback: m => { said.Add(m); cts.Cancel(); });
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("couldn't load @x.test's follows (boom); showing Discover", Assert.Single(said));
    }

    [Fact]
    public async Task Transient_failure_for_long_enough_with_nothing_buffered_switches_with_a_message()
    {
        var primary = new Flaky(int.MaxValue, new Fixed());
        var game = new RaiderGame(1);
        var said = new List<string>();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, primary, new Fixed(), "", cts.Token, onFallback: m => { said.Add(m); cts.Cancel(); }, backoffMs: 10, failoverMs: 100);
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("could not reach Bluesky for the feed; showing Discover", Assert.Single(said));
    }

    [Fact]
    public async Task One_follows_429_still_yields_the_others()
    {
        var h = new Handler(url =>
        {
            if (url.Contains("getFollows")) return (HttpStatusCode.OK, FollowsJson("did:plc:alice111", "did:plc:bob22222", "did:plc:carol333"));
            string actor = Uri.UnescapeDataString(url.Split("actor=")[1].Split('&')[0]);
            return actor == "did:plc:bob22222" ? (HttpStatusCode.TooManyRequests, "{}")
                : (HttpStatusCode.OK, "{\"feed\":[" + Post(actor, actor[8..] + ".test", "orig") + "]}");
        });
        var src = new FollowsFeedSource(new HttpClient(h), "me.test", NoDelay);
        var page = await src.GetPageAsync("", null, 30, default);
        Assert.Equal(["did:plc:alice111", "did:plc:carol333"], page.Posts.Select(p => p.AuthorDid));
        // the limited follow sits out and the source pauses: the next call makes no new author requests
        int before = h.Urls.Count;
        Assert.Empty((await src.GetPageAsync("", null, 30, default)).Posts);
        Assert.Equal(before, h.Urls.Count);
    }
}
