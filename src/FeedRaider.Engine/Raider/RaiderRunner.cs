using System.Diagnostics;
using System.Text.Json;

namespace FeedRaider.Engine.Raider;

/// <summary>Reads a feed generator from the public AppView, no login. The signed-out source of wingmen.</summary>
public sealed class PublicFeedSource(HttpClient http) : IFeedSource
{
    public async Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
    {
        string url = $"{BskyClient.AppView}/xrpc/app.bsky.feed.getFeed?feed={Uri.EscapeDataString(feedUri)}&limit={Math.Clamp(limit, 1, 100)}";
        if (cursor is not null) url += "&cursor=" + Uri.EscapeDataString(cursor);
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"getFeed failed: {(int)resp.StatusCode}");
        return FeedMapper.ParsePage(body, signedOut: true); // read without a login, so authors who opted out of logged-out viewers are dropped
    }
}

/// <summary>Thrown when the follows of a handle cannot be used (the lookup kept failing, or they follow nobody). The message is
/// fit to show the player.</summary>
public sealed class FollowsUnavailableException(string message) : Exception(message);

/// <summary>Posts from the accounts a handle follows (signed-out <c>?handle=</c> mode): each page takes a few original posts
/// (no reposts, no replies) from each of <paramref name="width"/> follows fetched concurrently, rotating through the follows.
/// A fetched author page is kept and served over later pages, so the request rate matches one request per author page. The feed
/// URI is ignored. The player's own posts are not mixed in. If the follows cannot be loaded after retries it throws
/// <see cref="FollowsUnavailableException"/>, once and for good.</summary>
public sealed class FollowsFeedSource(HttpClient http, string handle, Func<TimeSpan, CancellationToken, Task>? delay = null, int attempts = 3, int width = 5, int perAuthor = 3) : IFeedSource
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private List<Follow>? _follows;
    private FollowsUnavailableException? _failure;
    private readonly Dictionary<string, Queue<FeedPost>> _left = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _coolUntil = new(StringComparer.Ordinal);
    private long _pauseUntil;
    private int _next;

    private const int FailCooldownMs = 30_000, MaxRetryAfterMs = 120_000;

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
            for (int k = 0; k < perAuthor; k++)
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
            var page = await FetchAuthorAsync(f, ct).ConfigureAwait(false);
            if (page is null) return [];
            q = new Queue<FeedPost>(page.Posts);
            lock (_left) _left[f.Did] = q;
        }
        var got = new List<FeedPost>();
        while (got.Count < perAuthor && q.TryDequeue(out var p)) got.Add(p);
        return got;
    }

    private async Task<List<Follow>> LoadFollowsAsync(CancellationToken ct)
    {
        string who = "@" + handle.Trim().TrimStart('@');
        Exception? last = null;
        for (int i = 0; i < attempts; i++)
        {
            if (i > 0) await _delay(TimeSpan.FromSeconds(1 << i), ct).ConfigureAwait(false);
            try
            {
                var follows = await BskyClient.FetchFollowsAsync(http, handle, 60, ct).ConfigureAwait(false);
                if (follows.Count > 0) return follows;
                _failure = new FollowsUnavailableException($"{who} follows nobody");
                throw _failure;
            }
            catch (FollowsUnavailableException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { last = ex; }
        }
        _failure = new FollowsUnavailableException($"couldn't load {who}'s follows ({last?.Message})");
        throw _failure;
    }

    /// <summary>One follow's recent posts, or null when the request failed (that follow then sits out for a while; a 429 also
    /// pauses every follow for its Retry-After).</summary>
    private async Task<FeedPage?> FetchAuthorAsync(Follow f, CancellationToken ct)
    {
        string url = $"{BskyClient.AppView}/xrpc/app.bsky.feed.getAuthorFeed?actor={Uri.EscapeDataString(f.Did)}&limit=12&filter=posts_no_replies";
        try
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                long wait = FailCooldownMs;
                if ((int)resp.StatusCode == 429)
                {
                    var ra = resp.Headers.RetryAfter;
                    if (ra?.Delta is { } d) wait = (long)d.TotalMilliseconds;
                    else if (ra?.Date is { } at) wait = (long)(at - DateTimeOffset.UtcNow).TotalMilliseconds;
                    wait = Math.Clamp(wait, 1000, MaxRetryAfterMs);
                    _pauseUntil = Math.Max(_pauseUntil, Environment.TickCount64 + wait);
                }
                Cool(f, wait);
                return null;
            }
            var page = FeedMapper.ParsePage(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false), skipReposts: true, signedOut: true);
            return new FeedPage(page.Posts.Where(p => p.AuthorDid == f.Did).ToList(), null);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { Cool(f, FailCooldownMs); return null; }
    }

    private void Cool(Follow f, long ms) { lock (_coolUntil) _coolUntil[f.Did] = Environment.TickCount64 + ms; }
}

