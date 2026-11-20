using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class PostRulesTests
{
    private const string Long = "a plain joke about a very confused pigeon";

    private static FeedPost P(string text = Long, int n = 1, PostRef? parent = null, bool adult = false, bool optOut = false,
        bool linkText = false, AttachmentKind attachment = AttachmentKind.None) =>
        new($"at://did:plc:a/app.bsky.feed.post/{n}", "c", "did:plc:a", "a.test", text, 1, 0, 0, null, parent,
            HasEmbed: attachment != AttachmentKind.None, Adult: adult, AuthorOptOut: optOut, LinkText: linkText,
            Attachment: attachment);

    [Fact]
    public void A_text_post_is_shown() => Assert.True(PostRules.Shown(P(), false, false));

    [Fact]
    public void A_post_with_no_text_is_not_shown()
    {
        Assert.False(PostRules.Shown(P(""), false, false));
        Assert.False(PostRules.Shown(P("  \n\t "), false, false));
    }

    [Fact]
    public void A_post_whose_text_is_only_a_link_is_not_shown()
        => Assert.False(PostRules.Shown(P("https://example.com/x", linkText: true), false, false));

    [Fact]
    public void A_reply_is_not_shown()
        => Assert.False(PostRules.Shown(P(parent: new PostRef("at://x/p/0", "c0")), false, false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_adult_labelled_post_is_not_shown_in_any_mode(bool signedOut)
    {
        Assert.False(PostRules.Shown(P(adult: true), signedOut, false));
        Assert.False(PostRules.Shown(P(adult: true), signedOut, true));
        Assert.False(PostRules.Allowed(P(adult: true), signedOut));
    }

    [Fact]
    public void An_opted_out_authors_post_is_not_shown_signed_out_and_is_shown_signed_in()
    {
        Assert.False(PostRules.Shown(P(optOut: true), true, false));
        Assert.True(PostRules.Shown(P(optOut: true), false, false));
    }

    [Fact]
    public void With_textOnly_off_a_one_character_post_and_a_post_with_a_picture_are_shown()
    {
        Assert.True(PostRules.Shown(P("k"), false, false));
        Assert.True(PostRules.Shown(P(attachment: AttachmentKind.Picture), false, false));
    }

    [Theory]
    [InlineData(AttachmentKind.Picture)]
    [InlineData(AttachmentKind.Video)]
    [InlineData(AttachmentKind.Link)]
    [InlineData(AttachmentKind.Quote)]
    [InlineData(AttachmentKind.Other)]
    public void With_textOnly_on_a_post_with_any_attachment_is_not_shown(AttachmentKind kind)
        => Assert.False(PostRules.Shown(P(attachment: kind), false, true));

    [Fact]
    public void With_textOnly_on_a_post_under_20_characters_is_not_shown()
    {
        Assert.False(PostRules.Shown(P("nineteen chars long"), false, true));
        Assert.True(PostRules.Shown(P("twenty chars long!!!"), false, true));
    }

    [Fact]
    public void The_queue_hands_posts_back_in_order_and_one_offered_at_the_front_comes_out_first()
    {
        var q = new PostQueue();
        Assert.True(q.Offer(P(n: 1)));
        Assert.True(q.Offer(P(n: 2)));
        Assert.True(q.OfferFront(P(n: 3)));
        Assert.Equal(3, q.Count);
        Assert.True(q.TryDequeue(out var a, out _));
        Assert.True(q.TryDequeue(out var b, out _));
        Assert.True(q.TryDequeue(out var c, out _));
        Assert.False(q.TryDequeue(out _, out _));
        Assert.EndsWith("/3", a.Uri);
        Assert.EndsWith("/1", b.Uri);
        Assert.EndsWith("/2", c.Uri);
    }
}
