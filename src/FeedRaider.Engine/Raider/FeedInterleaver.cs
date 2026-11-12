namespace FeedRaider.Engine.Raider;

/// <summary>The one ordering rule between a feed source and the game: the next post is never by the same author as the one
/// just served. Replies never reach it (see <see cref="WingQueue.Eligible"/>): a thread is followed from its wingman, not read off the feed.
/// When the buffer holds only the previous author's posts they are held back (a repeat is served only on <c>allowRepeat</c>), so the caller can pull
/// more from other authors first. Only eligible posts (see <see cref="WingQueue.Eligible"/>) are buffered.</summary>
public sealed class FeedInterleaver(int capacity = Tuning.InterleaverCapacity, bool signedOut = false)
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
            if (!WingQueue.Eligible(p, signedOut) || !_seen.Add(p.Uri)) continue;
            _buf.Add(p);
        }
        if (_seen.Count > Tuning.SeenPostsMax) { _seen.Clear(); foreach (var p in _buf) _seen.Add(p.Uri); }
    }

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
        for (int i = 0; i < _buf.Count; i++)
            if (_buf[i].AuthorDid != _last.AuthorDid) return i;
        return allowRepeat ? 0 : -1;
    }
}
