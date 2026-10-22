namespace FeedRaider.Engine.Raider;

/// <summary>The one ordering rule between a feed source and the game: the next post is never by the same author as the one
/// just served, unless it continues that post's thread (its reply root or parent is the previous post or that post's root).
/// A self-thread continuation is taken ahead of everything else so the thread reads in order. When the buffer holds only
/// the previous author's posts they are held back (a repeat is served only on <c>allowRepeat</c>), so the caller can pull
/// more from other authors first. Only eligible posts (see <see cref="WingQueue.Eligible"/>) are buffered.</summary>
public sealed class FeedInterleaver(int capacity = 300)
{
    private readonly List<FeedPost> _buf = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private FeedPost? _last;

    public int Count => _buf.Count;

    public void Add(IEnumerable<FeedPost> posts)
    {
        foreach (var p in posts)
        {
            if (_buf.Count >= capacity) break;
            if (!WingQueue.Eligible(p) || !_seen.Add(p.Uri)) continue;
            _buf.Add(p);
        }
        if (_seen.Count > 4000) { _seen.Clear(); foreach (var p in _buf) _seen.Add(p.Uri); }
    }

    /// <summary>True when <paramref name="next"/> is a reply in the thread of <paramref name="prev"/>.</summary>
    public static bool ContinuesThread(FeedPost prev, FeedPost next) =>
        next.ReplyParent?.Uri == prev.Uri || next.ReplyRoot?.Uri == prev.Uri
        || prev.ReplyRoot is { } r && (next.ReplyRoot?.Uri == r.Uri || next.ReplyParent?.Uri == r.Uri);

    public bool TryTake(out FeedPost post, bool allowRepeat = false)
    {
        int i = Pick(allowRepeat);
        if (i < 0) { post = null!; return false; }
        post = _buf[i];
        _buf.RemoveAt(i);
        _last = post;
        return true;
    }

    private int Pick(bool allowRepeat)
    {
        if (_buf.Count == 0) return -1;
        if (_last is null) return 0;
        // a direct reply first, so a thread reads parent to child; then any other post of the same thread
        for (int i = 0; i < _buf.Count; i++)
            if (_buf[i].AuthorDid == _last.AuthorDid && _buf[i].ReplyParent?.Uri == _last.Uri) return i;
        for (int i = 0; i < _buf.Count; i++)
            if (_buf[i].AuthorDid == _last.AuthorDid && ContinuesThread(_last, _buf[i])) return i;
        for (int i = 0; i < _buf.Count; i++)
            if (_buf[i].AuthorDid != _last.AuthorDid) return i;
        return allowRepeat ? 0 : -1;
    }
}
