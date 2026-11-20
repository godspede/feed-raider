using System.Runtime.InteropServices.JavaScript;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

// Browser host: Feed Raider by default, the spectator view with ?mode=spectator. Both draw into xterm.js through
// JS interop; HttpClient and ClientWebSocket map to the browser's fetch and WebSocket.
public static partial class Host
{
    private const int InitialCols = 80, InitialRows = 24;
    private static int _cols = InitialCols, _rows = InitialRows;
    private static CancellationTokenSource? _cts;
    private static RaiderGame? _game;
    private static PauseGate _pause = new();
    private static bool _hidden, _menu;
    private static CancellationTokenSource? _tour;
    private static readonly PauseGate _tourPause = new();
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(20);
    private const string TabHold = "tab";
    private const string MenuHold = "menu";
    private const string SleepHold = "sleep";
    private const double TourSlowScale = 0.25;
    // The page's ?darkness= values, passed through by main.js; anything else turns darkness on.
    private const string DarknessOff = "off", DarknessDemoMode = "demo";

    public static void Main() { }

    [JSImport("writeFrame", "main.js")]
    private static partial void WriteFrame(string frame);

    [JSImport("showError", "main.js")]
    private static partial void ShowError(string message);

    [JSImport("showNotice", "main.js")]
    private static partial void ShowNotice(string message);

    [JSImport("announce", "main.js")]
    private static partial void Announce(string message);

    [JSImport("termBusy", "main.js")]
    private static partial bool TermBusy();

    [JSImport("musicCue", "main.js")]
    private static partial void MusicCue(string cue);

    [JSImport("sfxPlay", "main.js")]
    private static partial void SfxPlay(string name, double pan, int step, double size);

    [JSImport("bestScore", "main.js")]
    private static partial int BestScore();

    [JSImport("saveBestScore", "main.js")]
    private static partial void SaveBestScore(int score);

    [JSImport("fetchAvatar", "main.js")]
    private static partial Task<string?> FetchAvatar(string did, string? url);

    [JSImport("fetchPicture", "main.js")]
    private static partial Task<string?> FetchPicture(string did, string url, int width, int height);

    [JSExport]
    public static void SetSize(int cols, int rows)
    {
        _cols = cols;
        _rows = rows;
    }