/// <summary>The background pumps that feed a running game: darkness events in, feed posts in. Everything runs against the
/// game under <c>lock (game)</c>; the runner steps the game under the same lock.</summary>
public static class RaiderFeeds
{
    /// <summary>Label and hidden-reply streams in, one wave per real event. The same sources as the spectator.</summary>
    public static Task DarknessAsync(RaiderGame game, HttpClient http, CancellationToken ct, DarknessStats? stats = null,
        IAsyncEnumerable<LabelEvent>? labels = null, IAsyncEnumerable<string>? hidden = null, PauseGate? pause = null)
    {
        stats ??= new DarknessStats();
        labels ??= new LabelSource { Pause = pause }.ReadAsync(ct);
        hidden ??= new ThreadgateSource { Pause = pause }.ReadAsync(ct);
        Func<string, CancellationToken, Task<string?>> fetch = (uri, c) => PostTextFetcher.FetchAsync(http, uri, c);
        void Offer(DarkPost p) { lock (game) game.OfferDark(p); }
        return Task.WhenAll(
            Task.Run(() => DarknessFeed.RunAsync(labels, fetch, Offer, stats, ct, pause: pause), ct),
            Task.Run(() => DarknessFeed.RunHiddenAsync(hidden, fetch, Offer, stats, ct, pause: pause), ct));
    }

    private const int RepeatWaitMs = 4000;

    /// <summary>The one like call: the host's session writes it, then the game says "liked @author" or why it failed.</summary>
    public static async Task LikeAsync(RaiderGame game, IActions actions, FeedPost post, CancellationToken ct = default)
    {
        try
        {
            await actions.LikeAsync(post.Ref, ct).ConfigureAwait(false);
            lock (game) game.LikeConfirmed(post);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine("like failed: " + ex.Message);
            lock (game) game.LikeFailed(post.Uri, Plain.Like(ex));
        }
    }

