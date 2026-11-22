using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeedRaider.Engine;

// The engine's feed, action and session interfaces over the JS side (wwwroot/atproto.js, reached through main.js).
// Every call crosses as a JSON string. The writes are a like, a repost and the delete of either, each made only when the game
// asks: the player pressed Z or X, or clicked a post's button on its card or in the feed log.
public static partial class Bridge
{
    [JSImport("authSession", "main.js")]
    private static partial string? AuthSession();

    [JSImport("authGetFeed", "main.js")]
    private static partial Task<string> AuthGetFeed(string feed, string? cursor, int limit);

    [JSImport("authGetTimeline", "main.js")]
    private static partial Task<string> AuthGetTimeline(string? cursor, int limit);

    [JSImport("authGetPostThread", "main.js")]
    private static partial Task<string> AuthGetPostThread(string uri, int depth, int parentHeight);

    [JSImport("authLike", "main.js")]
    private static partial Task<string> AuthLike(string uri, string cid);

    [JSImport("authRepost", "main.js")]
    private static partial Task<string> AuthRepost(string uri, string cid);

    [JSImport("authDelete", "main.js")]
    private static partial Task AuthDelete(string recordUri);

    /// <summary>The signed-in feed, shared by the game host and the page's feed calls.</summary>
    internal static readonly JsFeedSource Feed = new(AuthGetFeed);
    /// <summary>The signed-in player's Following timeline (app.bsky.feed.getTimeline); the feed URI is ignored.</summary>
    internal static readonly JsFeedSource Timeline = new((_, cursor, limit) => AuthGetTimeline(cursor, limit));
    internal static readonly JsThreadSource Thread = new(AuthGetPostThread);
    internal static readonly JsActions Actions = new(AuthLike, AuthRepost, AuthDelete);
    internal static readonly JsSession Session = new();
    private static readonly PostWrites Writes = new(Actions);

    /// <summary>One page of the feed as JSON {posts:[...], cursor}. Signed out, JS throws and the page shows the sign-in prompt.
    /// The posts are remembered, so <see cref="Like"/> can find them by URI; the Random mix reads as Discover here.</summary>
    [JSExport]
    public static async Task<string> FeedPage(string feed, string? cursor, int limit)
    {
        var uri = Feeds.Resolve(feed) ?? throw new ArgumentException("not a feed generator URI");
        if (uri == Feeds.Random) uri = Feeds.Discover;
        var page = await Feed.GetPageAsync(uri, cursor, limit, default);
        Writes.Remember(page.Posts);
        return JsonSerializer.Serialize(page, WebJson.Default.FeedPage);
    }

    [JSExport]
    public static async Task<string> Like(string postUri)
    {
        var r = await Writes.LikeAsync(postUri, default);
        return JsonSerializer.Serialize(r, WebJson.Default.PostRef);
    }

    [JSExport]
    public static async Task<string> Repost(string postUri)
    {
        var r = await Writes.RepostAsync(postUri, default);
        return JsonSerializer.Serialize(r, WebJson.Default.PostRef);
    }

    /// <summary>Deletes one of the player's own like or repost records by its address.</summary>
    [JSExport]
    public static Task Delete(string recordUri) => Actions.DeleteAsync(recordUri, default);

    internal sealed class JsSession
    {
        public SessionInfo? Current => AuthSession() is { } json ? JsonSerializer.Deserialize(json, WebJson.Default.SessionInfo) : null;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FeedPage))]
[JsonSerializable(typeof(PostRef))]
[JsonSerializable(typeof(SessionInfo))]
internal partial class WebJson : JsonSerializerContext { }
