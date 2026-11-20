using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeedRaider.Engine;

/// <summary>A feed page read through a JS call that returns the XRPC body as a JSON string (the browser's signed-in session).</summary>
public sealed class JsFeedSource(Func<string, string?, int, Task<string>> fetch) : IFeedSource
{
    public async Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) =>
        FeedMapper.ParsePage(await fetch(feedUri, cursor, limit));
}

/// <summary>The write calls through JS functions: like and repost take (uri, cid) and answer the new record as JSON {uri, cid};
/// delete takes the record's address. A host that gives no repost or delete function cannot make those writes.</summary>
public sealed class JsActions(Func<string, string, Task<string>> like, Func<string, string, Task<string>>? repost = null, Func<string, Task>? delete = null) : IPostActions
{
    public async Task<PostRef> LikeAsync(PostRef post, CancellationToken ct) =>
        JsonSerializer.Deserialize(await like(post.Uri, post.Cid), EngineJson.Default.PostRef)
        ?? throw new InvalidOperationException("Bluesky answered the like with nothing");

    public async Task<PostRef> RepostAsync(PostRef post, CancellationToken ct) =>
        JsonSerializer.Deserialize(await (repost ?? throw new NotSupportedException("this page cannot repost"))(post.Uri, post.Cid), EngineJson.Default.PostRef)
        ?? throw new InvalidOperationException("Bluesky answered the repost with nothing");

    public Task DeleteAsync(string recordUri, CancellationToken ct) =>
        (delete ?? throw new NotSupportedException("this page cannot delete"))(recordUri);
}

/// <summary>Source-generated JSON for the Engine's own calls, so the like answer is read without reflection when trimmed.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PostRef))]
internal partial class EngineJson : JsonSerializerContext { }

/// <summary>Where the posts come from this run.</summary>
public sealed record FeedPlan(IFeedSource Primary, IFeedSource? Fallback, string FeedUri, string Name)
{
    /// <summary>Signed in, the default is the player's Following timeline; <c>?feed=discover</c>, <c>foryou</c> or a generator URI
    /// pick that feed instead. Signed out: the follows of <paramref name="handle"/> if given, else public Discover (or the named
    /// feed). <c>?feed=random</c> is the Random mix in either case; <paramref name="onTrouble"/> hears why one of its feeds was dropped. Null when the feed name is unknown.</summary>
    public static FeedPlan? Choose(string? feed, bool signedIn, string handle, IFeedSource timeline, IFeedSource generator,
        IFeedSource publicSource, Func<string, IFeedSource> follows, Action<string>? onTrouble = null)
    {
        var uri = Feeds.Resolve(feed);
        if (uri is null) return null;
        if (uri == Feeds.Random) return new FeedPlan(new Raider.RandomFeedSource(publicSource, publicSource, onTrouble), null, Feeds.Random, "public comedy feeds");
        if (signedIn)
            return Feeds.WantsTimeline(feed)
                ? new FeedPlan(timeline, publicSource, Feeds.Discover, "your Following timeline")
                : new FeedPlan(generator, publicSource, uri, "the feed you picked");
        if (handle.Length > 0) return new FeedPlan(follows(handle), publicSource, uri, "the people you follow");
        return new FeedPlan(publicSource, null, uri, "Discover");
    }
}

/// <summary>Turns a raw failure from the browser or Bluesky into a sentence a player can act on.</summary>
public static class Plain
{
    /// <summary>True when the sign-in is the problem (missing scope, expired or absent session): retrying cannot help.</summary>
    public static bool IsAuthProblem(Exception ex)
    {
        var m = ex.Message;
        return Has(m, "scope") || Has(m, "insufficient") || Has(m, "sign in again") || IsSignedOut(m) || Has(m, "sign-in unavailable")
            || Has(m, "401") || Has(m, "403") || Has(m, "unauthorized") || Has(m, "invalidtoken") || Has(m, "authmissing")
            || Has(m, "token") && (Has(m, "expired") || Has(m, "invalid") || Has(m, "revoked"));
    }

    /// <summary>The reason a like, repost or undo failed, in words a player can act on.</summary>
    public static string WriteFailureText(Exception ex, PostAction action, bool undo)
    {
        string noun = action == PostAction.Like ? "likes" : "reposts";
        if (IsAuthProblem(ex)) return IsSignedOut(ex.Message) ? "you are signed out" : $"your sign-in does not allow {(undo ? "undoing " : "")}{noun}; sign in again";
        if (RateLimited(ex.Message)) return $"too many {noun}; wait a moment";
        if (Network(ex.Message)) return "could not reach Bluesky";
        return Short(ex.Message);
    }

    public static string FeedFailureText(Exception ex, string name)
    {
        if (IsAuthProblem(ex)) return IsSignedOut(ex.Message) ? $"you are signed out, so {name} is not available" : $"your sign-in is not allowed to read {name}; sign out and sign in again";
        if (Network(ex.Message)) return $"could not reach Bluesky for {name}";
        return $"could not load {name}";
    }

    /// <summary>The words @atproto/api's agent throws when it holds no session; atproto.js throws the same words, so one match covers both.</summary>
    private const string NotSignedIn = "not logged in";

    private static bool IsSignedOut(string m) => Has(m, NotSignedIn);

    private static bool RateLimited(string m) => Has(m, "rate limit") || Has(m, "ratelimit") || Has(m, "429");

    private static bool Network(string m) => Has(m, "failed to fetch") || Has(m, "network") || Has(m, "timed out") || Has(m, "timeout");

    private static bool Has(string m, string part) => m.Contains(part, StringComparison.OrdinalIgnoreCase);

    private const int ShortLength = 60;

    private static string Short(string m)
    {
        m = m.Trim().Replace('\n', ' ');
        return m.Length == 0 ? "unknown error" : m.Length > ShortLength ? m[..ShortLength] + "..." : m;
    }
}

/// <summary>The browser's key codes (KeyboardEvent.code) as the game's input. One place, so the mapping is tested.</summary>
public static class HostKeys
{
    public static void Apply(Raider.InputState inp, string code, bool down)
    {
        switch (code)
        {
            case "KeyA" or "ArrowLeft": inp.Key(Raider.GameKey.Left, down); break;
            case "KeyD" or "ArrowRight": inp.Key(Raider.GameKey.Right, down); break;
            case "KeyW" or "ArrowUp": inp.Key(Raider.GameKey.Up, down); break;
            case "KeyS" or "ArrowDown": inp.Key(Raider.GameKey.Down, down); break;
            case "Space": inp.Key(Raider.GameKey.Fire, down); break;
            case "KeyF" when down: inp.ToggleAutoFire(); break;
            case "KeyB" when down: inp.Bomb(); break;
            case "KeyE" when down: inp.Like(); break;
            case "KeyR" when down: inp.Repost(); break;
            case "BracketLeft" when down: inp.MarkUp(); break;
            case "BracketRight" when down: inp.MarkDown(); break;
        }
    }
}
