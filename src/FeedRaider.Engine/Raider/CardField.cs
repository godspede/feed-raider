namespace FeedRaider.Engine.Raider;

/// <summary>The state of one button on a card.</summary>
public enum ButtonState { Off, Marked, Pending, Lit }

/// <summary>What the game knows about the player's like and repost of one post. One object per post, shared by its cards and log entries.</summary>
public sealed class PostMarks
{
    public ButtonState Like { get; set; }
    public ButtonState Repost { get; set; }
    public string? LikeUri { get; set; }
    public string? RepostUri { get; set; }
    /// <summary>Whether this post has already paid its reward (one per post).</summary>
    public bool Paid { get; set; }
    /// <summary>Whether a like of this post has already asked for the author's next reply: a like made again after a
    /// failed write does not ask twice.</summary>
    public bool ThreadAsked { get; set; }
    /// <summary>Whether this post's dead quote has made its mines: once per post, so a card measured again on a resize, or the post
    /// coming round again, never makes more.</summary>
    public bool MinesMade { get; set; }
    /// <summary>Whether the feed already counted the player's own like or repost when the post came in (the sign-in had made it before this run).</summary>
    private readonly bool _likeBase, _repostBase;

    public PostMarks(FeedPost post)
    {
        LikeUri = post.LikeUri;
        RepostUri = post.RepostUri;
        if (_likeBase = post.AlreadyLiked || post.LikeUri is not null) Like = ButtonState.Lit;
        if (_repostBase = post.AlreadyReposted || post.RepostUri is not null) Repost = ButtonState.Lit;
    }

    /// <summary>What to add to the post's own count so it agrees with the button: one for a like or repost Bluesky holds (or is being asked to), and
    /// one fewer than that for a post the feed had already counted. A mark made signed out is not counted.</summary>
    public int Delta(PostAction a) => (Of(a) is ButtonState.Lit or ButtonState.Pending ? 1 : 0) - ((a == PostAction.Like ? _likeBase : _repostBase) ? 1 : 0);

    public ButtonState Of(PostAction a) => a == PostAction.Like ? Like : Repost;
}

/// <summary>Marks by post address, so a card, a log entry and a repeat of the post share one object. Remembers the most recent posts.</summary>
public sealed class PostBook
{
    public const int Max = 2000;
    private readonly Dictionary<string, PostMarks> _marks = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public PostMarks MarksFor(FeedPost post)
    {
        if (_marks.TryGetValue(post.Uri, out var m)) return m;
        m = new PostMarks(post);
        _marks[post.Uri] = m;
        _order.Enqueue(post.Uri);
        while (_order.Count > Max) _marks.Remove(_order.Dequeue());
        return m;
    }

    /// <summary>The marks of a post that has been a card, or null when the book never held it or has let it go.</summary>
    public PostMarks? Find(string postUri) => _marks.GetValueOrDefault(postUri);
}

/// <summary>One post on the field: its marks, its measured shape and the field row of its top edge (negative while it enters).</summary>
public sealed class Card
{
    public FeedPost Post { get; }
    public PostMarks Marks { get; }
    /// <summary>Whether the card is the author's own next reply, brought in by a like of the post before it.</summary>
    public bool Thread { get; }
    public bool Gold => PostRules.Gold(Post);
    public CardShape Shape { get; internal set; }
    public int Top { get; internal set; }

    internal Card(FeedPost post, PostMarks marks, bool thread, CardShape shape, int top)
    {
        Post = post; Marks = marks; Thread = thread; Shape = shape; Top = top;
    }
}

/// <summary>The feed-speed steps: the number of simulation frames per row. A run starts at <see cref="Default"/>, 10 frames a row:
/// the page moves half again as fast as at 15, enough to feel it climb, and a card is still on screen long enough to read.</summary>
public static class FeedSpeed
{
    public const int Min = 1, Max = 5, Default = 4;
    private static readonly int[] Frames = [30, 20, 15, 10, 6];

    public static int Clamp(int step) => Math.Clamp(step, Min, Max);
    public static int FramesPerRow(int step) => Frames[Clamp(step) - 1];
}

/// <summary>The scrolling column of cards. Pure: it takes posts from a <see cref="PostQueue"/> and moves them down a row every
/// <see cref="FramesPerRow"/> steps, which the host calls once per simulation tick.</summary>
public sealed class CardField
{
    /// <summary>Columns left empty at each side of a card.</summary>
    public const int Margin = 2;
    /// <summary>Rows kept clear between a card and the next one.</summary>
    public const int Gap = 2;
    public const int PixelsPerCell = 2;

    private readonly PostQueue _posts;
    private readonly PostBook _book;
    private readonly List<Card> _cards = [];
    private int _speed, _pendingSpeed, _ticks;

    public CardField(PostQueue posts, PostBook book, int fieldCols, int fieldRows, int speed)
    {
        _posts = posts; _book = book;
        FieldCols = fieldCols; FieldRows = fieldRows;
        _speed = _pendingSpeed = FeedSpeed.Clamp(speed);
    }

    public int FieldCols { get; private set; }
    public int FieldRows { get; private set; }
    public IReadOnlyList<Card> Cards => _cards;
    /// <summary>The frames each row takes now: the player's speed, or a running burst's rush where that is faster.</summary>
    public int FramesPerRow => _rush is { } r ? Math.Min(r, FeedSpeed.FramesPerRow(_speed)) : FeedSpeed.FramesPerRow(_speed);

