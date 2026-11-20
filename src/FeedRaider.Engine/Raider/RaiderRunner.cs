using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace FeedRaider.Engine.Raider;

/// <summary>Reads a feed generator from the public AppView, without signing in. The signed-out source of posts.</summary>
public sealed class PublicFeedSource(HttpClient http) : IFeedSource
{
    private const int MaxPageLimit = 100;   // the most posts the AppView returns for one getFeed request

    public async Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
    {
        string url = $"{BskyClient.AppView}/xrpc/app.bsky.feed.getFeed?feed={Uri.EscapeDataString(feedUri)}&limit={Math.Clamp(limit, 1, MaxPageLimit)}";
        if (cursor is not null) url += "&cursor=" + Uri.EscapeDataString(cursor);
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"getFeed failed: {(int)resp.StatusCode}");
        return FeedMapper.ParsePage(body, signedOut: true); // read without signing in, so authors who opted out of signed-out viewers are dropped
    }
}

/// <summary>Thrown when the follows of a handle cannot be used (the lookup kept failing, or they follow nobody). The message is
/// fit to show the player.</summary>
public sealed class FollowsUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Posts from the accounts a handle follows (signed-out <c>?handle=</c> mode): each page takes a few original posts
/// (no reposts, no replies) from each of <paramref name="width"/> follows fetched concurrently, rotating through the follows.
/// A fetched author page is kept and served over later pages, so the request rate matches one request per author page. The feed
/// URI is ignored. The player's own posts are not mixed in. If the follows cannot be loaded (at once for a handle Bluesky
/// rejects or does not know, after retries for any other failure) it throws <see cref="FollowsUnavailableException"/>, once
/// and for good. <paramref name="onTrouble"/> hears when a follow's request fails
/// (once until a request succeeds again).</summary>
public sealed class FollowsFeedSource(HttpClient http, string handle, Func<TimeSpan, CancellationToken, Task>? delay = null, int width = FollowsFeedSource.DefaultWidth, Action<string>? onTrouble = null) : IFeedSource
{
    private const int DefaultWidth = 5;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private List<Follow>? _follows;
    private FollowsUnavailableException? _failure;
    private readonly Dictionary<string, Queue<FeedPost>> _left = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _coolUntil = new(StringComparer.Ordinal);
    private long _pauseUntil;
    private int _next;

    private const int FailCooldownMs = 30_000, MinRetryAfterMs = 1000, MaxRetryAfterMs = 120_000;
    // Follows asked for when the list loads, and posts asked for per author page (original posts only, so most of it is dropped).
    private const int FollowsLimit = 60, AuthorPageLimit = 12;
    private const int LoadAttempts = 3;   // tries at loading the follows, two seconds apart at first and doubling
    private const int PostsPerAuthor = 3;   // posts one author adds to a page
    private bool _troubled;

    public async Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
    {
        if (_failure is not null) throw _failure;
        _follows ??= await LoadFollowsAsync(ct).ConfigureAwait(false);
        if (Environment.TickCount64 < _pauseUntil) return new FeedPage([], null); // rate-limited: let the caller wait
        // A follow with nothing original to show is skipped, so one page is never empty while someone has posts.
        var posts = new List<FeedPost>();
        int remaining = _follows.Count;
        while (posts.Count == 0 && remaining > 0)
        {
            int n = Math.Min(Math.Max(1, width), remaining);
            remaining -= n;
            var batch = new List<Follow>();
            for (int i = 0; i < n; i++)
            {
                var f = _follows[_next++ % _follows.Count];
                bool cooling; lock (_coolUntil) cooling = _coolUntil.TryGetValue(f.Did, out var until) && Environment.TickCount64 < until;
                if (!cooling) batch.Add(f);
            }
            var taken = await Task.WhenAll(batch.Select(f => TakeAsync(f, ct))).ConfigureAwait(false);
            // round-robin across the batch: a1 b1 c1 a2 b2 ...
            for (int k = 0; k < PostsPerAuthor; k++)
                foreach (var t in taken) if (k < t.Count) posts.Add(t[k]);
        }
        return new FeedPage(posts, null);
    }

    private async Task<List<FeedPost>> TakeAsync(Follow f, CancellationToken ct)
    {
        Queue<FeedPost>? q;
        lock (_left) _left.TryGetValue(f.Did, out q);
        if (q is null || q.Count == 0)
        {
            var page = await FetchAuthorOrCoolDownAsync(f, ct).ConfigureAwait(false);
            if (page is null) return [];
            q = new Queue<FeedPost>(page.Posts);
            lock (_left) _left[f.Did] = q;
        }
        var got = new List<FeedPost>();
        while (got.Count < PostsPerAuthor && q.TryDequeue(out var p)) got.Add(p);
        return got;
    }

