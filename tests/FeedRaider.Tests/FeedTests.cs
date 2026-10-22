using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class FeedTests
{
    private static FeedPage Page() => FeedMapper.ParsePage(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feed-page.json")));

    [Fact]
    public void ParsePage_maps_posts_counts_and_cursor()
    {
        var page = Page();
        Assert.Equal("1760000000::bafyabc", page.Cursor);
        Assert.Equal(3, page.Posts.Count); // the post without an author is dropped
        var top = page.Posts[0];
        Assert.Equal("at://did:plc:aaaa/app.bsky.feed.post/3top", top.Uri);
        Assert.Equal("bafytop", top.Cid);
        Assert.Equal("did:plc:aaaa", top.AuthorDid);
        Assert.Equal("alice.test", top.AuthorHandle);
        Assert.Equal("a top-level post", top.Text);
        Assert.Equal((17, 2, 3), (top.LikeCount, top.RepostCount, top.ReplyCount));
        Assert.Null(top.ReplyRoot);
        Assert.Null(top.ReplyParent);
    }

    [Fact]
    public void ParsePage_keeps_reply_refs_and_tolerates_missing_fields()
    {
        var page = Page();
        var reply = page.Posts[1];
        Assert.Equal(new PostRef("at://did:plc:aaaa/app.bsky.feed.post/3top", "bafytop"), reply.ReplyRoot);
        Assert.Equal(new PostRef("at://did:plc:cccc/app.bsky.feed.post/3mid", "bafymid"), reply.ReplyParent);
        var minimal = page.Posts[2];
        Assert.Equal("did:plc:eeee", minimal.AuthorHandle); // falls back to the DID
        Assert.Equal("", minimal.Text);
        Assert.Equal(0, minimal.LikeCount);
    }

    [Theory]
    [InlineData(null, Feeds.Discover)]
    [InlineData("discover", Feeds.Discover)]
    [InlineData("foryou", Feeds.ForYou)]
    [InlineData("at://did:plc:x/app.bsky.feed.generator/y", "at://did:plc:x/app.bsky.feed.generator/y")]
    [InlineData("https://evil.example", null)]
    public void Feeds_resolve(string? input, string? expected) => Assert.Equal(expected, Feeds.Resolve(input));

    [Fact]
    public async Task Fakes_satisfy_the_interfaces()
    {
        var fake = new Fake();
        var page = await ((IFeedSource)fake).GetPageAsync(Feeds.Discover, null, 10, default);
        var liked = await ((IActions)fake).LikeAsync(page.Posts[0].Ref, default);
        Assert.Equal("at://me/app.bsky.feed.like/1", liked.Uri);
        Assert.Equal("did:plc:me", ((ISession)fake).Current?.Did);
    }

    private sealed class Fake : IFeedSource, IActions, ISession
    {
        public SessionInfo? Current => new("did:plc:me", "me.example");
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) => Task.FromResult(Page());
        public Task<PostRef> LikeAsync(PostRef post, CancellationToken ct) => Task.FromResult(new PostRef("at://me/app.bsky.feed.like/1", "c"));
    }
}
