using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace FeedRaider.Engine;

/// <summary>Where a monster came from: a moderation label, a thread owner hiding a reply, or the made-up demo.</summary>
public enum DarkSource { Label, Hidden, Demo }

/// <summary>What the room gets: a kind, a seed (a hash, not the URI) and anonymous clean text. Nothing about the author.</summary>
public sealed record DarkPost(DarknessKind Kind, ulong Seed, string Text, DarkSource Source = DarkSource.Label, string? Label = null);

public static partial class DarknessFilter
{
    [GeneratedRegex(@"^at://did:[a-z0-9]+:[A-Za-z0-9._:%-]+/app\.bsky\.feed\.post/[A-Za-z0-9._~:-]+$")]
    private static partial Regex PostUri();

    [GeneratedRegex(@"^did:(?:plc:[a-z2-7]{24}|web:[A-Za-z0-9.-]+)$")]
    private static partial Regex AccountDid();

    /// <summary>The kind of monster this label summons, or null: only allowlisted values, never negations, only posts.</summary>
    public static DarknessKind? Accept(LabelEvent l)
    {
        if (l.Neg) return null;
        if (!LabelFrame.Allowed.TryGetValue(l.Val, out var kind)) return null;
        return PostUri().IsMatch(l.Uri) ? kind : null;
    }

    /// <summary>An account-level <c>spam</c> label (the subject is a bare DID) summons a spam monster; no other account label does.</summary>
    public static DarknessKind? AcceptAccount(LabelEvent l) =>
        !l.Neg && l.Val == "spam" && AccountDid().IsMatch(l.Uri) ? DarknessKind.Spam : null;

    /// <summary>True for a post at:// URI (what a hidden reply must be before any fetch).</summary>
    public static bool IsPostUri(string uri) => PostUri().IsMatch(uri);
}

/// <summary>Reads post text from the public AppView, then from the author's PDS. Read-only GETs; the author's
/// profile fields in the responses are never parsed.</summary>
public static class PostTextFetcher
{
    public const string PlcDirectory = "https://plc.directory";

