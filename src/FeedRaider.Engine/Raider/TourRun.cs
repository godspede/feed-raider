namespace FeedRaider.Engine.Raider;

/// <summary>The start page's backdrop: a scripted tour of Feed Raider on built-in posts (see <see cref="Autopilot"/>), drawn with no
/// HUD or hints. A loop runs its script to the end and the next begins, until cancelled. Nothing in it can write anywhere:
/// the game is signed out and has no like, repost, best score, sound or music hooks and no network source, so the heart the pilot
/// likes only marks a built-in post, and the reply that like brings in is a built-in post too. Its ship is charmed: it is hit and
/// never dies.</summary>
public static class TourRun
{
    public static async Task RunAsync(Func<(int, int)> size, IFrameSink sink, ulong seed, CancellationToken ct, Func<bool>? skipRender = null,
        PauseGate? pause = null, double timeScale = 1)
    {
        for (ulong run = 0; !ct.IsCancellationRequested; run++)
        {
            var game = new RaiderGame(Hashing.Mix(seed + run)) { SignedIn = false, Charmed = true };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var runner = new RaiderRunner(game, size, sink, seed + run) { Pilot = new Autopilot(seed + run), Pause = pause, TimeScale = timeScale };
            var tourFeed = new TourFeedSource();
            var feed = RaiderFeeds.PostsAsync(game, tourFeed, null, "tour", cts.Token, pause: pause);
            game.ThreadWanted = post => _ = RaiderFeeds.ThreadAsync(game, tourFeed, post, cts.Token);
            runner.View.Backdrop = true;
            try { await runner.RunAsync(ct, skipRender).ConfigureAwait(false); }
            finally { cts.Cancel(); }
            // Cancelling is how a tour ends, so the feed's cancellation is the expected exit.
            try { await feed.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }
}
