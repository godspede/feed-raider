using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FeedRaider.Engine;

/// <summary>Where an enemy came from: a moderation label, a thread owner hiding a reply, or the made-up demo.</summary>
public enum DarknessSource { Label, Hidden, Demo }

/// <summary>What the room gets: a kind, a seed (a hash, not the URI) and anonymous clean text. Nothing about the author.</summary>
public sealed record DarknessPost(DarknessKind Kind, ulong Seed, string Text, DarknessSource Source = DarknessSource.Label, string? Label = null);

public static partial class DarknessFilter
{
    [GeneratedRegex(@"^at://did:(?:plc:[a-z2-7]{24}|web:[A-Za-z0-9.-]+)/app\.bsky\.feed\.post/[A-Za-z0-9._~:-]+\z")]
    private static partial Regex PostUri();

    [GeneratedRegex(@"^did:(?:plc:[a-z2-7]{24}|web:[A-Za-z0-9.-]+)\z")]
    private static partial Regex AccountDid();

    /// <summary>The kind of enemy this label summons, or null: only allowlisted values, never negations, only posts.</summary>
    public static DarknessKind? Accept(LabelEvent label)
    {
        if (label.Neg) return null;
        if (!LabelFrame.Allowed.TryGetValue(label.Val, out var kind)) return null;
        return PostUri().IsMatch(label.Uri) ? kind : null;
    }

    /// <summary>An account-level <c>spam</c> label (the subject is a bare DID) summons a spam enemy; no other account label does.</summary>
    public static DarknessKind? AcceptAccount(LabelEvent label) =>
        !label.Neg && label.Val == "spam" && AccountDid().IsMatch(label.Uri) ? DarknessKind.Spam : null;

    /// <summary>True for a post at:// URI (what a hidden reply must be before any fetch).</summary>
    public static bool IsPostUri(string uri) => PostUri().IsMatch(uri);
}

/// <summary>Reads post text from the public AppView alone, with read-only GETs: a post the AppView does not return has no text.
/// Of the author, only the labels in the AppView's answer are read, so that a signed-out player is never shown an author who asks
/// not to be shown to signed-out viewers.</summary>
public static class PostTextFetcher
{
    /// <summary>Text of the post in a getPosts response. Empty when the post is adult (<see cref="FeedMapper.IsAdult"/>), or when
    /// <paramref name="signedOut"/> and the author carries <see cref="FeedMapper.OptOutLabel"/> or names no author, or when
    /// <paramref name="languages"/> is given and the post is not in one of them: the post is found, and nothing of it is shown.
    /// A body that is not JSON throws <see cref="JsonException"/>, so the fetch counts as failed.</summary>
    public static string? ParseGetPosts(string json, bool signedOut, PlayerLanguages? languages = null)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("posts", out var posts) && posts.ValueKind == JsonValueKind.Array)
            foreach (var post in posts.EnumerateArray())
                if (TextOf(post, signedOut, languages) is { } text) return text;
        return null;
    }

    /// <summary>Text of the newest original post in a getAuthorFeed response. Reposts (they carry a <c>reason</c>) are
    /// skipped: that text belongs to someone else. Empty, as in <see cref="ParseGetPosts"/>, when that post is adult or when
    /// <paramref name="signedOut"/> and the author opts out, or when it is not in one of <paramref name="languages"/> (if given); an older post is not read in its place.
    /// A body that is not JSON throws <see cref="JsonException"/>, as in <see cref="ParseGetPosts"/>.</summary>
    public static string? ParseAuthorFeed(string json, bool signedOut, PlayerLanguages? languages = null)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("feed", out var feed) && feed.ValueKind == JsonValueKind.Array)
            foreach (var item in feed.EnumerateArray())
                if (!item.TryGetProperty("reason", out _) && item.TryGetProperty("post", out var post) && TextOf(post, signedOut, languages) is { } text)
                    return text;
        return null;
    }

    // an author missing from the view cannot be checked, and a label that cannot be checked counts as present
    private static bool AuthorOptsOut(JsonElement post) =>
        !post.TryGetProperty("author", out var author) || author.ValueKind != JsonValueKind.Object || FeedMapper.HasLabel(author, FeedMapper.OptOutLabel);

    // a post view's record text, or null when it carries none; empty when the post is not to be shown
    private static string? TextOf(JsonElement post, bool signedOut, PlayerLanguages? languages) =>
        post.TryGetProperty("record", out var record) && record.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { } said
            ? (FeedMapper.IsAdult(post) || (signedOut && AuthorOptsOut(post)) || (languages is not null && !languages.Reads(LangsOf(record), said)) ? "" : said)
            : null;

    // the record's optional BCP-47 tags; null when it has none
    private static List<string>? LangsOf(JsonElement record) =>
        record.TryGetProperty("langs", out var langs) && langs.ValueKind == JsonValueKind.Array
            ? langs.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()!).ToList()
            : null;

    /// <summary>Raw text of the post at an at:// URI, or of the newest original post when given a bare DID (an account-level
    /// label), asking the AppView alone. Null when the AppView's answer is not a success or holds no such post, so an account
    /// that is deactivated, suspended or taken down gives nothing. A network failure throws. With <paramref name="signedOut"/>,
    /// an author who carries <see cref="FeedMapper.OptOutLabel"/>, or whose label cannot be checked, gives nothing to show; nor does
    /// a post that is not in one of the <paramref name="languages"/> (every language when null).</summary>
    public static Task<string?> FetchAsync(HttpClient http, string subject, bool signedOut, CancellationToken ct, PlayerLanguages? languages = null) =>
        subject.StartsWith("did:", StringComparison.Ordinal) ? FetchLatestAsync(http, subject, signedOut, languages, ct) : FetchPostAsync(http, subject, signedOut, languages, ct);

    private static async Task<string?> FetchLatestAsync(HttpClient http, string did, bool signedOut, PlayerLanguages? languages, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{BskyClient.AppView}/xrpc/app.bsky.feed.getAuthorFeed?actor={Uri.EscapeDataString(did)}&limit=1&filter=posts_no_replies", ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? ParseAuthorFeed(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), signedOut, languages) : null;
    }

    private static async Task<string?> FetchPostAsync(HttpClient http, string atUri, bool signedOut, PlayerLanguages? languages, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{BskyClient.AppView}/xrpc/app.bsky.feed.getPosts?uris={Uri.EscapeDataString(atUri)}", ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? ParseGetPosts(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), signedOut, languages) : null;
    }
}

