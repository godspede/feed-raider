namespace FeedRaider.Engine.Raider;

/// <summary>The Random mode's wingmen: posts from public Bluesky comedy feeds. It reads its two primaries, <see cref="Feeds.RandomPrimary"/>
/// and <see cref="Feeds.RandomSecond"/>, page by page and alternates their posts. <see cref="Feeds.RandomBackfill"/> serves only
/// when the primaries bring nothing new. A feed that errors, or answers empty pages (<see cref="EmptyLimit"/> in a row, or one with
/// no cursor to follow), is dropped for the run. With all three gone, <paramref name="last"/> (Discover) serves, and its own errors
/// reach the caller. A post is never served twice, and a post that fails <see cref="Plain"/> is never served. The feed URI argument
/// is ignored.</summary>
public sealed class RandomFeedSource(IFeedSource source, IFeedSource? last = null) : IFeedSource
{
    public const int EmptyLimit = 2;

    private sealed class Lane(string uri)
    {
        public string Uri { get; } = uri;
        public string? Cursor;
        public int Empties;
        public bool Dead;
    }

    private readonly Lane[] _primaries = [new(Feeds.RandomPrimary), new(Feeds.RandomSecond)];
    private readonly Lane _backfill = new(Feeds.RandomBackfill);
    private readonly Lane _discover = new(Feeds.Discover);
    private readonly HashSet<string> _served = new(StringComparer.Ordinal);

    /// <summary>True once all the Random feeds have failed and Discover is what plays.</summary>
    public bool OnLastResort => _primaries.All(l => l.Dead) && _backfill.Dead;

    /// <summary>The tone guard: Random skips a post that carries a moderation label (see <see cref="FeedPost.Labelled"/>), on
    /// itself or its author. Such a post is plainly not a joke to play for strangers, whatever the feed it came from.</summary>
    public static bool Plain(FeedPost p) => !p.Labelled;

    public async Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
    {
        var fresh = new List<FeedPost>();
        for (int round = 0; round < 4 && fresh.Count == 0; round++)
        {
            var pages = await Task.WhenAll(_primaries.Select(l => PullAsync(l, limit, ct))).ConfigureAwait(false);
            Take(Alternate(pages), fresh);
            if (fresh.Count == 0) Take(await PullAsync(_backfill, limit, ct).ConfigureAwait(false), fresh);
            if (OnLastResort && fresh.Count == 0)
            {
                if (last is null) break;
                Take(await PullAsync(_discover, limit, ct, last, throwOnError: true).ConfigureAwait(false), fresh);
                break;
            }
        }
        return new FeedPage(fresh, "random");
    }

    private void Take(IEnumerable<FeedPost> posts, List<FeedPost> fresh)
    {
        foreach (var p in posts)
            if (_served.Add(p.Uri)) fresh.Add(p);
    }

    // One post from each page in turn; a page that runs out first leaves the rest to the others.
    private static List<FeedPost> Alternate(List<FeedPost>[] pages)
    {
        var mixed = new List<FeedPost>();
        for (int i = 0; mixed.Count < pages.Sum(p => p.Count); i++)
            foreach (var page in pages)
                if (i < page.Count) mixed.Add(page[i]);
        return mixed;
    }

    private async Task<List<FeedPost>> PullAsync(Lane lane, int limit, CancellationToken ct, IFeedSource? from = null, bool throwOnError = false)
    {
        var posts = new List<FeedPost>();
        if (lane.Dead) return posts;
        try
        {
            var page = await (from ?? source).GetPageAsync(lane.Uri, lane.Cursor, limit, ct).ConfigureAwait(false);
            lane.Cursor = page.Cursor;
            posts.AddRange(page.Posts.Where(Plain));
            if (page.Posts.Count == 0) lane.Empties++; else lane.Empties = 0;
            if (page.Cursor is null || lane.Empties >= EmptyLimit) lane.Dead = !ReferenceEquals(lane, _discover);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) when (!throwOnError)
        {
            lane.Dead = true;
        }
        return posts;
    }
}
