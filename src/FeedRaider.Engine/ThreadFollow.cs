using System.Text.Json;

namespace FeedRaider.Engine;

/// <summary>Finds the next post of an author's self-thread: the earliest direct reply by the same author to a post, from an
/// <c>app.bsky.feed.getPostThread</c> body requested with depth 1 and no parents.</summary>
public static class ThreadMapper
{
    /// <summary>The reply that follows <paramref name="liked"/> in its thread, or null when the thread ends there: no reply by the
    /// author, an unreadable body, or a first reply that is not fit to be a wingman (any embed, or too short to show).</summary>
    public static FeedPost? Next(string json, FeedPost liked)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
        using (doc)
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
            return best is not null && Raider.WingQueue.Eligible(best) ? best : null;
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
        string url = $"{BskyClient.AppView}/xrpc/app.bsky.feed.getPostThread?uri={Uri.EscapeDataString(post.Uri)}&depth=1&parentHeight=0";
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
        ThreadMapper.Next(await fetch(post.Uri, 1, 0).WaitAsync(ct).ConfigureAwait(false), post);
}

/// <summary>Tries <paramref name="first"/>, and the public source when that throws (a session without the right to read).</summary>
public sealed class FallbackThreadSource(IThreadSource first, IThreadSource second) : IThreadSource
{
    public async Task<FeedPost?> NextAsync(FeedPost post, CancellationToken ct)
    {
        try { return await first.NextAsync(post, ct).ConfigureAwait(false); }
        catch (Exception) when (!ct.IsCancellationRequested) { return await second.NextAsync(post, ct).ConfigureAwait(false); }
    }
}
