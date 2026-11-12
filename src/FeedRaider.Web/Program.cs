using System.Runtime.InteropServices.JavaScript;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

// Browser host: Feed Raider by default, the crowd view with ?mode=spectator. Both draw into xterm.js through
// JS interop; HttpClient and ClientWebSocket map to the browser's fetch and WebSocket.
public static partial class Host
{
    private static int _cols = 80, _rows = 24;
    private static CancellationTokenSource? _cts;
    private static RaiderGame? _game;
    private static PauseGate _pause = new();
    private static bool _hidden, _menu;
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(20);
    /// <summary>The avatar arrives as 8x8 pixels.</summary>
    private const int AvatarPixels = 64;

    public static void Main() { }

    [JSImport("writeFrame", "main.js")]
    private static partial void WriteFrame(string frame);

    [JSImport("showError", "main.js")]
    private static partial void ShowError(string message);

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

    [JSExport]
    public static void SetSize(int cols, int rows)
    {
        _cols = cols;
        _rows = rows;
    }

    [JSExport]
    public static async Task Start(string handle, string darkness)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        using var http = new HttpClient { Timeout = HttpTimeout };
        SafeCue(MusicCues.Gameplay);
        var spectator = new Spectator(http, () => (_cols, _rows), new JsSink()) { Darkness = darkness != "off", DarknessDemo = darkness == "demo" };
        try
        {
            await spectator.RunAsync(handle, _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError(Plain.Feed(ex, "@" + handle));
        }
    }

    /// <summary>Runs Feed Raider. Signed in, wingmen come from the account's feed and a like goes out through the session;
    /// signed out they come from the public Discover feed (or the follows of <paramref name="handle"/>) and the prompt only
    /// says to sign in. <paramref name="stage"/> above 1 starts the difficulty ramp there: a development aid (<c>?stage=</c>).</summary>
    [JSExport]
    public static async Task StartGame(string handle, string feed, string darkness, bool showFps, bool adaptive, int stage)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        using var http = new HttpClient { Timeout = HttpTimeout };
        var game = new RaiderGame((ulong)Environment.TickCount64);
        var me = Bridge.SessionSource.Current;
        game.LoggedIn = me is not null;
        if (stage > 1) game.StartAtStage(stage);
        if (me is not null) game.SetPlayer(me.Did);
        // The only write the game ever makes: the player pressed like on one post.
        game.OnLike = post => _ = RaiderFeeds.LikeAsync(game, Bridge.LikeActions, post, cts.Token);
        // Reads only: the next post of the liked post's thread, through the session when signed in, else the public AppView.
        IThreadSource threads = me is not null ? new FallbackThreadSource(Bridge.ThreadSource, new PublicThreadSource(http)) : new PublicThreadSource(http);
        game.ThreadWanted = post => _ = RaiderFeeds.ThreadAsync(game, threads, post, cts.Token, onTrouble: m => { lock (game) game.Say(m); });
        game.AvatarWanted = post => _ = AvatarAsync(game, post);
        game.RecordToBeat = Safe(BestScore);
        game.OnBest = score => Safe(() => SaveBestScore(score));
        game.Said = message => Safe(() => Announce(message));
        game.Music = new MusicDirector(SafeCue);
        game.Sfx = new SfxBus((name, pan, step, size) => Safe(() => SfxPlay(name, pan, step, size)));
        lock (game) _game = game;
        var pause = _pause = new PauseGate();
        pause.Hold("tab", _hidden);
        pause.Hold("menu", _menu);

        // Signed in, the wingmen are the player's Following timeline unless ?feed= names another; see FeedPlan.
        var plan = FeedPlan.Choose(feed, me is not null, handle, Bridge.TimelineSource, Bridge.FeedSource, new PublicFeedSource(http), h => new FollowsFeedSource(http, h), m => { lock (game) game.Say(m); });
        if (plan is null) { ShowError("unknown feed (following, discover, foryou or an at:// feed generator uri)"); return; }
        game.PlainOnly = plan.FeedUri == Feeds.Random;
        var runner = new RaiderRunner(game, () => (_cols, _rows), new JsSink()) { ShowFps = showFps, Adaptive = adaptive, Pause = pause };
        var tasks = new List<Task> { runner.RunAsync(cts.Token, TermBusy), RaiderFeeds.WingmenAsync(game, plan.Primary, plan.Fallback, plan.FeedUri, cts.Token, onFallback: ShowError, sourceName: plan.Name, onTrouble: ShowError, pause: pause, onStatus: message => { lock (game) game.Say(message); }) };
        if (darkness != "off") tasks.Add(RaiderFeeds.DarknessAsync(game, http, cts.Token, pause: pause));
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex.Message); }
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
    private static int Safe(Func<int> get) { try { return get(); } catch (JSException e) { ReportJsFailure(e); return 0; } }

    // The avatar is fetched and downsampled to 8x8 in JS (canvas); a CORS failure leaves the tinted fallback and is noted in the console.
    private static async Task AvatarAsync(RaiderGame game, FeedPost post)
    {
        try
        {
            string? csv = await FetchAvatar(post.AuthorDid, post.AvatarUrl);
            if (csv is null) return;
            var px = csv.Split(',').Select(uint.Parse).ToArray();
            if (px.Length == AvatarPixels) lock (game) game.SetAvatar(post.AuthorDid, px);
        }
        catch (Exception ex) when (ex is JSException or FormatException or OverflowException) { Console.Error.WriteLine($"avatar not drawn: {ex.Message}"); }
    }

    /// <summary>The page's tab went to the background (or came back): the game loop, feed paging and both sockets stop, then resume.</summary>
    [JSExport]
    public static void SetHidden(bool hidden)
    {
        _hidden = hidden;
        _pause.Hold("tab", hidden);
    }

    /// <summary>The page's pause menu opened (or closed): the game holds still behind it, as for a hidden tab.</summary>
    [JSExport]
    public static void SetMenu(bool open)
    {
        _menu = open;
        _pause.Hold("menu", open);
    }

    /// <summary>Back to the main menu: the run ends with no game-over song (the page cues the menu theme) and nothing of it is
    /// left to take input. The next <see cref="StartGame"/> builds a fresh game.</summary>
    [JSExport]
    public static void Quit()
    {
        _cts?.Cancel();
        _game = null;
        SetMenu(false);
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
    [JSExport] public static void HoldStart(double col, double row) => WithGame(g => g.Input.HoldStart(col, row));
    [JSExport] public static void HoldEnd() => WithGame(g => g.Input.HoldEnd());
    [JSExport] public static void Tap(double col, double row) => WithGame(g => g.Input.Tap(col, row));
    [JSExport] public static void Bomb() => WithGame(g => g.Input.Bomb());
    [JSExport] public static void Like() => WithGame(g => g.Input.Like());

    private sealed class JsSink : IFrameSink
    {
        public void Write(string frame) => WriteFrame(frame);
    }
}
