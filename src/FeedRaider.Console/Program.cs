using System.Text;
using System.Diagnostics;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using TUImation;

const string Usage = """
    usage: FeedRaider.Console [--mode game|spectator] [--handle <handle>] [--feed discover|foryou|random] [--seconds <n>] [--darkness on|off|demo]
           FeedRaider.Console --frames <n> [--out <dir>] [--size <cols>x<rows>] [--every <n>]   headless scripted run, a PNG every <n> frames
           FeedRaider.Console --bench <n> [--size <cols>x<rows>]                                headless render and encode timing over <n> frames
    """;
string? handle = null;
string mode = "game", feedName = "discover";
int? seconds = null;
bool darkness = true, demo = false;
int? frames = null, bench = null;
string outDir = "frames";
int fcols = 100, frows = 46, every = 15;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--handle" when i + 1 < args.Length: handle = args[++i]; break;
        case "--mode" when i + 1 < args.Length && args[i + 1] is "game" or "spectator": mode = args[++i]; break;
        case "--feed" when i + 1 < args.Length: feedName = args[++i]; break;
        case "--seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out int s): seconds = s; i++; break;
        case "--darkness" when i + 1 < args.Length && args[i + 1] is "on" or "off" or "demo": darkness = args[i + 1] != "off"; demo = args[++i] == "demo"; break;
        case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out int n): frames = n; i++; break;
        case "--bench" when i + 1 < args.Length && int.TryParse(args[i + 1], out int bn): bench = bn; i++; break;
        case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
        case "--size" when i + 1 < args.Length && args[i + 1].Split('x') is [var fc, var fr] && int.TryParse(fc, out fcols) && int.TryParse(fr, out frows): i++; break;
        case "--every" when i + 1 < args.Length && int.TryParse(args[i + 1], out every): i++; break;
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
if (bench is { } nbench) return Bench.Run(nbench, fcols, frows);
if (frames is { } nframes)
{
    Directory.CreateDirectory(outDir);
    return DemoRun.Run(nframes, outDir, fcols, frows, every);
}
if (mode == "game")
{
    return await GameHost.RunAsync(handle, feedName, seconds, darkness);
}
if (handle is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

Console.OutputEncoding = new UTF8Encoding(false);
using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16) { AutoFlush = false };

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (seconds is { } secs) cts.CancelAfter(TimeSpan.FromSeconds(secs));
QuitWatcher.Start();
var quitPoll = Task.Run(async () =>
{
    while (!cts.IsCancellationRequested)
    {
        if (QuitWatcher.Quit) { cts.Cancel(); return; }
        await Task.Delay(50);
    }
});

(int, int) Size()
{
    try { return (Console.WindowWidth, Console.WindowHeight); }
    catch { return (80, 24); }
}

var sink = new ConsoleSink(stdout);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("feed-raider/0.1 (read-only spectator)");
var spectator = new Spectator(http, Size, sink) { Hint = "q quits", Darkness = darkness, DarknessDemo = demo };

stdout.Write("\x1b[?1049h\x1b[?25l\x1b[2J");
stdout.Flush();
int exit = 0;
try
{
    await spectator.RunAsync(handle, cts.Token);
}
catch (OperationCanceledException) { }
catch (Exception ex)
{
    exit = 1;
    stdout.Write("\x1b[?25h\x1b[?1049l");
    stdout.Flush();
    Console.Error.WriteLine($"error: {ex.Message}");
    return exit;
}
stdout.Write("\x1b[0m\x1b[?25h\x1b[?1049l");
stdout.Flush();
if (seconds is not null)
{
    var d = spectator.DarkStats;
    Console.Error.WriteLine($"{spectator.PostsSeen} posts seen");
    if (darkness) Console.Error.WriteLine($"darkness: {d.LabelsSeen} labels arrived, {d.LabelsPassed} passed the filter, {d.AccountLabels} of them account-level spam, {d.PostsFetched} label texts fetched");
    if (darkness) Console.Error.WriteLine($"hidden replies: {d.HiddenNew} newly hidden, {d.HiddenFetched} texts fetched (threadgate stream {spectator.HiddenState})");
    if (darkness) Console.Error.WriteLine($"monsters spawned: {spectator.MonstersSpawned} (labels {spectator.SpawnedFromLabels}, hidden {spectator.SpawnedFromHidden})");
}
return exit;

sealed class ConsoleSink(StreamWriter w) : IFrameSink
{
    public void Write(string frame)
    {
        w.Write(frame);
        w.Flush();
    }
}
