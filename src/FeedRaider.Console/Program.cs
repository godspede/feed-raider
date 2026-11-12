using System.Text;
using System.Diagnostics;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Console.Audio;
using TUImation;

const string Usage = """
    usage: FeedRaider.Console [--mode game|spectator] [--handle <handle>] [--feed discover|foryou|random] [--seconds <n>] [--darkness on|off|demo] [--sfx on|off] [--music on|off]
           sound plays on the machine this runs on (over SSH, the remote one); M mutes; --sfx off / --music off turn a part off
           FeedRaider.Console --frames <n> [--out <dir>] [--size <cols>x<rows>] [--every <n>]   headless scripted run, a PNG every <n> frames
           FeedRaider.Console --bench <n> [--size <cols>x<rows>]                                headless render and encode timing over <n> frames
    """;
string? handle = null;
string mode = "game", feedName = "discover";
int? seconds = null;
bool darkness = true, demo = false, sfx = true, music = true;
string? audioDump = null;
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
        case "--sfx" when i + 1 < args.Length && args[i + 1] is "on" or "off": sfx = args[++i] == "on"; break;
        case "--music" when i + 1 < args.Length && args[i + 1] is "on" or "off": music = args[++i] == "on"; break;
        case "--audio-dump" when i + 1 < args.Length: audioDump = args[++i]; break;
        case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out int n): frames = n; i++; break;
        case "--bench" when i + 1 < args.Length && int.TryParse(args[i + 1], out int bn): bench = bn; i++; break;
        case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
        case "--size" when i + 1 < args.Length && args[i + 1].Split('x') is [var fc, var fr] && int.TryParse(fc, out fcols) && int.TryParse(fr, out frows): i++; break;
        case "--every" when i + 1 < args.Length && int.TryParse(args[i + 1], out every): i++; break;
        case "--help" or "-h":
            Console.WriteLine(Usage);
            return 0;
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
    return await GameHost.RunAsync(handle, feedName, seconds, darkness, new AudioOptions(sfx, music, audioDump));
}
if (handle is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

Console.OutputEncoding = new UTF8Encoding(false);
using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), GameHost.StdoutBuffer) { AutoFlush = false };

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (seconds is { } secs) cts.CancelAfter(TimeSpan.FromSeconds(secs));
QuitWatcher.Start();
var quitWait = Task.Run(() =>
{
    WaitHandle.WaitAny([QuitWatcher.Signal.WaitHandle, cts.Token.WaitHandle]);
    if (QuitWatcher.Quit) cts.Cancel();
});

var sink = new GameHost.Sink(stdout);
using var http = new HttpClient { Timeout = GameHost.HttpTimeout };
http.DefaultRequestHeaders.UserAgent.ParseAdd(GameHost.SpectatorUserAgent);
var spectator = new Spectator(http, GameHost.Size, sink) { Hint = "q quits", Darkness = darkness, DarknessDemo = demo };

stdout.Write(GameHost.EnterScreen);
stdout.Flush();
try
{
    await spectator.RunAsync(handle, cts.Token);
}
catch (OperationCanceledException) { }
catch (Exception ex)
{
    stdout.Write(GameHost.RestoreScreen);
    stdout.Flush();
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
stdout.Write(GameHost.LeaveScreen);
stdout.Flush();
if (seconds is not null)
{
    var d = spectator.DarkStats;
    Console.Error.WriteLine($"{spectator.PostsSeen} posts seen");
    if (darkness) Console.Error.WriteLine($"darkness: {d.LabelsSeen} labels arrived, {d.LabelsPassed} passed the filter, {d.AccountLabels} of them account-level spam, {d.PostsFetched} label texts fetched");
    if (darkness) Console.Error.WriteLine($"hidden replies: {d.HiddenNew} newly hidden, {d.HiddenFetched} texts fetched (threadgate stream {spectator.HiddenState})");
    if (darkness) Console.Error.WriteLine($"monsters spawned: {spectator.MonstersSpawned} (labels {spectator.SpawnedFromLabels}, hidden {spectator.SpawnedFromHidden})");
}
return 0;