/// <summary>The moderation labeler's firehose over a WebSocket (binary DAG-CBOR frames).</summary>
public sealed class LabelSource
{
    public const string Endpoint = "wss://mod.bsky.app/xrpc/com.atproto.label.subscribeLabels";
    /// <summary>How many sequence numbers to replay before the first live frame, so an enemy shows up within a minute.</summary>
    private const long SeedBack = 2000;
    /// <summary>The cursor that opens at the bare <see cref="Endpoint"/>, at "now".</summary>
    internal const long AtNow = -1;
    /// <summary>While paused the connection is closed; on resume it opens afresh at "now" (the paused time is not replayed).</summary>
    public PauseGate? Pause { get; init; }

    public async IAsyncEnumerable<LabelEvent> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        long cursor = AtNow, last = AtNow;
        bool seeded = false, paused = false;
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            if (Pause is not null)
            {
                var beforeWait = Pause.PauseToken;
                try { await Pause.WaitWhilePausedAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) { yield break; }
                paused |= beforeWait.IsCancellationRequested;   // a pause that began with no connection open, as in the wait after a failed connect
            }
            cursor = NextConnection(cursor, last, paused);
            last = cursor;   // so a socket that drops before its first frame opens where it did, not back in the paused time
            paused = false;
            var pauseToken = Pause?.PauseToken ?? default;
            using var link = CancellationTokenSource.CreateLinkedTokenSource(ct, pauseToken);
            var wsCt = link.Token;
            using var ws = new ClientWebSocket();
            string url = cursor >= 0 ? $"{Endpoint}?cursor={cursor}" : Endpoint;
            try { await ws.ConnectAsync(new Uri(url), wsCt).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { yield break; }
            // only a pause cancels the connect past here, and a socket failure while paused is moot: the loop waits for the resume
            catch (Exception error) when (error is OperationCanceledException || error is WebSocketException && Pause?.IsPaused == true)
            {
                paused = true;
                continue;
            }
            catch (WebSocketException)
            {
                // the labeler is unreachable or refused the handshake: retry with backoff
                failures++;
                try { await Task.Delay(JetstreamSource.Backoff(failures), ct); } catch (OperationCanceledException) { yield break; }
                continue;
            }
            failures = 0;
            var buffer = new byte[JetstreamSource.ReceiveBufferBytes];
            while (ws.State == WebSocketState.Open && !wsCt.IsCancellationRequested)
            {
                var (frame, _) = await JetstreamSource.ReceiveMessageAsync(ws, buffer, wsCt);
                if (frame is null) break;
                var labels = LabelFrame.Decode(frame, out long seq);
                if (seq >= 0) last = seq;
                if (!seeded && seq >= 0)
                {
                    // first live frame tells us where "now" is; reconnect a little behind it to replay recent labels
                    seeded = true;
                    last = cursor = Math.Max(0, seq - SeedBack);
                    break;
                }
                foreach (var label in labels) yield return label;
            }
            paused = pauseToken.IsCancellationRequested;
        }
    }

    /// <summary>
    /// Where the next connection opens: a sequence number, or <see cref="AtNow"/>. A dropped socket carries on from the last
    /// sequence number seen, which is the seed cursor right after seeding. A pause opens at "now", so the paused time is not
    /// replayed; the run stays seeded, so it replays at most once however often it pauses.
    /// </summary>
    internal static long NextConnection(long cursor, long last, bool paused) => paused ? AtNow : Math.Max(cursor, last);

}

