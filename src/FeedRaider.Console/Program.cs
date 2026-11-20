using System.Text;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Console.Audio;
using TUImation;

const int MaxBenchBoss = 10;   // the boss fights the bench can open on
const int DefaultFrameCols = 100, DefaultFrameRows = 46;   // the terminal size a headless run draws at without --size
const int DefaultFrameEvery = 15;   // the frames between two PNGs without --every
const int RefusedExitCode = 2;
string usage = $"""
    usage: FeedRaider.Console [--mode game|spectator] [--handle <handle>] [--feed discover|foryou|random|at://<feed generator uri>] [--seconds <n>] [--darkness on|off|demo] [--sfx on|off] [--music on|off] [--feed-speed {FeedSpeed.Min}-{FeedSpeed.Max}] [--audio-dump <file>] [-h|--help]
           sound plays on the machine this runs on (over SSH, the remote one); M mutes; --sfx off / --music off turn that sound off
           - and + (or =) slow and speed up the feed; --feed-speed {FeedSpeed.Min} to {FeedSpeed.Max} starts it at that step (default {FeedSpeed.Default})
           Esc, Q or Ctrl+C quits (there is no pause)
           FeedRaider.Console --frames <n> [--out <dir>] [--size <cols>x<rows>] [--every <n>]   headless run, a PNG every <n> frames
           FeedRaider.Console --bench <n> [--size <cols>x<rows>] [--boss <1-{MaxBenchBoss}>]                headless render and encode timing over <n> frames (a boss fight with --boss)
    """;
var flagRules = new Dictionary<string, string>
{
    ["--handle"] = "a handle",
    ["--mode"] = "game or spectator",
    ["--feed"] = "discover, foryou, random or an at:// feed generator uri",
    ["--seconds"] = "a whole number above 0",
    ["--darkness"] = "on, off or demo",
    ["--sfx"] = "on or off",
    ["--music"] = "on or off",
    ["--feed-speed"] = $"{FeedSpeed.Min} to {FeedSpeed.Max}",
    ["--audio-dump"] = "a file path",
    ["--frames"] = "a whole number above 0",
    ["--bench"] = "a whole number above 0",
    ["--boss"] = $"1 to {MaxBenchBoss}",
    ["--out"] = "a directory",
    ["--size"] = SizeFlag.Rule,
    ["--every"] = "a whole number above 0",
};
string? handle = null;
string mode = "game", feedName = "discover";
int? seconds = null;
bool darkness = true, demo = false, sfx = true, music = true;
int feedSpeed = FeedSpeed.Default;
string? audioDump = null;
int? frames = null, bench = null;
int benchBoss = 0;
bool gaveOut = false, gaveSize = false, gaveEvery = false;
string outDir = "frames";
int fcols = DefaultFrameCols, frows = DefaultFrameRows, every = DefaultFrameEvery;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--handle" when i + 1 < args.Length: handle = args[++i]; break;
        case "--mode" when i + 1 < args.Length && args[i + 1] is "game" or "spectator": mode = args[++i]; break;
        case "--feed" when i + 1 < args.Length: feedName = args[++i]; break;
        case "--seconds" when i + 1 < args.Length && int.TryParse(args[i + 1], out int s) && s > 0: seconds = s; i++; break;
        case "--darkness" when i + 1 < args.Length && args[i + 1] is "on" or "off" or "demo": darkness = args[i + 1] != "off"; demo = args[++i] == "demo"; break;
        case "--sfx" when i + 1 < args.Length && args[i + 1] is "on" or "off": sfx = args[++i] == "on"; break;
        case "--music" when i + 1 < args.Length && args[i + 1] is "on" or "off": music = args[++i] == "on"; break;
        case "--feed-speed" when i + 1 < args.Length && FeedSpeedFlag.TryParse(args[i + 1], out int sc): feedSpeed = sc; i++; break;
        case "--audio-dump" when i + 1 < args.Length: audioDump = args[++i]; break;
        case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out int n) && n > 0: frames = n; i++; break;
        case "--bench" when i + 1 < args.Length && int.TryParse(args[i + 1], out int bn) && bn > 0: bench = bn; i++; break;
        case "--boss" when i + 1 < args.Length && int.TryParse(args[i + 1], out benchBoss) && benchBoss is >= 1 and <= MaxBenchBoss: i++; break;
        case "--out" when i + 1 < args.Length: outDir = args[++i]; gaveOut = true; break;
        case "--size" when i + 1 < args.Length && SizeFlag.TryParse(args[i + 1], out fcols, out frows): i++; gaveSize = true; break;
        case "--every" when i + 1 < args.Length && int.TryParse(args[i + 1], out every) && every > 0: i++; gaveEvery = true; break;
        case "--help" or "-h":
            Console.WriteLine(usage);
            return 0;
        default:
            if (flagRules.TryGetValue(args[i], out var rule))
                return Refuse(i + 1 < args.Length ? $"invalid value for {args[i]}: {args[i + 1]} ({rule})" : $"missing value for {args[i]} ({rule})");
            return Refuse($"unknown argument: {args[i]}");
    }
}
if (bench is null && benchBoss != 0)
{
    return Refuse("--boss needs --bench");
}
if (frames is null && bench is null && gaveSize)
{
    return Refuse("--size needs --frames or --bench");
}
if (frames is null && (gaveOut || gaveEvery))
{
    return Refuse("--out and --every need --frames");
}
if (audioDump is not null && (mode != "game" || frames is not null || bench is not null))
{
    return Refuse("--audio-dump needs the game, not --mode spectator, --frames or --bench");
}
if (audioDump is not null && !sfx && !music)
{
    return Refuse("--audio-dump needs --sfx or --music on");
}
if (bench is { } nbench) return Bench.Run(nbench, fcols, frows, benchBoss);
if (frames is { } nframes)
{
    if (WhyNot(() => Directory.CreateDirectory(outDir)) is { } noDir)
    {
        return Refuse($"cannot create the --out directory {outDir}: {noDir}");
    }
    return FramesRun.Run(nframes, outDir, fcols, frows, every);
}
if (mode == "game")
{
    if (Feeds.Resolve(feedName) is not { } feedUri)
    {
        return Refuse($"invalid value for --feed: {feedName} (discover, foryou, random or an at:// feed generator uri)");
    }
    if (audioDump is not null && WhyNot(() => File.Create(audioDump).Dispose()) is { } noFile)
    {
        return Refuse($"cannot write the --audio-dump file {audioDump}: {noFile}");
    }
    return await GameHost.RunAsync(handle, feedUri, seconds, darkness, new AudioOptions(sfx, music, audioDump), feedSpeed);
}
if (handle is null)
{
    return Refuse("--mode spectator needs --handle <handle>");
}

