using System.Text.Json;
using System.Text.RegularExpressions;

namespace FeedRaider.Engine;

/// <summary>A strong reference to a record: its at:// URI plus the CID of the version seen.</summary>
public sealed record PostRef(string Uri, string Cid);

/// <summary>What a post carries besides its text, for the one-line note on a card.</summary>
public enum AttachmentKind { None, Picture, Video, Link, Quote, Other }

/// <summary>One picture attached to a post: the address of its thumbnail, and its shape in pixels (0 by 0 when the post does not say).</summary>
public sealed record PostPicture(string Thumb, int Width = 0, int Height = 0);

/// <summary>A link card: the title, the host of its address, a description (empty when none) and the address of its thumbnail (null when none).</summary>
public sealed record LinkCard(string Title, string Host, string Description, string? ThumbUrl);

/// <summary>A video whose thumbnail the game may read: the author's DID, the video's CID, its shape (width over height) when the post says it, and its alt text.</summary>
public sealed record VideoRef(string Did, string Cid, double? AspectRatio, string? Alt);

/// <summary>What a quote shows: the quoted post, or the notice Bluesky gives for one that is gone.</summary>
public enum QuoteState { Shown, Deleted, Blocked, Detached, Hidden, Other }

/// <summary>A quoted post. <see cref="Text"/> and the author are set only when <see cref="State"/> is Shown; <see cref="OtherLabel"/> only for Other
/// (a feed, list, labeler or starter pack, such as "a feed: Name"). <see cref="AuthorOptOut"/>: the quoted author opted out of signed-out viewers.
/// <see cref="Pictures"/>, <see cref="Link"/> and <see cref="Video"/> are the quoted post's own media (one kind at most); <see cref="HasMedia"/> says
/// whether it has any. <see cref="AuthorDid"/> is the quoted author's DID, which a card's ask for the quoted post's pictures names. A quote
/// inside the quoted post is not read: <see cref="QuotesAnother"/> only says there is one.</summary>
public sealed record PostQuote(
    QuoteState State, string? AuthorName, string? AuthorHandle, string Text, bool HasMedia, string? OtherLabel,
    bool AuthorOptOut = false, IReadOnlyList<PostPicture>? Pictures = null, LinkCard? Link = null, VideoRef? Video = null, string? AuthorDid = null,
    bool QuotesAnother = false);

/// <summary>A post from a feed. <see cref="ReplyRoot"/> and <see cref="ReplyParent"/> are null for a top-level post.
/// <see cref="AuthorOptOut"/>: the author carries the <c>!no-unauthenticated</c> label (no signed-out viewers).
/// <see cref="Adult"/>: the post or its author carries an adult or graphic label.
/// <see cref="Labelled"/>: the post or its author carries any other label, bar <see cref="FeedMapper.HarmlessLabels"/>.
/// <see cref="LinkText"/>: the text is only a link, or points at another bsky.app post (a quote without the embed).
/// <see cref="Pictures"/>: the pictures attached to the post itself, at most <see cref="FeedMapper.MaxPictures"/>; null when there are none.
/// <see cref="Link"/>: the post's own link card. <see cref="Video"/>: the post's own video, null when the view gave no thumbnail that names the author and the video.
/// <see cref="Quote"/>: the post it quotes. For a quote with media (<c>recordWithMedia</c>) the own media are in <see cref="Pictures"/>, <see cref="Link"/> or <see cref="Video"/> while <see cref="Attachment"/> stays Quote.</summary>
public sealed record FeedPost(
    string Uri, string Cid, string AuthorDid, string AuthorHandle, string Text,
    int LikeCount, int RepostCount, int ReplyCount, PostRef? ReplyRoot, PostRef? ReplyParent,
    string? AvatarUrl = null, bool HasEmbed = false, bool AlreadyLiked = false, bool AuthorOptOut = false, bool Adult = false,
    bool Labelled = false, bool LinkText = false,
    string? DisplayName = null, bool AlreadyReposted = false, string? LikeUri = null, string? RepostUri = null,
    AttachmentKind Attachment = AttachmentKind.None, string? AttachmentText = null, IReadOnlyList<PostPicture>? Pictures = null,
    LinkCard? Link = null, VideoRef? Video = null, PostQuote? Quote = null)
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

