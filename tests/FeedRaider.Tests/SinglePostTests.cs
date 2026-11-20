using System.Text.Json;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Which feed items become posts to show: a top-level post with some text, not a repost and not only a link. A quote or an
// attachment is marked on the post (a card shows it), and a reply that follows its own thread must be plain. The JSON is
// shaped like real AppView responses with placeholder authors.
public class SinglePostTests
{
    private const string Text = "a plain sentence long enough to be shown";

    private static string Item(string id, string record = "", string post = "", string item = "") =>
        $$"""{"post":{"uri":"at://did:plc:b1/app.bsky.feed.post/{{id}}","cid":"c{{id}}","author":{"did":"did:plc:b1","handle":"b1.test"},"record":{"text":"{{Text}}"{{record}}}{{post}}}{{item}}}""";

    private static bool Shown(string item)
    {
        var page = FeedMapper.ParsePage($$"""{"feed":[{{item}}]}""");
        return page.Posts.Count == 1 && PostRules.Shown(page.Posts[0], signedOut: false, textOnly: false);
    }

    private static FeedPost One(string item) => Assert.Single(FeedMapper.ParsePage($$"""{"feed":[{{item}}]}""").Posts);

    [Fact]
    public void A_plain_post_is_shown()
    {
        Assert.True(Shown(Item("1")));
        Assert.True(Shown(Item("1", post: ",\"embed\":null")));
    }

    [Theory]
    [InlineData("app.bsky.embed.images#view")]
    [InlineData("app.bsky.embed.video#view")]
    [InlineData("app.bsky.embed.external#view")]
    [InlineData("app.bsky.embed.record#view")]
    [InlineData("app.bsky.embed.recordWithMedia#view")]
    [InlineData("app.bsky.embed.gallery#view")]
    public void Any_embed_kind_is_marked_on_the_post_and_keeps_it_from_a_thread_follow(string kind)
    {
        var post = One(Item("1", post: $$""","embed":{"$type":"{{kind}}"}"""));
        Assert.True(post.HasEmbed);
        Assert.False(PostRules.Followable(post));
    }

    [Fact]
    public void A_record_that_carries_an_embed_is_marked_even_when_the_view_dropped_it()
    {
        Assert.True(One(Item("1", record: ""","embed":{"$type":"app.bsky.embed.record","record":{"uri":"at://did:plc:b2/app.bsky.feed.post/9","cid":"c9"}}""")).HasEmbed);
        Assert.True(One(Item("1", record: ""","embed":{"$type":"app.bsky.embed.images","images":[]}""")).HasEmbed);
    }

    [Fact]
    public void A_reply_shown_without_its_parent_is_out()
    {
        const string reply = ""","reply":{"root":{"uri":"at://did:plc:b2/app.bsky.feed.post/r","cid":"cr"},"parent":{"uri":"at://did:plc:b2/app.bsky.feed.post/r","cid":"cr"}}""";
        Assert.False(Shown(Item("1", record: reply)));
        Assert.False(Shown(Item("1", record: ""","reply":{"root":{"uri":"at://did:plc:b2/app.bsky.feed.post/r","cid":"cr"}}""")));
    }

    [Fact]
    public void A_repost_is_out_in_every_source()
    {
        const string repost = ""","reason":{"$type":"app.bsky.feed.defs#reasonRepost","by":{"did":"did:plc:b3","handle":"b3.test"}}""";
        Assert.Empty(FeedMapper.ParsePage($$"""{"feed":[{{Item("1", item: repost)}}]}""").Posts);
        Assert.Single(FeedMapper.ParsePage($$"""{"feed":[{{Item("1", item: repost)}}]}""", skipReposts: false).Posts);
    }

    [Theory]
    [InlineData("https://example.test/a/very/long/path/to/an/article")]
    [InlineData("https://bsky.app/profile/b2.test/post/3kabc quoted above for context in this thread")]
    [InlineData("bsky.app/profile/did:plc:b2/post/3kabc and then some words to pad the length out")]
    public void Text_that_is_a_link_or_a_post_url_is_out(string text) =>
        Assert.False(Shown(Item("1").Replace(Text, text)));

    [Fact]
    public void A_link_facet_to_a_post_is_out_but_a_short_label_or_a_sentence_with_one_link_is_not()
    {
        const string facet = ""","facets":[{"index":{"byteStart":0,"byteEnd":4},"features":[{"$type":"app.bsky.richtext.facet#link","uri":"https://example.test/x"}]}]""";
        Assert.True(Shown(Item("1", record: facet).Replace(Text, "link")));   // a label is words, however short
        Assert.True(Shown(Item("1").Replace(Text, "see https://example.test/a")));
        Assert.False(Shown(Item("1", record: ""","facets":[{"index":{"byteStart":0,"byteEnd":4},"features":[{"$type":"app.bsky.richtext.facet#link","uri":"https://bsky.app/profile/b2.test/post/3k"}]}]""")));
        Assert.True(Shown(Item("1").Replace(Text, "a sentence of real words that also mentions https://example.test once")));
    }

    // ------------------------------------------------------------ the author's own thread reply stays

    private static string Thread(string recordExtras, string postExtras = "") =>
        $$$"""{"thread":{"post":{},"replies":[{"post":{"uri":"at://did:plc:b1/app.bsky.feed.post/2","cid":"c2","author":{"did":"did:plc:b1","handle":"b1.test"},"record":{"text":"{{{Text}}}","createdAt":"2026-01-01T00:00:00Z","reply":{"root":{"uri":"at://did:plc:b1/app.bsky.feed.post/1","cid":"c1"},"parent":{"uri":"at://did:plc:b1/app.bsky.feed.post/1","cid":"c1"}}{{{recordExtras}}}}{{{postExtras}}}}}]}}""";

    private static readonly FeedPost Root = new("at://did:plc:b1/app.bsky.feed.post/1", "c1", "did:plc:b1", "b1.test", Text, 0, 0, 1, null, null);

    [Fact]
    public void The_authors_own_reply_still_opens_under_the_post()
    {
        var next = ThreadMapper.Next(Thread(""), Root);
        Assert.NotNull(next);
        Assert.NotNull(next!.ReplyParent);
    }

    [Fact]
    public void A_thread_reply_follows_the_same_plain_rule()
    {
        Assert.Null(ThreadMapper.Next(Thread("", ",\"embed\":{\"$type\":\"app.bsky.embed.record#view\"}"), Root));
        Assert.Null(ThreadMapper.Next(Thread("").Replace(Text, "https://example.test/only/a/link/here"), Root));
        Assert.Null(ThreadMapper.Next(Thread(",\"embed\":{\"$type\":\"app.bsky.embed.images\"}"), Root));
    }

    [Fact]
    public void The_game_refuses_a_non_plain_thread_reply_but_takes_a_plain_one()
    {
        var plain = ThreadMapper.Next(Thread(""), Root)!;
        Assert.True(PostRules.Followable(plain));
        Assert.False(PostRules.Shown(plain, signedOut: false, textOnly: false));   // as a feed item it would be a reply alone
        Assert.False(PostRules.Followable(plain with { LinkText = true }));
        Assert.False(PostRules.Followable(plain with { HasEmbed = true }));
    }
}