    private async Task<List<Follow>> LoadFollowsAsync(CancellationToken ct)
    {
        string who = "@" + handle.Trim().TrimStart('@');
        Exception? last = null;
        for (int i = 0; i < LoadAttempts; i++)
        {
            if (i > 0) await _delay(TimeSpan.FromSeconds(1 << i), ct).ConfigureAwait(false);
            try
            {
                var follows = await BskyClient.FetchFollowsAsync(http, handle, FollowsLimit, ct).ConfigureAwait(false);
                if (follows.Count > 0) return follows;
                _failure = new FollowsUnavailableException($"{who} follows nobody the game can show.");
                throw _failure;
            }
            catch (FollowsUnavailableException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or BskyStatusException or TaskCanceledException)
            {
                // the network, a body that is not JSON, a status Bluesky refused with, or the client's timeout (a cancel was rethrown above)
                last = ex;
                if (BlamesTheHandle(ex)) break;
            }
        }
        _failure = new FollowsUnavailableException(FollowsFailureSentence(who, last), last);
        throw _failure;
    }

    // Bluesky answers 400 for text that is not a handle and 404 for a handle it does not know: either way the handle is the problem.
    private static bool BlamesTheHandle(Exception? failure) =>
        failure is BskyStatusException { Status: (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.NotFound };

    private static string FollowsFailureSentence(string who, Exception? failure) => failure switch
    {
        _ when BlamesTheHandle(failure) => $"Could not find {who} on Bluesky.",
        BskyStatusException => $"Bluesky could not load who {who} follows just now.",
        JsonException => $"Bluesky's list of who {who} follows could not be read.",
        _ => $"Could not reach Bluesky to load who {who} follows.",
    };

    /// <summary>One follow's recent posts, or null when the request failed (that follow then sits out for a while; a 429 also
    /// pauses every follow for its Retry-After).</summary>
    private async Task<FeedPage?> FetchAuthorOrCoolDownAsync(Follow f, CancellationToken ct)
    {
        string url = $"{BskyClient.AppView}/xrpc/app.bsky.feed.getAuthorFeed?actor={Uri.EscapeDataString(f.Did)}&limit={AuthorPageLimit}&filter=posts_no_replies";
        try
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                long wait = FailCooldownMs;
                if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var ra = resp.Headers.RetryAfter;
                    if (ra?.Delta is { } d) wait = (long)d.TotalMilliseconds;
                    else if (ra?.Date is { } at) wait = (long)(at - DateTimeOffset.UtcNow).TotalMilliseconds;
                    wait = Math.Clamp(wait, MinRetryAfterMs, MaxRetryAfterMs);
                    _pauseUntil = Math.Max(_pauseUntil, Environment.TickCount64 + wait);
                }
                CoolDown(f, wait);
                return null;
            }
            var page = FeedMapper.ParsePage(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false), skipReposts: true, signedOut: true);
            _troubled = false;
            return new FeedPage(page.Posts.Where(p => p.AuthorDid == f.Did).ToList(), null);
        }
        // One follow's request failing (network, a request timeout, a body that does not parse) is not worth stopping the others for:
        // that follow sits out for FailCooldownMs and the page is built from the rest.
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            if (!_troubled) { _troubled = true; onTrouble?.Invoke("A followed account could not be read, so it sits out for a while."); }
            CoolDown(f, FailCooldownMs);
            return null;
        }
    }

    private void CoolDown(Follow f, long ms) { lock (_coolUntil) _coolUntil[f.Did] = Environment.TickCount64 + ms; }
}

/// <summary>The background pumps that feed a running game: darkness events in, feed posts in. Everything runs against the
/// game under <c>lock (game)</c>; the runner steps the game under the same lock.</summary>
public static class RaiderFeeds
{
    /// <summary>Label and hidden-reply streams in, one wave per real event. The same sources as the spectator; only posts in the
    /// player's <paramref name="languages"/> become enemies.</summary>
    public static Task DarknessAsync(RaiderGame game, HttpClient http, PlayerLanguages languages, CancellationToken ct, PauseGate? pause = null)
    {
        var stats = new DarknessStats();
        var labels = new LabelSource { Pause = pause }.ReadAsync(ct);
        var hidden = new ThreadgateSource { Pause = pause }.ReadAsync(ct);
        bool signedOut = !game.SignedIn;
        Func<string, CancellationToken, Task<string?>> fetch = (uri, c) => PostTextFetcher.FetchAsync(http, uri, signedOut, c, languages);
        void Offer(DarkPost p) { lock (game) game.OfferDark(p); }
        return Task.WhenAll(
            Task.Run(() => DarknessFeed.RunAsync(labels, fetch, Offer, stats, ct, pause: pause), ct),
            Task.Run(() => DarknessFeed.RunHiddenAsync(hidden, fetch, Offer, stats, ct, pause: pause), ct));
    }

