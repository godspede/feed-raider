using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class TerminalKeysTests
{
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
    [InlineData("e", TermKey.Like, KeyAction.Press)]
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
        Assert.Empty(Parse("\x1b[5~\x1b[1;5:3u\x1b[120;5uz"));
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
    [InlineData(0x25, true, TermKey.Left, KeyAction.Press)]
    [InlineData(0x26, false, TermKey.Up, KeyAction.Release)]
    [InlineData(0x27, true, TermKey.Right, KeyAction.Press)]
    [InlineData(0x28, false, TermKey.Down, KeyAction.Release)]
    [InlineData(0x57, true, TermKey.Up, KeyAction.Press)]
    [InlineData(0x41, false, TermKey.Left, KeyAction.Release)]
    [InlineData(0x53, true, TermKey.Down, KeyAction.Press)]
    [InlineData(0x44, true, TermKey.Right, KeyAction.Press)]
    [InlineData(0x20, false, TermKey.Fire, KeyAction.Release)]
    [InlineData(0x46, true, TermKey.AutoFire, KeyAction.Press)]
    [InlineData(0x42, true, TermKey.Bomb, KeyAction.Press)]
    [InlineData(0x45, true, TermKey.Like, KeyAction.Press)]
    [InlineData(0x4D, true, TermKey.Mute, KeyAction.Press)]
    [InlineData(0x4D, false, TermKey.Mute, KeyAction.Release)]
    [InlineData(0x51, true, TermKey.Quit, KeyAction.Press)]
    [InlineData(0x1B, true, TermKey.Quit, KeyAction.Press)]
    public void MapsWin32Records(int vk, bool down, TermKey key, KeyAction action)
    {
        Assert.Equal(new TermKeyEvent(key, action), Win32Keys.Map(vk, down));
    }

    [Fact]
    public void UnmappedWin32KeyIsNull() => Assert.Null(Win32Keys.Map(0x10, true)); // Shift

    [Fact]
    public void WindowsTimingFollowsTheControlPanelScales()
    {
        var slowest = KeyRepeatTiming.FromWindows(3, 0);
        Assert.Equal(1000, slowest.DelayMs);
        Assert.Equal(400, slowest.IntervalMs);
        var fastest = KeyRepeatTiming.FromWindows(0, 31);
        Assert.Equal(250, fastest.DelayMs);
        Assert.Equal(33, fastest.IntervalMs);
        Assert.Equal(KeyRepeatTiming.FromWindows(3, 31), KeyRepeatTiming.FromWindows(99, 99));
    }

    [Fact]
    public void FirstHoldBridgesTheDelayAndRepeatHoldsOnlyTheGap()
    {
        var t = new KeyRepeatTiming(500, 40);
        Assert.True(t.FirstHoldMs > t.DelayMs + t.IntervalMs);
        Assert.True(t.RepeatHoldMs > t.IntervalMs);
        Assert.True(t.RepeatHoldMs < 2 * t.IntervalMs + 40);
    }

    private static int FramesHeld(InputState inp, GameKey k)
    {
        int n = 0;
        while (inp.Held(k) && n < 10_000) { inp.EndFrame(); n++; }
        return n;
    }

    private static int Frames(int ms) => (ms * Tuning.Fps + 999) / 1000;

    [Fact]
    public void FallbackFirstPressHoldsPastTheRepeatDelay()
    {
        var inp = new InputState();
        var t = new KeyRepeatTiming(500, 40);
        inp.Press(GameKey.Right, t);
        Assert.Equal(Frames(t.FirstHoldMs), FramesHeld(inp, GameKey.Right));
        Assert.True(Frames(t.FirstHoldMs) * 1000 / Tuning.Fps >= t.DelayMs);
    }

    [Fact]
    public void FallbackRepeatsKeepTheKeyHeldAndReleaseLandsWithinAnInterval()
    {
        var inp = new InputState();
        var t = new KeyRepeatTiming(500, 40);
        inp.Press(GameKey.Left, t);
        int frames = Tuning.Fps / 2; // ~500 ms: the first repeat arrives
        for (int i = 0; i < frames; i++) inp.EndFrame();
        Assert.True(inp.Held(GameKey.Left));
        for (int r = 0; r < 20; r++)
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
        var t = new KeyRepeatTiming(500, 40);
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
        for (int i = 0; i < 5 * Tuning.Fps; i++) inp.EndFrame();
        Assert.True(inp.Held(GameKey.Right)); // far longer than any timer
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
    public void BombAndLikeLatch()
    {
        var inp = new InputState();
        var r = new KeyRouter(inp, true, KeyRepeatTiming.Default);
        r.Apply(new(TermKey.Bomb, KeyAction.Press));
        r.Apply(new(TermKey.Like, KeyAction.Press));
        Assert.True(inp.BombPressed);
        Assert.Equal(InputState.LikeLatchFrames, inp.LikeLatch);
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
        var t = new KeyRepeatTiming(500, 40);
        var r = new KeyRouter(inp, false, t);
        r.Apply(new(TermKey.Down, KeyAction.Press));
        Assert.Equal(Frames(t.FirstHoldMs), FramesHeld(inp, GameKey.Down));
    }
}
