using System.Text.Json;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class PostFieldsTests
{
    private static FeedPost Parse(string extra = "", string author = "")
    {
        string json = $$"""{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"c1","author":{"did":"did:plc:a","handle":"a.test"{{author}}},"record":{"text":"hello there"}{{extra}}}""";
        using var doc = JsonDocument.Parse(json);
        return FeedMapper.ParsePost(doc.RootElement)!;
    }

    [Fact]
    public void The_display_name_is_read_from_the_author()
        => Assert.Equal("Alice A", Parse(author: ",\"displayName\":\"Alice A\"").DisplayName);

    [Theory]
    [InlineData("")]
    [InlineData(",\"displayName\":\"   \"")]
    public void The_display_name_is_null_when_missing_or_only_spaces(string author)
        => Assert.Null(Parse(author: author).DisplayName);

    [Fact]
    public void A_liked_post_carries_the_like_records_address()
    {
        var p = Parse(",\"viewer\":{\"like\":\"at://did:plc:me/app.bsky.feed.like/9\"}");
        Assert.Equal("at://did:plc:me/app.bsky.feed.like/9", p.LikeUri);
        Assert.True(p.AlreadyLiked);
        Assert.Null(p.RepostUri);
        Assert.False(p.AlreadyReposted);
    }

    [Fact]
    public void A_reposted_post_carries_the_repost_records_address_and_reads_as_already_reposted()
    {
        var p = Parse(",\"viewer\":{\"repost\":\"at://did:plc:me/app.bsky.feed.repost/7\"}");
        Assert.Equal("at://did:plc:me/app.bsky.feed.repost/7", p.RepostUri);
        Assert.True(p.AlreadyReposted);
        Assert.Null(p.LikeUri);
        Assert.False(p.AlreadyLiked);
    }

    [Fact]
    public void A_post_with_pictures_reads_as_Picture_with_the_first_alt_text()
    {
        var p = Parse(",\"embed\":{\"$type\":\"app.bsky.embed.images#view\",\"images\":[{\"alt\":\"a red fox\"},{\"alt\":\"second\"}]}");
        Assert.Equal(AttachmentKind.Picture, p.Attachment);
        Assert.Equal("a red fox", p.AttachmentText);
    }

    [Fact]
    public void A_picture_without_alt_text_has_no_attachment_text()
    {
        var p = Parse(",\"embed\":{\"$type\":\"app.bsky.embed.images#view\",\"images\":[{\"alt\":\"\"}]}");
        Assert.Equal(AttachmentKind.Picture, p.Attachment);
        Assert.True(string.IsNullOrEmpty(p.AttachmentText));
    }

    [Fact]
    public void A_video_reads_as_Video()
        => Assert.Equal(AttachmentKind.Video, Parse(",\"embed\":{\"$type\":\"app.bsky.embed.video#view\",\"cid\":\"x\"}").Attachment);

    [Fact]
    public void A_link_card_reads_as_Link_with_its_title_and_host()
    {
        var p = Parse(",\"embed\":{\"$type\":\"app.bsky.embed.external#view\",\"external\":{\"uri\":\"https://news.example.com/a/b?x=1\",\"title\":\"Big news\"}}");
        Assert.Equal(AttachmentKind.Link, p.Attachment);
        Assert.Equal("Big news, news.example.com", p.AttachmentText);
    }

    [Fact]
    public void A_quote_reads_as_Quote()
        => Assert.Equal(AttachmentKind.Quote, Parse(",\"embed\":{\"$type\":\"app.bsky.embed.record#view\",\"record\":{\"uri\":\"at://x\"}}").Attachment);

    [Fact]
    public void A_quote_with_media_reads_as_Quote()
        => Assert.Equal(AttachmentKind.Quote, Parse(",\"embed\":{\"$type\":\"app.bsky.embed.recordWithMedia#view\",\"media\":{\"$type\":\"app.bsky.embed.images#view\",\"images\":[]}}").Attachment);

    [Fact]
    public void An_embed_of_an_unknown_kind_reads_as_Other()
        => Assert.Equal(AttachmentKind.Other, Parse(",\"embed\":{\"$type\":\"app.bsky.embed.future#view\"}").Attachment);

    [Fact]
    public void A_post_with_no_attachment_reads_as_None()
    {
        var p = Parse();
        Assert.Equal(AttachmentKind.None, p.Attachment);
        Assert.False(p.HasEmbed);
    }

    [Fact]
    public void The_attachment_text_has_no_line_breaks_and_no_control_characters()
    {
        var p = Parse(",\"embed\":{\"$type\":\"app.bsky.embed.images#view\",\"images\":[{\"alt\":\"line one\\nline\\ttwo\\u0007 end\\r\\n\"}]}");
        Assert.Equal("line one line two end", p.AttachmentText);
        Assert.DoesNotContain(p.AttachmentText!, char.IsControl);
    }
}