    private const int RepeatWaitMs = 4000, PageLimit = 30;
    private const string FallbackName = "Discover";   // what the notices call the fallback feed
    private const int DefaultBackoffMs = 2000, DefaultFailoverMs = 30_000;   // a failing feed is retried after the first, and gives way to its fallback after the second
    private const int MaxBackoffDoublings = 4;   // the retry delay doubles this many times at most, up to MaxBackoffMs
    private const int MaxBackoffMs = 15_000, FallbackRetryMs = 8000, EmptyPageWaitMs = 5000, NextPageWaitMs = 400;

    /// <summary>The one write of a finished hold: the host's session makes the like or repost record, then the game lights the
    /// button and keeps the record's address, or puts the button back and says why it failed.</summary>
    public static async Task WriteAsync(RaiderGame game, IPostActions actions, FeedPost post, PostAction action, CancellationToken ct = default, bool undo = false)
    {
        try
        {
            if (undo)
            {
                string? record;
                lock (game) record = game.Book.Find(post.Uri) is { } m ? (action == PostAction.Like ? m.LikeUri : m.RepostUri) : null;
                if (record is null) throw new InvalidOperationException("the record's address is not known");
                await actions.DeleteAsync(record, ct).ConfigureAwait(false);
                lock (game) game.WriteConfirmed(post.Uri, action, null, undo: true);
                return;
            }
            var made = action == PostAction.Like
                ? await actions.LikeAsync(post.Ref, ct).ConfigureAwait(false)
                : await actions.RepostAsync(post.Ref, ct).ConfigureAwait(false);
            lock (game) game.WriteConfirmed(post.Uri, action, made.Uri);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine((action == PostAction.Like ? "like" : "repost") + (undo ? " undo" : "") + " failed: " + ex.Message);
            lock (game) game.WriteFailed(post.Uri, action, Plain.WriteFailureText(ex, action, undo), undo);
        }
    }

    /// <summary>Looks up the author's own next reply to a liked post, off the game loop, and hands it to the game. A lookup that
    /// fails, or runs past <paramref name="timeoutMs"/>, changes nothing in the game and nothing waits on it: the player liked a
    /// post and did not ask for a thread, so a missing reply is no news to them. A failure goes to the error stream.</summary>
    public static async Task ThreadAsync(RaiderGame game, IThreadSource threads, FeedPost post, CancellationToken ct = default, int timeoutMs = Tuning.ThreadLookupMs)
    {
        FeedPost? next = null;
        try { next = await threads.NextAsync(post, ct).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }   // safe to drop: the reply is an extra, and the game carries on without it
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { Console.Error.WriteLine($"a thread could not be read: {ex.Message}"); }   // logged, not shown: the player did not ask for the thread
        lock (game)
        {
            if (next is not null) game.ThreadOpened(next);
        }
    }

