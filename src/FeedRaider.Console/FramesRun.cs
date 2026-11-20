using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

/// <summary>The --frames run: a fixed-seed <see cref="PilotedGame"/> (the tour's pilot and built-in posts, three
/// darkness posts) drawn frame by frame, so the PNG frames and the timing are repeatable without a network. In the first loop the
/// pilot holds a gold card's heart until the like lands and the author's reply comes in as a thread card.</summary>
static class FramesRun
{
    public const ulong Seed = 42;

    public static int Run(int frames, string outDir, int cols, int rows, int every)
    {
        var run = new PilotedGame(cols, rows);
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        var enc = new DiffEncoder();
        var sw = new Stopwatch();
        long bytes = 0;
        for (int f = 0; f < frames; f++)
        {
            run.ReadyNextTick();
            run.Game.Step();
            sw.Start();
            view.FitAndRender(run.Game, scr);
            bytes += enc.Encode(scr).Length;
            sw.Stop();
            if (f % every == 0) FrameImage.Write(scr, Path.Combine(outDir, $"frame{f:D4}.png"));
        }
        Console.WriteLine($"{frames} frames, {sw.Elapsed.TotalMilliseconds / frames:F2} ms/frame render+encode, {bytes / frames} bytes/frame, score {run.Game.Score}, likes {run.Likes}");
        return 0;
    }
}

/// <summary>A signed-in game at <see cref="FramesRun.Seed"/> for the headless runs, flown by the tour's <see cref="Autopilot"/> on
/// the tour's built-in posts (<see cref="TourFeedSource"/>), the author's reply to a liked post included, with three darkness
/// posts offered by tick. The ship is <see cref="RaiderGame.Charmed"/> as the tour's is, because the pilot holds still on a heart
/// and lets the charm take the hits. Without a boss it is the tour's loop: <see cref="Autopilot.Drive"/> plays the tour's script, and when
/// the loop is <see cref="Autopilot.Finished"/> a fresh game and pilot begin, so a run of any length repeats the same loop.
/// With a boss the game opens on that boss's warning and <see cref="Autopilot.Fly"/> fights it without the tour's scenes; when
/// the boss is down or has left, a fresh game opens on the warning again.</summary>
sealed class PilotedGame
{
    /// <summary>The feed asked of <see cref="TourFeedSource"/>, which serves the same built-in posts whatever it is asked for.</summary>
    const string TourFeedUri = "tour";

    /// <summary>The built-in posts. Every task it returns is already complete, since nothing is fetched, so taking a result
    /// here never waits and the run stays one thread from first frame to last.</summary>
    static readonly TourFeedSource TourPosts = new();

    readonly int _cols, _rows, _boss;
    Autopilot _pilot;
    (int At, Action Do)[] _script;
    string? _cursor;

    /// <summary>The game being flown; a fresh one after each loop or each boss fight.</summary>
    public RaiderGame Game { get; private set; }

    /// <summary>The likes landed in the game being flown: the tour's pilot likes one gold card in each loop, and a boss fight has none.</summary>
    public int Likes => _pilot.Liked ? 1 : 0;

    /// <param name="boss">The boss from 1 to 10 whose fight this is, or 0 for the tour's loop.</param>
    public PilotedGame(int cols, int rows, int boss = 0)
    {
        (_cols, _rows, _boss) = (cols, rows, boss);
        Start();
    }

    /// <summary>Readies the coming tick, before <see cref="RaiderGame.Step"/>: a fresh game when the last one is done, the darkness
    /// posts due at this tick, the post queue topped up from the built-in posts, and the pilot's keys.</summary>
    public void ReadyNextTick()
    {
        if (_boss > 0 ? Game.BossesBeaten >= _boss : _pilot.Finished()) Start();
        foreach (var (at, act) in _script) if (at == _pilot.Ticks) act();
        if (Game.Posts.Count < Tuning.PostQueueLow)
        {
            var page = TourPosts.GetPageAsync(TourFeedUri, _cursor, TourFeedSource.PerPage, default).GetAwaiter().GetResult();
            _cursor = page.Cursor;
            foreach (var p in page.Posts) Game.Posts.Offer(p);
        }
        if (_boss > 0) _pilot.Fly(Game); else _pilot.Drive(Game);
    }

    [MemberNotNull(nameof(Game), nameof(_pilot), nameof(_script))]
    void Start()
    {
        var game = new RaiderGame(FramesRun.Seed) { SignedIn = true, Charmed = true };
        game.SetLayout(FieldLayout.Compute(_cols, _rows));
        if (_boss > 0) game.StartAtBoss(_boss);
        game.ThreadWanted = liked =>
        {
            if (TourPosts.NextAsync(liked, default).GetAwaiter().GetResult() is { } reply) game.ThreadOpened(reply);
        };
        Game = game;
        _pilot = new Autopilot(FramesRun.Seed);
        _script = SetFeedSpeedAndScript(game);
        _cursor = null;
    }

    /// <summary>Sets the cards to scroll at the fastest speed, so a short run sees posts reach the feed log, and gives the darkness
    /// posts by the pilot's tick: a spam post, a rude post and a second spam post.</summary>
    static (int At, Action Do)[] SetFeedSpeedAndScript(RaiderGame game)
    {
        game.FeedSpeed = FeedSpeed.Max;
        return
        [
            (60, () => game.OfferDark(new DarkPost(DarknessKind.Spam, Seed: 11, "buy followers cheap click my profile now"))),
            (200, () => game.OfferDark(new DarkPost(DarknessKind.Rude, Seed: 12, "sample text standing in for a rude post"))),
            (420, () => game.OfferDark(new DarkPost(DarknessKind.Spam, Seed: 13, "free crypto giveaway dm me"))),
        ];
    }
}