    /// <summary>Looks up the post's next thread post off the game loop and hands it to the game. A failed or slow lookup (past
    /// <see cref="Tuning.ThreadLookupMs"/>) ends the thread without a word: no toast, nothing for the frame to wait on.</summary>
    public static async Task ThreadAsync(RaiderGame game, IThreadSource threads, FeedPost post, CancellationToken ct = default, int timeoutMs = Tuning.ThreadLookupMs)
    {
        FeedPost? next = null;
        try { next = await threads.NextAsync(post, ct).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct).ConfigureAwait(false); }
        catch (Exception) { }
        lock (game)
        {
            if (next is null) game.ThreadEnded(post.Uri); else game.ThreadOpened(post.Uri, next);
        }
    }

    /// <summary>Keeps the wingman queue topped up from <paramref name="primary"/> (falling back to <paramref name="fallback"/> when it
    /// throws). Fetches a page only when the queue runs low. Every post passes through a <see cref="FeedInterleaver"/>.</summary>
    public static async Task WingmenAsync(RaiderGame game, IFeedSource primary, IFeedSource? fallback, string feedUri, CancellationToken ct, int lowWater = 4, Action<string>? onFallback = null, int backoffMs = 2000, int failoverMs = 30_000, string sourceName = "the feed", Action<string>? onTrouble = null, PauseGate? pause = null)
    {
        string? cursor = null;
        bool usingFallback = false;
        var mix = new FeedInterleaver(signedOut: !game.LoggedIn);
        lock (game) game.Queue.SignedOut = !game.LoggedIn;
        long stuckSince = 0, failingSince = 0;
        int failures = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (pause is not null) await pause.WaitWhilePausedAsync(ct).ConfigureAwait(false);
                int queued;
                lock (game)
                {
                    bool moved = false;
                    while (game.Queue.Count < Tuning.WingQueueMax && mix.TryTake(out var next)) { game.OfferPost(next); moved = true; }
                    if (moved) stuckSince = 0;
                    queued = game.Queue.Count;
                    // Only one author is buffered: pull more from others first; repeat after a short wait.
                    if (queued < lowWater && mix.Count > 0)
                    {
                        if (stuckSince == 0) stuckSince = Environment.TickCount64;
                        else if (Environment.TickCount64 - stuckSince >= RepeatWaitMs && mix.TryTake(out var rep, allowRepeat: true)) { game.OfferPost(rep); stuckSince = 0; queued = game.Queue.Count; }
                    }
                }
                if (queued >= lowWater) { await Task.Delay(1000, ct).ConfigureAwait(false); continue; }
                FeedPage page;
                try { page = await (usingFallback ? fallback! : primary).GetPageAsync(feedUri, cursor, 30, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (!usingFallback)
                {
                    cursor = null;
                    if (fallback is not null && ex is FollowsUnavailableException)
                    {
                        usingFallback = true;
                        onFallback?.Invoke($"{ex.Message}; showing Discover");
                        continue;
                    }
                    // A sign-in problem (missing scope, expired session) cannot clear by waiting: say so and switch at once.
                    if (fallback is not null && Plain.IsAuthProblem(ex))
                    {
                        usingFallback = true;
                        onFallback?.Invoke($"{Plain.Feed(ex, sourceName)}; showing Discover");
                        continue;
                    }
                    // Anything else is transient: the primary is retried with a growing delay and the fallback never fills in silently.
                    long now = Environment.TickCount64;
                    if (failures++ == 0) { failingSince = now; onTrouble?.Invoke($"{Plain.Feed(ex, sourceName)}; trying again"); }
                    bool empty; lock (game) empty = game.Queue.Count == 0 && mix.Count == 0;
                    if (fallback is not null && empty && now - failingSince >= failoverMs)
                    {
                        usingFallback = true;
                        onFallback?.Invoke($"{Plain.Feed(ex, sourceName)}; showing Discover");
                        continue;
                    }
                    await Task.Delay(Math.Min(15_000, backoffMs << Math.Min(failures - 1, 4)), ct).ConfigureAwait(false);
                    continue;
                }
                catch { await Task.Delay(8000, ct).ConfigureAwait(false); continue; }
                failures = 0;
                mix.Add(page.Posts);
                cursor = page.Cursor;
                await Task.Delay(page.Posts.Count == 0 ? 5000 : 400, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }
}

/// <summary>
/// Picks how many sim ticks go into one drawn frame (1 = draw every tick, 2 = every other, 3 = every third). The sim always
/// steps at <see cref="Tuning.Fps"/>, so the game plays at the same speed; a slow device just sees fewer, bigger updates.
/// It judges by throughput, not by what a frame costs: a level is "keeping up" when it draws most of the frames it aims for
/// (30 / divisor per second). It steps down one level only when drawing falls short of that and the host is pushing back (the
/// terminal's write backlog, ticks running late). Every move is a trial: after <see cref="TrialWindows"/> windows the new level
/// must draw clearly more frames per second than the old one did, else it is undone and moves are held off for a growing
/// cooldown, so it never settles on a divider that draws less than its neighbour. With headroom it probes one level back up
/// after <see cref="ProbeWindows"/> windows. Fed one short window at a time, a pure state machine.
/// </summary>
public sealed class AdaptiveQuality
{
    public const int MaxDivisor = 3;
    public const double KeepUp = 0.85, PushBack = 0.25, Gain = 1.1;
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
        double fps = drawn / Math.Max(1e-3, seconds);
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

/// <summary>The game loop shared by the console and browser hosts: steps at 30 Hz (catching up if a frame was late), draws,
/// and writes only the changed cells to the sink. A host that cannot take a frame right now says so through
/// <c>skipRender</c>; the sim still steps and the next frame carries the difference.</summary>
public sealed class RaiderRunner
{
    public RaiderGame Game { get; }
    public RaiderView View { get; }
    private readonly Func<(int Cols, int Rows)> _size;
    private readonly IFrameSink _sink;
    private readonly Screen[] _screens = new Screen[1];
    private readonly DiffEncoder _enc = new();

    /// <summary>Frames drawn per second over the last second, and the mean milliseconds one step + render + encode took.</summary>
    public double Fps { get; private set; }
    public double FrameMs { get; private set; }
    public bool ShowFps { get; set; }
    /// <summary>While paused the loop neither steps nor draws, and on resume it carries on from where it stopped instead of catching up.</summary>
    public PauseGate? Pause { get; set; }

    /// <summary>Ticks per drawn frame right now (see <see cref="AdaptiveQuality"/>); 1 is full rate.</summary>
    public int Divisor => _quality.Divisor;
    private readonly AdaptiveQuality _quality = new();
    /// <summary>Set false to always draw every tick (benchmarks, tests).</summary>
    public bool Adaptive { get; set; } = true;

    public RaiderRunner(RaiderGame game, Func<(int, int)> size, IFrameSink sink, ulong seed = 7)
    {
        Game = game; _size = size; _sink = sink;
        View = new RaiderView(seed);
    }

    public async Task RunAsync(CancellationToken ct, Func<bool>? skipRender = null)
    {
        var clock = Stopwatch.StartNew();
        long stepped = 0, windowStart = 0, qStart = 0;
        int drawn = 0, sinceDraw = 0, qDrawn = 0, qTicks = 0, qPushed = 0;
        double workMs = 0;
        bool wasPaused = false;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / Tuning.Fps / 2));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (Pause?.IsPaused == true) { wasPaused = true; continue; }
                long due = (long)(clock.Elapsed.TotalSeconds * Tuning.Fps);
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
                if (_screens[0] is null || _screens[0].Cols != cols || _screens[0].Rows != rows)
                {
                    _screens[0] = new Screen(Math.Max(1, cols), Math.Max(1, rows));
                    _enc.Invalidate();
                }
                string? frame = null;
                lock (Game)
                {
                    int n = (int)Math.Min(3, due - stepped);
                    for (int i = 0; i < n; i++) Game.Step();
                    stepped = n == 3 ? due : stepped + n;
                    qTicks++;
                    if (n > 1) qPushed++;
                    bool turn = ++sinceDraw >= _quality.Divisor;
                    if (turn)
                    {
                        if (skipRender?.Invoke() == true) qPushed++;
                        else
                        {
                            sinceDraw = 0;
                            View.Render(Game, _screens[0], ShowFps ? $"{Fps:F0}fps {FrameMs:F1}ms{(_quality.Divisor > 1 ? " /" + _quality.Divisor : "")}" : null);
                            frame = _enc.Encode(_screens[0]);
                        }
                    }
                }
                if (frame is { Length: > 0 }) _sink.Write(frame);
                if (frame is not null) { drawn++; qDrawn++; }
                workMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                if (clock.ElapsedMilliseconds - windowStart >= 1000)
                {
                    Fps = drawn * 1000.0 / (clock.ElapsedMilliseconds - windowStart);
                    FrameMs = drawn > 0 ? workMs / drawn : 0;
                    drawn = 0; workMs = 0; windowStart = clock.ElapsedMilliseconds;
                }
                if (clock.ElapsedMilliseconds - qStart >= 500)
                {
                    if (Adaptive) _quality.Update(qDrawn, qTicks, qPushed, (clock.ElapsedMilliseconds - qStart) / 1000.0);
                    qDrawn = qTicks = qPushed = 0; qStart = clock.ElapsedMilliseconds;
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}