    /// <summary>Keeps the post queue topped up from <paramref name="primary"/> (falling back to <paramref name="fallback"/> when it
    /// throws). <paramref name="onStatus"/> hears "loading" at the start and "empty" when a page has no posts. Fetches a page only when the queue runs low. Every post passes through a <see cref="FeedInterleaver"/>.</summary>
    public static async Task PostsAsync(RaiderGame game, IFeedSource primary, IFeedSource? fallback, string feedUri, CancellationToken ct, Action<string>? onFallback = null, int backoffMs = DefaultBackoffMs, int failoverMs = DefaultFailoverMs, string sourceName = "the feed", Action<string>? onTrouble = null, PauseGate? pause = null, Action<string>? onStatus = null)
    {
        string? cursor = null;
        bool usingFallback = false;
        var mix = new FeedInterleaver(signedOut: !game.SignedIn, textOnly: game.PlainOnly);
        long stuckSince = 0, failingSince = 0;
        int failures = 0;
        bool toldEmpty = false;
        string SwitchNotice(Exception ex) => $"{Plain.FeedFailureText(ex, sourceName)}; showing {FallbackName}";
        static string RetryNotice(Exception ex, string source) => $"{Plain.FeedFailureText(ex, source)}; trying again";
        onStatus?.Invoke(FeedStatus.Loading);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (pause is not null) await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);
                int queued;
                lock (game)
                {
                    bool moved = false;
                    while (game.Posts.Count < Tuning.PostQueueMax && mix.TryTake(out var next)) { game.Posts.Offer(next); moved = true; }
                    if (moved) stuckSince = 0;
                    queued = game.Posts.Count;
                    // Only one author is buffered: pull more from others first; repeat after a short wait.
                    if (queued < Tuning.PostQueueLow && mix.Count > 0)
                    {
                        if (stuckSince == 0) stuckSince = Environment.TickCount64;
                        else if (Environment.TickCount64 - stuckSince >= RepeatWaitMs && mix.TryTake(out var rep, allowRepeat: true)) { game.Posts.Offer(rep); stuckSince = 0; queued = game.Posts.Count; }
                    }
                }
                if (queued >= Tuning.PostQueueLow) { await game.Posts.WaitBelowLowAsync(ct).ConfigureAwait(false); continue; }
                FeedPage page;
                try { page = await (usingFallback ? fallback! : primary).GetPageAsync(feedUri, cursor, PageLimit, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (!usingFallback)
                {
                    cursor = null;
                    if (fallback is not null && ex is FollowsUnavailableException)
                    {
                        usingFallback = true;
                        onFallback?.Invoke($"{ex.Message} Showing {FallbackName} instead.");
                        continue;
                    }
                    // A sign-in problem (missing scope, expired session) cannot clear by waiting: say so and switch at once.
                    if (fallback is not null && Plain.IsAuthProblem(ex))
                    {
                        usingFallback = true;
                        onFallback?.Invoke(SwitchNotice(ex));
                        continue;
                    }
                    // Anything else is transient: the primary is retried with a growing delay and the fallback never fills in silently.
                    long now = Environment.TickCount64;
                    if (failures++ == 0) { failingSince = now; onTrouble?.Invoke(RetryNotice(ex, sourceName)); }
                    bool empty; lock (game) empty = game.Posts.Count == 0 && mix.Count == 0;
                    if (fallback is not null && empty && now - failingSince >= failoverMs)
                    {
                        usingFallback = true;
                        onFallback?.Invoke(SwitchNotice(ex));
                        continue;
                    }
                    await Task.Delay(Math.Min(MaxBackoffMs, backoffMs << Math.Min(failures - 1, MaxBackoffDoublings)), ct).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex)
                {
                    // the fallback failing too: say so once, then keep retrying it
                    if (failures++ == 0) onTrouble?.Invoke(RetryNotice(ex, FallbackName));
                    await Task.Delay(FallbackRetryMs, ct).ConfigureAwait(false);
                    continue;
                }
                failures = 0;
                if (page.Posts.Count == 0) { if (!toldEmpty) onStatus?.Invoke(FeedStatus.Empty); toldEmpty = true; }
                else toldEmpty = false;
                mix.Add(page.Posts);
                cursor = page.Cursor;
                await Task.Delay(page.Posts.Count == 0 ? EmptyPageWaitMs : NextPageWaitMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }   // the caller cancelling is how the loop is stopped
    }
}

/// <summary>
/// Picks how many sim ticks go into one drawn frame (1 = draw every tick, 2 = every other, 3 = every third). The sim always
/// steps at <see cref="Tuning.Fps"/>, so the game plays at the same speed; a slow device just sees fewer, bigger updates.
/// It judges by throughput, not by what a frame costs: a level is "keeping up" when it draws most of the frames it aims for
/// (<see cref="Tuning.Fps"/> / divisor per second). It steps down one level only when drawing falls short of that and the host is pushing back (the
/// terminal's write backlog, ticks running late). Every move is a trial: after <see cref="TrialWindows"/> windows the new level
/// must draw clearly more frames per second than the old one did, else it is undone and moves are held off for a growing
/// cooldown, so it never settles on a divider that draws less than its neighbour. With headroom it probes one level back up
/// after <see cref="ProbeWindows"/> windows. Fed one short window at a time, a pure state machine.
/// </summary>
public sealed class AdaptiveQuality
{
    public const int MaxDivisor = 3;
    public const double KeepUp = 0.85, PushBack = 0.25, Gain = 1.1;
    private const double MinWindowSeconds = 1e-3;
    public const int DownWindows = 2, TrialWindows = 2, ProbeWindows = 6, MinCooldown = 4, MaxCooldown = 32;

