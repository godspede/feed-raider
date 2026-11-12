using System.Text;
using FeedRaider.Console.Audio;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

/// <summary>Feed Raider in a terminal: keyboard only, signed out (the like prompt says so). Wingmen come from Discover, or from
/// the follows of --handle.</summary>
static class GameHost
{
    public static async Task<int> RunAsync(string? handle, string feedName, int? seconds, bool darkness, AudioOptions audioOptions)
    {
        string? feed = Feeds.Resolve(feedName);
        if (feed is null) { Console.Error.WriteLine("unknown --feed (discover, foryou, random or an at:// feed generator uri)"); return 2; }
        Console.OutputEncoding = new UTF8Encoding(false);
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), StdoutBuffer) { AutoFlush = false };
        using var http = new HttpClient { Timeout = HttpTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        if (seconds is { } secs) cts.CancelAfter(TimeSpan.FromSeconds(secs));

        var game = new RaiderGame((ulong)Environment.TickCount64);
        using var audio = AudioEngine.TryStart(audioOptions);
        if (audio is not null)
        {
            game.Music = new MusicDirector(audio.Cue);
            game.Sfx = new SfxBus(audio.Sfx);
        }
        game.PlainOnly = handle is null && feed == Feeds.Random;
        var threads = new PublicThreadSource(http);
        game.ThreadWanted = post => _ = RaiderFeeds.ThreadAsync(game, threads, post, cts.Token, onTrouble: m => { lock (game) game.Say(m); });
        var runner = new RaiderRunner(game, Size, new Sink(stdout));
        var pub = new PublicFeedSource(http);
        IFeedSource primary = handle is not null ? new FollowsFeedSource(http, handle) : feed == Feeds.Random ? new RandomFeedSource(pub, pub, m => { lock (game) game.Say(m); }) : pub;
        IFeedSource? fallback = handle is null ? null : new PublicFeedSource(http);

        using var keyInput = KeyInput.Open(stdout);
        // A crash skips the finally below; the exit handler still puts the keyboard back.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => keyInput.Dispose();
        stdout.Write(EnterScreen); stdout.Flush();
        var tasks = new List<Task> { runner.RunAsync(cts.Token), RaiderFeeds.WingmenAsync(game, primary, fallback, feed, cts.Token, onFallback: m => { lock (game) game.Say(m); }) };
        if (darkness) tasks.Add(RaiderFeeds.DarknessAsync(game, http, cts.Token).ContinueWith(t =>
        {
            // The game plays on without the darkness feed, so a fault is reported on screen instead of ending the run.
            if (t.IsFaulted) lock (game) game.Say($"darkness feed stopped: {t.Exception.GetBaseException().Message}");
        }, CancellationToken.None));
        // The input blocks on a read, so a quit from elsewhere leaves this loop parked on a background thread until the process exits.
        var keys = Task.Factory.StartNew(() => keyInput.Loop(game, cts, audio is null ? null : () => game.Say(audio.ToggleMute() ? "sound off (M)" : "sound on (M)")), TaskCreationOptions.LongRunning);
        try
        {
            await Task.WhenAny(Task.WhenAll(tasks), keys);
            cts.Cancel();
            await Task.WhenAll(tasks).WaitAsync(ShutdownGrace);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }   // a task that outlives the grace period is abandoned: the process is ending
        finally { keyInput.Dispose(); stdout.Write(LeaveScreen); stdout.Flush(); }
        if (seconds is not null) Console.Error.WriteLine($"score {game.Score}, {game.LiveWavesSpawned} live waves of {game.WavesSpawned}, {runner.Fps:F0} fps, {runner.FrameMs:F2} ms/frame");
        return 0;
    }

    public const int StdoutBuffer = 1 << 16;
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(2);   // how long the feed tasks get to stop after a quit

    public const string UserAgent = "feed-raider/0.1 (read-only)";
    public const string SpectatorUserAgent = "feed-raider/0.1 (read-only spectator)";

    // Alternate screen on, cursor hidden, screen cleared; and the way back (reset colours, cursor shown, main screen).
    public const string EnterScreen = "\x1b[?1049h\x1b[?25l\x1b[2J";
    public const string RestoreScreen = "\x1b[?25h\x1b[?1049l";
    public const string LeaveScreen = "\x1b[0m" + RestoreScreen;

    private const int FallbackCols = 80, FallbackRows = 24;

    /// <summary>The window size, or 80x24 when there is no console to ask (output redirected); the game still renders to a pipe.</summary>
    public static (int, int) Size()
    {
        try { return (Console.WindowWidth, Console.WindowHeight); }
        catch (IOException) { return (FallbackCols, FallbackRows); }
    }

    /// <summary>Writes each frame to the terminal and flushes at once.</summary>
    public sealed class Sink(StreamWriter w) : IFrameSink
    {
        public void Write(string frame) { w.Write(frame); w.Flush(); }
    }
}
