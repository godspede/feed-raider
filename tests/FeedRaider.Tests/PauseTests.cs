using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

/// <summary>A hidden browser tab pauses the game loop, the feed pager and the socket reads; coming back resumes them.</summary>
public class PauseTests
{
    // Pausing has no signal to wait for, so the tests sleep: SettleMs for a tick already in flight to finish, then WatchMs (or
    // LongWatchMs, over the frame sink) to see that nothing moves.
    private const int SettleMs = 100, WatchMs = 300, LongWatchMs = 500, CancelAfterMs = 50;
    private const int DoneSeconds = 10, ResumeSeconds = 5;
    // RanTicks is enough ticks to say the loop is running. MaxResumeTicks is over a second of ticks at Tuning.Fps: a slow box
    // stays under it, and a loop that came back spinning free of its timer does not.
    private const long RanTicks = 3, MaxResumeTicks = 40;
    // the reasons a page holds the game paused
    private const string Asleep = "asleep", Tab = "tab", Menu = "menu";

    private sealed class CountSink : IFrameSink
    {
        public int Frames;
        public void Write(string frame) => Interlocked.Increment(ref Frames);
    }

    private sealed class CountingFeed : IFeedSource
    {
        public int Calls;
        public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new FeedPage([], null));
        }
    }

    // Hands out timers that tick only when a test says so, and counts how many were made and how many are still alive.
    private sealed class CountingClock : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        public int Made { get { lock (_timers) return _timers.Count; } }
        public int Alive { get { lock (_timers) return _timers.Count(timer => !timer.Disposed); } }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            lock (_timers) _timers.Add(timer);
            return timer;
        }

        public void TickNewest()
        {
            ManualTimer newest;
            lock (_timers) newest = _timers[^1];
            newest.Tick();
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private volatile bool _disposed;
        public bool Disposed => _disposed;
        public void Tick() => callback(state);
        // the runner never changes its timer's period
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Runner_keeps_no_timer_alive_while_the_gate_is_held_and_ends_when_cancelled_there()
    {
        var gate = new PauseGate();
        var clock = new CountingClock();
        var runner = new RaiderRunner(new RaiderGame(1), () => (60, 20), new CountSink()) { Adaptive = false, Pause = gate, PacingClock = clock };
        gate.Hold(Asleep, true);
        using var cts = new CancellationTokenSource();
        var run = runner.RunAsync(cts.Token);
        Assert.False(run.IsCompleted);
        Assert.Equal(0, clock.Made);

        gate.Hold(Asleep, false);
        await Wait.UntilAsync(() => clock.Alive == 1, "the open gate to make a timer");

        gate.Hold(Asleep, true);
        clock.TickNewest();   // the loop sees the hold on the tick after it
        await Wait.UntilAsync(() => clock.Alive == 0, "the held gate to dispose the timer");
        Assert.Equal(1, clock.Made);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
        Assert.Equal(1, clock.Made);
    }

    [Fact]
    public async Task Runner_without_a_gate_keeps_one_timer_for_the_whole_run()
    {
        var clock = new CountingClock();
        var runner = new RaiderRunner(new RaiderGame(1), () => (60, 20), new CountSink()) { Adaptive = false, PacingClock = clock };
        using var cts = new CancellationTokenSource();
        var run = runner.RunAsync(cts.Token);
        await Wait.UntilAsync(() => clock.Alive == 1, "the run to make its timer");
        clock.TickNewest();
        clock.TickNewest();

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
        Assert.Equal(1, clock.Made);
        Assert.Equal(0, clock.Alive);
    }

    [Fact]
    public async Task Gate_pauses_cancels_its_token_and_resumes_with_a_fresh_one()
    {
        var gate = new PauseGate();
        Assert.False(gate.IsPaused);
        Assert.False(gate.PauseToken.IsCancellationRequested);
        Assert.True(gate.WaitWhilePausedAsync(CancellationToken.None).IsCompletedSuccessfully);

        var before = gate.PauseToken;
        gate.Pause();
        gate.Pause();
        Assert.True(gate.IsPaused);
        Assert.True(before.IsCancellationRequested);
        var wait = gate.WaitWhilePausedAsync(CancellationToken.None);
        Assert.False(wait.IsCompleted);

        gate.Resume();
        await wait.WaitAsync(TimeSpan.FromSeconds(ResumeSeconds));
        Assert.False(gate.IsPaused);
        Assert.False(gate.PauseToken.IsCancellationRequested);
    }

    [Fact]
    public void Gate_stays_paused_while_any_reason_holds_it()
    {
        var gate = new PauseGate();
        gate.Hold(Tab, true);
        gate.Hold(Menu, true);
        Assert.True(gate.IsPaused);

        gate.Hold(Tab, false);   // the tab is back, but the pause menu is still open
        Assert.True(gate.IsPaused);
        gate.Hold(Menu, true);   // holding twice is one hold
        gate.Hold(Menu, false);
        Assert.False(gate.IsPaused);
        Assert.False(gate.PauseToken.IsCancellationRequested);

        gate.Hold(Menu, false);  // letting go of a reason that is not held changes nothing
        Assert.False(gate.IsPaused);
    }

    [Fact]
    public async Task Runner_sits_still_while_the_menu_holds_the_gate_and_carries_on_after()
    {
        var game = new RaiderGame(1);
        var gate = new PauseGate();
        var runner = new RaiderRunner(game, () => (60, 20), new CountSink()) { Adaptive = false, Pause = gate };
        using var cts = new CancellationTokenSource();
        var run = runner.RunAsync(cts.Token);
        await Wait.UntilAsync(() => { lock (game) return game.Tick > RanTicks; }, "the loop to run");

        gate.Hold(Menu, true);
        await Task.Delay(SettleMs);
        long atPause; lock (game) atPause = game.Tick;
        await Task.Delay(WatchMs);
        long whilePaused; lock (game) whilePaused = game.Tick;
        Assert.Equal(atPause, whilePaused);

        gate.Hold(Menu, false);
        await Wait.UntilAsync(() => { lock (game) return game.Tick > whilePaused; }, "the loop to carry on");

        cts.Cancel();   // back to the start page: the run ends
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
    }

    [Fact]
    public async Task A_wait_while_paused_ends_when_the_run_is_cancelled()
    {
        var gate = new PauseGate();
        gate.Pause();
        using var cts = new CancellationTokenSource(CancelAfterMs);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.WaitWhilePausedAsync(cts.Token));
    }

    [Fact]
    public async Task Runner_neither_steps_nor_draws_while_paused_and_carries_on_after()
    {
        var game = new RaiderGame(1);
        var sink = new CountSink();
        var gate = new PauseGate();
        var runner = new RaiderRunner(game, () => (60, 20), sink) { Adaptive = false, Pause = gate };
        using var cts = new CancellationTokenSource();
        var run = runner.RunAsync(cts.Token);

        await Wait.UntilAsync(() => { lock (game) return game.Tick > RanTicks; }, "the loop to run before the pause");

        gate.Pause();
        await Task.Delay(SettleMs);
        long atPause; lock (game) atPause = game.Tick;
        int framesAtPause = sink.Frames;
        await Task.Delay(LongWatchMs);
        long whilePaused; lock (game) whilePaused = game.Tick;
        Assert.Equal(atPause, whilePaused);
        Assert.Equal(framesAtPause, sink.Frames);

        gate.Resume();
        await Wait.UntilAsync(() => { lock (game) return game.Tick - whilePaused >= RanTicks; }, "the loop to resume");
        long after; lock (game) after = game.Tick;
        Assert.InRange(after - whilePaused, RanTicks, MaxResumeTicks);   // resumed, at the timer's pace

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
    }

    [Fact]
    public async Task Feed_paging_waits_while_paused()
    {
        var game = new RaiderGame(1);
        var feed = new CountingFeed();
        var gate = new PauseGate();
        gate.Pause();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.PostsAsync(game, feed, null, "", cts.Token, pause: gate);

        await Task.Delay(WatchMs);
        Assert.Equal(0, feed.Calls);

        gate.Resume();
        await Wait.UntilAsync(() => feed.Calls > 0, "paging to start");

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
    }

    [Fact]
    public async Task Sockets_wait_for_the_resume_before_connecting_and_stop_on_cancel()
    {
        var gate = new PauseGate();
        gate.Pause();
        using var cts = new CancellationTokenSource();
        var labels = new LabelSource { Pause = gate }.ReadAsync(cts.Token);
        var hidden = new ThreadgateSource { Pause = gate }.ReadAsync(cts.Token);
        var labelReader = Task.Run(async () => { await foreach (var _ in labels) { } });
        var hiddenReader = Task.Run(async () => { await foreach (var _ in hidden) { } });

        await Task.Delay(WatchMs);   // paused from the start: no connection is attempted, no network is touched
        Assert.False(labelReader.IsCompleted);
        Assert.False(hiddenReader.IsCompleted);

        cts.Cancel();
        await Task.WhenAll(labelReader, hiddenReader).WaitAsync(TimeSpan.FromSeconds(DoneSeconds));
    }

    /// <summary>A label stream's cursor once seeded, and the sequence number it has read up to since.</summary>
    private const long SeedCursor = 3000, LastSeen = 3450;

    [Theory]
    [InlineData(LabelSource.AtNow, LabelSource.AtNow, false, LabelSource.AtNow)] // the socket dropped before any frame: open at "now" again
    [InlineData(SeedCursor, SeedCursor, false, SeedCursor)]                     // seeding: the first frame set the cursor behind it, so open there to replay
    [InlineData(SeedCursor, LastSeen, false, LastSeen)]                         // the socket dropped after reading: carry on from the last sequence number seen
    [InlineData(LabelSource.AtNow, LabelSource.AtNow, true, LabelSource.AtNow)]  // a pause before seeding: open at "now"
    [InlineData(SeedCursor, LastSeen, true, LabelSource.AtNow)]                 // a pause after seeding: open at "now", so the paused time is not replayed
    public void Label_source_opens_the_next_connection_where_the_last_left_off_and_after_a_pause_at_now_without_a_replay(
        long cursor, long last, bool paused, long nextCursor) =>
        Assert.Equal(nextCursor, LabelSource.NextConnection(cursor, last, paused));
}
