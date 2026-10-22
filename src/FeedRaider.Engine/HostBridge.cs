using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeedRaider.Engine;

/// <summary>A feed page read through a JS call that returns the XRPC body as a JSON string (the browser's signed-in session).</summary>
public sealed class JsFeedSource(Func<string, string?, int, Task<string>> fetch) : IFeedSource
{
    public async Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct) =>
        FeedMapper.ParsePage(await fetch(feedUri, cursor, limit));
}

/// <summary>The like call through a JS function (uri, cid) that answers the new like record as JSON {uri, cid}.</summary>
public sealed class JsActions(Func<string, string, Task<string>> like) : IActions
{
    public async Task<PostRef> LikeAsync(PostRef post, CancellationToken ct) =>
        JsonSerializer.Deserialize(await like(post.Uri, post.Cid), EngineJson.Default.PostRef)
        ?? throw new InvalidOperationException("Bluesky answered the like with nothing");
}

/// <summary>Source-generated JSON for the Engine's own calls, so the like answer is read without reflection when trimmed.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PostRef))]
internal partial class EngineJson : JsonSerializerContext { }

/// <summary>Where the wingmen come from this run.</summary>
public sealed record FeedPlan(IFeedSource Primary, IFeedSource? Fallback, string FeedUri, string Name)
{
    /// <summary>Signed in, the default is the player's Following timeline; <c>?feed=discover</c>, <c>foryou</c> or a generator URI
    /// pick that feed instead. Signed out: the follows of <paramref name="handle"/> if given, else public Discover (or the named
    /// feed). Null when the feed name is unknown.</summary>
    public static FeedPlan? Choose(string? feed, bool signedIn, string handle, IFeedSource timeline, IFeedSource generator,
        IFeedSource publicSource, Func<string, IFeedSource> follows)
    {
        var uri = Feeds.Resolve(feed);
        if (uri is null) return null;
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
        return Has(m, "scope") || Has(m, "insufficient") || Has(m, "not logged in") || Has(m, "sign-in unavailable")
            || Has(m, "401") || Has(m, "403") || Has(m, "unauthorized") || Has(m, "invalidtoken") || Has(m, "authmissing")
            || Has(m, "token") && (Has(m, "expired") || Has(m, "invalid") || Has(m, "revoked"));
    }

    public static string Like(Exception ex)
    {
        if (IsAuthProblem(ex)) return Has(ex.Message, "not logged in") ? "you are signed out" : "your sign-in does not allow likes, sign out and sign in again";
        if (Has(ex.Message, "rate limit") || Has(ex.Message, "ratelimit") || Has(ex.Message, "429")) return "too many likes, wait a moment";
        if (Network(ex.Message)) return "could not reach Bluesky";
        return Short(ex.Message);
    }

    public static string Feed(Exception ex, string name)
    {
        if (IsAuthProblem(ex)) return Has(ex.Message, "not logged in") ? $"you are signed out, so {name} is not available" : $"your sign-in is not allowed to read {name}, sign out and sign in again";
        if (Network(ex.Message)) return $"could not reach Bluesky for {name}";
        return $"could not load {name}: {Short(ex.Message)}";
    }

    private static bool Network(string m) => Has(m, "failed to fetch") || Has(m, "networkerror") || Has(m, "network") || Has(m, "timed out") || Has(m, "timeout");

    private static bool Has(string m, string part) => m.Contains(part, StringComparison.OrdinalIgnoreCase);

    private static string Short(string m)
    {
        m = m.Trim().Replace('\n', ' ');
        return m.Length == 0 ? "unknown error" : m.Length > 60 ? m[..60] + "..." : m;
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
        }
    }
}