public static partial class FeedMapper
{
    // An embed's $type is one of these lexicon ids followed by #view (or #main in a record); each is matched as a prefix. The
    // record id is itself a prefix of recordWithMedia, so a match on it takes both.
    private const string EmbedImages = "app.bsky.embed.images", EmbedExternal = "app.bsky.embed.external", EmbedVideo = "app.bsky.embed.video";
    private const string EmbedRecord = "app.bsky.embed.record", EmbedRecordWithMedia = "app.bsky.embed.recordWithMedia";

    /// <summary>Maps an <c>app.bsky.feed.getFeed</c> response body to a page. Items without uri, cid or author DID are skipped, and so
    /// are reposts (the post belongs to another author, who was not asked for) and adult posts. With <paramref name="signedOut"/> (the page was read without signing in) so are authors who opted out of signed-out viewers.</summary>
    public static FeedPage ParsePage(string json, bool skipReposts = true, bool signedOut = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var posts = new List<FeedPost>();
        if (root.TryGetProperty("feed", out var feed) && feed.ValueKind == JsonValueKind.Array)
            foreach (var item in feed.EnumerateArray())
                if (!(skipReposts && IsRepost(item)) && item.TryGetProperty("post", out var p) && ParsePost(p) is { } post && Raider.PostRules.Allowed(post, signedOut)) posts.Add(post);
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
        var viewer = ViewerOf(p);
        var (kind, attText) = AttachmentOf(p);
        var media = OwnMediaOf(p, did);
        return new FeedPost(uri, cid, did, Str(author, "handle") ?? did, text,
            Int(p, "likeCount"), Int(p, "repostCount"), Int(p, "replyCount"), rootRef, parentRef,
            Str(author, "avatar"), HasEmbed(p) || HasRecordEmbed(p), viewer.Like is not null,
            HasLabel(author, OptOutLabel), IsAdult(p),
            HasOtherLabel(p) || HasOtherLabel(author) || HasSelfLabel(p, null),
            IsLinkOrQuotedPost(p, text),
            Clean(Str(author, "displayName")), viewer.Repost is not null, viewer.Like, viewer.Repost, kind, attText, kind == AttachmentKind.Picture ? PicturesOf(p) : media.Pictures,
            media.Link, media.Video, QuoteOf(p));
    }

    /// <summary>The most pictures a post may carry.</summary>
    public const int MaxPictures = 4;

    /// <summary>The pictures of the post's own embed view (<c>app.bsky.embed.images#view</c>): each one's thumbnail address and, where
    /// the post gives it, its shape. Null when there is none: a quoted post's pictures, a video's poster and a link card's are not read.</summary>
    public static IReadOnlyList<PostPicture>? PicturesOf(JsonElement p) =>
        p.TryGetProperty("embed", out var e) && e.ValueKind == JsonValueKind.Object ? PicturesOfEmbed(e) : null;

    private static IReadOnlyList<PostPicture>? PicturesOfEmbed(JsonElement e)
    {
        if (Str(e, "$type") is not { } type || !type.StartsWith(EmbedImages, StringComparison.Ordinal)
            || !e.TryGetProperty("images", out var imgs) || imgs.ValueKind != JsonValueKind.Array) return null;
        var list = new List<PostPicture>();
        foreach (var img in imgs.EnumerateArray())
        {
            if (list.Count >= MaxPictures) break;
            if (img.ValueKind != JsonValueKind.Object || Str(img, "thumb") is not { Length: > 0 } thumb) continue;
            int w = 0, h = 0;
            if (img.TryGetProperty("aspectRatio", out var ar) && ar.ValueKind == JsonValueKind.Object)
            {
                w = Int(ar, "width");
                h = Int(ar, "height");
                if (w <= 0 || h <= 0) w = h = 0;
            }
            list.Add(new PostPicture(thumb, w, h));
        }
        return list.Count > 0 ? list : null;
    }

    private readonly record struct Media(IReadOnlyList<PostPicture>? Pictures, LinkCard? Link, VideoRef? Video)
    {
        public bool Any => Pictures is not null || Link is not null || Video is not null;
    }

    // the media view itself: images#view, external#view or video#view; the author's DID checks a video's thumbnail
    private static Media MediaOfEmbed(JsonElement e, string did)
    {
        if (e.ValueKind != JsonValueKind.Object || Str(e, "$type") is not { } type) return default;
        if (type.StartsWith(EmbedImages, StringComparison.Ordinal)) return new Media(PicturesOfEmbed(e), null, null);
        if (type.StartsWith(EmbedExternal, StringComparison.Ordinal)) return new Media(null, LinkOf(e), null);
        if (type.StartsWith(EmbedVideo, StringComparison.Ordinal)) return new Media(null, null, VideoOf(e, did));
        return default;
    }

