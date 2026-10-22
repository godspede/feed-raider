using System.Text;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

/// <summary>Feed Raider in a terminal: keyboard only, signed out (the like prompt says so). Wingmen come from Discover, or from
/// the follows of --handle.</summary>
static class GameHost
{
    public static async Task<int> RunAsync(string? handle, string feedName, int? seconds, bool darkness)
    {
        string? feed = Feeds.Resolve(feedName);
        if (feed is null) { Console.Error.WriteLine("unknown --feed (discover, foryou or an at:// feed generator uri)"); return 2; }
        Console.OutputEncoding = new UTF8Encoding(false);
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16) { AutoFlush = false };
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("feed-raider/0.1 (read-only)");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        if (seconds is { } secs) cts.CancelAfter(TimeSpan.FromSeconds(secs));

        var game = new RaiderGame((ulong)Environment.TickCount64);
        var runner = new RaiderRunner(game, Size, new Sink(stdout));
        IFeedSource primary = handle is null ? new PublicFeedSource(http) : new FollowsFeedSource(http, handle);
        IFeedSource? fallback = handle is null ? null : new PublicFeedSource(http);

        stdout.Write("\x1b[?1049h\x1b[?25l\x1b[2J"); stdout.Flush();
        var tasks = new List<Task> { runner.RunAsync(cts.Token), RaiderFeeds.WingmenAsync(game, primary, fallback, feed, cts.Token, onFallback: m => { lock (game) game.Say(m); }) };
        if (darkness) tasks.Add(RaiderFeeds.DarknessAsync(game, http, cts.Token).ContinueWith(_ => { }, CancellationToken.None));
        var keys = Task.Run(() => KeyLoop(game, cts));
        try { await Task.WhenAny(Task.WhenAll(tasks), keys); cts.Cancel(); await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { }
        stdout.Write("\x1b[0m\x1b[?25h\x1b[?1049l"); stdout.Flush();
        if (seconds is not null) Console.Error.WriteLine($"score {game.Score}, {game.LiveWavesSpawned} live waves of {game.WavesSpawned}, {runner.Fps:F0} fps, {runner.FrameMs:F2} ms/frame");
        return 0;
    }

    // A terminal reports presses but never releases, so each press holds for KeyHoldMs (see InputState.Press).
    private static void KeyLoop(RaiderGame game, CancellationTokenSource cts)
    {
        while (!cts.IsCancellationRequested)
        {
            if (!Console.KeyAvailable) { Thread.Sleep(5); continue; }
            var k = Console.ReadKey(true);
            lock (game)
            {
                var inp = game.Input;
                switch (k.Key)
                {
                    case ConsoleKey.W or ConsoleKey.UpArrow: inp.Press(GameKey.Up); break;
                    case ConsoleKey.S or ConsoleKey.DownArrow: inp.Press(GameKey.Down); break;
                    case ConsoleKey.A or ConsoleKey.LeftArrow: inp.Press(GameKey.Left); break;
                    case ConsoleKey.D or ConsoleKey.RightArrow: inp.Press(GameKey.Right); break;
                    case ConsoleKey.Spacebar: inp.Press(GameKey.Fire); break;
                    case ConsoleKey.F: inp.ToggleAutoFire(); break;
                    case ConsoleKey.B: inp.Bomb(); break;
                    case ConsoleKey.E: inp.Like(); break;
                    case ConsoleKey.Q or ConsoleKey.Escape: cts.Cancel(); return;
                }
            }
        }
    }

    private static (int, int) Size()
    {
        try { return (Console.WindowWidth, Console.WindowHeight); } catch { return (80, 24); }
    }

    private sealed class Sink(StreamWriter w) : IFrameSink
    {
        public void Write(string frame) { w.Write(frame); w.Flush(); }
    }
}
