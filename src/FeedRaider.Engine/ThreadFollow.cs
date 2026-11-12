using System.Text.Json;

namespace FeedRaider.Engine;

/// <summary>Finds the next post of an author's self-thread: the earliest direct reply by the same author to a post, from an
/// <c>app.bsky.feed.getPostThread</c> body requested with depth 1 and no parents.</summary>
public static class ThreadMapper
{
    /// <summary>How many levels of replies, and of parents above the post, a thread request asks for.</summary>
    public const int ReplyDepth = 1, ParentHeight = 0;

    /// <summary>The reply that follows <paramref name="liked"/> in its thread, or null when the thread ends there: no reply by the
    /// author, or a first reply that is not fit to be a wingman (any embed, or too short to show). A body that is not JSON throws <see cref="JsonException"/>, so that case is told from a thread's
    /// normal end.</summary>
    public static FeedPost? Next(string json, FeedPost liked)
    {
        using (var doc = JsonDocument.Parse(json))
        {
            if (!doc.RootElement.TryGetProperty("thread", out var thread) || thread.ValueKind != JsonValueKind.Object) return null;
            if (!thread.TryGetProperty("replies", out var replies) || replies.ValueKind != JsonValueKind.Array) return null;
            FeedPost? best = null;
            string bestAt = "";
            foreach (var node in replies.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("post", out var p) || p.ValueKind != JsonValueKind.Object) continue;   // blocked or missing replies have no post
                if (FeedMapper.ParsePost(p) is not { } post || post.AuthorDid != liked.AuthorDid || post.ReplyParent?.Uri != liked.Uri) continue;
                string at = CreatedAt(p);
                if (best is null || string.CompareOrdinal(at, bestAt) < 0) { best = post; bestAt = at; }
            }
            return best is not null && Raider.WingQueue.Allowed(best, false) && Raider.WingQueue.Plain(best) ? best : null;
        }
    }

    private static string CreatedAt(JsonElement p)
    {
        if (p.TryGetProperty("record", out var r) && r.ValueKind == JsonValueKind.Object && r.TryGetProperty("createdAt", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        return p.TryGetProperty("indexedAt", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() ?? "" : "";
    }
}

/// <summary>Reads the next post of a thread. The answer is null when the thread ends; a failure throws.</summary>
public interface IThreadSource
{
    Task<FeedPost?> NextAsync(FeedPost post, CancellationToken ct);
}

/// <summary>A thread read from the public AppView, no login. The signed-out source.</summary>
public sealed class PublicThreadSource(HttpClient http) : IThreadSource
{
    public async Task<FeedPost?> NextAsync(FeedPost post, CancellationToken ct)
    {
        string url = $"{BskyClient.AppView}/xrpc/app.bsky.feed.getPostThread?uri={Uri.EscapeDataString(post.Uri)}&depth={ThreadMapper.ReplyDepth}&parentHeight={ThreadMapper.ParentHeight}";
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"getPostThread failed: {(int)resp.StatusCode}");
        return ThreadMapper.Next(body, post);
    }
}

/// <summary>A thread read through a JS call (uri, depth, parentHeight) that answers the XRPC body as a JSON string: the browser's signed-in session.</summary>
public sealed class JsThreadSource(Func<string, int, int, Task<string>> fetch) : IThreadSource
{
    public async Task<FeedPost?> NextAsync(FeedPost post, CancellationToken ct) =>
        ThreadMapper.Next(await fetch(post.Uri, ThreadMapper.ReplyDepth, ThreadMapper.ParentHeight).WaitAsync(ct).ConfigureAwait(false), post);
}

/// <summary>Tries <paramref name="first"/>, and the public source when that throws (a session without the right to read).</summary>
public sealed class FallbackThreadSource(IThreadSource first, IThreadSource second) : IThreadSource
{
    public async Task<FeedPost?> NextAsync(FeedPost post, CancellationToken ct)
    {
        try { return await first.NextAsync(post, ct).ConfigureAwait(false); }
        // The first source's failure is dropped on purpose: the second source answers, or throws its own failure to the caller.
        catch (Exception) when (!ct.IsCancellationRequested) { return await second.NextAsync(post, ct).ConfigureAwait(false); }
    }
}