    [JSExport]
    public static async Task StartSpectator(string handle, string darkness)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        using var http = new HttpClient { Timeout = HttpTimeout };
        SafeCue(MusicCues.Gameplay);
        var spectator = new Spectator(http, () => (_cols, _rows), new JsSink()) { Darkness = darkness != DarknessOff, DarknessDemo = darkness == DarknessDemoMode };
        try
        {
            await spectator.RunAsync(handle, _cts.Token);
        }
        catch (OperationCanceledException) { }   // cancelling is how this ends
        catch (Exception ex)
        {
            ShowError(Plain.FeedFailureText(ex, "@" + handle));
        }
    }

    /// <summary>The start page's backdrop: the game plays itself on built-in posts under its buttons, drawn into the same terminal.
    /// No account, no network, no sound effects and no saved score; the page plays the start page's music. <paramref name="slow"/> runs it at a
    /// quarter speed (reduced motion). Runs until <see cref="StopTour"/>, <see cref="StartGame"/> or <see cref="Quit"/>.</summary>
    [JSExport]
    public static async Task StartTour(int seed, bool slow)
    {
        _tour?.Cancel();
        var cts = _tour = new CancellationTokenSource();
        _tourPause.Hold(TabHold, _hidden);
        try { await TourRun.RunAsync(() => (_cols, _rows), new JsSink(), (ulong)(uint)seed, cts.Token, TermBusy, _tourPause, slow ? TourSlowScale : 1); }
        catch (OperationCanceledException) { }   // cancelling is how this ends
        catch (Exception ex)
        {
            // The backdrop is decoration: the start page works without it, so a failure is reported to the console and the page carries on.
            Console.Error.WriteLine($"the start page tour stopped: {ex.Message}");
        }
    }

    /// <summary>Ends the start page's tour; nothing of it is left running or drawing.</summary>
    [JSExport]
    public static void StopTour()
    {
        _tour?.Cancel();
        _tour = null;
    }

    /// <summary>The start page's tour sleeps (<paramref name="asleep"/>): it is neither stepped nor drawn, so its last frame stays on the
    /// field; false wakes it where it slept. Its own hold on the tour's pause, so a hidden tab coming back does not wake it.</summary>
    [JSExport]
    public static void SetTourAsleep(bool asleep) => _tourPause.Hold(SleepHold, asleep);

    /// <summary>Runs Feed Raider. Signed in, posts come from the account's feed and a like goes out through the session;
    /// signed out they come from the public Discover feed (or the follows of <paramref name="handle"/>) and the prompt only
    /// says to sign in. <paramref name="stage"/> above 1 starts the difficulty ramp there: a development aid (<c>?stage=</c>). <paramref name="bossGates"/> false (<c>?gates=off</c>) runs without boss gates.</summary>
    [JSExport]
    public static async Task StartGame(string handle, string feed, string darkness, bool showFps, bool adaptive, int stage, int boss, bool bossGates)
    {
        StopTour();
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        using var http = new HttpClient { Timeout = HttpTimeout };
        var game = new RaiderGame((ulong)Environment.TickCount64) { BossGates = bossGates };
        var me = Bridge.SessionSource.Current;
        game.SignedIn = me is not null;
        game.Grants = _grants;
        if (boss >= 1) game.StartAtBoss(boss);   // development aid: ?boss=4 opens on the warning of stage 4's boss
        else if (stage > 1) game.StartAtStage(stage);
        if (me is not null) game.SetPlayer(me.Did);
        // The only writes the game makes here: the player held the ship on one post's heart, or its repost loop, for three seconds.
        game.OnLike = post => _ = RaiderFeeds.WriteAsync(game, Bridge.LikeActions, post, PostAction.Like, cts.Token);
        game.OnRepost = post => _ = RaiderFeeds.WriteAsync(game, Bridge.LikeActions, post, PostAction.Repost, cts.Token);
        game.OnDelete = (post, action) => _ = RaiderFeeds.WriteAsync(game, Bridge.LikeActions, post, action, cts.Token, undo: true);
        // A read, never a write: the author's own next reply to a post the player liked, through the session when signed in
        // and from the public AppView when signed out or when the session may not read it.
        IThreadSource threads = me is not null ? new FallbackThreadSource(Bridge.ThreadSource, new PublicThreadSource(http)) : new PublicThreadSource(http);
        game.ThreadWanted = post => _ = RaiderFeeds.ThreadAsync(game, threads, post, cts.Token);
        game.AvatarWanted = post => _ = AvatarAsync(game, post);
        game.PictureWanted = (post, index, width, height) => _ = PictureAsync(game, post, index, width, height);
        game.BestToBeat = Safe(BestScore) ?? 0;   // no readable best: the run has nothing to beat
        game.OnBest = score => Safe(() => SaveBestScore(score));
        game.Said = message => Safe(() => Announce(message));
        game.Music = new MusicDirector(SafeCue);
        game.Sfx = new SfxBus((name, pan, step, size) => Safe(() => SfxPlay(name, pan, step, size)));
        lock (game) _game = game;
        var pause = _pause = new PauseGate();
        pause.Hold(TabHold, _hidden);
        pause.Hold(MenuHold, _menu);

        // Signed in, the posts are the player's Following timeline unless ?feed= names another; see FeedPlan.
        var plan = FeedPlan.Choose(feed, me is not null, handle, Bridge.TimelineSource, Bridge.FeedSource, new PublicFeedSource(http), h => new FollowsFeedSource(http, h, onTrouble: ShowNotice), m => { lock (game) game.Say(m); });
        if (plan is null) { ShowError("Unknown feed. Use following, discover, foryou, random or an at:// feed generator URI."); return; }
        game.PlainOnly = plan.FeedUri == Feeds.Random;
        var runner = _runner = new RaiderRunner(game, () => (_cols, _rows), new JsSink()) { ShowFps = showFps, Adaptive = adaptive, Pause = pause };
        var tasks = new List<Task> { runner.RunAsync(cts.Token, TermBusy), RaiderFeeds.PostsAsync(game, plan.Primary, plan.Fallback, plan.FeedUri, cts.Token, onFallback: ShowNotice, sourceName: plan.Name, onTrouble: ShowNotice, pause: pause, onStatus: message => { lock (game) game.Say(message); }) };
        if (darkness != DarknessOff) tasks.Add(RaiderFeeds.DarknessAsync(game, http, cts.Token, pause: pause));
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }   // cancelling is how this ends
        catch (Exception ex) { ShowError(Plain.FeedFailureText(ex, plan.Name)); }
    }

    // Music and sound effects are never allowed to break the game: a JS error from a cue or the best-score store is written to the
    // browser console (the first one only, since a cue fires often) and the game plays on. Not a toast: Said itself goes through Safe.
    private static void SafeCue(string cue) => Safe(() => MusicCue(cue));
    private static bool _jsFailureReported;
    private static void ReportJsFailure(JSException e)
    {
        if (_jsFailureReported) return;
        _jsFailureReported = true;
        Console.Error.WriteLine($"a call into the page failed: {e.Message}");
    }
    private static void Safe(Action act) { try { act(); } catch (JSException e) { ReportJsFailure(e); } }
    private static int? Safe(Func<int> get) { try { return get(); } catch (JSException e) { ReportJsFailure(e); return null; } }

    // The avatar is fetched and downsampled to 8x8 in JS (canvas); a CORS failure leaves the tinted fallback and is noted in the console.
    private static async Task AvatarAsync(RaiderGame game, FeedPost post)
    {
        try
        {
            string? csv = await FetchAvatar(post.AuthorDid, post.AvatarUrl);
            if (csv is null) return;
            if (AvatarCsv.Parse(csv) is { } px) lock (game) game.SetAvatar(post.AuthorDid, px);
        }
        catch (Exception ex) when (ex is JSException or FormatException or OverflowException) { Console.Error.WriteLine($"avatar not drawn: {ex.Message}"); }
    }

    // A post's picture is fetched and downsampled to the size of its box in JS; a failure leaves the empty box on the card.
    private static async Task PictureAsync(RaiderGame game, FeedPost post, int index, int width, int height)
    {
        try
        {
            if (post.Pictures is not { } pictures || index >= pictures.Count) return;
            string? csv = await FetchPicture(post.AuthorDid, pictures[index].Thumb, width, height);
            if (csv is null) return;
            var px = csv.Split(',').Select(uint.Parse).ToArray();
            lock (game) game.SetPicture(pictures[index].Thumb, width, height, px);
        }
        catch (Exception ex) when (ex is JSException or FormatException or OverflowException) { Console.Error.WriteLine($"picture not drawn: {ex.Message}"); }
    }

    /// <summary>The page's tab went to the background (or came back): the game loop, feed paging and both sockets stop, then resume.</summary>
    [JSExport]
    public static void SetHidden(bool hidden)
    {
        _hidden = hidden;
        _pause.Hold(TabHold, hidden);
        _tourPause.Hold(TabHold, hidden);
    }

    /// <summary>The page's pause menu opened (or closed): the game holds still behind it, as for a hidden tab.</summary>
    [JSExport]
    public static void SetMenu(bool open)
    {
        _menu = open;
        _pause.Hold(MenuHold, open);
    }

    /// <summary>Back to the start page: the run ends with no game-over theme (the page cues the start page's theme) and nothing of it is
    /// left to take input. The next <see cref="StartGame"/> builds a fresh game.</summary>
    [JSExport]
    public static void Quit()
    {
        _cts?.Cancel();
        _game = null;
        _runner = null;
        SetMenu(false);
    }

    private static Grants _grants = Grants.All;

    /// <summary>What the player's sign-in may write, as the page read it from the granted scope: given before a run starts, and
    /// again when the player signs in afresh from the pause menu, so buttons that were locked unlock in the run under way.</summary>
    [JSExport]
    public static void SetGrants(bool like, bool unlike, bool repost, bool unrepost)
    {
        _grants = new Grants(like, unlike, repost, unrepost);
        WithGame(g => g.Grants = _grants);
    }

    // --- input from the page (main.js turns DOM events into these) ---
    private static void WithGame(Action<RaiderGame> act)
    {
        if (_game is { } g) lock (g) act(g);
    }

    [JSExport]
    public static void Key(string code, bool down) => WithGame(g =>
    {
        HostKeys.Apply(g.Input, code, down);
    });

    [JSExport] public static void Pointer(double col, double row) => WithGame(g => g.Input.Pointer(col, row));
    [JSExport] public static void PointerGone() => WithGame(g => g.Input.PointerGone());
    [JSExport] public static void Drag(double dCols, double dRows) => WithGame(g => g.Input.Drag(dCols, dRows));
    [JSExport] public static void Tap(double col, double row) => WithGame(g => g.Input.Tap(col, row));
    [JSExport] public static void Bomb() => WithGame(g => g.Input.Bomb());

    private static RaiderRunner? _runner;

    /// <summary>A click or tap on a cell of the screen: on a button of the feed log's last drawn frame it toggles that like or repost,
    /// anywhere else it does nothing.</summary>
    [JSExport]
    public static void Click(int col, int row) => WithGame(g =>
    {
        if (_runner is { } r) r.View.ToggleLogButtonAt(g, col, row);
    });

    /// <summary>The pause menu's press on a feed log button: <paramref name="action"/> is "like" or "repost".</summary>
    [JSExport]
    public static void Toggle(string uri, string action) => WithGame(g => FeedLogJson.Toggle(g, uri, action));

    /// <summary>The feed log as JSON for the pause menu's list, newest first; "[]" before a run.</summary>
    [JSExport]
    public static string FeedLog()
    {
        if (_game is not { } g) return "[]";
        lock (g) return FeedLogJson.Build(g);
    }

    /// <summary>The feed speed, step 1 to 5. <paramref name="say"/> shows the toast "Feed speed n of 5": a key or the closing menu
    /// asks for it, the start of a run does not. A card already on the field keeps its place; the new speed starts at the next row step.</summary>
    [JSExport] public static void SetFeedSpeed(int step, bool say) => WithGame(g => { if (say) g.SetFeedSpeed(step); else g.FeedSpeed = step; });

    private sealed class JsSink : IFrameSink
    {
        public void Write(string frame) => WriteFrame(frame);
    }
}
