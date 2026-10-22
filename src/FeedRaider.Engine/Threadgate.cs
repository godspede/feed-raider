using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace FeedRaider.Engine;

/// <summary>Remembers each threadgate's last-seen <c>hiddenReplies</c> and reports what a new version adds.
/// Gates are keyed by a hash of (did, rkey), so no account is kept; memory is bounded.</summary>
public sealed class ThreadgateDiffer
{
    public const string GateCollection = "app.bsky.feed.threadgate";
    public const int MaxGates = 20000;
    private readonly Dictionary<ulong, HashSet<string>> _last = [];

    public int Tracked => _last.Count;

    /// <summary>Replies hidden by this version of the gate that the previous version did not hide. A gate never seen
    /// before counts every entry as new. A delete forgets the gate and reports nothing.</summary>
    public List<string> Apply(string did, string rkey, string operation, IReadOnlyCollection<string> hiddenReplies)
    {
        ulong key = Hashing.Fnv64($"{did}/{rkey}");
        var added = new List<string>();
        if (operation == "delete") { _last.Remove(key); return added; }
        if (operation is not ("create" or "update")) return added;

        _last.TryGetValue(key, out var before);
        foreach (var u in hiddenReplies)
            if ((before is null || !before.Contains(u)) && !added.Contains(u)) added.Add(u);
        if (before is null && _last.Count >= MaxGates) _last.Clear();
        _last[key] = new HashSet<string>(hiddenReplies, StringComparer.Ordinal);
        return added;
    }

    /// <summary>Parses one Jetstream message. True for a threadgate create/update/delete commit; <paramref name="hidden"/> is
    /// the record's hiddenReplies (empty for a delete).</summary>
    public static bool TryParse(string json, out string did, out string rkey, out string operation, out List<string> hidden)
    {
        did = rkey = operation = "";
        hidden = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "kind") != "commit" || !root.TryGetProperty("commit", out var c)) return false;
            if (Str(c, "collection") != GateCollection) return false;
            operation = Str(c, "operation") ?? "";
            did = Str(root, "did") ?? "";
            rkey = Str(c, "rkey") ?? "";
            if (did.Length == 0 || rkey.Length == 0 || operation is not ("create" or "update" or "delete")) return false;
            if (c.TryGetProperty("record", out var rec) && rec.ValueKind == JsonValueKind.Object
                && rec.TryGetProperty("hiddenReplies", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: > 0 } u) hidden.Add(u);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Network-wide Jetstream of threadgate commits, on its own connection (no wantedDids). Yields only the URIs of
/// replies a thread owner has newly hidden; nothing about the owner or the reply's author leaves this class.</summary>
public sealed class ThreadgateSource : IHasState
{
    public const string Endpoint = "wss://jetstream2.us-east.bsky.network/subscribe?wantedCollections=app.bsky.feed.threadgate";
    public string State { get; private set; } = "connecting";
    public int GatesSeen { get; private set; }
    /// <summary>While paused the connection is closed; on resume it opens afresh (the paused time is not replayed).</summary>
    public PauseGate? Pause { get; init; }
    private readonly ThreadgateDiffer _differ = new();

    public async IAsyncEnumerable<string> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            if (Pause is not null)
            {
                try { await Pause.WaitWhilePausedAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) { yield break; }
            }
            using var link = CancellationTokenSource.CreateLinkedTokenSource(ct, Pause?.PauseToken ?? default);
            var wsCt = link.Token;
            using var ws = new ClientWebSocket();
            try { await ws.ConnectAsync(new Uri(Endpoint), wsCt).ConfigureAwait(false); }
            catch (Exception) when (Pause?.IsPaused == true && !ct.IsCancellationRequested) { State = "paused"; continue; }
            catch (Exception)
            {
                failures++;
                State = $"reconnecting ({failures})";
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures, 5))), ct); } catch (OperationCanceledException) { yield break; }
                continue;
            }
            failures = 0;
            State = "live";
            var buffer = new byte[16 * 1024];
            while (ws.State == WebSocketState.Open && !wsCt.IsCancellationRequested)
            {
                string? msg = await ReceiveAsync(ws, buffer, wsCt);
                if (msg is null) { State = Pause?.IsPaused == true ? "paused" : "reconnecting"; break; }
                if (!ThreadgateDiffer.TryParse(msg, out var did, out var rkey, out var op, out var hidden)) continue;
                GatesSeen++;
                foreach (var u in _differ.Apply(did, rkey, op, hidden)) yield return u;
            }
        }
    }

    private static async Task<string?> ReceiveAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
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
        catch (Exception) { return null; }
    }
}
