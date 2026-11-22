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
    // the bound on memory: a new gate seen with this many remembered clears the table, so it never holds more
    public const int MaxGates = 20000;
    // Jetstream's words: a commit event's kind, which also names the object holding the commit, and the operation that removes a record
    private const string Commit = "commit", Delete = "delete";
    private readonly Dictionary<ulong, HashSet<string>> _last = [];

    public int Tracked => _last.Count;

    /// <summary>Replies hidden by this version of the gate that the previous version did not hide. A gate never seen
    /// before counts every entry as new. A delete forgets the gate and reports nothing.</summary>
    public List<string> Apply(string did, string rkey, string operation, IReadOnlyCollection<string> hiddenReplies)
    {
        ulong key = Hashing.MixedFnv64($"{did}/{rkey}");
        var added = new List<string>();
        if (operation == Delete) { _last.Remove(key); return added; }

        _last.TryGetValue(key, out var before);
        foreach (var uri in hiddenReplies)
            if ((before is null || !before.Contains(uri)) && !added.Contains(uri)) added.Add(uri);
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
            if (root.ValueKind != JsonValueKind.Object || Str(root, "kind") != Commit || !root.TryGetProperty(Commit, out var commitBody)) return false;
            if (Str(commitBody, "collection") != GateCollection) return false;
            operation = Str(commitBody, "operation");
            did = Str(root, "did");
            rkey = Str(commitBody, "rkey");
            if (did.Length == 0 || rkey.Length == 0 || operation is not ("create" or "update" or Delete)) return false;
            if (commitBody.TryGetProperty("record", out var record) && record.ValueKind == JsonValueKind.Object
                && record.TryGetProperty("hiddenReplies", out var entries) && entries.ValueKind == JsonValueKind.Array)
                foreach (var entry in entries.EnumerateArray())
                    if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } uri) hidden.Add(uri);
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Jetstream's clock at one message: its <c>time_us</c>, in microseconds since the epoch. Null when the message is
    /// not JSON or carries none.</summary>
    public static long? EventTime(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("time_us", out var time)
                && time.ValueKind == JsonValueKind.Number && time.TryGetInt64(out long micros) ? micros : null;
        }
        catch (JsonException) { return null; }
    }

    // a string property's value, or empty when it is missing or not a string
    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}

/// <summary>Network-wide Jetstream of threadgate commits, on its own connection (no wantedDids). Yields only the URIs of
/// replies a thread owner has newly hidden; nothing about the owner or the reply's author leaves this class.</summary>
public sealed class ThreadgateSource
{
    public const string Endpoint = $"{JetstreamSource.DefaultEndpoint}?wantedCollections={ThreadgateDiffer.GateCollection}";
    /// <summary>How far before its first event the run's stream starts. Replies are hidden too seldom for a boss to have one to say
    /// through a fight, so the stream begins with those hidden in this time, which arrive within seconds of the start.</summary>
    public static readonly TimeSpan Replay = TimeSpan.FromMinutes(Raider.Tuning.HiddenReplayMinutes);
    private const string Connecting = "connecting", Paused = "paused", Reconnecting = "reconnecting", Live = "live";
    public string State { get; private set; } = Connecting;
    /// <summary>While paused the connection is closed; on resume it opens afresh at "now" (the paused time is not replayed).</summary>
    public PauseGate? Pause { get; init; }
    private readonly ThreadgateDiffer _differ = new();
    private readonly string _endpoint;

    /// <param name="endpoint">The stream's address with its query, which a cursor is appended to.</param>
    public ThreadgateSource(string endpoint = Endpoint) => _endpoint = endpoint;

    /// <summary>The first event names "now" on Jetstream's own clock and is not reported; the run's stream then opens again
    /// <see cref="Replay"/> before it, once. Every later connection, after a drop or a pause, opens at "now".</summary>
    public async IAsyncEnumerable<string> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        int failures = 0;
        long? replayFrom = null;
        bool seeded = false;
        while (!ct.IsCancellationRequested)
        {
            if (Pause is not null)
            {
                try { await Pause.WaitWhilePausedAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) { yield break; }
            }
            using var link = CancellationTokenSource.CreateLinkedTokenSource(ct, Pause?.PauseToken ?? default);
            var wsCt = link.Token;
            using var ws = new ClientWebSocket();
            string url = replayFrom is { } from ? $"{_endpoint}&cursor={from}" : _endpoint;
            try { await ws.ConnectAsync(new Uri(url), wsCt).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { yield break; }
            // only a pause cancels the connect past here, and a socket failure while paused is moot: the loop waits for the resume
            catch (Exception error) when (error is OperationCanceledException || error is WebSocketException && Pause?.IsPaused == true)
            {
                State = Paused;
                continue;
            }
            catch (WebSocketException error)
            {
                // Jetstream is unreachable or refused the handshake: retry with backoff
                failures++;
                State = $"{Reconnecting} ({failures}: {error.Message})";
                try { await Task.Delay(JetstreamSource.Backoff(failures), ct); } catch (OperationCanceledException) { yield break; }
                continue;
            }
            failures = 0;
            replayFrom = null;
            State = Live;
            var buffer = new byte[JetstreamSource.ReceiveBufferBytes];
            while (ws.State == WebSocketState.Open && !wsCt.IsCancellationRequested)
            {
                var (bytes, failure) = await JetstreamSource.ReceiveMessageAsync(ws, buffer, wsCt);
                if (bytes is null)
                {
                    State = Pause?.IsPaused == true ? Paused : failure is null ? Reconnecting : $"{Reconnecting} ({failure})";
                    break;
                }
                string message = Encoding.UTF8.GetString(bytes);
                if (!seeded)
                {
                    if (ThreadgateDiffer.EventTime(message) is not { } now) continue;
                    seeded = true;
                    replayFrom = now - (long)Replay.TotalMicroseconds;
                    break;
                }
                if (!ThreadgateDiffer.TryParse(message, out var did, out var rkey, out var operation, out var hidden)) continue;
                foreach (var uri in _differ.Apply(did, rkey, operation, hidden)) yield return uri;
            }
        }
    }
}
