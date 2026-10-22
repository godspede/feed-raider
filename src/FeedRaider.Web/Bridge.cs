using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeedRaider.Engine;

// The engine's feed, action and session interfaces over the JS side (wwwroot/atproto.js, reached through main.js).
// Every call crosses as a JSON string. Writes happen only when the page calls Like, which it does on a click.
public static partial class Bridge
{
    [JSImport("authSession", "main.js")]
    private static partial string? AuthSession();

    [JSImport("authGetFeed", "main.js")]
    private static partial Task<string> AuthGetFeed(string feed, string? cursor, int limit);

    [JSImport("authGetTimeline", "main.js")]
    private static partial Task<string> AuthGetTimeline(string? cursor, int limit);

    [JSImport("authLike", "main.js")]
    private static partial Task<string> AuthLike(string uri, string cid);

    private static readonly JsFeedSource Feed = new(AuthGetFeed);
    private static readonly JsFeedSource Timeline = new((_, cursor, limit) => AuthGetTimeline(cursor, limit));
    private static readonly JsActions Actions = new(AuthLike);
    private static readonly JsSession Session = new();
    private static readonly Dictionary<string, FeedPost> Seen = new();

    /// <summary>The signed-in feed, like call and session, for the game host. Same JS-backed objects the page buttons use.</summary>
    internal static IFeedSource FeedSource => Feed;
    /// <summary>The signed-in player's Following timeline (app.bsky.feed.getTimeline); the feed URI is ignored.</summary>
    internal static IFeedSource TimelineSource => Timeline;
    internal static IActions LikeActions => Actions;
    internal static ISession SessionSource => Session;

    /// <summary>One page of the feed as JSON {posts:[...], cursor}. Signed out, JS throws and the page shows the sign-in prompt.</summary>
    [JSExport]
    public static async Task<string> FeedPage(string feed, string? cursor, int limit)
    {
        var uri = Feeds.Resolve(feed) ?? throw new ArgumentException("not a feed generator URI");
        var page = await Feed.GetPageAsync(uri, cursor, limit, default);
        foreach (var p in page.Posts) Seen[p.Uri] = p;
        return JsonSerializer.Serialize(page, WebJson.Default.FeedPage);
    }

    [JSExport]
    public static async Task<string> Like(string postUri)
    {
        var r = await Actions.LikeAsync(Known(postUri).Ref, default);
        return JsonSerializer.Serialize(r, WebJson.Default.PostRef);
    }

    /// <summary>The signed-in account as JSON {did, handle}, or null.</summary>
    [JSExport]
    public static string? CurrentSession() => Session.Current is { } s ? JsonSerializer.Serialize(s, WebJson.Default.SessionInfo) : null;

    private static FeedPost Known(string uri) => Seen.TryGetValue(uri, out var p) ? p : throw new ArgumentException("post not in the loaded feed");

    private sealed class JsSession : ISession
    {
        public SessionInfo? Current => AuthSession() is { } json ? JsonSerializer.Deserialize(json, WebJson.Default.SessionInfo) : null;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FeedPage))]
[JsonSerializable(typeof(PostRef))]
[JsonSerializable(typeof(SessionInfo))]
internal partial class WebJson : JsonSerializerContext { }