    public static string? ParseGetPosts(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("posts", out var posts) && posts.ValueKind == JsonValueKind.Array)
                foreach (var p in posts.EnumerateArray())
                    if (p.TryGetProperty("record", out var rec) && rec.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        return t.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    public static string? ParseGetRecord(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("value", out var v) && v.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Text of the newest original post in a getAuthorFeed response. Reposts (they carry a <c>reason</c>) are
    /// skipped: that text belongs to someone else.</summary>
    public static string? ParseAuthorFeed(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("feed", out var feed) && feed.ValueKind == JsonValueKind.Array)
                foreach (var item in feed.EnumerateArray())
                    if (!item.TryGetProperty("reason", out _) && item.TryGetProperty("post", out var p)
                        && p.TryGetProperty("record", out var rec) && rec.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        return t.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Text of the first record in a listRecords response (newest first).</summary>
    public static string? ParseListRecords(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("records", out var recs) && recs.ValueKind == JsonValueKind.Array)
                foreach (var r in recs.EnumerateArray())
                    if (r.TryGetProperty("value", out var v) && v.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        return t.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    public static string? ParsePds(string didDocJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(didDocJson);
            if (doc.RootElement.TryGetProperty("service", out var svc) && svc.ValueKind == JsonValueKind.Array)
                foreach (var s in svc.EnumerateArray())
                    if (s.TryGetProperty("type", out var ty) && ty.GetString() == "AtprotoPersonalDataServer"
                        && s.TryGetProperty("serviceEndpoint", out var ep) && ep.GetString() is { } e
                        && Uri.TryCreate(e, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps)
                        return u.GetLeftPart(UriPartial.Authority);
        }
        catch (JsonException) { }
        return null;
    }

    private static async Task<string?> ResolvePdsAsync(HttpClient http, string did, CancellationToken ct)
    {
        string docUrl = did.StartsWith("did:plc:", StringComparison.Ordinal) ? $"{PlcDirectory}/{did}"
            : did.StartsWith("did:web:", StringComparison.Ordinal) ? $"https://{did["did:web:".Length..]}/.well-known/did.json" : "";
        if (docUrl.Length == 0) return null;
        using var r = await http.GetAsync(docUrl, ct).ConfigureAwait(false);
        return r.IsSuccessStatusCode ? ParsePds(await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) : null;
    }

    /// <summary>Raw text of the post at an at:// URI, or of the newest original post when given a bare DID (an account-level
    /// label). Null if neither the AppView nor the PDS will give it.</summary>
    public static async Task<string?> FetchAsync(HttpClient http, string subject, CancellationToken ct)
    {
        try
        {
            return subject.StartsWith("did:", StringComparison.Ordinal)
                ? await FetchLatestAsync(http, subject, ct).ConfigureAwait(false)
                : await FetchPostAsync(http, subject, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<string?> FetchLatestAsync(HttpClient http, string did, CancellationToken ct)
    {
        using (var r = await http.GetAsync($"{BskyClient.AppView}/xrpc/app.bsky.feed.getAuthorFeed?actor={Uri.EscapeDataString(did)}&limit=1&filter=posts_no_replies", ct).ConfigureAwait(false))
            if (r.IsSuccessStatusCode && ParseAuthorFeed(await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) is { } text) return text;

        // suspended or taken-down accounts are gone from the AppView, but their repo may still answer
        string? pds = await ResolvePdsAsync(http, did, ct).ConfigureAwait(false);
        if (pds is null) return null;
        using var l = await http.GetAsync($"{pds}/xrpc/com.atproto.repo.listRecords?repo={Uri.EscapeDataString(did)}&collection=app.bsky.feed.post&limit=1", ct).ConfigureAwait(false);
        return l.IsSuccessStatusCode ? ParseListRecords(await l.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) : null;
    }

    private static async Task<string?> FetchPostAsync(HttpClient http, string atUri, CancellationToken ct)
    {
        using (var r = await http.GetAsync($"{BskyClient.AppView}/xrpc/app.bsky.feed.getPosts?uris={Uri.EscapeDataString(atUri)}", ct).ConfigureAwait(false))
            if (r.IsSuccessStatusCode && ParseGetPosts(await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) is { } text) return text;

        var parts = atUri["at://".Length..].Split('/');
        if (parts.Length != 3) return null;
        string? pds = await ResolvePdsAsync(http, parts[0], ct).ConfigureAwait(false);
        if (pds is null) return null;
        using var g = await http.GetAsync($"{pds}/xrpc/com.atproto.repo.getRecord?repo={Uri.EscapeDataString(parts[0])}&collection={Uri.EscapeDataString(parts[1])}&rkey={Uri.EscapeDataString(parts[2])}", ct).ConfigureAwait(false);
        return g.IsSuccessStatusCode ? ParseGetRecord(await g.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) : null;
    }
}

/// <summary>The moderation labeler's firehose over a WebSocket (binary DAG-CBOR frames).</summary>
public sealed class LabelSource : IHasState
{
    public const string Endpoint = "wss://mod.bsky.app/xrpc/com.atproto.label.subscribeLabels";
    public string State { get; private set; } = "connecting";
    /// <summary>How many sequence numbers to replay before the first live frame, so a monster shows up within a minute.</summary>
    public long SeedBack { get; init; } = 2000;
    public int FramesSeen { get; private set; }
    /// <summary>While paused the connection is closed; on resume it opens afresh at "now" (the paused time is not replayed).</summary>
    public PauseGate? Pause { get; init; }

    public async IAsyncEnumerable<LabelEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        long cursor = -1, last = -1;
        bool seeded = SeedBack <= 0;
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
            string url = cursor >= 0 ? $"{Endpoint}?cursor={cursor}" : Endpoint;
            try { await ws.ConnectAsync(new Uri(url), wsCt).ConfigureAwait(false); }
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
                var frame = await ReceiveAsync(ws, buffer, wsCt);
                if (frame is null) break;
                var labels = LabelFrame.Decode(frame, out long seq);
                FramesSeen++;
                if (seq >= 0) last = seq;
                if (!seeded && seq >= 0)
                {
                    // first live frame tells us where "now" is; reconnect a little behind it to replay recent labels
                    seeded = true;
                    last = cursor = Math.Max(0, seq - SeedBack);
                    State = "seeding";
                    break;
                }
                foreach (var l in labels) yield return l;
            }
            if (Pause?.IsPaused == true)
            {
                State = "paused";
                cursor = last = -1;
                seeded = SeedBack <= 0;
                continue;
            }
            if (seeded && last >= 0 && cursor < last) cursor = last;
        }
    }

    private static async Task<byte[]?> ReceiveAsync(ClientWebSocket ws, byte[] buffer, CancellationToken ct)
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
            return ms.ToArray();
        }
        catch (Exception) { return null; }
    }
}

public sealed class DarknessStats
{
    public int LabelsSeen, LabelsPassed, PostsFetched, Haunted;
    /// <summary>Account-level spam labels that passed (a subset of <see cref="LabelsPassed"/>).</summary>
    public int AccountLabels;
    /// <summary>Replies newly hidden by threadgates, and texts fetched for them.</summary>
    public int HiddenNew, HiddenFetched;
}

/// <summary>Subjects in, anonymous <see cref="DarkPost"/>s out. Fetching is paced (one at a time) and the backlog drops its oldest.</summary>
public static class DarknessFeed
{
    /// <summary>Labels in. Post-level spam/engagement-farming/rude and account-level spam pass; the subject is fetched, then forgotten.</summary>
    public static Task RunAsync(IAsyncEnumerable<LabelEvent> labels, Func<string, CancellationToken, Task<string?>> fetchText,
        Action<DarkPost> onPost, DarknessStats stats, CancellationToken ct, int paceMs = 600, PauseGate? pause = null) =>
        PumpAsync(Subjects(labels, stats, ct), fetchText, DarkSource.Label, onPost, () => stats.PostsFetched++, ct, paceMs, pause);

    /// <summary>Newly hidden reply URIs in; each becomes a rude monster.</summary>
    public static Task RunHiddenAsync(IAsyncEnumerable<string> hidden, Func<string, CancellationToken, Task<string?>> fetchText,
        Action<DarkPost> onPost, DarknessStats stats, CancellationToken ct, int paceMs = 600, PauseGate? pause = null) =>
        PumpAsync(HiddenSubjects(hidden, stats, ct), fetchText, DarkSource.Hidden, onPost, () => stats.HiddenFetched++, ct, paceMs, pause);

    private static async IAsyncEnumerable<(string Subject, DarknessKind Kind, string? Label)> Subjects(IAsyncEnumerable<LabelEvent> labels, DarknessStats stats,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var l in labels.WithCancellation(ct).ConfigureAwait(false))
        {
            stats.LabelsSeen++;
            if (DarknessFilter.Accept(l) is null && DarknessFilter.AcceptAccount(l) is null) continue;
            if (!seen.Add(l.Uri)) continue;
            if (seen.Count > 5000) seen.Clear();
            if (DarknessFilter.Accept(l) is { } kind) { stats.LabelsPassed++; yield return (l.Uri, kind, l.Val); }
            else if (DarknessFilter.AcceptAccount(l) is { } k) { stats.LabelsPassed++; stats.AccountLabels++; yield return (l.Uri, k, l.Val); }
        }
    }

    private static async IAsyncEnumerable<(string Subject, DarknessKind Kind, string? Label)> HiddenSubjects(IAsyncEnumerable<string> hidden, DarknessStats stats,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var uri in hidden.WithCancellation(ct).ConfigureAwait(false))
        {
            if (!DarknessFilter.IsPostUri(uri) || !seen.Add(uri)) continue;
            if (seen.Count > 5000) seen.Clear();
            stats.HiddenNew++;
            yield return (uri, DarknessKind.Rude, null);
        }
    }