    /// <summary>The player's own speed step, which a rush never changes.</summary>
    public int Speed => _pendingSpeed;
    public int CardCols => Math.Max(1, FieldCols - 2 * Margin);
    /// <summary>The field column of every card's left edge.</summary>
    public int CardLeft => Margin;

    /// <summary>True for a post that must not become a card (the feed shows a post in one place at a time: a rude post held to be spoken is no card). Set by the host.</summary>
    public Func<FeedPost, bool> Skip { get; set; } = _ => false;

    /// <summary>Whether the viewer is signed out, which hides a quote whose author opted out of signed-out viewers. Set by the host;
    /// a card measured before it changes keeps its shape.</summary>
    public bool SignedOut { get; set; } = true;

    /// <summary>Raised once for each card as it is made, when it joins the field above the top.</summary>
    public event Action<Card>? Entered;

    /// <summary>Raised once for each card that has left the bottom, after it is removed.</summary>
    public event Action<Card>? Left;

    /// <summary>Takes effect at the next row step; with no card on the field there is no row to wait for, so it takes effect at once.</summary>
    public void SetSpeed(int step)
    {
        _pendingSpeed = FeedSpeed.Clamp(step);
        if (_cards.Count == 0) _speed = _pendingSpeed;
    }

    private int? _rush;

    /// <summary>Holds the page at <paramref name="framesPerRow"/> frames a row, or at the player's speed if that is faster, until it
    /// is called again; null gives the page back to the player's speed. The speed burst calls it every tick it runs.</summary>
    public void Rush(int? framesPerRow) => _rush = framesPerRow is { } r ? Math.Max(1, r) : null;

    /// <summary>Measures every card again for the new field and keeps its top row.</summary>
    public void Resize(int fieldCols, int fieldRows)
    {
        FieldCols = fieldCols; FieldRows = fieldRows;
        foreach (var c in _cards) c.Shape = CardLayout.Measure(c.Post, CardCols, c.Thread, SignedOut);
    }

    /// <summary>One simulation tick.</summary>
    public void Step()
    {
        if (_ticks >= FramesPerRow)
        {
            _ticks = 0;
            _speed = _pendingSpeed;
            foreach (var c in _cards) c.Top++;
            for (int i = 0; i < _cards.Count;)
            {
                if (_cards[i].Top < FieldRows) { i++; continue; }
                var gone = _cards[i];
                _cards.RemoveAt(i);
                Left?.Invoke(gone);
            }
        }
        _ticks++;
        TryEnter();
    }

    private void TryEnter()
    {
        if (_cards.Count > 0 && _cards[^1].Top < Gap) return;
        FeedPost post;
        bool thread;
        do { if (!_posts.TryDequeue(out post!, out thread)) return; } while (Skip(post));
        var shape = CardLayout.Measure(post, CardCols, thread, SignedOut);
        var card = new Card(post, _book.MarksFor(post), thread, shape, -shape.Rows);
        _cards.Add(card);
        Entered?.Invoke(card);
    }

    /// <summary>The button's box in field pixels.</summary>
    public PixelRect ButtonBox(Card card, PostAction action)
    {
        var button = action == PostAction.Like ? card.Shape.LikeButton : card.Shape.RepostButton;
        return new PixelRect((CardLeft + button.X) * PixelsPerCell, (card.Top + button.Y) * PixelsPerCell, button.W * PixelsPerCell, button.H * PixelsPerCell);
    }

    /// <summary>The button a press at field pixel (<paramref name="x"/>, <paramref name="y"/>) lands on: one whose box, grown by
    /// <paramref name="pad"/> pixels on each side, holds the point and is clear of the HUD's rows. Of two, the one whose centre is
    /// nearer the point.</summary>
    public (Card Card, PostAction Action)? ButtonAt(double x, double y, int pad)
    {
        (Card, PostAction)? best = null;
        double bestD = double.MaxValue;
        foreach (var c in _cards)
        {
            foreach (var a in (ReadOnlySpan<PostAction>)[PostAction.Like, PostAction.Repost])
            {
                var p = ButtonBox(c, a);
                if (p.Y < CardPainter.HudPixelRows) continue;
                if (x < p.X - pad || x >= p.X + p.W + pad || y < p.Y - pad || y >= p.Y + p.H + pad) continue;
                double dx = p.X + p.W / 2.0 - x, dy = p.Y + p.H / 2.0 - y, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = (c, a); }
            }
        }
        return best;
    }

    /// <summary>The card's box in field pixels.</summary>
    public PixelRect CardBox(Card card) =>
        new(CardLeft * PixelsPerCell, card.Top * PixelsPerCell, card.Shape.Cols * PixelsPerCell, card.Shape.Rows * PixelsPerCell);

    /// <summary>The card the like and repost keys act on for a ship centred at field pixel (<paramref name="x"/>, <paramref name="y"/>):
    /// the one it is over, else the nearest whose edge is within <paramref name="reach"/> pixels; null when none is.</summary>
    public Card? CardNear(double x, double y, int reach)
    {
        Card? best = null;
        double bestD = double.MaxValue;
        foreach (var c in _cards)
        {
            var b = CardBox(c);
            double dx = Math.Max(0, Math.Max(b.X - x, x - (b.X + b.W))), dy = Math.Max(0, Math.Max(b.Y - y, y - (b.Y + b.H)));
            if (dx > reach || dy > reach) continue;
            double d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }
}
