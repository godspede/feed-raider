using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class AutopilotTests
{
    private static RaiderGame Fresh(ulong seed, int cols = 120, int rows = 46)
    {
        var g = new RaiderGame(seed) { SignedIn = false, Charmed = true };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        return g;
    }

    /// <summary>One loop of the tour, flown as the tour flies it: the built-in posts kept topped up, the pilot driving every tick.</summary>
    private sealed class Tour
    {
        public readonly RaiderGame Game;
        public readonly Autopilot Pilot;
        private readonly TourFeedSource _feed = new();
        private string? _cursor;

        public Tour(ulong seed, int cols = 120, int rows = 46)
        {
            Game = Fresh(seed, cols, rows);
            Pilot = new Autopilot(seed);
        }

        public void Tick()
        {
            if (Game.Posts.Count < Tuning.PostQueueLow)
            {
                var page = _feed.GetPageAsync("tour", _cursor, 8, default).GetAwaiter().GetResult();
                _cursor = page.Cursor;
                foreach (var p in page.Posts) Game.Posts.Offer(p);
            }
            Pilot.Drive(Game);
            Game.Step();
        }

        public void Fly(int ticks, Action<Tour>? each = null)
        {
            for (int i = 0; i < ticks; i++) { Tick(); each?.Invoke(this); }
        }
    }

    private static List<string> Fly(ulong seed, int ticks)
    {
        var tour = new Tour(seed);
        var trace = new List<string>();
        tour.Fly(ticks, t => trace.Add($"{t.Game.PX:F3},{t.Game.PY:F3},{t.Game.Score},{t.Game.Shrugged},{t.Game.Bombs},{t.Game.Enemies.Count},{t.Game.Bullets.Count},{t.Game.Gate}"));
        return trace;
    }

    public static TheoryData<ulong> Seeds => [1, 7, 42, 1234567, 0x7ffffffe];

    [Fact]
    public void The_same_seed_flies_the_same_tour()
    {
        Assert.Equal(Fly(42, Autopilot.MaxRunTicks), Fly(42, Autopilot.MaxRunTicks));
    }

    [Fact]
    public void The_pilot_moves_fires_and_stays_in_the_field()
    {
        var tour = new Tour(5);
        var g = tour.Game;
        double lo = double.MaxValue, hi = double.MinValue;
        bool fired = false;
        tour.Fly(900, _ =>
        {
            lo = Math.Min(lo, g.PX); hi = Math.Max(hi, g.PX);
            fired |= g.Bullets.Any(b => b.Friendly);
        });
        Assert.True(hi - lo > 10, "it moves across the field");
        Assert.True(fired, "it fires");
        Assert.InRange(lo, 0, g.PW);
        Assert.InRange(hi, 0, g.PW);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_pilot_never_dies_in_a_loop(ulong seed)
    {
        var tour = new Tour(seed);
        tour.Fly(Autopilot.MaxRunTicks, t => Assert.True(t.Game.Alive, $"alive at tick {t.Pilot.Ticks}"));
        Assert.Equal(0, tour.Game.Deaths);
        Assert.True(tour.Pilot.Finished(), "and the loop ends at the script's last tick");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_tour_likes_a_heart_with_a_click_and_nothing_is_sent(ulong seed)
    {
        var tour = new Tour(seed);
        var g = tour.Game;
        // the tour's game is signed out and carries no like, repost or best-score hook: a like can only mark the card
        Assert.False(g.SignedIn);
        Assert.Null(g.OnLike);
        Assert.Null(g.OnRepost);
        Assert.Null(g.OnBest);
        Assert.Null(g.Sfx);
        Assert.Null(g.Music);
        var liked = new HashSet<string>();
        tour.Fly(Autopilot.MaxRunTicks, _ =>
        {
            Assert.Equal(0, g.Input.ShipLikeLatch);
            foreach (var c in g.Cards.Cards)
            {
                Assert.NotEqual(ButtonState.Pending, c.Marks.Like);
                Assert.NotEqual(ButtonState.Lit, c.Marks.Like);
                Assert.NotEqual(ButtonState.Pending, c.Marks.Repost);
                Assert.NotEqual(ButtonState.Lit, c.Marks.Repost);
                if (c.Marks.Like == ButtonState.Marked) liked.Add(c.Post.Uri);
            }
            if (liked.Count == 0) Assert.True(tour.Pilot.Ticks <= Autopilot.LikeUntil, "the like lands in the scene written for it");
        });
        Assert.True(tour.Pilot.Liked, "the pilot liked a heart");
        Assert.NotEmpty(liked);
        Assert.All(liked, uri => Assert.StartsWith("tour://", uri));
        Assert.True(g.Score > 0, "it scored, and still nothing was saved");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_scripted_click_shows_a_pointer_that_reaches_the_heart_before_it_lights(ulong seed)
    {
        var tour = new Tour(seed);
        var g = tour.Game;
        var path = new List<(double X, double Y)>();
        bool clicked = false, shownBeforeClick = false;
        tour.Fly(Autopilot.MaxRunTicks, _ =>
        {
            if (g.TourPointer is { } at && !clicked) path.Add(at);
            if (!clicked && g.Cards.Cards.Any(c => c.Marks.Like == ButtonState.Marked)) { clicked = true; shownBeforeClick = path.Count > 1; }
        });
        Assert.True(clicked, "the click lit a heart");
        Assert.True(shownBeforeClick, "and was on the page for more than a frame before it did");
        Assert.True(tour.Pilot.Liked, "the pilot's like is the one that lit the heart");
        Assert.Null(g.TourPointer);
        double travelled = Math.Abs(path[^1].X - path[0].X) + Math.Abs(path[^1].Y - path[0].Y);
        Assert.True(travelled > 10, "it glides to the heart rather than appearing on it");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_tour_takes_a_speed_burst_before_the_mini_boss(ulong seed)
    {
        var tour = new Tour(seed);
        bool burst = false;
        tour.Fly(Autopilot.MiniBossAt, t => burst |= t.Game.Bursting);
        Assert.True(burst);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_tour_meets_a_mini_boss_and_three_bosses_and_takes_parts_off_them(ulong seed)
    {
        var tour = new Tour(seed);
        var g = tour.Game;
        var bosses = new List<EnemyKind>();
        var spoke = new HashSet<EnemyKind>();
        var broken = new HashSet<EnemyKind>();
        bool warden = false;
        tour.Fly(Autopilot.MaxRunTicks, _ =>
        {
            warden |= g.Enemies.Any(e => e.Kind == EnemyKind.Warden && e.Y > 0);
            if (g.Boss is not { } b) return;
            if (bosses.Count == 0 || bosses[^1] != b.Kind) bosses.Add(b.Kind);
            if (b.SpeechLines is { Length: > 0 }) spoke.Add(b.Kind);
            if (b.Parts?.Any(p => p.State == PartState.Dead) == true) broken.Add(b.Kind);
        });
        Assert.True(warden, "the mini-boss came on");
        Assert.Equal([EnemyKind.Bulwark, EnemyKind.Thread, EnemyKind.Schism], bosses);
        Assert.Equal(Autopilot.Scenes.Length, tour.Pilot.BossesMet);
        Assert.Equal(3, spoke.Count);
        Assert.True(broken.Count >= 2, $"parts came off {broken.Count} of the three bosses");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Something_is_on_the_field_in_every_ten_seconds(ulong seed)
    {
        var tour = new Tour(seed);
        var g = tour.Game;
        int quiet = 0, longest = 0;
        tour.Fly(Autopilot.MaxRunTicks, _ =>
        {
            bool busy = g.Enemies.Any(e => e.Y > 0 && e.Y < g.PH) || g.Gate != GateState.None;
            quiet = busy ? 0 : quiet + 1;
            longest = Math.Max(longest, quiet);
        });
        Assert.True(longest < 10 * Tuning.Fps, $"the longest empty stretch was {longest} ticks");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Flying_only_fights_the_boss_on_the_field_and_plays_none_of_the_tour(ulong seed)
    {
        const int Boss = 3;
        var g = Fresh(seed);
        g.StartAtBoss(Boss);
        var pilot = new Autopilot(seed);
        double lo = double.MaxValue, hi = double.MinValue;
        bool fired = false, fought = false;
        for (int i = 0; i < Autopilot.Scenes[0].Until; i++)
        {
            var before = (g.Enemies.Count, g.Pickups.Count, g.Gate, g.Stage, g.Boss);
            pilot.Fly(g);
            Assert.Equal(before, (g.Enemies.Count, g.Pickups.Count, g.Gate, g.Stage, g.Boss));
            Assert.False(g.Input.BombPressed, $"no bomb at tick {pilot.Ticks}");
            g.Step();
            lo = Math.Min(lo, g.PX); hi = Math.Max(hi, g.PX);
            fired |= g.Bullets.Any(b => b.Friendly);
            fought |= g.Gate == GateState.Fighting;
        }
        Assert.True(hi - lo > 10, "it moves across the field");
        Assert.True(fired, "it fires");
        Assert.True(fought, "the boss came on and was fought");
        Assert.Equal(0, pilot.BossesMet);
        Assert.Equal(Autopilot.Scenes[0].Until, pilot.Ticks);
    }

    [Fact]
    public void A_played_game_is_not_charmed()
    {
        Assert.False(new RaiderGame(1).Charmed);
    }

    [Fact]
    public void The_backdrop_draws_the_fight_without_the_score_rows_or_the_key_hints()
    {
        string Draw(bool backdrop)
        {
            var g = Fresh(3);
            var view = new RaiderView(3) { Backdrop = backdrop };
            var scr = new Screen(120, 46);
            for (int i = 0; i < 30; i++) g.Step();
            view.FitAndRender(g, scr, null);
            return new string(scr.Ch);
        }
        Assert.Contains("SCORE", Draw(false));
        Assert.Contains("WASD", Draw(false));
        Assert.DoesNotContain("SCORE", Draw(true));
        Assert.DoesNotContain("WASD", Draw(true));
        Assert.DoesNotContain("FEED LOG", Draw(true));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_tour_plays_out_on_a_phone_sized_field_too(ulong seed)
    {
        var tour = new Tour(seed, 34, 60);
        var kinds = new HashSet<EnemyKind>();
        tour.Fly(Autopilot.MaxRunTicks, t =>
        {
            Assert.True(t.Game.Alive);
            if (t.Game.Boss is { } b) kinds.Add(b.Kind);
        });
        Assert.True(tour.Pilot.Liked, "the like landed");
        Assert.Equal(3, kinds.Count);
    }

    [Fact]
    public async Task Tour_posts_are_shown_and_every_page_is_new()
    {
        var src = new TourFeedSource();
        var seen = new HashSet<string>();
        string? cursor = null;
        for (int p = 0; p < 5; p++)
        {
            var page = await src.GetPageAsync("tour", cursor, 30, default);
            cursor = page.Cursor;
            foreach (var post in page.Posts)
            {
                Assert.True(PostRules.Shown(post, signedOut: true, textOnly: false), post.Text);
                Assert.True(seen.Add(post.Uri));
            }
        }
    }

    [Fact]
    public async Task The_tour_loop_draws_frames_and_stops_when_cancelled()
    {
        var sink = new CountSink();
        using var cts = new CancellationTokenSource();
        var run = TourRun.RunAsync(() => (100, 40), sink, 3, cts.Token);
        await sink.Drawn.Task.WaitAsync(TimeSpan.FromSeconds(15));   // a loaded box draws slowly: wait for the frames, not for a clock
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(run.IsCompletedSuccessfully, "the loop ends cleanly on cancel");
    }

    private sealed class CountSink : IFrameSink
    {
        private const int FramesBeforeDrawn = 3;
        public int Frames;
        public readonly TaskCompletionSource Drawn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Write(string frame)
        {
            if (Interlocked.Increment(ref Frames) > FramesBeforeDrawn) Drawn.TrySetResult();
        }
    }
}
