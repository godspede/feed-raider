using System.Text.Json;

namespace FeedRaider.Engine;

/// <summary>A strong reference to a record: its at:// URI plus the CID of the version seen.</summary>
public sealed record PostRef(string Uri, string Cid);

/// <summary>A post from a feed. <see cref="ReplyRoot"/> and <see cref="ReplyParent"/> are null for a top-level post.
/// <see cref="AuthorOptOut"/>: the author carries the <c>!no-unauthenticated</c> label (no logged-out viewers).
/// <see cref="Adult"/>: the post or its author carries an adult or graphic label.
/// <see cref="Labelled"/>: the post or its author carries any other label, bar <see cref="FeedMapper.HarmlessLabels"/>.</summary>
public sealed record FeedPost(
    string Uri, string Cid, string AuthorDid, string AuthorHandle, string Text,
    int LikeCount, int RepostCount, int ReplyCount, PostRef? ReplyRoot, PostRef? ReplyParent,
    string? AvatarUrl = null, bool HasEmbed = false, bool AlreadyLiked = false, bool AuthorOptOut = false, bool Adult = false,
    bool Labelled = false)
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

/// <summary>Known feed generators. Every URI was checked with app.bsky.feed.getFeedGenerator.</summary>
public static class Feeds
{
    public const string Discover = "at://did:plc:z72i7hdynmk6r22z27h6tvur/app.bsky.feed.generator/whats-hot";
    public const string ForYou = "at://did:plc:3guzzweuqraryl3rdkimjamk/app.bsky.feed.generator/for-you";

    /// <summary>The name that asks for the Random mix (see <see cref="RandomFeedSource"/>).</summary>
    public const string Random = "random";

    /// <summary>Random's first primary feed, display name "LMAO (Comedy/Humor)": hand-picked comedy posters, almost all jokes, mostly text.
    /// A run reaches its end after about 450 posts.</summary>
    public const string RandomPrimary = "at://did:plc:h34txfcpwsnswikajv7tos6w/app.bsky.feed.generator/aaachysnvmkze";

    /// <summary>Random's second primary feed, display name "Daily Bangers": the best-liked jokes of the last day. It shares some posts
    /// with <see cref="RandomPrimary"/> and adds about as many again.</summary>
    public const string RandomSecond = "at://did:plc:sdkxyw2r7xlx5kjhsolgagv6/app.bsky.feed.generator/aaak3mu6ug2fi";

    /// <summary>Random's backfill feed, a comedy starter-pack feed: the posts of the people in a comedy starter pack. Fewer of its posts
    /// are jokes than the primaries', but it pages on much further, so it serves only when the primaries run short.</summary>
    public const string RandomBackfill = "at://did:plc:xaanmkiwwy3cjscnjgg2b2b3/app.bsky.feed.generator/aaaeki3ljjz7u";

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
            Random => Random,
            "discover" or "following" or "timeline" => Discover,
            "foryou" or "for-you" => ForYou,
            _ => value.StartsWith("at://", StringComparison.Ordinal) && value.Contains("/app.bsky.feed.generator/", StringComparison.Ordinal) ? value : null,
        };
    }
}

public static class FeedMapper
{
    /// <summary>Maps an <c>app.bsky.feed.getFeed</c> response body to a page. Items without uri, cid or author DID are skipped, and so
    /// are adult posts. With <paramref name="signedOut"/> (the page was read without a login) so are authors who opted out of logged-out viewers.</summary>
    public static FeedPage ParsePage(string json, bool skipReposts = false, bool signedOut = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var posts = new List<FeedPost>();
        if (root.TryGetProperty("feed", out var feed) && feed.ValueKind == JsonValueKind.Array)
            foreach (var item in feed.EnumerateArray())
                if (!(skipReposts && IsRepost(item)) && item.TryGetProperty("post", out var p) && ParsePost(p) is { } post && Raider.WingQueue.Allowed(post, signedOut)) posts.Add(post);
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
            Str(author, "avatar"), HasEmbed(p), AlreadyLiked(p),
            HasLabel(author, OptOutLabel), HasLabel(p, AdultLabels) || HasLabel(author, AdultLabels) || HasSelfLabel(p, AdultLabels),
            HasOtherLabel(p) || HasOtherLabel(author) || HasSelfLabel(p, null));
    }

    /// <summary>The author's request not to be shown to logged-out viewers.</summary>
    public const string OptOutLabel = "!no-unauthenticated";

    /// <summary>Labels that keep a post (or a post by that author) out of the game in every mode.</summary>
    public static readonly string[] AdultLabels = ["porn", "sexual", "nudity", "graphic-media"];

    /// <summary>Labels that say nothing about a post's content, so they never count as <see cref="FeedPost.Labelled"/>.</summary>
    public static readonly string[] HarmlessLabels = [OptOutLabel, "bridged-from-bridgy-fed-activitypub"];

    /// <summary>True when <c>e.labels[].val</c> holds one of <paramref name="vals"/>.</summary>
    public static bool HasLabel(JsonElement e, params string[] vals)
    {
        if (!e.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array) return false;
        foreach (var l in labels.EnumerateArray())
            if (l.ValueKind == JsonValueKind.Object && Str(l, "val") is { } v && Array.IndexOf(vals, v) >= 0) return true;
        return false;
    }

    // a label in e.labels[] that is not adult (handled on its own) and not harmless
    private static bool HasOtherLabel(JsonElement e)
    {
        if (!e.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array) return false;
        foreach (var l in labels.EnumerateArray())
            if (l.ValueKind == JsonValueKind.Object && Str(l, "val") is { } v && Other(v)) return true;
        return false;
    }

    private static bool Other(string v) => Array.IndexOf(AdultLabels, v) < 0 && Array.IndexOf(HarmlessLabels, v) < 0;

    // the post's own self-labels (record.labels.values[].val): one of vals, or with vals null any label Other counts
    private static bool HasSelfLabel(JsonElement p, string[]? vals)
    {
        if (!p.TryGetProperty("record", out var rec) || rec.ValueKind != JsonValueKind.Object
            || !rec.TryGetProperty("labels", out var l) || l.ValueKind != JsonValueKind.Object
            || !l.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) return false;
        foreach (var v in values.EnumerateArray())
            if (v.ValueKind == JsonValueKind.Object && Str(v, "val") is { } s && (vals is null ? Other(s) : Array.IndexOf(vals, s) >= 0)) return true;
        return false;
    }

    /// <summary>True when the signed-in viewer already liked the post (<c>viewer.like</c> is the viewer's like record).</summary>
    public static bool AlreadyLiked(JsonElement p) =>
        p.TryGetProperty("viewer", out var v) && v.ValueKind == JsonValueKind.Object && Str(v, "like") is { Length: > 0 };

    /// <summary>True when the post has any embed: images, video, an external link card, a quoted post or a quote with media.</summary>
    public static bool HasEmbed(JsonElement p) =>
        p.TryGetProperty("embed", out var e) && e.ValueKind == JsonValueKind.Object;

    private static PostRef? Ref(JsonElement e, string name) =>
        e.TryGetProperty(name, out var r) && r.ValueKind == JsonValueKind.Object && Str(r, "uri") is { } u && Str(r, "cid") is { } c ? new PostRef(u, c) : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
}
