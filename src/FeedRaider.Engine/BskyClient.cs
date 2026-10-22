using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace FeedRaider.Engine;

/// <summary>Read-only access to the public AppView. No auth, no writes.</summary>
public static class BskyClient
{
    public const string AppView = "https://public.api.bsky.app";
    public const int DefaultCap = 100;

    /// <summary>Fetches the accounts <paramref name="handle"/> follows, paginating up to <paramref name="cap"/>.
    /// getFollows takes a handle or a DID as the actor, so no separate resolve call is needed.</summary>
    public static async Task<List<Follow>> FetchFollowsAsync(HttpClient http, string handle, int cap = DefaultCap, CancellationToken ct = default)
    {
        string actor = handle.Trim().TrimStart('@');
        var follows = new List<Follow>();
        string? cursor = null;
        do
        {
            string url = $"{AppView}/xrpc/app.bsky.graph.getFollows?actor={Uri.EscapeDataString(actor)}&limit={Math.Min(100, cap - follows.Count)}";
            if (cursor is not null) url += "&cursor=" + Uri.EscapeDataString(cursor);
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"getFollows for '{actor}' failed: {(int)resp.StatusCode} {Truncate(body, 120)}");
            var (page, next) = ParseFollows(body);
            follows.AddRange(page);
            cursor = page.Count == 0 ? null : next;
        } while (cursor is not null && follows.Count < cap);

        return follows.Take(cap).ToList();
    }

    public static (List<Follow> Follows, string? Cursor) ParseFollows(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<Follow>();
        if (doc.RootElement.TryGetProperty("follows", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var f in arr.EnumerateArray())
                if (f.TryGetProperty("did", out var did) && did.GetString() is { Length: > 0 } d)
                    list.Add(new Follow(d, f.TryGetProperty("handle", out var h) ? h.GetString() ?? d : d));
        string? cursor = doc.RootElement.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        return (list, cursor);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>Jetstream over <see cref="ClientWebSocket"/> (the browser's WebSocket under wasm). Reconnects on drop.</summary>
public sealed class JetstreamSource : IEventSource, IHasState
{
    public string State { get; private set; } = "connecting";

    public const string DefaultEndpoint = "wss://jetstream2.us-east.bsky.network/subscribe";
    /// <summary>Above this the DIDs are sent as an options_update message instead of in the URL.</summary>
    public const int MaxUrlLength = 8000;

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
            if (!await TryConnectAsync(ws, url, viaUpdate ? BuildOptionsUpdate(_dids) : null, ct))
            {
                failures++;
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5))), ct); }
                catch (OperationCanceledException) { yield break; }
                continue;
            }

            failures = 0;
            State = "live";
            var buffer = new byte[16 * 1024];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                string? msg = await ReceiveTextAsync(ws, buffer, ct);
                if (msg is null) { State = "reconnecting"; break; }
                if (JetstreamParser.TryParse(msg, out var post)) yield return post!;
            }
        }
    }

    private static async Task<bool> TryConnectAsync(ClientWebSocket ws, string url, string? firstMessage, CancellationToken ct)
    {
        try
        {
            await ws.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);
            if (firstMessage is not null)
                await ws.SendAsync(Encoding.UTF8.GetBytes(firstMessage), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>One whole text message, or null if the socket closed or failed.</summary>
    private static async Task<string?> ReceiveTextAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var r = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buffer, 0, r.Count);
                if (r.EndOfMessage) break;
            }
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
