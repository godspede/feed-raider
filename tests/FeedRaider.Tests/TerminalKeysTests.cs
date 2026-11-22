using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class TerminalKeysTests
{
    /// <summary>A key held with no release ends inside this many frames, or the hold is counted as never ending.</summary>
    private const int MaxFramesHeld = 10_000;
    /// <summary>The auto-repeats a terminal sends while a key is held down.</summary>
    private const int RepeatsSent = 20;
    private const int MsPerSecond = 1000;
    private const int VkShift = 0x10;
    /// <summary>The ends of the Control Panel's delay (0 to 3) and speed (0 to 31) scales, and a value past both.</summary>
    private const int ShortestDelay = 0, LongestDelay = 3, SlowestSpeed = 0, FastestSpeed = 31, PastBothScales = 99;

    private static List<TermKeyEvent> Parse(string input)
    {
        var p = new TerminalKeyParser();
        var ev = new List<TermKeyEvent>();
        foreach (char c in input) p.Feed((byte)c, ev);
        return ev;
    }

    [Theory]
    [InlineData("\x1b[A", TermKey.Up, KeyAction.Press)]
    [InlineData("\x1b[1;1:1B", TermKey.Down, KeyAction.Press)]
    [InlineData("\x1b[1;1:2C", TermKey.Right, KeyAction.Repeat)]
    [InlineData("\x1b[1;1:3D", TermKey.Left, KeyAction.Release)]
    [InlineData("\x1b[1;2:3A", TermKey.Up, KeyAction.Release)]
    [InlineData("\x1bOA", TermKey.Up, KeyAction.Press)]
    [InlineData("\x1b[32;1:1u", TermKey.Fire, KeyAction.Press)]
    [InlineData("\x1b[32;1:3u", TermKey.Fire, KeyAction.Release)]
    [InlineData("\x1b[97;1:2u", TermKey.Left, KeyAction.Repeat)]
    [InlineData("\x1b[119u", TermKey.Up, KeyAction.Press)]
    [InlineData("\x1b[100;65:3u", TermKey.Right, KeyAction.Release)] // caps lock set
    [InlineData("\x1b[27u", TermKey.Quit, KeyAction.Press)]
    [InlineData("\x1b[99;5u", TermKey.Quit, KeyAction.Press)]
    [InlineData("\x1b[99;5:3u", TermKey.Quit, KeyAction.Release)]
    [InlineData(" ", TermKey.Fire, KeyAction.Press)]
    [InlineData("W", TermKey.Up, KeyAction.Press)]
    [InlineData("f", TermKey.AutoFire, KeyAction.Press)]
    [InlineData("b", TermKey.Bomb, KeyAction.Press)]
    [InlineData("B", TermKey.Bomb, KeyAction.Press)]
    [InlineData("[98u", TermKey.Bomb, KeyAction.Press)]
    [InlineData("e", TermKey.LogLike, KeyAction.Press)]
    [InlineData("r", TermKey.LogRepost, KeyAction.Press)]
    [InlineData("R", TermKey.LogRepost, KeyAction.Press)]
    [InlineData("z", TermKey.ShipLike, KeyAction.Press)]
    [InlineData("Z", TermKey.ShipLike, KeyAction.Press)]
    [InlineData("x", TermKey.ShipRepost, KeyAction.Press)]
    [InlineData("\x1b[122u", TermKey.ShipLike, KeyAction.Press)]
    [InlineData("\x1b[120;1:3u", TermKey.ShipRepost, KeyAction.Release)]
    [InlineData("[", TermKey.MarkUp, KeyAction.Press)]
    [InlineData("]", TermKey.MarkDown, KeyAction.Press)]
    [InlineData("[114u", TermKey.LogRepost, KeyAction.Press)]
    [InlineData("[91u", TermKey.MarkUp, KeyAction.Press)]
    [InlineData("[93;1:3u", TermKey.MarkDown, KeyAction.Release)]
    [InlineData("m", TermKey.Mute, KeyAction.Press)]
    [InlineData("q", TermKey.Quit, KeyAction.Press)]
    [InlineData("\x03", TermKey.Quit, KeyAction.Press)]
    public void ParsesSequence(string input, TermKey key, KeyAction action)
    {
        Assert.Equal([new TermKeyEvent(key, action)], Parse(input));
    }

    [Fact]
    public void IgnoresRepliesAndOtherKeys()
    {
        Assert.Empty(Parse("\x1b[?0u\x1b[?62;4c"));
        Assert.Empty(Parse("\x1b[5~\x1b[1;5:3u\x1b[120;5uy"));
        Assert.Empty(Parse("\x1b[1;1:3H"));
    }

    [Fact]
    public void SequenceSplitAcrossReadsStillParses()
    {
        var p = new TerminalKeyParser();
        var ev = new List<TermKeyEvent>();
        foreach (char c in "\x1b[1;1") p.Feed((byte)c, ev);
        Assert.Empty(ev);
        foreach (char c in ":3C") p.Feed((byte)c, ev);
        Assert.Equal([new TermKeyEvent(TermKey.Right, KeyAction.Release)], ev);
    }

    [Fact]
    public void LoneEscapeIsQuitOnlyAfterFlush()
    {
        var p = new TerminalKeyParser();
        var ev = new List<TermKeyEvent>();
        p.Feed(0x1b, ev);
        Assert.Empty(ev);
        Assert.True(p.Pending);
        p.Flush(ev);
        Assert.Equal([new TermKeyEvent(TermKey.Quit, KeyAction.Press)], ev);
        p.Flush(ev);
        Assert.Single(ev);
    }

    [Fact]
    public void EscapeBeforeATypedKeyKeepsBoth()
    {
        Assert.Equal([new TermKeyEvent(TermKey.Quit, KeyAction.Press), new TermKeyEvent(TermKey.Fire, KeyAction.Press)], Parse("\x1b "));
    }

    [Theory]
    [InlineData("\x1b[?1u", true)]
    [InlineData("\x1b[?u\x1b[?62c", false)]
    [InlineData("\x1b[?62;4c", false)]
    [InlineData("junk\x1b[?0u\x1b[?62c", true)]
    [InlineData("", false)]
    public void DetectsKittyReply(string received, bool kitty) => Assert.Equal(kitty, TerminalKeyParser.IsKittyReply(received));

    [Theory]
    [InlineData((int)ConsoleKey.LeftArrow, true, TermKey.Left, KeyAction.Press)]
    [InlineData((int)ConsoleKey.UpArrow, false, TermKey.Up, KeyAction.Release)]
    [InlineData((int)ConsoleKey.RightArrow, true, TermKey.Right, KeyAction.Press)]
    [InlineData((int)ConsoleKey.DownArrow, false, TermKey.Down, KeyAction.Release)]
    [InlineData((int)ConsoleKey.W, true, TermKey.Up, KeyAction.Press)]
    [InlineData((int)ConsoleKey.A, false, TermKey.Left, KeyAction.Release)]
    [InlineData((int)ConsoleKey.S, true, TermKey.Down, KeyAction.Press)]
    [InlineData((int)ConsoleKey.D, true, TermKey.Right, KeyAction.Press)]
    [InlineData((int)ConsoleKey.Spacebar, false, TermKey.Fire, KeyAction.Release)]
    [InlineData((int)ConsoleKey.F, true, TermKey.AutoFire, KeyAction.Press)]
    [InlineData((int)ConsoleKey.B, true, TermKey.Bomb, KeyAction.Press)]
    [InlineData((int)ConsoleKey.E, true, TermKey.LogLike, KeyAction.Press)]
    [InlineData((int)ConsoleKey.R, true, TermKey.LogRepost, KeyAction.Press)]
    [InlineData((int)ConsoleKey.Z, true, TermKey.ShipLike, KeyAction.Press)]
    [InlineData((int)ConsoleKey.X, true, TermKey.ShipRepost, KeyAction.Press)]
    [InlineData((int)ConsoleKey.Oem4, true, TermKey.MarkUp, KeyAction.Press)]
    [InlineData((int)ConsoleKey.Oem6, true, TermKey.MarkDown, KeyAction.Press)]
    [InlineData((int)ConsoleKey.M, true, TermKey.Mute, KeyAction.Press)]
    [InlineData((int)ConsoleKey.M, false, TermKey.Mute, KeyAction.Release)]
    [InlineData((int)ConsoleKey.Q, true, TermKey.Quit, KeyAction.Press)]
    [InlineData((int)ConsoleKey.Escape, true, TermKey.Quit, KeyAction.Press)]
    public void MapsWin32Records(int vk, bool down, TermKey key, KeyAction action)
    {
        Assert.Equal(new TermKeyEvent(key, action), Win32Keys.Map(vk, down));
    }

    [Fact]
    public void UnmappedWin32KeyIsNull() => Assert.Null(Win32Keys.Map(VkShift, true));

    [Fact]
    public void WindowsTimingFollowsTheControlPanelScales()
    {
        var slowest = KeyRepeatTiming.FromWindows(LongestDelay, SlowestSpeed);
        Assert.Equal(1000, slowest.DelayMs);
        Assert.Equal(400, slowest.IntervalMs);
        var fastest = KeyRepeatTiming.FromWindows(ShortestDelay, FastestSpeed);
        Assert.Equal(250, fastest.DelayMs);
        Assert.Equal(33, fastest.IntervalMs);
        Assert.Equal(KeyRepeatTiming.FromWindows(LongestDelay, FastestSpeed), KeyRepeatTiming.FromWindows(PastBothScales, PastBothScales));
    }

    [Fact]
    public void FirstHoldBridgesTheDelayAndRepeatHoldsOnlyTheGap()
    {
        var t = KeyRepeatTiming.Default;
        Assert.True(t.FirstHoldMs > t.DelayMs + t.IntervalMs);
        Assert.True(t.RepeatHoldMs > t.IntervalMs);
        Assert.True(t.RepeatHoldMs < 2 * t.IntervalMs);
    }

    private static int FramesHeld(InputState inp, GameKey k)
    {
        int n = 0;
        while (inp.Held(k) && n < MaxFramesHeld) { inp.EndFrame(); n++; }
        return n;
    }

    private static int Frames(int ms) => (ms * Tuning.Fps + MsPerSecond - 1) / MsPerSecond;

    [Fact]
    public void FallbackFirstPressHoldsPastTheRepeatDelay()
    {
        var inp = new InputState();
        var t = KeyRepeatTiming.Default;
        inp.Press(GameKey.Right, t);
        Assert.Equal(Frames(t.FirstHoldMs), FramesHeld(inp, GameKey.Right));
        Assert.True(Frames(t.FirstHoldMs) * MsPerSecond / Tuning.Fps >= t.DelayMs);
    }

    [Fact]
    public void FallbackRepeatsKeepTheKeyHeldAndReleaseLandsWithinAnInterval()
    {
        var inp = new InputState();
        var t = KeyRepeatTiming.Default;
        inp.Press(GameKey.Left, t);
        int frames = Frames(t.DelayMs);
        for (int i = 0; i < frames; i++) inp.EndFrame();
        Assert.True(inp.Held(GameKey.Left));
        for (int r = 0; r < RepeatsSent; r++)
        {
            inp.Press(GameKey.Left, t); // auto-repeat each interval
            inp.EndFrame();
            Assert.True(inp.Held(GameKey.Left));
        }
        // After the last repeat, the hold is the repeat hold, not the long first hold.
        Assert.True(FramesHeld(inp, GameKey.Left) <= Frames(t.RepeatHoldMs));
    }

    [Fact]
    public void FallbackRepeatNeverShortensAHold()
    {
        var inp = new InputState();
        var t = KeyRepeatTiming.Default;
        inp.Press(GameKey.Up, t);
        inp.Press(GameKey.Up, t);
        Assert.Equal(Frames(t.FirstHoldMs), FramesHeld(inp, GameKey.Up));
    }

    [Fact]
    public void ReleaseEventsStopTheKeyAtOnce()
    {
        var inp = new InputState();
        var r = new KeyRouter(inp, hasReleases: true, KeyRepeatTiming.Default);
        r.Apply(new(TermKey.Right, KeyAction.Press));
        const int FarPastAnyTimer = 5 * Tuning.Fps;
        for (int i = 0; i < FarPastAnyTimer; i++) inp.EndFrame();
        Assert.True(inp.Held(GameKey.Right));
        r.Apply(new(TermKey.Right, KeyAction.Release));
        Assert.False(inp.Held(GameKey.Right));
    }

    [Fact]
    public void HeldFireStaysDownUntilReleased()
    {
        var inp = new InputState();
        var r = new KeyRouter(inp, true, KeyRepeatTiming.Default);
        r.Apply(new(TermKey.Fire, KeyAction.Press));
        r.Apply(new(TermKey.Fire, KeyAction.Repeat));
        Assert.True(inp.Firing);
        r.Apply(new(TermKey.Fire, KeyAction.Release));
        Assert.False(inp.Firing);
    }

    [Fact]
    public void OneShotsFireOnFreshPressOnly()
    {
        var inp = new InputState();
        var r = new KeyRouter(inp, true, KeyRepeatTiming.Default);
        r.Apply(new(TermKey.AutoFire, KeyAction.Press));
        r.Apply(new(TermKey.AutoFire, KeyAction.Press)); // a Win32 auto-repeat looks like another down
        r.Apply(new(TermKey.AutoFire, KeyAction.Repeat));
        Assert.True(inp.AutoToggle);
        r.Apply(new(TermKey.AutoFire, KeyAction.Release));
        r.Apply(new(TermKey.AutoFire, KeyAction.Press));
        Assert.False(inp.AutoToggle);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MuteFiresOnAFreshPressOnly(bool hasReleases)
    {
        int muted = 0;
        var r = new KeyRouter(new InputState(), hasReleases, KeyRepeatTiming.Default, () => muted++);
        r.Apply(new(TermKey.Mute, KeyAction.Press));
        r.Apply(new(TermKey.Mute, KeyAction.Repeat));
        if (hasReleases)
        {
            r.Apply(new(TermKey.Mute, KeyAction.Release));
            r.Apply(new(TermKey.Mute, KeyAction.Press));
            Assert.Equal(2, muted);
        }
        else Assert.Equal(1, muted);
    }

    [Fact]
    public void MuteWithNoActionIsIgnored()
    {
        var r = new KeyRouter(new InputState(), true, KeyRepeatTiming.Default);
        Assert.False(r.Apply(new(TermKey.Mute, KeyAction.Press)));
    }

    [Fact]
    public void BombAndLogLikeLatch()
    {
        var inp = new InputState();
        var r = new KeyRouter(inp, true, KeyRepeatTiming.Default);
        r.Apply(new(TermKey.Bomb, KeyAction.Press));
        r.Apply(new(TermKey.LogLike, KeyAction.Press));
        Assert.True(inp.BombPressed);
        Assert.Equal(InputState.LikeLatchFrames, inp.LogLikeLatch);
    }

    [Fact]
    public void LogRepostAndTheMarkKeysLatchOnTheInput()
    {
        var inp = new InputState();
        var r = new KeyRouter(inp, true, KeyRepeatTiming.Default);
        r.Apply(new(TermKey.LogRepost, KeyAction.Press));
        r.Apply(new(TermKey.MarkUp, KeyAction.Press));
        r.Apply(new(TermKey.MarkDown, KeyAction.Press));
        Assert.Equal(InputState.LikeLatchFrames, inp.LogRepostLatch);
        Assert.True(inp.MarkUpPressed);
        Assert.True(inp.MarkDownPressed);
    }

    [Fact]
    public void The_z_and_x_keys_latch_a_like_and_a_repost_on_the_post_under_the_ship()
    {
        var inp = new InputState();
        var r = new KeyRouter(inp, true, KeyRepeatTiming.Default);
        r.Apply(new(TermKey.ShipLike, KeyAction.Press));
        r.Apply(new(TermKey.ShipRepost, KeyAction.Press));
        Assert.Equal(InputState.LikeLatchFrames, inp.ShipLikeLatch);
        Assert.Equal(InputState.LikeLatchFrames, inp.ShipRepostLatch);
        Assert.Equal(0, inp.LogLikeLatch);
        Assert.Equal(0, inp.LogRepostLatch);
    }

    [Fact]
    public void QuitIsReported()
    {
        var r = new KeyRouter(new InputState(), true, KeyRepeatTiming.Default);
        Assert.False(r.Apply(new(TermKey.Up, KeyAction.Press)));
        Assert.True(r.Apply(new(TermKey.Quit, KeyAction.Press)));
    }

    [Fact]
    public void LosingFocusReleasesEveryKey()
    {
        var inp = new InputState();
        var r = new KeyRouter(inp, true, KeyRepeatTiming.Default);
        r.Apply(new(TermKey.Up, KeyAction.Press));
        r.Apply(new(TermKey.Fire, KeyAction.Press));
        r.ReleaseAll();
        Assert.False(inp.Held(GameKey.Up));
        Assert.False(inp.Firing);
    }

    [Fact]
    public void WithoutReleasesAKeyUpEventIsIgnoredAndPressesUseTheTimer()
    {
        var inp = new InputState();
        var t = KeyRepeatTiming.Default;
        var r = new KeyRouter(inp, false, t);
        r.Apply(new(TermKey.Down, KeyAction.Press));
        r.Apply(new(TermKey.Down, KeyAction.Release));
        Assert.Equal(Frames(t.FirstHoldMs), FramesHeld(inp, GameKey.Down));
    }

    [Fact]
    public void A_terminal_key_press_hands_firing_back_from_the_pointer()
    {
        var inp = new InputState();
        const int AnyCol = 10, AnyRow = 5;
        inp.Pointer(AnyCol, AnyRow);
        Assert.True(inp.Firing);
        inp.Press(GameKey.Left, KeyRepeatTiming.Default);
        Assert.False(inp.Firing);
    }

    [Fact]
    public void No_byte_above_ascii_is_a_key()
    {
        const int FirstHigh = 0x80, LastHigh = 0xFF;
        for (int b = FirstHigh; b <= LastHigh; b++)
        {
            var ev = new List<TermKeyEvent>();
            new TerminalKeyParser().Feed((byte)b, ev);
            Assert.True(ev.Count == 0, $"byte 0x{b:X2} made a key");
        }
    }
}