    private int _short, _calm, _trial, _cooldown, _backoff = MinCooldown;
    private int _from;
    private double _fromFps, _trialFps;

    /// <summary>Ticks per drawn frame.</summary>
    public int Divisor { get; private set; } = 1;

    /// <param name="drawn">Frames handed to the terminal in the window.</param>
    /// <param name="ticks">Sim ticks that ran in the window.</param>
    /// <param name="pushed">Ticks where the host pushed back: the terminal was behind, or the loop was late.</param>
    /// <param name="seconds">Length of the window.</param>
    public void Update(int drawn, int ticks, int pushed, double seconds)
    {
        double fps = drawn / Math.Max(MinWindowSeconds, seconds);
        double target = Tuning.Fps / (double)Divisor;
        bool keepingUp = fps >= target * KeepUp;
        bool pushing = ticks > 0 ? pushed / (double)ticks > PushBack : drawn == 0;
        if (_cooldown > 0) _cooldown--;

        if (_trial > 0)
        {
            _trialFps += fps;
            if (--_trial > 0) return;
            if (_trialFps / TrialWindows > _fromFps * Gain || keepingUp && Divisor < _from) { _backoff = MinCooldown; _calm = _short = 0; return; }
            Divisor = _from; _cooldown = _backoff; _backoff = Math.Min(MaxCooldown, _backoff * 2); _calm = _short = 0;
            return;
        }

        if (!keepingUp && pushing)
        {
            _calm = 0;
            if (_cooldown == 0 && Divisor < MaxDivisor && ++_short >= DownWindows) StartTrial(Divisor + 1, fps);
        }
        else
        {
            _short = 0;
            if (Divisor > 1 && keepingUp && !pushing && _cooldown == 0 && ++_calm >= ProbeWindows) StartTrial(Divisor - 1, fps);
            else if (!keepingUp || pushing) _calm = 0;
        }
    }

    private void StartTrial(int to, double fromFps)
    {
        _from = Divisor; _fromFps = fromFps; Divisor = to; _trial = TrialWindows; _trialFps = 0; _calm = _short = 0;
    }
}

/// <summary>The game loop shared by the console and browser hosts: steps <see cref="Tuning.Fps"/> times a second (catching up if a frame was late), draws,
/// and writes only the changed cells to the sink. A host that cannot take a frame right now says so through
/// <c>skipRender</c>; the sim still steps and the next frame carries the difference.</summary>
public sealed class RaiderRunner
{
    /// <summary>The most ticks one drawn frame catches up; past it the sim drops the backlog rather than spiral behind.</summary>
    private const int MaxCatchUpTicks = 3;
    public RaiderGame Game { get; }
    public RaiderView View { get; }
    private readonly Func<(int Cols, int Rows)> _size;
    private readonly IFrameSink _sink;
    private Screen? _screen;
    // snapped, so a glyph-atlas renderer sees a bounded set of colours (see ColourSnap)
    private readonly DiffEncoder _enc = DiffEncoder.Snapped();
    internal DiffEncoder Encoder => _enc;

    /// <summary>Frames drawn per second over the last second, and the mean milliseconds one step + render + encode took.</summary>
    public double Fps { get; private set; }
    public double FrameMs { get; private set; }
    public bool ShowFps { get; set; }
    /// <summary>While paused the loop neither steps nor draws, and on resume it carries on from where it stopped instead of catching up.</summary>
    public PauseGate? Pause { get; set; }
    /// <summary>A tour's pilot: it sets the keys before each tick, and the loop ends once it says the run is over.</summary>
    public Autopilot? Pilot { get; set; }
    /// <summary>Sim speed against real time (1 = normal); the tour is slowed under reduced motion.</summary>
    public double TimeScale { get; set; } = 1;

    /// <summary>Ticks per drawn frame right now (see <see cref="AdaptiveQuality"/>); 1 is full rate.</summary>
    public int Divisor => _quality.Divisor;
    private readonly AdaptiveQuality _quality = new();
    /// <summary>Set false to always draw every tick (benchmarks, tests).</summary>
    public bool Adaptive { get; set; } = true;

    public RaiderRunner(RaiderGame game, Func<(int, int)> size, IFrameSink sink, ulong seed = RaiderView.DefaultBackgroundSeed)
    {
        Game = game; _size = size; _sink = sink;
        View = new RaiderView(seed);
    }

