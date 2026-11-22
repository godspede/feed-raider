using System.Text.Json;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class FeedEmbedTests
{
    private const string Did = "did:plc:alice";
    private const string VideoCid = "bafkreivideo";

    private static FeedPost Post(string embedJson)
    {
        string json = $$$$"""
            {"feed":[{"post":{"uri":"at://{{{{Did}}}}/app.bsky.feed.post/1","cid":"c1",
              "author":{"did":"{{{{Did}}}}","handle":"alice.test","labels":[]},
              "record":{"text":"words"},"embed":{{{{embedJson}}}}}}]}
            """;
        return FeedMapper.ParsePage(json, skipReposts: false).Posts.Single();
    }

    private static FeedPage PageOf(string embedJson) =>
        FeedMapper.ParsePage($$$$"""
            {"feed":[{"post":{"uri":"at://{{{{Did}}}}/app.bsky.feed.post/1","cid":"c1","author":{"did":"{{{{Did}}}}","handle":"alice.test"},
              "record":{"text":"words"},"embed":{{{{embedJson}}}}}}]}
            """, skipReposts: false);

    private static string Video(string thumbnail, string extra = "") =>
        $$$$"""{"$type":"app.bsky.embed.video#view","cid":"{{{{VideoCid}}}}","playlist":"https://video.bsky.app/watch/x/y/playlist.m3u8","thumbnail":"{{{{thumbnail}}}}","alt":"a clip"{{{{extra}}}}}""";

    private static string Thumb(string did = Did, string cid = VideoCid) => $"https://video.bsky.app/watch/{did}/{cid}/thumbnail.jpg";

    private const string External = """
        {"$type":"app.bsky.embed.external#view","external":{"uri":"https://example.test/a/b","title":"A title","description":"Some words.","thumb":"https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:alice/bafkreithumb@jpeg"}}
        """;

    [Fact]
    public void An_external_view_becomes_a_link_card_with_its_thumb()
    {
        var p = Post(External);
        Assert.Equal(new LinkCard("A title", "example.test", "Some words.", "https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:alice/bafkreithumb@jpeg"), p.Link);
        Assert.Equal(AttachmentKind.Link, p.Attachment);
    }

    [Fact]
    public void An_external_view_without_a_thumb_is_a_link_card_with_no_thumb_address()
    {
        var p = Post("""{"$type":"app.bsky.embed.external#view","external":{"uri":"https://example.test/","title":"T","description":""}}""");
        Assert.NotNull(p.Link);
        Assert.Null(p.Link!.ThumbUrl);
    }

    [Fact]
    public void A_video_whose_thumbnail_names_its_author_and_cid_is_kept_with_its_shape()
    {
        var p = Post(Video(Thumb(), extra: ",\"aspectRatio\":{\"width\":1280,\"height\":720}"));
        Assert.Equal(new VideoRef(Did, VideoCid, 1280.0 / 720, "a clip"), p.Video);
    }

    [Fact]
    public void A_video_without_a_thumbnail_is_left_out_of_the_feed()
    {
        var page = PageOf("""{"$type":"app.bsky.embed.video#view","cid":"bafkreivideo","playlist":"https://video.bsky.app/watch/x/y/playlist.m3u8"}""");
        var p = Assert.Single(page.Posts);
        Assert.Null(p.Video);
        Assert.False(PostRules.Shown(p, false, false));
    }

    [Theory]
    [InlineData("did:plc:mallory", VideoCid)]
    [InlineData(Did, "bafkreiother")]
    public void A_video_whose_thumbnail_names_another_did_or_cid_is_left_out_of_the_feed(string did, string cid)
    {
        var p = Post(Video(Thumb(did, cid)));
        Assert.Null(p.Video);
        Assert.False(PostRules.Shown(p, false, false));
    }

    [Fact]
    public void A_video_thumbnail_on_another_host_is_left_out_of_the_feed()
    {
        var p = Post(Video($"https://evil.test/watch/{Did}/{VideoCid}/thumbnail.jpg"));
        Assert.Null(p.Video);
        Assert.False(PostRules.Shown(p, false, false));
    }

    [Fact]
    public void A_video_with_a_good_thumbnail_is_shown()
    {
        Assert.True(PostRules.Shown(Post(Video(Thumb())), false, false));
    }

    private static string Quote(string recordJson) => $$$$"""{"$type":"app.bsky.embed.record#view","record":{{{{recordJson}}}}}""";

    private static string ViewRecord(string embeds = "[]", string authorExtra = "") => $$$$"""
        {"$type":"app.bsky.embed.record#viewRecord","uri":"at://did:plc:bob/app.bsky.feed.post/9","cid":"c9",
         "author":{"did":"did:plc:bob","handle":"bob.test","displayName":"Bob"{{{{authorExtra}}}}},"value":{"text":"quoted words"},"embeds":{{{{embeds}}}}}
        """;

    [Fact]
    public void A_live_quote_keeps_the_author_and_text()
    {
        var q = Post(Quote(ViewRecord())).Quote!;
        Assert.Equal(QuoteState.Shown, q.State);
        Assert.Equal(("Bob", "bob.test", "quoted words", false), (q.AuthorName, q.AuthorHandle, q.Text, q.HasMedia));
        Assert.False(q.QuotesAnother);
    }

    [Theory]
    [InlineData("app.bsky.embed.record#view")]
    [InlineData("app.bsky.embed.recordWithMedia#view")]
    public void A_quoted_post_that_quotes_another_says_so_and_carries_none_of_it(string type)
    {
        var inner = $$$$"""[{"$type":"{{{{type}}}}","record":{"$type":"app.bsky.embed.record#viewNotFound","uri":"at://did:plc:c/app.bsky.feed.post/1"}}]""";
        var q = Post(Quote(ViewRecord(inner))).Quote!;
        Assert.Equal(QuoteState.Shown, q.State);
        Assert.True(q.QuotesAnother);
        Assert.Equal("quoted words", q.Text);
        // PostQuote has no field for a quote of its own: the inner record reaches it only as the QuotesAnother flag, never as
        // media or as the label of a record that is not a post.
        Assert.False(q.HasMedia);
        Assert.Null(q.Pictures);
        Assert.Null(q.Link);
        Assert.Null(q.Video);
        Assert.Null(q.OtherLabel);
    }

    [Theory]
    [InlineData("app.bsky.embed.record#viewNotFound", QuoteState.Deleted)]
    [InlineData("app.bsky.embed.record#viewBlocked", QuoteState.Blocked)]
    [InlineData("app.bsky.embed.record#viewDetached", QuoteState.Detached)]
    public void A_dead_quote_maps_to_its_state(string type, QuoteState state)
    {
        var q = Post(Quote($$$$"""{"$type":"{{{{type}}}}","uri":"at://did:plc:bob/app.bsky.feed.post/9"}""")).Quote!;
        Assert.Equal(state, q.State);
        Assert.Equal("", q.Text);
    }

    [Theory]
    [InlineData("app.bsky.feed.defs#generatorView", "\"displayName\":\"Cats\"", "a feed: Cats")]
    [InlineData("app.bsky.graph.defs#listView", "\"name\":\"Birds\"", "a list: Birds")]
    [InlineData("app.bsky.labeler.defs#labelerView", "\"creator\":{\"displayName\":\"Mod\"}", "a labeler: Mod")]
    public void A_quoted_feed_list_or_labeler_maps_to_other_with_its_name(string type, string field, string label)
    {
        var q = Post(Quote($$$$"""{"$type":"{{{{type}}}}","uri":"at://x/y/z",{{{{field}}}}}""")).Quote!;
        Assert.Equal(QuoteState.Other, q.State);
        Assert.Equal(label, q.OtherLabel);
    }

    [Fact]
    public void A_quoted_posts_images_are_read_from_its_embeds()
    {
        var images = """[{"$type":"app.bsky.embed.images#view","images":[{"thumb":"https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:bob/bafkreia@jpeg","alt":"x","aspectRatio":{"width":4,"height":3}}]}]""";
        var q = Post(Quote(ViewRecord(images))).Quote!;
        Assert.True(q.HasMedia);
        Assert.Equal(new PostPicture("https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:bob/bafkreia@jpeg", 4, 3), Assert.Single(q.Pictures!));
    }

    [Fact]
    public void A_quoted_posts_video_is_checked_against_the_quoted_author()
    {
        string Own(string did) => $$$$"""[{"$type":"app.bsky.embed.video#view","cid":"{{{{VideoCid}}}}","thumbnail":"{{{{Thumb(did)}}}}"}]""";
        Assert.Equal(new VideoRef("did:plc:bob", VideoCid, null, null), Post(Quote(ViewRecord(Own("did:plc:bob")))).Quote!.Video);
        Assert.Null(Post(Quote(ViewRecord(Own(Did)))).Quote!.Video);
    }

    [Fact]
    public void A_quoted_posts_link_card_is_read_from_a_record_with_media_among_its_embeds()
    {
        var embeds = $$$$"""[{"$type":"app.bsky.embed.recordWithMedia#view","media":{{{{External}}}},"record":{"record":{"$type":"app.bsky.embed.record#viewNotFound"}}}]""";
        var q = Post(Quote(ViewRecord(embeds))).Quote!;
        Assert.Equal("A title", q.Link!.Title);
    }

    [Fact]
    public void A_record_with_media_keeps_its_own_media_and_the_quote()
    {
        var p = Post($$$$"""{"$type":"app.bsky.embed.recordWithMedia#view","media":{{{{Video(Thumb())}}}},"record":{"record":{{{{ViewRecord()}}}}}}""");
        Assert.Equal(AttachmentKind.Quote, p.Attachment);
        Assert.Equal(VideoCid, p.Video!.Cid);
        Assert.Equal(QuoteState.Shown, p.Quote!.State);
    }

    [Fact]
    public void A_quoted_author_with_an_adult_label_is_hidden()
    {
        var q = Post(Quote(ViewRecord(authorExtra: ",\"labels\":[{\"val\":\"porn\"}]"))).Quote!;
        Assert.Equal(QuoteState.Hidden, q.State);
        Assert.Equal("", q.Text);
    }

    [Fact]
    public void A_quoted_author_who_opted_out_is_hidden_signed_out_and_shown_signed_in()
    {
        var q = Post(Quote(ViewRecord(authorExtra: ",\"labels\":[{\"val\":\"!no-unauthenticated\"}]"))).Quote!;
        Assert.Equal(QuoteState.Hidden, PostRules.QuoteStateFor(q, signedOut: true));
        Assert.Equal(QuoteState.Shown, PostRules.QuoteStateFor(q, signedOut: false));
    }

    [Fact]
    public void A_quote_inside_a_quote_is_not_read_as_media()
    {
        var nested = """[{"$type":"app.bsky.embed.record#view","record":{"$type":"app.bsky.embed.record#viewNotFound"}}]""";
        var q = Post(Quote(ViewRecord(nested))).Quote!;
        Assert.False(q.HasMedia);
    }

    [Fact]
    public void The_quoting_posts_own_media_show_and_the_quoted_posts_do_not()
    {
        var images = """[{"$type":"app.bsky.embed.images#view","images":[{"thumb":"https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:bob/bafkreia@jpeg"}]}]""";
        var p = Post($$$$"""{"$type":"app.bsky.embed.recordWithMedia#view","media":{{{{External}}}},"record":{"record":{{{{ViewRecord(images)}}}}}}""");
        var shown = PostRules.MediaShown(p);
        Assert.False(shown.Quoted);
        Assert.NotNull(shown.Link);
        Assert.Null(shown.Pictures);
    }

    [Fact]
    public void A_quote_without_own_media_shows_the_quoted_posts_media()
    {
        var images = """[{"$type":"app.bsky.embed.images#view","images":[{"thumb":"https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:bob/bafkreia@jpeg"}]}]""";
        var shown = PostRules.MediaShown(Post(Quote(ViewRecord(images))));
        Assert.True(shown.Quoted);
        Assert.Single(shown.Pictures!);
    }

    [Fact]
    public void A_post_with_no_media_and_a_dead_quote_shows_no_media()
    {
        var shown = PostRules.MediaShown(Post(Quote("""{"$type":"app.bsky.embed.record#viewNotFound"}""")));
        Assert.Equal(new PostRules.CardMedia(null, null, null, false), shown);
    }

    [Fact]
    public void A_post_with_no_embed_has_no_quote_link_or_video()
    {
        var p = FeedMapper.ParsePost(JsonDocument.Parse($$$$"""{"uri":"at://{{{{Did}}}}/x/1","cid":"c","author":{"did":"{{{{Did}}}}"},"record":{"text":"hi"}}""").RootElement)!;
        Assert.Null(p.Quote);
        Assert.Null(p.Link);
        Assert.Null(p.Video);
    }
}
