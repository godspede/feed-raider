using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class RandomFeedTests
{
    private static string Fix(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));

    private static FeedPage Parse(string name, bool signedOut = true) => FeedMapper.ParsePage(Fix(name), signedOut: signedOut);

    /// <summary>Answers each feed URI from a list of fixture names, the last one repeating; a name starting with "!" throws, "" is an empty page.</summary>
    private sealed class FakeFeeds(Dictionary<string, string[]> script) : IFeedSource
    {
        private readonly Dictionary<string, int> _at = [];
        public List<string> Calls { get; } = [];
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
        {
            Calls.Add(feedUri);
            var list = script.GetValueOrDefault(feedUri) ?? ["!"];
            int i = _at.GetValueOrDefault(feedUri);
            _at[feedUri] = i + 1;
            string step = list[Math.Min(i, list.Length - 1)];
            if (step.StartsWith('!')) throw new InvalidOperationException("getFeed failed: 502");
            return Task.FromResult(step == "" ? new FeedPage([], null) : Parse(step));
        }
    }

    private static async Task<List<FeedPost>> Drain(RandomFeedSource src, int pages)
    {
        var all = new List<FeedPost>();
        for (int i = 0; i < pages; i++) all.AddRange((await src.GetPageAsync(Feeds.Random, null, 30, default)).Posts);
        return all;
    }

    [Fact]
    public void Random_name_resolves_and_the_feed_uris_are_the_researched_ones()
    {
        Assert.Equal(Feeds.Random, Feeds.Resolve("random"));
        Assert.Equal(Feeds.Random, Feeds.Resolve(" Random "));
        Assert.EndsWith("/aaachysnvmkze", Feeds.RandomPrimary);
        Assert.EndsWith("/aaak3mu6ug2fi", Feeds.RandomSecond);
        Assert.EndsWith("/aaaeki3ljjz7u", Feeds.RandomBackfill);
    }

    [Fact]
    public void Author_opt_out_is_dropped_signed_out_only()
    {
        var signedOut = Parse("feed-labels", signedOut: true);
        var signedIn = Parse("feed-labels", signedOut: false);
        Assert.DoesNotContain(signedOut.Posts, p => p.AuthorHandle == "optout.example.com");
        var optout = signedIn.Posts.Single(p => p.AuthorHandle == "optout.example.com");
        Assert.True(optout.AuthorOptOut);
        Assert.True(WingQueue.Eligible(optout));
        Assert.False(WingQueue.Eligible(optout, signedOut: true));
    }

    [Theory]
    [InlineData("porn")]
    [InlineData("sexual")]
    [InlineData("nudity")]
    [InlineData("graphic")]
    [InlineData("aporn")]
    [InlineData("asexual")]
    [InlineData("anudity")]
    [InlineData("agraphic")]
    public void Adult_labels_on_the_post_or_the_author_are_dropped_in_every_mode(string who)
    {
        foreach (bool signedOut in new[] { true, false })
            Assert.DoesNotContain(Parse("feed-labels", signedOut).Posts, p => p.AuthorHandle == who + ".example.com");
    }

    [Fact]
    public void Eligible_rejects_adult_posts_built_by_hand()
    {
        var p = new FeedPost("at://x/p/1", "c", "did:plc:x", "x.example.com", "a plain joke about a very confused pigeon", 1, 0, 0, null, null, Adult: true);
        Assert.False(WingQueue.Eligible(p));
        Assert.True(WingQueue.Eligible(p with { Adult = false }));
        Assert.False(new WingQueue().Offer(p));
    }

    [Fact]
    public void Harmless_labels_and_the_plain_post_stay()
    {
        var handles = Parse("feed-labels").Posts.Select(p => p.AuthorHandle).ToList();
        Assert.Equal(["plain.example.com", "harmless.example.com"], handles);
    }

    [Fact]
    public void Interleaver_drops_opted_out_authors_only_when_signed_out()
    {
        var all = Parse("feed-labels", signedOut: false).Posts;
        var signedOut = new FeedInterleaver(signedOut: true);
        signedOut.Add(all);
        var signedIn = new FeedInterleaver();
        signedIn.Add(all);
        Assert.Equal(2, signedOut.Count);
        Assert.Equal(3, signedIn.Count); // plain, harmless and the opted-out author; adult posts never get in
    }

    [Fact]
    public async Task The_two_primaries_alternate_page_through_and_never_repeat()
    {
        var feeds = new FakeFeeds(new()
        {
            [Feeds.RandomPrimary] = ["random-primary-1", "random-primary-2"],
            [Feeds.RandomSecond] = ["random-second-1"],
            [Feeds.RandomBackfill] = ["random-backfill-1"],
        });
        var posts = await Drain(new RandomFeedSource(feeds), 2);
        var uris = posts.Select(p => p.Uri).ToList();
        Assert.Equal(uris.Count, uris.Distinct().Count()); // lmao6 is on both primary pages and lmao2 on both feeds, each served once
        Assert.Equal(9, posts.Count(p => p.AuthorHandle.StartsWith("lmao")));
        Assert.Equal(3, posts.Count(p => p.AuthorHandle.StartsWith("bang")));
        Assert.Equal(["lmao1", "bang1", "lmao2", "lmao3", "bang2"], posts.Take(5).Select(p => p.AuthorHandle.Split('.')[0]));
        Assert.Equal(2, feeds.Calls.Count(c => c == Feeds.RandomPrimary)); // more than one page
        Assert.DoesNotContain(posts, p => p.AuthorHandle.StartsWith("poster")); // the primaries had new posts each time
        Assert.DoesNotContain(Feeds.RandomBackfill, feeds.Calls);
    }

    [Fact]
    public async Task Backfill_serves_once_the_primaries_bring_nothing_new()
    {
        var feeds = new FakeFeeds(new()
        {
            [Feeds.RandomPrimary] = ["random-primary-2"],
            [Feeds.RandomSecond] = ["random-second-1"],
            [Feeds.RandomBackfill] = ["random-backfill-1"],
        });
        var src = new RandomFeedSource(feeds);
        var first = (await src.GetPageAsync(Feeds.Random, null, 30, default)).Posts;
        Assert.DoesNotContain(first, p => p.AuthorHandle.StartsWith("poster"));
        var second = (await src.GetPageAsync(Feeds.Random, null, 30, default)).Posts; // the primary is done and the second repeats itself
        Assert.Equal(["poster1.example.com", "poster2.example.com", "poster3.example.com", "poster4.example.com"], second.Select(p => p.AuthorHandle));
        Assert.False(src.OnLastResort);
    }

    [Fact]
    public async Task One_primary_failing_leaves_the_other_and_is_not_asked_again()
    {
        var feeds = new FakeFeeds(new()
        {
            [Feeds.RandomPrimary] = ["!"],
            [Feeds.RandomSecond] = ["random-second-1"],
            [Feeds.RandomBackfill] = ["random-backfill-1"],
        });
        var src = new RandomFeedSource(feeds);
        var posts = (await src.GetPageAsync(Feeds.Random, null, 30, default)).Posts;
        Assert.Equal(["bang1", "lmao2", "bang2", "bang3"], posts.Select(p => p.AuthorHandle.Split('.')[0]));
        await src.GetPageAsync(Feeds.Random, null, 30, default);
        Assert.Equal(1, feeds.Calls.Count(c => c == Feeds.RandomPrimary));
    }

    [Fact]
    public async Task Primaries_failing_or_empty_fall_through_to_the_backfill()
    {
        var feeds = new FakeFeeds(new() { [Feeds.RandomPrimary] = [""], [Feeds.RandomSecond] = ["!"], [Feeds.RandomBackfill] = ["random-backfill-1"] });
        var posts = await Drain(new RandomFeedSource(feeds), 1);
        Assert.NotEmpty(posts);
        Assert.All(posts, p => Assert.StartsWith("poster", p.AuthorHandle));
    }

    [Fact]
    public void Labels_beyond_opt_out_and_adult_mark_the_post_and_the_harmless_ones_do_not()
    {
        var byHandle = Parse("random-labels").Posts.ToDictionary(p => p.AuthorHandle.Split('.')[0], p => p.Labelled);
        Assert.Equal(6, byHandle.Count); // parsing keeps them all; only Random skips the marked ones
        Assert.False(byHandle["plainjoke"]);
        Assert.False(byHandle["bridged"]);
        Assert.True(byHandle["rude"]);       // on the post
        Assert.True(byHandle["intolerant"]); // on the author
        Assert.True(byHandle["selflabel"]);  // the post's own label
        Assert.True(byHandle["warned"]);
        Assert.True(Parse("feed-labels").Posts.Single(p => p.AuthorHandle == "harmless.example.com").Labelled);
    }

    [Fact]
    public async Task Tone_guard_keeps_labelled_posts_out_of_every_Random_feed()
    {
        foreach (var uri in new[] { Feeds.RandomPrimary, Feeds.RandomSecond, Feeds.RandomBackfill, Feeds.Discover })
        {
            var script = new Dictionary<string, string[]> { [Feeds.RandomPrimary] = ["!"], [Feeds.RandomSecond] = ["!"], [Feeds.RandomBackfill] = ["!"], [uri] = ["random-labels"] };
            var feeds = new FakeFeeds(script);
            var page = await new RandomFeedSource(feeds, feeds).GetPageAsync(Feeds.Random, null, 30, default);
            Assert.Equal(["plainjoke.example.com", "bridged.example.com"], page.Posts.Select(p => p.AuthorHandle));
        }
        Assert.False(RandomFeedSource.Plain(new FeedPost("at://x/p/1", "c", "did:plc:x", "x.example.com", "a plain joke about a very confused pigeon", 1, 0, 0, null, null, Labelled: true)));
    }

    [Fact]
    public async Task Both_fail_then_Discover_plays()
    {
        var feeds = new FakeFeeds(new()
        {
            [Feeds.RandomPrimary] = ["!"],
            [Feeds.RandomSecond] = ["!"],
            [Feeds.RandomBackfill] = ["!"],
            [Feeds.Discover] = ["random-discover"],
        });
        var src = new RandomFeedSource(feeds, feeds);
        var page = await src.GetPageAsync(Feeds.Random, null, 30, default);
        Assert.Equal(["disc1.example.com", "disc2.example.com"], page.Posts.Select(p => p.AuthorHandle));
        Assert.True(src.OnLastResort);
    }

    [Fact]
    public async Task Discover_failing_too_reaches_the_caller()
    {
        var feeds = new FakeFeeds([]);
        var src = new RandomFeedSource(feeds, feeds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => src.GetPageAsync(Feeds.Random, null, 30, default));
    }

    [Fact]
    public void Random_plan_is_the_same_signed_in_or_out()
    {
        var s = new FakeFeeds([]);
        foreach (bool signedIn in new[] { false, true })
        {
            var plan = FeedPlan.Choose("random", signedIn, "", s, s, s, _ => s)!;
            Assert.IsType<RandomFeedSource>(plan.Primary);
            Assert.Null(plan.Fallback);
        }
    }
}