    // the post's own media: its embed, or for a quote with media (recordWithMedia) the media half of it
    private static Media OwnMediaOf(JsonElement p, string did)
    {
        if (!p.TryGetProperty("embed", out var e) || e.ValueKind != JsonValueKind.Object) return default;
        if (Str(e, "$type") is { } t && t.StartsWith(EmbedRecordWithMedia, StringComparison.Ordinal))
            return e.TryGetProperty("media", out var m) ? MediaOfEmbed(m, did) : default;
        return MediaOfEmbed(e, did);
    }

    private static LinkCard? LinkOf(JsonElement e)
    {
        var ext = e.TryGetProperty("external", out var x) && x.ValueKind == JsonValueKind.Object ? x : e;
        if (Str(ext, "uri") is not { } u || !Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return null;
        string title = Clean(Str(ext, "title")) ?? uri.Host;
        return new LinkCard(title, uri.Host, Clean(Str(ext, "description")) ?? "", Str(ext, "thumb") is { Length: > 0 } t ? t : null);
    }

    /// <summary>The host the video service answers a thumbnail request on, before it redirects to the CDN.</summary>
    public const string VideoServiceHost = "video.bsky.app";

    // null unless the thumbnail is video.bsky.app/watch/<author did>/<embed cid>/thumbnail.jpg: the game builds the address it fetches from the DID and CID, never from the view
    private static VideoRef? VideoOf(JsonElement e, string did)
    {
        if (Str(e, "cid") is not { Length: > 0 } cid || Str(e, "thumbnail") is not { } thumb
            || !Uri.TryCreate(thumb, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, VideoServiceHost, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "watch" || Uri.UnescapeDataString(parts[1]) != did || Uri.UnescapeDataString(parts[2]) != cid) return null;
        double? ratio = null;
        if (e.TryGetProperty("aspectRatio", out var ar) && ar.ValueKind == JsonValueKind.Object && Int(ar, "width") is > 0 and var w && Int(ar, "height") is > 0 and var h)
            ratio = (double)w / h;
        return new VideoRef(did, cid, ratio, Clean(Str(e, "alt")));
    }

    /// <summary>The quote a post carries, from its embed view (<c>record</c> or <c>recordWithMedia</c>); null when the post quotes nothing.</summary>
    public static PostQuote? QuoteOf(JsonElement p)
    {
        if (!p.TryGetProperty("embed", out var e) || e.ValueKind != JsonValueKind.Object || Str(e, "$type") is not { } type
            || !type.StartsWith(EmbedRecord, StringComparison.Ordinal)) return null;
        JsonElement view = default;
        if (type.StartsWith(EmbedRecordWithMedia, StringComparison.Ordinal))
        {
            if (e.TryGetProperty("record", out var outer) && outer.ValueKind == JsonValueKind.Object && outer.TryGetProperty("record", out var inner)) view = inner;
        }
        else if (e.TryGetProperty("record", out var direct)) view = direct;
        if (view.ValueKind != JsonValueKind.Object) return new PostQuote(QuoteState.Other, null, null, "", false, null);
        string vt = Str(view, "$type") ?? "";
        if (vt.EndsWith("#viewRecord", StringComparison.Ordinal)) return ShownQuote(view);
        if (vt.EndsWith("#viewNotFound", StringComparison.Ordinal)) return Dead(QuoteState.Deleted);
        if (vt.EndsWith("#viewBlocked", StringComparison.Ordinal)) return Dead(QuoteState.Blocked);
        if (vt.EndsWith("#viewDetached", StringComparison.Ordinal)) return Dead(QuoteState.Detached);
        return new PostQuote(QuoteState.Other, null, null, "", false, OtherLabelOf(view, vt));

        static PostQuote Dead(QuoteState s) => new(s, null, null, "", false, null);
    }

    private static PostQuote ShownQuote(JsonElement view)
    {
        var author = view.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
        bool hasAuthor = author.ValueKind == JsonValueKind.Object;
        string? did = hasAuthor ? Str(author, "did") : null;
        var value = view.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
        bool adult = hasAuthor && HasLabel(author, AdultLabels) || HasLabel(view, AdultLabels)
            || value.ValueKind == JsonValueKind.Object && RecordHasSelfLabel(value, AdultLabels);
        if (adult) return new PostQuote(QuoteState.Hidden, null, null, "", false, null);
        string text = value.ValueKind == JsonValueKind.Object ? Clean(Str(value, "text")) ?? "" : "";
        var media = default(Media);
        bool quotesAnother = false;
        if (view.TryGetProperty("embeds", out var all) && all.ValueKind == JsonValueKind.Array)
            foreach (var em in all.EnumerateArray())
                quotesAnother |= em.ValueKind == JsonValueKind.Object && Str(em, "$type") is { } et && et.StartsWith(EmbedRecord, StringComparison.Ordinal);
        if (did is not null && view.TryGetProperty("embeds", out var embeds) && embeds.ValueKind == JsonValueKind.Array)
            foreach (var em in embeds.EnumerateArray())
            {
                media = MediaOfEmbed(em, did);
                if (!media.Any && em.ValueKind == JsonValueKind.Object && Str(em, "$type") is { } t
                    && t.StartsWith(EmbedRecordWithMedia, StringComparison.Ordinal) && em.TryGetProperty("media", out var m))
                    media = MediaOfEmbed(m, did);
                if (media.Any) break;
            }
        return new PostQuote(QuoteState.Shown, hasAuthor ? Clean(Str(author, "displayName")) : null, hasAuthor ? Str(author, "handle") ?? did : null, text, media.Any, null,
            hasAuthor && HasLabel(author, OptOutLabel), media.Pictures, media.Link, media.Video, did, quotesAnother);
    }

    // "a feed: Name" and the like, for a record that is not a post
    private static string OtherLabelOf(JsonElement view, string type)
    {
        string kind = type.EndsWith("#generatorView", StringComparison.Ordinal) ? "a feed"
            : type.EndsWith("#listView", StringComparison.Ordinal) ? "a list"
            : type.EndsWith("#labelerView", StringComparison.Ordinal) ? "a labeler"
            : type.Contains("starterPack", StringComparison.Ordinal) ? "a starter pack" : "a record";
        string? name = Clean(Str(view, "displayName") ?? Str(view, "name"))
            ?? (view.TryGetProperty("record", out var r) && r.ValueKind == JsonValueKind.Object ? Clean(Str(r, "name")) : null)
            ?? (view.TryGetProperty("creator", out var c) && c.ValueKind == JsonValueKind.Object ? Clean(Str(c, "displayName")) : null);
        return name is null ? kind : $"{kind}: {name}";
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : CellText.Clean(s) is { Length: > 0 } c ? c : null;

    private readonly record struct Viewer(string? Like, string? Repost);

    private static Viewer ViewerOf(JsonElement p) =>
        p.TryGetProperty("viewer", out var v) && v.ValueKind == JsonValueKind.Object
            ? new Viewer(Str(v, "like") is { Length: > 0 } l ? l : null, Str(v, "repost") is { Length: > 0 } r ? r : null)
            : default;

    /// <summary>What the post carries: from the embed view, else from the stored record's embed. The text is the first alt text of
    /// pictures, the alt text of a video, or "title, host" of a link card; cleaned of line breaks and control characters, or null.</summary>
    public static (AttachmentKind Kind, string? Text) AttachmentOf(JsonElement p)
    {
        JsonElement e;
        if (p.TryGetProperty("embed", out var ev) && ev.ValueKind == JsonValueKind.Object) e = ev;
        else if (p.TryGetProperty("record", out var r) && r.ValueKind == JsonValueKind.Object && r.TryGetProperty("embed", out var er) && er.ValueKind == JsonValueKind.Object) e = er;
        else return (AttachmentKind.None, null);
        string type = Str(e, "$type") ?? "";
        if (type.StartsWith(EmbedRecord, StringComparison.Ordinal)) return (AttachmentKind.Quote, null);
        if (type.StartsWith(EmbedImages, StringComparison.Ordinal))
        {
            string? alt = null;
            if (e.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array && imgs.GetArrayLength() > 0 && imgs[0].ValueKind == JsonValueKind.Object)
                alt = Str(imgs[0], "alt");
            return (AttachmentKind.Picture, Clean(alt));
        }
        if (type.StartsWith(EmbedVideo, StringComparison.Ordinal)) return (AttachmentKind.Video, Clean(Str(e, "alt")));
        if (type.StartsWith(EmbedExternal, StringComparison.Ordinal))
        {
            var ext = e.TryGetProperty("external", out var x) && x.ValueKind == JsonValueKind.Object ? x : e;
            string? title = Clean(Str(ext, "title"));
            string? host = Str(ext, "uri") is { } u && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : null;
            return (AttachmentKind.Link, title is not null && host is not null ? $"{title}, {host}" : title ?? host);
        }
        return (AttachmentKind.Other, null);
    }

    /// <summary>The author's request not to be shown to signed-out viewers.</summary>
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
            if (l.ValueKind == JsonValueKind.Object && Str(l, "val") is { } v && IsUnlistedLabel(v)) return true;
        return false;
    }

    private static bool IsUnlistedLabel(string v) => Array.IndexOf(AdultLabels, v) < 0 && Array.IndexOf(HarmlessLabels, v) < 0;

    /// <summary>True when a post view carries one of <see cref="AdultLabels"/>: on the post, on its author, or as a self-label of its record.
    /// Such a post's words are shown nowhere in the game.</summary>
    public static bool IsAdult(JsonElement p) =>
        HasLabel(p, AdultLabels)
        || (p.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object && HasLabel(author, AdultLabels))
        || HasSelfLabel(p, AdultLabels);

    /// <summary>True when a post record (not a post view) self-labels itself with one of <see cref="AdultLabels"/>.</summary>
    public static bool IsSelfLabelledAdult(JsonElement record) => RecordHasSelfLabel(record, AdultLabels);

    // the post's own self-labels (record.labels.values[].val): one of vals, or with vals null any label Other counts
    private static bool HasSelfLabel(JsonElement p, string[]? vals) =>
        p.TryGetProperty("record", out var rec) && rec.ValueKind == JsonValueKind.Object && RecordHasSelfLabel(rec, vals);

    private static bool RecordHasSelfLabel(JsonElement rec, string[]? vals)
    {
        if (!rec.TryGetProperty("labels", out var l) || l.ValueKind != JsonValueKind.Object
            || !l.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) return false;
        foreach (var v in values.EnumerateArray())
            if (v.ValueKind == JsonValueKind.Object && Str(v, "val") is { } s && (vals is null ? IsUnlistedLabel(s) : Array.IndexOf(vals, s) >= 0)) return true;
        return false;
    }

    /// <summary>True when the post has any embed: images, video, an external link card, a quoted post or a quote with media.</summary>
    public static bool HasEmbed(JsonElement p) =>
        p.TryGetProperty("embed", out var e) && e.ValueKind == JsonValueKind.Object;

    // the stored record's own embed: a feed that sends a bare record, or a view that dropped the embed, must not let a quote or picture through
    private static bool HasRecordEmbed(JsonElement p) =>
        p.TryGetProperty("record", out var r) && r.ValueKind == JsonValueKind.Object && r.TryGetProperty("embed", out var e) && e.ValueKind == JsonValueKind.Object;

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Url();

    [GeneratedRegex(@"bsky\.app/profile/\S+/post/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BskyPostUrl();

    /// <summary>True when the text links to another bsky.app post, or is only a link (nothing is left once links are removed). The facets count as well as the visible text: a link facet can sit under a shortened label.</summary>
    public static bool IsLinkOrQuotedPost(JsonElement p, string text)
    {
        bool facetLink = false;
        if (p.TryGetProperty("record", out var rec) && rec.ValueKind == JsonValueKind.Object && rec.TryGetProperty("facets", out var facets) && facets.ValueKind == JsonValueKind.Array)
            foreach (var f in facets.EnumerateArray())
            {
                if (!f.TryGetProperty("features", out var feats) || feats.ValueKind != JsonValueKind.Array) continue;
                foreach (var ft in feats.EnumerateArray())
                    if (ft.ValueKind == JsonValueKind.Object && Str(ft, "$type") is { } t && t.EndsWith("#link", StringComparison.Ordinal))
                    {
                        facetLink = true;
                        if (Str(ft, "uri") is { } u && BskyPostUrl().IsMatch(u)) return true;
                    }
            }
        if (BskyPostUrl().IsMatch(text)) return true;
        if (!facetLink && !Url().IsMatch(text)) return false;
        return CellText.Clean(Url().Replace(text, " ")).Length == 0;
    }

    private static PostRef? Ref(JsonElement e, string name) =>
        e.TryGetProperty(name, out var r) && r.ValueKind == JsonValueKind.Object && Str(r, "uri") is { } u && Str(r, "cid") is { } c ? new PostRef(u, c) : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
}
