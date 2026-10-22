using System.Text.Json;

namespace FeedRaider.Engine;

/// <summary>A strong reference to a record: its at:// URI plus the CID of the version seen.</summary>
public sealed record PostRef(string Uri, string Cid);

/// <summary>A post from a feed. <see cref="ReplyRoot"/> and <see cref="ReplyParent"/> are null for a top-level post.</summary>
public sealed record FeedPost(
    string Uri, string Cid, string AuthorDid, string AuthorHandle, string Text,
    int LikeCount, int RepostCount, int ReplyCount, PostRef? ReplyRoot, PostRef? ReplyParent,
    string? AvatarUrl = null, bool HasMedia = false, bool AlreadyLiked = false)
{
    public PostRef Ref => new(Uri, Cid);
}

public sealed record FeedPage(IReadOnlyList<FeedPost> Posts, string? Cursor);

/// <summary>Signed-in account, as reported by the host.</summary>
public sealed record SessionInfo(string Did, string Handle);

/// <summary>Reads pages of a feed generator's posts (<c>app.bsky.feed.getFeed</c>).</summary>
public interface IFeedSource
{
    Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct);
}

/// <summary>Writes to the signed-in user's repo. Each call is one public record; callers only invoke it on an explicit user action.</summary>
public interface IActions
{
    Task<PostRef> LikeAsync(PostRef post, CancellationToken ct);
}

public interface ISession
{
    /// <summary>The signed-in account, or null when signed out.</summary>
    SessionInfo? Current { get; }
}

/// <summary>Known feed generators. Both URIs were checked with app.bsky.feed.getFeedGenerator.</summary>
public static class Feeds
{
    public const string Discover = "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.generator/whats-hot";
    public const string ForYou = "at://did:plc:3guzzweuqraryl3rdkimjamk/app.bsky.feed.generator/for-you";

    /// <summary>True when the value asks for the signed-in Following timeline: nothing at all, or "following"/"timeline".
    /// Signed out there is no timeline, so the caller falls back to Discover.</summary>
    public static bool WantsTimeline(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().ToLowerInvariant() is "following" or "timeline";

    /// <summary>Resolves a short name ("discover", "foryou") or a full at:// feed generator URI; anything else is null.</summary>
    public static string? Resolve(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return Discover;
        return value.ToLowerInvariant() switch
        {
            "discover" or "following" or "timeline" => Discover,
            "foryou" or "for-you" => ForYou,
            _ => value.StartsWith("at://", StringComparison.Ordinal) && value.Contains("/app.bsky.feed.generator/", StringComparison.Ordinal) ? value : null,
        };
    }
}

public static class FeedMapper
{
    /// <summary>Maps an <c>app.bsky.feed.getFeed</c> response body to a page. Items without uri, cid or author DID are skipped.</summary>
    public static FeedPage ParsePage(string json, bool skipReposts = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var posts = new List<FeedPost>();
        if (root.TryGetProperty("feed", out var feed) && feed.ValueKind == JsonValueKind.Array)
            foreach (var item in feed.EnumerateArray())
                if (!(skipReposts && IsRepost(item)) && item.TryGetProperty("post", out var p) && ParsePost(p) is { } post) posts.Add(post);
        var cursor = root.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        return new FeedPage(posts, cursor);
    }

    /// <summary>True when the feed item is someone's repost (<c>reason: reasonRepost</c>): its post belongs to another author.</summary>
    public static bool IsRepost(JsonElement item) =>
        item.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.Object
        && Str(r, "$type") is { } t && t.EndsWith("reasonRepost", StringComparison.Ordinal);

    public static FeedPost? ParsePost(JsonElement p)
    {
        var uri = Str(p, "uri");
        var cid = Str(p, "cid");
        if (uri is null || cid is null || !p.TryGetProperty("author", out var author)) return null;
        var did = Str(author, "did");
        if (did is null) return null;
        string text = "";
        PostRef? rootRef = null, parentRef = null;
        if (p.TryGetProperty("record", out var rec) && rec.ValueKind == JsonValueKind.Object)
        {
            text = Str(rec, "text") ?? "";
            if (rec.TryGetProperty("reply", out var reply) && reply.ValueKind == JsonValueKind.Object)
            {
                rootRef = Ref(reply, "root");
                parentRef = Ref(reply, "parent");
            }
        }
        return new FeedPost(uri, cid, did, Str(author, "handle") ?? did, text,
            Int(p, "likeCount"), Int(p, "repostCount"), Int(p, "replyCount"), rootRef, parentRef,
            Str(author, "avatar"), HasMedia(p), AlreadyLiked(p));
    }

    /// <summary>True when the signed-in viewer already liked the post (<c>viewer.like</c> is the viewer's like record).</summary>
    public static bool AlreadyLiked(JsonElement p) =>
        p.TryGetProperty("viewer", out var v) && v.ValueKind == JsonValueKind.Object && Str(v, "like") is { Length: > 0 };

    /// <summary>True when the post carries images, video or an external link card. A quote of another post alone is not media.</summary>
    public static bool HasMedia(JsonElement p)
    {
        if (!p.TryGetProperty("embed", out var e) || e.ValueKind != JsonValueKind.Object) return false;
        return IsMediaType(Str(e, "$type")) || e.TryGetProperty("media", out var m) && m.ValueKind == JsonValueKind.Object && IsMediaType(Str(m, "$type"));
    }

    private static bool IsMediaType(string? t) =>
        t is not null && (t.StartsWith("app.bsky.embed.images", StringComparison.Ordinal) || t.StartsWith("app.bsky.embed.video", StringComparison.Ordinal) || t.StartsWith("app.bsky.embed.external", StringComparison.Ordinal));

    private static PostRef? Ref(JsonElement e, string name) =>
        e.TryGetProperty(name, out var r) && r.ValueKind == JsonValueKind.Object && Str(r, "uri") is { } u && Str(r, "cid") is { } c ? new PostRef(u, c) : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
}
