namespace FeedRaider.Engine.Raider;

/// <summary>Which feed posts may be shown, in one place so the README's rule and the code cannot drift apart.</summary>
public static class PostRules
{
    /// <summary>The media a card shows; see <see cref="MediaShown"/>.</summary>
    public readonly record struct CardMedia(IReadOnlyList<PostPicture>? Pictures, LinkCard? Link, VideoRef? Video, bool Quoted);

    /// <summary>Whether the post may be shown at all: never an adult or graphic post, and when the viewer is signed out never one by an
    /// author who opted out of signed-out viewers (a signed-in feed is governed by the AppView).</summary>
    public static bool Allowed(FeedPost p, bool signedOut) => !p.Adult && !(signedOut && p.AuthorOptOut);

    /// <summary>Whether the post carries pictures of its own that a card can draw.</summary>
    public static bool HasPictures(FeedPost p) => p.Attachment == AttachmentKind.Picture && p.Pictures is { Count: > 0 };

    /// <summary>The media a card shows, decided as Bluesky's app does: the post's own pictures, link card or video if it has any, else those of the
    /// quoted post. <see cref="CardMedia.Quoted"/> says the media belong to the quote, so the quote box holds them and the post body none.</summary>
    public static CardMedia MediaShown(FeedPost p)
    {
        if (p.Pictures is { Count: > 0 } || p.Link is not null || p.Video is not null) return new CardMedia(p.Pictures, p.Link, p.Video, false);
        if (p.Quote is { State: QuoteState.Shown, HasMedia: true } q) return new CardMedia(q.Pictures, q.Link, q.Video, true);
        return new CardMedia(null, null, null, false);
    }

    /// <summary>The state a quote shows for this viewer: Hidden when its author opted out of signed-out viewers and the viewer is signed out.</summary>
    public static QuoteState QuoteStateFor(PostQuote q, bool signedOut) => q.State == QuoteState.Shown && signedOut && q.AuthorOptOut ? QuoteState.Hidden : q.State;

    /// <summary>Whether the post is a gold card: one with <see cref="Tuning.GoldLikes"/> likes or more, counted as the feed gave them.</summary>
    public static bool Gold(FeedPost p) => p.LikeCount >= Tuning.GoldLikes;

    /// <summary>Whether the post becomes a card: allowed, a top-level post, and with something of its own to show, which is some text
    /// that is not only a link, or pictures (a post of pictures and no words is a card). With <paramref name="textOnly"/> (Random) a
    /// post of words has no attachment and at least <see cref="Tuning.TextOnlyMinChars"/> characters; a post with pictures is taken
    /// as it is, and one with a video, a link card, a quote or anything else attached is not.</summary>
    public static bool Shown(FeedPost p, bool signedOut, bool textOnly)
    {
        if (!Allowed(p, signedOut) || p.ReplyParent is not null || p.ReplyRoot is not null) return false;
        if (p.Attachment == AttachmentKind.Video && p.Video is null) return false;
        bool pictures = HasPictures(p);
        int len = CellText.Clean(p.Text).Length;
        if (len == 0) return pictures;
        if (p.LinkText) return false;
        return !textOnly || pictures || (p.Attachment == AttachmentKind.None && !p.HasEmbed && len >= Tuning.TextOnlyMinChars);
    }

    /// <summary>Whether the author's own next reply may be the card a like brings in. <see cref="Shown"/> refuses every
    /// reply, so this stands in for it: never an adult or graphic post, with text that is more than a link or with pictures, and
    /// with nothing else attached, since the thread note stands on the line that would name a video, link or quote. Whether a
    /// signed-out viewer may see the author is asked by the game, which knows who is playing.</summary>
    public static bool Followable(FeedPost p)
    {
        if (!Allowed(p, false)) return false;
        bool pictures = HasPictures(p);
        if (p.HasEmbed && !pictures) return false;
        return CellText.Clean(p.Text).Length == 0 ? pictures : !p.LinkText;
    }
}

/// <summary>The posts waiting to become cards, each post at most once. The caller filters with <see cref="PostRules.Shown"/>, or
/// with <see cref="PostRules.Followable"/> for a thread follow.</summary>
public sealed class PostQueue
{
    private readonly LinkedList<(FeedPost Post, bool Thread)> _q = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public int Count => _q.Count;

    /// <summary>Adds at the back. False when the post was offered before or the queue is full.</summary>
    public bool Offer(FeedPost p)
    {
        if (_q.Count >= Tuning.PostQueueMax || !Remember(p)) return false;
        _q.AddLast((p, false));
        return true;
    }

    /// <summary>Adds a thread follow at the front, so it is the next one out, whatever the queue holds. False when the post was
    /// offered before.</summary>
    public bool OfferFront(FeedPost p)
    {
        if (!Remember(p)) return false;
        _q.AddFirst((p, true));
        return true;
    }

    /// <summary>Takes the next post out. <paramref name="thread"/> says whether it came in as a thread follow.</summary>
    public bool TryDequeue(out FeedPost post, out bool thread)
    {
        if (_q.First is not { } n) { post = null!; thread = false; return false; }
        (post, thread) = n.Value;
        _q.RemoveFirst();
        if (_q.Count < Tuning.PostQueueLow)
            lock (_belowLowGate)
            {
                _belowLow.TrySetResult();
                _belowLow = NewSignal();
            }
        return true;
    }

    /// <summary>Completes once the queue holds fewer than <see cref="Tuning.PostQueueLow"/> posts: at once when it already does,
    /// else at the take that brings it below. Safe to call from a thread that does not hold the lock the queue is read under.</summary>
    public Task WaitBelowLowAsync(CancellationToken ct)
    {
        lock (_belowLowGate) return _q.Count < Tuning.PostQueueLow ? Task.CompletedTask : _belowLow.Task.WaitAsync(ct);
    }

    private readonly Lock _belowLowGate = new();
    private TaskCompletionSource _belowLow = NewSignal();

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool Remember(FeedPost p)
    {
        if (_seen.Contains(p.Uri)) return false;
        if (_seen.Count > Tuning.SeenPostsMax) _seen.Clear();
        _seen.Add(p.Uri);
        return true;
    }
}