Console.OutputEncoding = new UTF8Encoding(false);
using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), GameHost.StdoutBuffer) { AutoFlush = false };

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
if (seconds is { } secs) cts.CancelAfter(TimeSpan.FromSeconds(secs));
QuitWatcher.Start();
_ = Task.Run(() =>
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
catch (OperationCanceledException)
{
    // --seconds and the quit key end the run by cancelling the token, so this is the normal exit.
}
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
    var dark = spectator.DarkStats;
    Console.Error.WriteLine($"{spectator.PostsSeen} posts seen");
    if (darkness) Console.Error.WriteLine($"darkness: {dark.LabelsSeen} labels arrived, {dark.LabelsPassed} passed the filter, {dark.AccountLabels} of them account-level spam, {dark.PostsFetched} label texts fetched, {dark.FetchFailures} fetches failed");
    if (darkness) Console.Error.WriteLine($"hidden replies: {dark.HiddenNew} newly hidden, {dark.HiddenFetched} texts fetched (threadgate stream {spectator.HiddenState})");
    if (darkness) Console.Error.WriteLine($"enemies arrived: {spectator.EnemiesSpawned} (labels {spectator.SpawnedFromLabels}, hidden {spectator.SpawnedFromHidden})");
}
return 0;

int Refuse(string message)
{
    Console.Error.WriteLine($"error: {message}");
    Console.Error.WriteLine(usage);
    return RefusedExitCode;
}

// Why the path a flag names cannot be made, or null once `make` has made it.
static string? WhyNot(Action make)
{
    try
    {
        make();
        return null;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        return ex.Message;
    }
}
