using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class FeedInterleaverTests
{
    private static FeedPost P(string author, string id, string? parent = null, string? root = null) =>
        new($"at://{author}/app.bsky.feed.post/{id}", "c" + id, author, author + ".test", "a long enough synthetic post text " + id, 0, 0, 0,
            root is null ? null : new PostRef($"at://{root}", "cr"), parent is null ? null : new PostRef($"at://{parent}", "cp"));

    private static string Uri(string author, string id) => $"{author}/app.bsky.feed.post/{id}";

    private static List<FeedPost> Drain(FeedInterleaver m, bool repeat = false)
    {
        var o = new List<FeedPost>();
        while (m.TryTake(out var p, repeat)) o.Add(p);
        return o;
    }

    [Fact]
    public void No_two_consecutive_posts_share_an_author()
    {
        var m = new FeedInterleaver();
        m.Add(Enumerable.Range(1, 6).Select(i => P("a", "a" + i)).Concat(Enumerable.Range(1, 4).Select(i => P("b", "b" + i))).Concat(Enumerable.Range(1, 3).Select(i => P("c", "c" + i))));
        var o = Drain(m);
        Assert.Equal(["a", "b", "a", "b", "a", "b", "a", "b", "a", "c", "a", "c"], o.Select(p => p.AuthorDid).Take(12));
        for (int i = 1; i < o.Count; i++) Assert.NotEqual(o[i - 1].AuthorDid, o[i].AuthorDid);
        Assert.Equal(1, m.Count); // what is left is a lone "a", held back
    }

    [Fact]
    public void Self_thread_stays_in_order_and_contiguous()
    {
        var m = new FeedInterleaver();
        var a1 = P("a", "1");
        var a2 = P("a", "2", parent: Uri("a", "1"), root: Uri("a", "1"));
        var a3 = P("a", "3", parent: Uri("a", "2"), root: Uri("a", "1"));
        m.Add([P("b", "1"), a1, P("c", "1"), a3, a2, P("b", "2")]);
        var o = Drain(m, repeat: true).Select(p => p.Uri).ToList();
        int i = o.IndexOf(a1.Uri);
        Assert.Equal([a1.Uri, a2.Uri, a3.Uri], o.Skip(i).Take(3));
    }

    [Fact]
    public void All_one_author_waits_until_a_repeat_is_allowed()
    {
        var m = new FeedInterleaver();
        m.Add([P("a", "1"), P("a", "2"), P("a", "3")]);
        Assert.True(m.TryTake(out _));
        Assert.False(m.TryTake(out _));
        Assert.Equal(2, m.Count);
        m.Add([P("b", "1")]);
        Assert.True(m.TryTake(out var p));
        Assert.Equal("b", p.AuthorDid);
        Assert.True(m.TryTake(out p, allowRepeat: false));
        Assert.Equal("a", p.AuthorDid);
        Assert.False(m.TryTake(out _));
        Assert.True(m.TryTake(out p, allowRepeat: true));
        Assert.Equal("a", p.AuthorDid);
    }

    [Fact]
    public void Duplicates_and_ineligible_posts_are_dropped()
    {
        var m = new FeedInterleaver();
        m.Add([P("a", "1"), P("a", "1"), P("b", "x") with { Text = "hi" }]);
        Assert.Equal(1, m.Count);
    }

    [Fact]
    public async Task Wingmen_serve_an_alternating_queue()
    {
        var posts = Enumerable.Range(1, 5).Select(i => P("a", "a" + i)).Concat(Enumerable.Range(1, 5).Select(i => P("b", "b" + i))).ToArray();
        var game = new RaiderGame(1);
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, new Once(posts), null, "", cts.Token);
        await Task.Delay(1500);
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        var served = new List<FeedPost>();
        lock (game) while (game.Queue.TryDequeue(out var p)) served.Add(p);
        Assert.Equal(10, served.Count);
        for (int i = 1; i < served.Count; i++) Assert.NotEqual(served[i - 1].AuthorDid, served[i].AuthorDid);
    }

    private sealed class Once(FeedPost[] posts) : IFeedSource
    {
        private bool _done;
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
        {
            var r = _done ? [] : posts; _done = true;
            return Task.FromResult(new FeedPage(r, null));
        }
    }
}
