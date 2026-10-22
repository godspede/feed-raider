using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

/// <summary>A hidden browser tab pauses the game loop, the feed pager and the socket reads; coming back resumes them.</summary>
public class PauseTests
{
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
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(gate.IsPaused);
        Assert.False(gate.PauseToken.IsCancellationRequested);
    }

    [Fact]
    public void Gate_stays_paused_while_any_reason_holds_it()
    {
        var gate = new PauseGate();
        gate.Hold("tab", true);
        gate.Hold("menu", true);
        Assert.True(gate.IsPaused);

        gate.Hold("tab", false);   // the tab is back, but the menu is still open
        Assert.True(gate.IsPaused);
        gate.Hold("menu", true);   // holding twice is one hold
        gate.Hold("menu", false);
        Assert.False(gate.IsPaused);
        Assert.False(gate.PauseToken.IsCancellationRequested);

        gate.Hold("menu", false);  // letting go of a reason that is not held changes nothing
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
        await Task.Delay(300);

        gate.Hold("menu", true);
        await Task.Delay(100);
        long atPause; lock (game) atPause = game.Tick;
        await Task.Delay(300);
        long whilePaused; lock (game) whilePaused = game.Tick;
        Assert.Equal(atPause, whilePaused);

        gate.Hold("menu", false);
        await Task.Delay(300);
        long after; lock (game) after = game.Tick;
        Assert.True(after > whilePaused, "the run goes on once the menu closes");

        cts.Cancel();   // back to the main menu: the run ends
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_wait_while_paused_ends_when_the_run_is_cancelled()
    {
        var gate = new PauseGate();
        gate.Pause();
        using var cts = new CancellationTokenSource(50);
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

        await Task.Delay(400);
        long ran; lock (game) ran = game.Tick;
        Assert.True(ran > 3, $"the loop runs before the pause ({ran} ticks)");

        gate.Pause();
        await Task.Delay(100);   // let a tick in flight finish
        long atPause; lock (game) atPause = game.Tick;
        int framesAtPause = sink.Frames;
        await Task.Delay(500);
        long whilePaused; lock (game) whilePaused = game.Tick;
        Assert.Equal(atPause, whilePaused);
        Assert.Equal(framesAtPause, sink.Frames);

        gate.Resume();
        await Task.Delay(500);
        long after; lock (game) after = game.Tick;
        Assert.InRange(after - whilePaused, 3, 40);   // resumed, and did not replay the 0.5 s it sat out in one burst

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Feed_paging_waits_while_paused()
    {
        var game = new RaiderGame(1);
        var feed = new CountingFeed();
        var gate = new PauseGate();
        gate.Pause();
        using var cts = new CancellationTokenSource();
        var run = RaiderFeeds.WingmenAsync(game, feed, null, "", cts.Token, pause: gate);

        await Task.Delay(300);
        Assert.Equal(0, feed.Calls);

        gate.Resume();
        await Task.Delay(300);
        Assert.True(feed.Calls > 0, "paging starts once resumed");

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Sockets_wait_for_the_resume_before_connecting_and_stop_on_cancel()
    {
        var gate = new PauseGate();
        gate.Pause();
        using var cts = new CancellationTokenSource();
        var labels = new LabelSource { Pause = gate }.ReadAsync(cts.Token);
        var hidden = new ThreadgateSource { Pause = gate }.ReadAsync(cts.Token);
        var l = Task.Run(async () => { await foreach (var _ in labels) { } });
        var h = Task.Run(async () => { await foreach (var _ in hidden) { } });

        await Task.Delay(200);   // paused from the start: no connection is attempted, no network is touched
        Assert.False(l.IsCompleted);
        Assert.False(h.IsCompleted);

        cts.Cancel();
        await Task.WhenAll(l, h).WaitAsync(TimeSpan.FromSeconds(10));
    }
}