    private static async Task PumpAsync(IAsyncEnumerable<(string Subject, DarknessKind Kind, string? Label)> items, Func<string, CancellationToken, Task<string?>> fetchText,
        DarkSource source, Action<DarkPost> onPost, Action fetched, CancellationToken ct, int paceMs, PauseGate? pause = null)
    {
        var queue = Channel.CreateBounded<(string Subject, DarknessKind Kind, string? Label)>(new BoundedChannelOptions(24) { FullMode = BoundedChannelFullMode.DropOldest });
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var reader = Task.Run(async () =>
        {
            try
            {
                await foreach (var (subject, kind, label) in items.WithCancellation(ct).ConfigureAwait(false))
                {
                    if (!seen.Add(subject)) continue;
                    if (seen.Count > 5000) seen.Clear();
                    queue.Writer.TryWrite((subject, kind, label));
                }
            }
            catch (OperationCanceledException) { }
            finally { queue.Writer.TryComplete(); }
        }, ct);

        try
        {
            await foreach (var (subject, kind, label) in queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (pause is not null) await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);
                string clean = DarknessText.Clean(await fetchText(subject, ct).ConfigureAwait(false));
                if (clean.Length > 0)
                {
                    fetched();
                    onPost(new DarkPost(kind, Hashing.Fnv64(subject), clean, source, label));
                }
                await Task.Delay(paceMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        try { await reader.ConfigureAwait(false); } catch (OperationCanceledException) { }
    }
}
