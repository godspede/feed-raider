namespace FeedRaider.Engine;

/// <summary>The two things a player can do to a post.</summary>
public enum PostAction { Like, Repost }

/// <summary>What a sign-in may do: create a like, delete one, create a repost, delete one.</summary>
public sealed record Grants(bool Like, bool Unlike, bool Repost, bool Unrepost)
{
    public static readonly Grants All = new(true, true, true, true);
    public static readonly Grants None = new(false, false, false, false);
}

/// <summary>The writes the game may make to the signed-in player's own repo: a like or repost created, a like or repost record deleted.</summary>
public interface IPostActions
{
    /// <summary>Creates a like of the post; answers the new record's uri and cid.</summary>
    Task<PostRef> LikeAsync(PostRef post, CancellationToken ct);

    /// <summary>Creates a repost of the post; answers the new record's uri and cid.</summary>
    Task<PostRef> RepostAsync(PostRef post, CancellationToken ct);

    /// <summary>Deletes one of the player's own like or repost records, by its address.</summary>
    Task DeleteAsync(string recordUri, CancellationToken ct);
}

/// <summary>The writes the page asks for by post address: the posts of the pages it has loaded are remembered, and a like or repost
/// goes out for the one named, with that post's own uri and cid. A post the page has not loaded is refused.</summary>
public sealed class PostWrites(IPostActions actions)
{
    private readonly Dictionary<string, FeedPost> _seen = new();

    /// <summary>Remembers the posts of a loaded page, so a later like or repost can name them by uri.</summary>
    public void Remember(IEnumerable<FeedPost> posts)
    {
        foreach (var p in posts) _seen[p.Uri] = p;
    }

    public Task<PostRef> LikeAsync(string postUri, CancellationToken ct) => actions.LikeAsync(Known(postUri).Ref, ct);

    public Task<PostRef> RepostAsync(string postUri, CancellationToken ct) => actions.RepostAsync(Known(postUri).Ref, ct);

    private FeedPost Known(string uri) => _seen.TryGetValue(uri, out var p) ? p : throw new ArgumentException("post not in the loaded feed");
}