public sealed class DarknessStats
{
    public int LabelsSeen, LabelsPassed, PostsFetched, Haunted;
    /// <summary>Text fetches that failed on the network; the subject is dropped.</summary>
    public int FetchFailures;
    /// <summary>Account-level spam labels that passed (a subset of <see cref="LabelsPassed"/>).</summary>
    public int AccountLabels;
    /// <summary>Replies newly hidden by threadgates, and texts fetched for them.</summary>
    public int HiddenNew, HiddenFetched;
}

/// <summary>Subjects in, anonymous <see cref="DarknessPost"/>s out. Fetching is paced (one at a time) and the backlog drops its oldest.</summary>
public static class DarknessFeed
{
    private const int SeenCap = 5000;
    /// <summary>The wait between two text fetches, in milliseconds.</summary>
    public const int DefaultPaceMs = 600;

    /// <summary>Labels in. Post-level spam/engagement-farming/rude and account-level spam pass; the subject is fetched, then forgotten.</summary>
    public static Task RunAsync(IAsyncEnumerable<LabelEvent> labels, Func<string, CancellationToken, Task<string?>> fetchText,
        Action<DarknessPost> onPost, DarknessStats stats, CancellationToken ct, int paceMs = DefaultPaceMs, PauseGate? pause = null) =>
        PumpAsync(Subjects(labels, stats, ct), fetchText, DarknessSource.Label, onPost, () => stats.PostsFetched++, () => stats.FetchFailures++, ct, paceMs, pause);

    /// <summary>Newly hidden reply URIs in; each becomes a rude enemy.</summary>
    public static Task RunHiddenAsync(IAsyncEnumerable<string> hidden, Func<string, CancellationToken, Task<string?>> fetchText,
        Action<DarknessPost> onPost, DarknessStats stats, CancellationToken ct, int paceMs = DefaultPaceMs, PauseGate? pause = null) =>
        PumpAsync(HiddenSubjects(hidden, stats, ct), fetchText, DarknessSource.Hidden, onPost, () => stats.HiddenFetched++, () => stats.FetchFailures++, ct, paceMs, pause);