    // the frame-rate readout averages over one second; the adaptive quality judges over half of that
    private const int MsPerSecond = 1000, FpsWindowMs = MsPerSecond, QualityWindowMs = 500;
    private const int TimerTicksPerFrame = 2;   // the pacing timer ticks this often per sim frame, so a late frame is caught within half a frame

    /// <summary>The clock the pacing timer runs on; a test gives one that counts the timers made and disposed.</summary>
    internal TimeProvider PacingClock { get; init; } = TimeProvider.System;

    public async Task RunAsync(CancellationToken ct, Func<bool>? skipRender = null)
    {
        var clock = Stopwatch.StartNew();
        long stepped = 0, windowStart = 0, qStart = 0;
        int drawn = 0, sinceDraw = 0, qDrawn = 0, qTicks = 0, qPushed = 0;
        double workMs = 0;
        bool wasPaused = false;
        // a live periodic timer wakes the process on every tick even with nothing awaiting it, so a held gate lets go of it
        PeriodicTimer? timer = null;
        try
        {
            while (true)
            {
                if (Pause is { IsPaused: true } gate)
                {
                    wasPaused = true;
                    timer?.Dispose();
                    timer = null;
                    await gate.WaitWhilePausedAsync(ct).ConfigureAwait(false);
                    continue;
                }
                timer ??= new PeriodicTimer(TimeSpan.FromMilliseconds((double)MsPerSecond / Tuning.Fps / TimerTicksPerFrame), PacingClock);
                // false only after Dispose, and only this loop disposes it, at the top while the gate is held
                await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
                if (Pause?.IsPaused == true) continue;   // held during the wait: the top of the loop lets go of the timer
                long due = (long)(clock.Elapsed.TotalSeconds * Tuning.Fps * TimeScale);
                if (wasPaused)
                {
                    wasPaused = false;
                    stepped = due;
                    windowStart = qStart = clock.ElapsedMilliseconds;
                    drawn = qDrawn = qTicks = qPushed = 0; workMs = 0;
                    continue;
                }
                if (due <= stepped) continue;
                long t0 = Stopwatch.GetTimestamp();
                var (cols, rows) = _size();
                var screen = _screen;
                if (screen is null || screen.Cols != cols || screen.Rows != rows)
                {
                    _screen = screen = new Screen(Math.Max(1, cols), Math.Max(1, rows));
                    _enc.Invalidate();
                }
                string? frame = null;
                bool over = false;
                lock (Game)
                {
                    int n = (int)Math.Min(MaxCatchUpTicks, due - stepped);
                    for (int i = 0; i < n; i++) { Pilot?.Drive(Game); Game.Step(); }
                    over = Pilot?.Finished() == true;
                    stepped = n == MaxCatchUpTicks ? due : stepped + n;
                    qTicks++;
                    if (n > 1) qPushed++;
                    bool turn = ++sinceDraw >= _quality.Divisor;
                    if (turn)
                    {
                        if (skipRender?.Invoke() == true) qPushed++;
                        else
                        {
                            sinceDraw = 0;
                            View.FitAndRender(Game, screen, ShowFps ? $"{Fps:F0}fps {FrameMs:F1}ms{(_quality.Divisor > 1 ? " /" + _quality.Divisor : "")}" : null);
                            frame = _enc.Encode(screen);
                        }
                    }
                }
                if (frame is { Length: > 0 }) _sink.Write(frame);
                if (over) return;
                if (frame is not null) { drawn++; qDrawn++; }
                workMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                if (clock.ElapsedMilliseconds - windowStart >= FpsWindowMs)
                {
                    Fps = drawn * (double)MsPerSecond / (clock.ElapsedMilliseconds - windowStart);
                    FrameMs = drawn > 0 ? workMs / drawn : 0;
                    drawn = 0; workMs = 0; windowStart = clock.ElapsedMilliseconds;
                }
                if (clock.ElapsedMilliseconds - qStart >= QualityWindowMs)
                {
                    if (Adaptive) _quality.Update(qDrawn, qTicks, qPushed, (clock.ElapsedMilliseconds - qStart) / (double)MsPerSecond);
                    qDrawn = qTicks = qPushed = 0; qStart = clock.ElapsedMilliseconds;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }   // the caller cancelling is how the loop is stopped
        finally { timer?.Dispose(); }
    }
}

/// <summary>What the feed loader says about its own state, for the host to show and announce.</summary>
public static class FeedStatus
{
    public const string Loading = "loading your feed…";
    public const string Empty = "no posts to show";
}
