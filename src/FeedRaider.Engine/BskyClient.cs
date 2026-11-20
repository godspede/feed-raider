using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace FeedRaider.Engine;

/// <summary>An AppView request answered with an error status. The message is for a developer; <see cref="Status"/> lets the caller pick words for the player.</summary>
public sealed class BskyStatusException(int status, string message) : InvalidOperationException(message)
{
    public int Status { get; } = status;
}

/// <summary>Read-only access to the public AppView. No auth, no writes.</summary>
public static class BskyClient
{
    public const string AppView = "https://public.api.bsky.app";
    public const int DefaultCap = 100;
    private const int MaxErrorBodyChars = 120;   // how much of a failed response's body goes into the error
    private const int PageLimit = 100;   // the most follows getFollows returns in one page

    /// <summary>Fetches the accounts <paramref name="handle"/> follows, paginating up to <paramref name="cap"/>.
    /// getFollows takes a handle or a DID as the actor, so no separate resolve call is needed.</summary>
    public static async Task<List<Follow>> FetchFollowsAsync(HttpClient http, string handle, int cap = DefaultCap, CancellationToken ct = default)
    {
        string actor = handle.Trim().TrimStart('@');
        var follows = new List<Follow>();
        string? cursor = null;
        do
        {
            string url = $"{AppView}/xrpc/app.bsky.graph.getFollows?actor={Uri.EscapeDataString(actor)}&limit={Math.Min(PageLimit, cap - follows.Count)}";
            if (cursor is not null) url += "&cursor=" + Uri.EscapeDataString(cursor);
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new BskyStatusException((int)resp.StatusCode, $"getFollows for '{actor}' failed: {(int)resp.StatusCode} {Truncate(body, MaxErrorBodyChars)}");
            var (page, next) = ParseFollows(body);
            follows.AddRange(page);
            cursor = page.Count == 0 ? null : next;
        } while (cursor is not null && follows.Count < cap);

        return follows.Take(cap).ToList();
    }

