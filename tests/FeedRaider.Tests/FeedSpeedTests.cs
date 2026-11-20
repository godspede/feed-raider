using System.Diagnostics;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The feed-speed option as the engine and the terminal host see it. The page's half is in web/test.
public class FeedSpeedTests
{
    private const int ExitTimeoutMs = 30_000;
    /// <summary>The exit code the terminal build gives for a command line it refuses.</summary>
    private const int RefusedExitCode = 2;
    private const int ScrCols = 130, ScrRows = 46;
    /// <summary>A like count for a post that is nothing special.</summary>
    private const int OrdinaryLikes = 10;

    private static FeedPost Post(int n) =>
        new($"at://did:plc:s{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:s{n}", $"s{n}.test", $"post number {n} says a few ordinary words and ends with tail{n}word", OrdinaryLikes, 0, 0, null, null, DisplayName: "Display Name");

    private static RaiderGame New()
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(ScrCols, ScrRows));
        return g;
    }

    [Theory]
    [InlineData(1, 1, "Feed speed 1 of 5")]
    [InlineData(2, 2, "Feed speed 2 of 5")]
    [InlineData(5, 5, "Feed speed 5 of 5")]
    [InlineData(0, 1, "Feed speed 1 of 5")]
    [InlineData(-7, 1, "Feed speed 1 of 5")]
    [InlineData(9, 5, "Feed speed 5 of 5")]
    public void Setting_the_speed_clamps_it_and_shows_the_toast(int asked, int step, string toast)
    {
        var g = New();
        g.SetFeedSpeed(asked);
        Assert.Equal(step, g.FeedSpeed);
        Assert.Equal(toast, g.Toast);
    }

    [Fact]
    public void Setting_the_property_at_start_up_shows_no_toast()
    {
        var g = New();
        g.FeedSpeed = FeedSpeed.Max;
        Assert.Null(g.Toast);
    }

    [Fact]
    public void A_speed_set_before_any_card_exists_applies_at_once()
    {
        var g = New();
        g.FeedSpeed = FeedSpeed.Max;
        Assert.Equal(FeedSpeed.FramesPerRow(FeedSpeed.Max), g.Cards.FramesPerRow);
        g.Posts.Offer(Post(1));
        g.Step();
        var card = Assert.Single(g.Cards.Cards);
        int entered = card.Top;
        // the first row step comes at the set speed's pace, not the default's
        for (int i = 0; i < FeedSpeed.FramesPerRow(FeedSpeed.Max); i++) g.Step();
        Assert.Equal(entered + 1, card.Top);
    }

    [Fact]
    public void A_speed_changed_with_cards_on_the_field_waits_for_the_next_row_step_and_leaves_places_alone()
    {
        var g = New();
        g.Posts.Offer(Post(1));
        const int onFieldFrames = 60, rowStepLimitFrames = 40;
        for (int i = 0; i < onFieldFrames; i++) g.Step();
        var card = g.Cards.Cards[0];
        int top = card.Top;
        g.SetFeedSpeed(FeedSpeed.Max);
        Assert.Equal(top, card.Top);
        Assert.Equal(FeedSpeed.FramesPerRow(FeedSpeed.Default), g.Cards.FramesPerRow);
        for (int i = 0; i < rowStepLimitFrames && g.Cards.FramesPerRow != FeedSpeed.FramesPerRow(FeedSpeed.Max); i++) g.Step();
        Assert.Equal(FeedSpeed.FramesPerRow(FeedSpeed.Max), g.Cards.FramesPerRow);
        int at = card.Top;
        for (int i = 0; i < FeedSpeed.FramesPerRow(FeedSpeed.Max); i++) g.Step();
        Assert.Equal(at + 1, card.Top);
    }

    /// <summary>The posts and the frames of one recorded run.</summary>
    private const int RunPosts = 4, RunFrames = 900;

    private static List<string> Run(Action<RaiderGame, int>? each)
    {
        var g = New();
        for (int i = 1; i <= RunPosts; i++) g.Posts.Offer(Post(i));
        var seen = new List<string>();
        for (int t = 0; t < RunFrames; t++)
        {
            each?.Invoke(g, t);
            g.Step();
            seen.Add(string.Join(",", g.Cards.Cards.Select(c => $"{c.Post.Uri[^1]}@{c.Top}")));
        }
        return seen;
    }

    [Fact]
    public void Two_runs_with_the_same_seed_and_speed_changes_at_the_same_ticks_match()
    {
        static void Changes(RaiderGame g, int t)
        {
            if (t == 100) g.SetFeedSpeed(5);
            if (t == 400) g.SetFeedSpeed(1);
            if (t == 700) g.SetFeedSpeed(4);
        }
        var a = Run(Changes);
        Assert.Equal(a, Run(Changes));
        Assert.NotEqual(a, Run(null));
    }

    [Theory]
    [InlineData("-", TermKey.SlowerFeed)]
    [InlineData("+", TermKey.FasterFeed)]
    [InlineData("=", TermKey.FasterFeed)]
    public void The_terminal_reads_minus_plus_and_equals(string typed, TermKey key)
    {
        var p = new TerminalKeyParser();
        var ev = new List<TermKeyEvent>();
        foreach (char c in typed) p.Feed((byte)c, ev);
        Assert.Equal([new TermKeyEvent(key, KeyAction.Press)], ev);
    }

    [Theory]
    [InlineData("\x1b[45u", TermKey.SlowerFeed, KeyAction.Press)]
    [InlineData("\x1b[61;1:1u", TermKey.FasterFeed, KeyAction.Press)]
    [InlineData("\x1b[61;2u", TermKey.FasterFeed, KeyAction.Press)]   // the + key is shift and =
    [InlineData("\x1b[45;1:3u", TermKey.SlowerFeed, KeyAction.Release)]
    public void The_terminal_reads_the_speed_keys_from_the_kitty_protocol(string input, TermKey key, KeyAction action)
    {
        var p = new TerminalKeyParser();
        var ev = new List<TermKeyEvent>();
        foreach (char c in input) p.Feed((byte)c, ev);
        Assert.Equal([new TermKeyEvent(key, action)], ev);
    }

    [Fact]
    public void Ctrl_minus_is_not_a_speed_key()
    {
        var p = new TerminalKeyParser();
        var ev = new List<TermKeyEvent>();
        foreach (char c in "\x1b[45;5u") p.Feed((byte)c, ev);
        Assert.Empty(ev);
    }

    [Theory]
    [InlineData(0xBD, TermKey.SlowerFeed)]   // the minus key
    [InlineData(0x6D, TermKey.SlowerFeed)]   // keypad minus
    [InlineData(0xBB, TermKey.FasterFeed)]   // the = and + key
    [InlineData(0x6B, TermKey.FasterFeed)]   // keypad plus
    public void Windows_maps_the_speed_keys(int vk, TermKey key) => Assert.Equal(new TermKeyEvent(key, KeyAction.Press), Win32Keys.Map(vk, true));

    [Fact]
    public void The_router_steps_the_speed_once_per_fresh_press()
    {
        var steps = new List<int>();
        var r = new KeyRouter(new InputState(), true, KeyRepeatTiming.Default, onFeedSpeed: steps.Add);
        r.Apply(new(TermKey.FasterFeed, KeyAction.Press));
        r.Apply(new(TermKey.FasterFeed, KeyAction.Repeat));
        r.Apply(new(TermKey.FasterFeed, KeyAction.Release));
        r.Apply(new(TermKey.SlowerFeed, KeyAction.Press));
        r.Apply(new(TermKey.SlowerFeed, KeyAction.Release));
        Assert.Equal([1, -1], steps);
    }

    [Fact]
    public void The_game_steps_by_one_and_stays_at_the_ends()
    {
        var g = New();
        var r = new KeyRouter(g.Input, false, KeyRepeatTiming.Default, onFeedSpeed: d => g.SetFeedSpeed(g.FeedSpeed + d));
        // more presses than there are steps to either end, so the last ones press against it
        const int slowerPresses = 4, fasterPresses = 9;
        for (int i = 0; i < slowerPresses; i++) r.Apply(new(TermKey.SlowerFeed, KeyAction.Press));
        Assert.Equal(FeedSpeed.Min, g.FeedSpeed);
        Assert.Equal("Feed speed 1 of 5", g.Toast);
        for (int i = 0; i < fasterPresses; i++) r.Apply(new(TermKey.FasterFeed, KeyAction.Press));
        Assert.Equal(FeedSpeed.Max, g.FeedSpeed);
        Assert.Equal("Feed speed 5 of 5", g.Toast);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData("5", 5)]
    public void The_feed_speed_flag_takes_one_to_five(string text, int step)
    {
        Assert.True(FeedSpeedFlag.TryParse(text, out int got));
        Assert.Equal(step, got);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("9")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void The_feed_speed_flag_refuses_anything_else(string? text) => Assert.False(FeedSpeedFlag.TryParse(text, out _));

    [Theory]
    [InlineData(Tuning.FieldMinCols, Tuning.FieldMinRows)]
    [InlineData(SizeFlag.MaxCols, SizeFlag.MaxRows)]
    public void The_size_flag_takes_the_smallest_and_the_largest_size(int cols, int rows)
    {
        Assert.True(SizeFlag.TryParse($"{cols}x{rows}", out int gotCols, out int gotRows));
        Assert.Equal((cols, rows), (gotCols, gotRows));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("40")]
    [InlineData("40x")]
    [InlineData("40x20x3")]
    [InlineData("-40x20")]
    [InlineData("2147483647x2147483647")]
    [InlineData("99999999999x20")]
    [InlineData("")]
    [InlineData(null)]
    public void The_size_flag_refuses_anything_that_is_not_a_size_in_range(string? text) => Assert.False(SizeFlag.TryParse(text, out _, out _));

    [Fact]
    public async Task Feed_speed_9_prints_the_usage_line_and_exits_with_2()
    {
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" --feed-speed 9") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        Task<string> err = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
        Assert.Equal(RefusedExitCode, p.ExitCode);
        Assert.Contains("--feed-speed", await err);
    }

    [Theory]
    [InlineData("--seconds", "-5")]
    [InlineData("--seconds", "0")]
    [InlineData("--frames", "-1")]
    [InlineData("--bench", "0")]
    [InlineData("--every", "0")]
    [InlineData("--size", "0x10")]
    [InlineData("--boss", "99")]
    [InlineData("--feed", "nonsense")]
    [InlineData("--feed-speed", "9")]
    [InlineData("--sfx", "maybe")]
    public async Task A_bad_value_exits_with_2_and_names_its_flag_on_stderr(string flag, string value)
    {
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {flag} {value}") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        Task<string> err = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
        Assert.Equal(RefusedExitCode, p.ExitCode);
        Assert.StartsWith($"error: invalid value for {flag}: {value}", await err);
    }

    [Theory]
    [InlineData("--seconds", "error: missing value for --seconds (a whole number above 0)")]
    [InlineData("--bogus", "error: unknown argument: --bogus")]
    public async Task A_missing_value_or_an_unknown_argument_exits_with_2_and_shows_the_usage(string flags, string message)
    {
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {flags}") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        Task<string> err = p.StandardError.ReadToEndAsync();
        Task<string> output = p.StandardOutput.ReadToEndAsync();
        Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
        Assert.Equal(RefusedExitCode, p.ExitCode);
        string errText = await err;
        Assert.StartsWith(message + Environment.NewLine + "usage: FeedRaider.Console", errText);
        Assert.Equal("", await output);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public async Task Help_prints_a_usage_that_names_the_help_flag_and_exits_with_0(string flag)
    {
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {flag}") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        Task<string> output = p.StandardOutput.ReadToEndAsync();
        Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
        Assert.Equal(0, p.ExitCode);
        Assert.Contains("[-h|--help]", await output);
    }

    [Fact]
    public async Task Help_names_the_keys_that_quit_and_says_there_is_no_pause()
    {
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" --help") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        Task<string> output = p.StandardOutput.ReadToEndAsync();
        Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
        Assert.Equal(0, p.ExitCode);
        Assert.Contains("Esc, Q or Ctrl+C quits (there is no pause)", await output);
    }

    [Fact]
    public async Task Spectator_mode_without_a_handle_exits_with_2_and_says_it_needs_one()
    {
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" --mode spectator") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        Task<string> err = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
        Assert.Equal(RefusedExitCode, p.ExitCode);
        Assert.StartsWith("error: --mode spectator needs --handle <handle>", await err);
    }

    [Theory]
    [InlineData("--boss 3", "error: --boss needs --bench")]
    [InlineData("--out frames", "error: --out and --every need --frames")]
    [InlineData("--every 5", "error: --out and --every need --frames")]
    [InlineData("--size 80x24", "error: --size needs --frames or --bench")]
    [InlineData("--audio-dump dump.wav --frames 3", "error: --audio-dump needs the game, not --mode spectator, --frames or --bench")]
    [InlineData("--audio-dump dump.wav --bench 3", "error: --audio-dump needs the game, not --mode spectator, --frames or --bench")]
    [InlineData("--audio-dump dump.wav --mode spectator --handle someone.example", "error: --audio-dump needs the game, not --mode spectator, --frames or --bench")]
    [InlineData("--audio-dump dump.wav --sfx off --music off", "error: --audio-dump needs --sfx or --music on")]
    public async Task A_flag_without_what_it_needs_exits_with_2_and_shows_the_usage(string flags, string message)
    {
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {flags}") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        Task<string> err = p.StandardError.ReadToEndAsync();
        Task<string> output = p.StandardOutput.ReadToEndAsync();
        Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
        Assert.Equal(RefusedExitCode, p.ExitCode);
        string errText = await err;
        Assert.StartsWith(message, errText);
        Assert.Contains("usage: FeedRaider.Console", errText);
        Assert.Equal("", await output);
    }

    [Theory]
    [InlineData("--frames 5 --out", "error: cannot create the --out directory")]
    [InlineData("--audio-dump", "error: cannot write the --audio-dump file")]
    public async Task A_path_under_a_file_exits_with_2_and_names_the_flag_without_a_stack_trace(string flags, string message)
    {
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        string file = Path.GetTempFileName();
        string under = Path.Combine(file, "under");
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {flags} \"{under}\"") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            Task<string> err = p.StandardError.ReadToEndAsync();
            Task<string> output = p.StandardOutput.ReadToEndAsync();
            Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
            Assert.Equal(RefusedExitCode, p.ExitCode);
            string errText = await err;
            Assert.StartsWith($"{message} {under}: ", errText);
            Assert.Contains("usage: FeedRaider.Console", errText);
            Assert.DoesNotContain("   at ", errText);
            Assert.Equal("", await output);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("--frames 1", Tuning.FieldMinCols - 1, Tuning.FieldMinRows)]
    [InlineData("--frames 1", Tuning.FieldMinCols, Tuning.FieldMinRows - 1)]
    [InlineData("--bench 1", Tuning.FieldMinCols - 1, Tuning.FieldMinRows)]
    [InlineData("--bench 1", Tuning.FieldMinCols, Tuning.FieldMinRows - 1)]
    [InlineData("--frames 1", SizeFlag.MaxCols + 1, SizeFlag.MaxRows)]
    [InlineData("--bench 1", SizeFlag.MaxCols, SizeFlag.MaxRows + 1)]
    public async Task A_size_one_outside_the_smallest_or_the_largest_exits_with_2_shows_the_usage_and_writes_nothing(string run, int cols, int rows)
    {
        string size = $"{cols}x{rows}";
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string outFlag = run.StartsWith("--frames") ? $" --out \"{outDir}\"" : "";
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {run}{outFlag} --size {size}") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        Task<string> err = p.StandardError.ReadToEndAsync();
        Task<string> output = p.StandardOutput.ReadToEndAsync();
        Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
        Assert.Equal(RefusedExitCode, p.ExitCode);
        string errText = await err;
        Assert.StartsWith($"error: invalid value for --size: {size} ({SizeFlag.Rule})" + Environment.NewLine + "usage: FeedRaider.Console", errText);
        Assert.DoesNotContain("   at ", errText);
        Assert.Equal("", await output);
        Assert.False(Directory.Exists(outDir));
    }

    [Theory]
    [InlineData("--frames 1 --every 1", "1 frames,", Tuning.FieldMinCols, Tuning.FieldMinRows)]
    [InlineData("--bench 1", "bench ", Tuning.FieldMinCols, Tuning.FieldMinRows)]
    [InlineData("--frames 1 --every 1", "1 frames,", SizeFlag.MaxCols, SizeFlag.MaxRows)]
    [InlineData("--bench 1", "bench ", SizeFlag.MaxCols, SizeFlag.MaxRows)]
    public async Task The_smallest_and_the_largest_size_run_and_exit_with_0(string run, string firstOutput, int cols, int rows)
    {
        string size = $"{cols}x{rows}";
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        string outDir = Directory.CreateTempSubdirectory("frames").FullName;
        string outFlag = run.StartsWith("--frames") ? $" --out \"{outDir}\"" : "";
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" {run}{outFlag} --size {size}") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            Task<string> err = p.StandardError.ReadToEndAsync();
            Task<string> output = p.StandardOutput.ReadToEndAsync();
            Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
            Assert.Equal("", await err);
            Assert.Equal(0, p.ExitCode);
            Assert.StartsWith(firstOutput, await output);
        }
        finally
        {
            Directory.Delete(outDir, true);
        }
    }

    [Fact]
    public async Task The_frames_result_line_goes_to_stdout_and_stderr_stays_empty()
    {
        const int FrameCount = 2;
        string dll = typeof(FeedSpeedFlag).Assembly.Location;
        string outDir = Directory.CreateTempSubdirectory("frames").FullName;
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" --frames {FrameCount} --out \"{outDir}\" --every 1") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            Task<string> err = p.StandardError.ReadToEndAsync();
            Task<string> output = p.StandardOutput.ReadToEndAsync();
            Assert.True(p.WaitForExit(ExitTimeoutMs), "the terminal build did not exit");
            Assert.Equal(0, p.ExitCode);
            Assert.StartsWith($"{FrameCount} frames,", await output);
            Assert.Equal("", await err);
        }
        finally
        {
            Directory.Delete(outDir, true);
        }
    }
}
