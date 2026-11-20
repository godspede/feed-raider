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

    /// <summary>A queue holding exactly <see cref="Tuning.PostQueueLow"/> posts: one take short of asking for more.</summary>
    private static PostQueue AtLow(PostQueue q)
    {
        for (int n = 1; n <= Tuning.PostQueueLow; n++) q.Offer(P(n: n));
        return q;
    }

    [Fact]
    public void Waiting_for_the_queue_to_run_low_completes_at_once_when_it_already_is()
    {
        var q = new PostQueue();
        Assert.True(q.WaitBelowLowAsync(CancellationToken.None).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Waiting_for_the_queue_to_run_low_completes_at_the_take_that_brings_it_below_and_not_before()
    {
        var q = AtLow(new PostQueue());
        var waiting = q.WaitBelowLowAsync(CancellationToken.None);
        Assert.False(waiting.IsCompleted);
        q.Offer(P(n: Tuning.PostQueueLow + 1));
        Assert.True(q.TryDequeue(out _, out _));   // back to Low: still not below it
        Assert.False(waiting.IsCompleted);
        Assert.True(q.TryDequeue(out _, out _));
        await waiting.WaitAsync(TimeSpan.FromSeconds(WakeSeconds));
    }

    [Fact]
    public async Task Waiting_for_the_queue_to_run_low_ends_when_its_token_is_cancelled()
    {
        var q = AtLow(new PostQueue());
        using var cts = new CancellationTokenSource();
        var waiting = q.WaitBelowLowAsync(cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    private const int WakeSeconds = 10, FullWatchMs = 300;

    private sealed class CountingFeed : IFeedSource
    {
        public int Calls;
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new FeedPage([], null));
        }
    }

    [Fact]
    public async Task The_feed_loop_waiting_on_a_full_queue_asks_for_a_page_once_the_game_takes_it_below_low()
    {
        var game = new RaiderGame(1);
        lock (game) AtLow(game.Posts);
        var feed = new CountingFeed();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, feed, null, "", cts.Token);

        await Task.Delay(FullWatchMs);
        Assert.Equal(0, Volatile.Read(ref feed.Calls));

        lock (game) Assert.True(game.Posts.TryDequeue(out _, out _));
        await Wait.UntilAsync(() => Volatile.Read(ref feed.Calls) > 0, "the feed loop to wake and ask for a page");

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(WakeSeconds));
    }
}