    /// <summary>The follows on one getFollows page, and the cursor to the next. A follow that carries
    /// <see cref="FeedMapper.OptOutLabel"/> is left out: follows are only read for signed-out viewers (Play as someone and the
    /// spectator view), and that author asks not to be shown to them.</summary>
    public static (List<Follow> Follows, string? Cursor) ParseFollows(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<Follow>();
        if (doc.RootElement.TryGetProperty("follows", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var f in arr.EnumerateArray())
                if (f.TryGetProperty("did", out var did) && did.GetString() is { Length: > 0 } d && !FeedMapper.HasLabel(f, FeedMapper.OptOutLabel))
                    list.Add(new Follow(d, f.TryGetProperty("handle", out var h) ? h.GetString() ?? d : d));
        string? cursor = doc.RootElement.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        return (list, cursor);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>Jetstream over <see cref="ClientWebSocket"/> (the browser's WebSocket under wasm). Reconnects on drop.</summary>
public sealed class JetstreamSource
{
    public string State { get; private set; } = "connecting";

    public const string DefaultEndpoint = "wss://jetstream2.us-east.bsky.network/subscribe";
    /// <summary>Above this the DIDs are sent as an options_update message instead of in the URL.</summary>
    public const int MaxUrlLength = 8000;
    internal const int ReceiveBufferBytes = 16 * 1024;
    private const int BackoffCapSeconds = 30;
    private const int BackoffMaxDoublings = 5;   // 1 << 5 = 32 s, already above the cap

    /// <summary>The wait before reconnect number <paramref name="failures"/>: 2, 4, 8, 16, then the cap.</summary>
    internal static TimeSpan Backoff(int failures) => TimeSpan.FromSeconds(Math.Min(BackoffCapSeconds, 1 << Math.Min(failures, BackoffMaxDoublings)));

    private readonly IReadOnlyList<string> _dids;
    private readonly string _endpoint;

    public JetstreamSource(IEnumerable<string> dids, string endpoint = DefaultEndpoint)
    {
        _dids = dids.Distinct().ToList();
        _endpoint = endpoint;
    }

    public static string BuildUrl(string endpoint, IEnumerable<string> dids)
    {
        var sb = new StringBuilder(endpoint).Append("?wantedCollections=").Append(JetstreamParser.PostCollection);
        foreach (var d in dids) sb.Append("&wantedDids=").Append(Uri.EscapeDataString(d));
        return sb.ToString();
    }

    /// <summary>Written by hand with Utf8JsonWriter: reflection-based serialisation breaks under wasm trimming.</summary>
    public static string BuildOptionsUpdate(IEnumerable<string> dids)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", "options_update");
            w.WriteStartObject("payload");
            w.WriteStartArray("wantedCollections");
            w.WriteStringValue(JetstreamParser.PostCollection);
            w.WriteEndArray();
            w.WriteStartArray("wantedDids");
            foreach (var d in dids) w.WriteStringValue(d);
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public async IAsyncEnumerable<PostEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        if (_dids.Count == 0) yield break;
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            string url = BuildUrl(_endpoint, _dids);
            bool viaUpdate = url.Length > MaxUrlLength;
            if (viaUpdate) url = BuildUrl(_endpoint, []);

            State = failures == 0 ? "connecting" : $"reconnecting ({failures})";
            var (connected, connectFailure) = await TryConnectAsync(ws, url, viaUpdate ? BuildOptionsUpdate(_dids) : null, ct);
            if (!connected)
            {
                failures++;
                State = connectFailure is null ? $"reconnecting ({failures})" : $"reconnecting ({failures}: {connectFailure})";
                try { await Task.Delay(Backoff(failures), ct); }
                catch (OperationCanceledException) { yield break; }
                continue;
            }

            failures = 0;
            State = "live";
            var buffer = new byte[ReceiveBufferBytes];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var (text, failure) = await ReceiveTextAsync(ws, buffer, ct);
                if (text is null) { State = failure is null ? "reconnecting" : $"reconnecting ({failure})"; break; }
                if (JetstreamParser.TryParse(text, out var post)) yield return post!;
            }
        }
    }

    /// <summary>Connects and sends the first message, if any. Failure says why it failed (null for a cancel), so the caller can
    /// show the cause while it waits to retry.</summary>
    private static async Task<(bool Connected, string? Failure)> TryConnectAsync(ClientWebSocket ws, string url, string? firstMessage, CancellationToken ct)
    {
        try
        {
            await ws.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);
            if (firstMessage is not null)
                await ws.SendAsync(Encoding.UTF8.GetBytes(firstMessage), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            return (false, e.Message);
        }
        catch (OperationCanceledException)
        {
            return (false, null);
        }
    }

    /// <summary>One whole text message, decoded straight from the receive buffer: no byte copy of the message is made, and a
    /// character split across two frames is joined by the decoder. Text is null when the socket closed, was cancelled or failed;
    /// Failure then says why it failed (null for a close or a cancel).</summary>
    internal static async Task<(string? Text, string? Failure)> ReceiveTextAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        try
        {
            var r = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (r.MessageType == WebSocketMessageType.Close) return (null, null);
            if (r.EndOfMessage) return (Encoding.UTF8.GetString(buffer, 0, r.Count), null);   // the usual case: the whole message in one frame
            var decoder = Encoding.UTF8.GetDecoder();
            var text = new System.Text.StringBuilder();
            var chars = new char[buffer.Length];
            while (true)
            {
                int n = decoder.GetChars(buffer, 0, r.Count, chars, 0, flush: r.EndOfMessage);
                text.Append(chars, 0, n);
                if (r.EndOfMessage) return (text.ToString(), null);
                r = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close) return (null, null);
            }
        }
        catch (OperationCanceledException)
        {
            return (null, null);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            return (null, e.Message);
        }
    }

    /// <summary>One whole message. Message is null when the socket closed, was cancelled or failed; Failure then says why it failed
    /// (null for a close or a cancel), so a caller that reconnects can still report the cause.</summary>
    internal static async Task<(byte[]? Message, string? Failure)> ReceiveMessageAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var r = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close) return (null, null);
                ms.Write(buffer, 0, r.Count);
                if (r.EndOfMessage) break;
            }
            return (ms.ToArray(), null);
        }
        catch (OperationCanceledException)
        {
            return (null, null);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            return (null, e.Message);
        }
    }
}