    private static async IAsyncEnumerable<(string Subject, DarknessKind Kind, string? Label)> Subjects(IAsyncEnumerable<LabelEvent> labels, DarknessStats stats,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var label in labels.WithCancellation(ct).ConfigureAwait(false))
        {
            stats.LabelsSeen++;
            var postKind = DarknessFilter.Accept(label);
            var accountKind = postKind is null ? DarknessFilter.AcceptAccount(label) : null;
            if ((postKind ?? accountKind) is not { } kind || !seen.Add(label.Uri)) continue;
            if (seen.Count > SeenCap) seen.Clear();
            stats.LabelsPassed++;
            if (accountKind is not null) stats.AccountLabels++;
            yield return (label.Uri, kind, label.Val);
        }
    }

    private static async IAsyncEnumerable<(string Subject, DarknessKind Kind, string? Label)> HiddenSubjects(IAsyncEnumerable<string> hidden, DarknessStats stats,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var uri in hidden.WithCancellation(ct).ConfigureAwait(false))
        {
            if (!DarknessFilter.IsPostUri(uri) || !seen.Add(uri)) continue;
            if (seen.Count > SeenCap) seen.Clear();
            stats.HiddenNew++;
            yield return (uri, DarknessKind.Rude, null);
        }
    }

    private static async Task PumpAsync(IAsyncEnumerable<(string Subject, DarknessKind Kind, string? Label)> items, Func<string, CancellationToken, Task<string?>> fetchText,
        DarknessSource source, Action<DarknessPost> onPost, Action fetched, Action fetchFailed, CancellationToken ct, int paceMs, PauseGate? pause)
    {
        var backlog = new FetchBacklog();

        var reader = Task.Run(async () =>
        {
            try
            {
                await foreach (var (subject, kind, label) in items.WithCancellation(ct).ConfigureAwait(false))
                {
                    backlog.Add(subject, kind, label);
                }
            }
            catch (OperationCanceledException) { }   // cancelling is how this ends
            finally { backlog.Complete(); }
        }, ct);

        try
        {
            while (await backlog.TakeAsync(ct).ConfigureAwait(false) is var (subject, kind, label))
            {
                if (pause is not null) await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);
                string? text;
                try { text = await fetchText(subject, ct).ConfigureAwait(false); }
                catch (Exception error) when (!ct.IsCancellationRequested && error is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
                {
                    // a network failure, a timeout, a body that is not JSON or a response of the wrong shape loses only this subject: it is counted and the stream goes on
                    fetchFailed();
                    continue;
                }
                string clean = DarknessText.Clean(text, DarknessText.MaxPostChars);
                if (clean.Length > 0)
                {
                    fetched();
                    onPost(new DarknessPost(kind, Hashing.MixedFnv64(subject), clean, source, label));
                }
                await Task.Delay(paceMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }   // cancelling is how this ends
        try { await reader.ConfigureAwait(false); } catch (OperationCanceledException) { }   // cancelling is how this ends
    }
}

/// <summary>
/// The subjects waiting for their text to be fetched. A label stream replays its recent past in one burst, far faster than texts can be
/// fetched, and most of it is spam; so rude subjects wait apart from the rest, and the two take turns. The rude line holds its newest
/// <see cref="RudeCapacity"/>, enough for the whole hidden-reply replay, the rest its newest <see cref="RestCapacity"/>. A rude post in
/// the replay is therefore fetched among the first, none of the replay is lost, and a boss met early has several things to say.
/// </summary>
internal sealed class FetchBacklog
{
    /// <summary>Rude subjects held before the oldest is dropped.</summary>
    public const int RudeCapacity = Raider.Tuning.HiddenBacklog;
    /// <summary>Other subjects held before the oldest is dropped.</summary>
    public const int RestCapacity = 24;

    private readonly Queue<(string Subject, DarknessKind Kind, string? Label)> _rude = new(), _rest = new();
    private readonly SemaphoreSlim _held = new(0);
    private bool _rudeTurn = true;

    public void Add(string subject, DarknessKind kind, string? label)
    {
        lock (_rude)
        {
            var line = kind == DarknessKind.Rude ? _rude : _rest;
            line.Enqueue((subject, kind, label));
            // a dropped subject hands its place in the count to the one that pushed it out
            if (line.Count > (line == _rude ? RudeCapacity : RestCapacity)) line.Dequeue();
            else _held.Release();
        }
    }

    /// <summary>No more will be added: once the lines are empty, <see cref="TakeAsync"/> gives null.</summary>
    public void Complete()
    {
        _held.Release();
    }

    /// <summary>The next subject, waiting for one if none is held: a rude one and another by turns while both lines hold some. Null when
    /// the backlog is complete and empty.</summary>
    public async Task<(string Subject, DarknessKind Kind, string? Label)?> TakeAsync(CancellationToken ct)
    {
        await _held.WaitAsync(ct).ConfigureAwait(false);
        lock (_rude)
        {
            if (_rude.Count + _rest.Count == 0)
            {
                // only the completion's own release can be taken with nothing held; it is put back for any later call
                _held.Release();
                return null;
            }
            var line = _rest.Count == 0 || (_rudeTurn && _rude.Count > 0) ? _rude : _rest;
            _rudeTurn = line != _rude;
            return line.Dequeue();
        }
    }
}
