using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Deterministic, no-network tests of the Feed Raider sim: every run is the same seed and the same inputs.
public class RaiderTests
{
    private const string LongText = "this is a perfectly ordinary post with enough words in it";

    private static RaiderGame NewGame(bool loggedIn = true)
    {
        var g = new RaiderGame(5) { LoggedIn = loggedIn };
        g.SetLayout(FieldLayout.Compute(120, 46));
        return g;
    }

    private static FeedPost Post(int n, string text = LongText, int likes = 10, bool media = false) =>
        new($"at://did:plc:t{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:t{n}", $"t{n}.test", text, likes, 0, 0, null, null, HasEmbed: media);

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    /// <summary>Steps until a wingman has flown in near its home and the player has been moved onto it.</summary>
    private static Wingman WingmanInRange(RaiderGame g)
    {
        for (int i = 0; i < 400 && !g.Wings.Any(w => w.Age > Tuning.WingEnterFrames); i++) g.Step();
        var w = g.Wings.First(x => x.Age > Tuning.WingEnterFrames);
        MoveOnto(g, w);
        return w;
    }

    private static void MoveOnto(RaiderGame g, Wingman w)
    {
        for (int i = 0; i < 200; i++)
        {
            g.Input.Key(GameKey.Left, g.PX > w.X + 1); g.Input.Key(GameKey.Right, g.PX < w.X - 1);
            g.Input.Key(GameKey.Up, g.PY > w.Y + 1); g.Input.Key(GameKey.Down, g.PY < w.Y - 1);
            g.Step();
            if (Math.Abs(g.PX - w.X) < 3 && Math.Abs(g.PY - w.Y) < 3) break;
        }
        foreach (var k in Enum.GetValues<GameKey>()) g.Input.Key(k, false);
    }

    // ---------------------------------------------------------------- the text-only filter

    [Fact]
    public void Only_text_posts_of_enough_length_may_become_wingmen()
    {
        Assert.True(WingQueue.Eligible(Post(1)));
        Assert.False(WingQueue.Eligible(Post(2, media: true)));
        Assert.False(WingQueue.Eligible(Post(3, text: "too short")));
        Assert.False(WingQueue.Eligible(Post(4, text: "   a   b   ")));
    }

    [Fact]
    public void Queue_takes_each_post_once_and_stays_bounded()
    {
        var q = new WingQueue();
        Assert.True(q.Offer(Post(1)));
        Assert.False(q.Offer(Post(1)));
        for (int i = 2; i < 100; i++) q.Offer(Post(i));
        Assert.Equal(Tuning.WingQueueMax, q.Count);
        Assert.True(q.TryDequeue(out var first));
        Assert.Equal(Post(1).Uri, first.Uri);
    }

    private static FeedPage EmbedPage() => FeedMapper.ParsePage(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feed-embeds.json")), skipReposts: true);

    [Fact]
    public void Feed_mapper_flags_every_embed_and_keeps_the_avatar()
    {
        var posts = EmbedPage().Posts;
        Assert.Equal(8, posts.Count);
        Assert.Equal([false, true, true, true, true, true, true, false], posts.Select(p => p.HasEmbed));
        Assert.Equal([true, false, false, false, false, false, false, true], posts.Select(WingQueue.Eligible));
        const string one = """{"feed":[{"post":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"c","author":{"did":"did:plc:a","handle":"a.test","avatar":"https://cdn.example/a.jpg"},"record":{"text":"hello there"},"embed":null}}]}""";
        var plain = FeedMapper.ParsePage(one).Posts.Single();
        Assert.False(plain.HasEmbed);
        Assert.Equal("https://cdn.example/a.jpg", plain.AvatarUrl);
    }

    [Fact]
    public void The_queue_and_the_interleaver_take_only_posts_without_an_embed()
    {
        var posts = EmbedPage().Posts;
        var q = new WingQueue();
        foreach (var p in posts) q.Offer(p);
        Assert.Equal(2, q.Count);
        var mix = new FeedInterleaver();
        mix.Add(posts);
        Assert.Equal(2, mix.Count);
        while (mix.TryTake(out var p, allowRepeat: true)) Assert.False(p.HasEmbed);
    }

    // ---------------------------------------------------------------- wingman life cycle

    [Fact]
    public void A_wingman_flies_in_fights_and_leaves_while_the_next_one_queues()
    {
        var g = NewGame();
        g.OfferPost(Post(1)); g.OfferPost(Post(2)); g.OfferPost(Post(3));
        Run(g, 5);
        Assert.Single(g.Wings);
        Assert.Equal(WingPhase.Enter, g.Wings[0].Phase);
        Run(g, Tuning.WingStaggerFrames);
        Assert.Single(g.Wings);                               // one ally at a time
        Assert.Equal(2, g.Queue.Count);                       // the others wait
        var first = g.Wings[0];
        for (int i = 0; i < 2000 && first.Phase != WingPhase.Fight; i++) g.Step();
        Assert.Equal(WingPhase.Fight, first.Phase);
        for (int i = 0; i < 2000 && first.Phase != WingPhase.Leave; i++) g.Step();
        Assert.Equal(WingPhase.Leave, first.Phase);
        for (int i = 0; i < 2000 && g.Wings.Contains(first); i++) g.Step();
        Assert.DoesNotContain(first, g.Wings);                // gone
        for (int i = 0; i < 200 && !g.Wings.Any(w => w.Post.Uri == Post(2).Uri); i++) g.Step();
        var next = Assert.Single(g.Wings);                    // the queued one took the slot
        Assert.Equal(Post(2).Uri, next.Post.Uri);
        Assert.NotEqual(first.Slot, next.Slot);               // and flies on the other side
    }

    [Fact]
    public void Firepower_grows_with_the_log_of_likes()
    {
        var g = NewGame();
        g.OfferPost(Post(1, likes: 0)); Run(g, 60);
        var low = g.Wings.Single(w => w.Post.LikeCount == 0).Firepower;
        var h = NewGame();
        h.OfferPost(Post(2, likes: 900)); Run(h, 60);
        var high = h.Wings.Single(w => w.Post.LikeCount == 900).Firepower;
        Assert.Equal(1.0, low, 6);
        Assert.True(high > low && high < low * 3);
    }

    [Fact]
    public void A_post_with_a_thousand_likes_drops_a_screen_clearing_bomb_with_its_text_as_banner()
    {
        var g = NewGame();
        g.OfferDark(new DarkPost(DarknessKind.Spam, 3, "buy followers now cheap"));
        Run(g, 150);
        Assert.NotEmpty(g.Enemies);
        g.OfferPost(Post(1, likes: Tuning.BombLikeThreshold));
        for (int i = 0; i < 70 && g.Banner is null; i++) g.Step();
        Assert.NotNull(g.Banner);
        Assert.Contains("1,000", g.Banner);
        Assert.Contains("ordinary post", g.Banner);
        Assert.True(g.Shake > 0 || g.HitStop > 0);
        Assert.DoesNotContain(g.Enemies, e => e.Hp > 0 && e.Age > 1);
    }

    // ---------------------------------------------------------------- like and power-up

    [Fact]
    public void The_prompt_shows_only_within_the_like_radius()
    {
        var g = NewGame();
        g.OfferPost(Post(1));
        for (int i = 0; i < 400 && !g.Wings.Any(w => w.Age > Tuning.WingEnterFrames); i++) g.Step();
        var w = g.Wings[0];
        Assert.Null(g.Prompt);                                 // the player starts at the bottom, far away
        MoveOnto(g, w);
        Assert.Same(w, g.Prompt);
    }

    [Fact]
    public void No_like_goes_out_without_a_press_even_when_in_range()
    {
        var g = NewGame();
        var liked = new List<FeedPost>();
        g.OnLike = liked.Add;
        g.OfferPost(Post(1));
        var w = WingmanInRange(g);
        Run(g, 60);
        Assert.Empty(liked);
        Assert.False(w.Liked);
        Assert.False(g.Powered);
    }

    [Fact]
    public void A_like_press_calls_like_exactly_once_per_post_and_starts_the_power_up()
    {
        var g = NewGame();
        var liked = new List<FeedPost>();
        g.OnLike = liked.Add;
        g.OfferPost(Post(1));
        var w = WingmanInRange(g);
        g.Input.Like(); g.Step();
        Assert.Single(liked);
        Assert.True(w.Liked);
        Assert.True(g.Powered);
        Assert.Contains(g.Particles, p => p.Kind == ParticleKind.Heart);
        for (int i = 0; i < 5; i++) { MoveOnto(g, w); g.Input.Like(); g.Step(); }
        Assert.Single(liked);                                  // pressing again does nothing
    }

    [Fact]
    public void A_stale_like_press_does_not_fire_later()
    {
        var g = NewGame();
        var liked = new List<FeedPost>();
        g.OnLike = liked.Add;
        g.OfferPost(Post(1));
        for (int i = 0; i < 400 && !g.Wings.Any(w => w.Age > Tuning.WingEnterFrames); i++) g.Step();
        g.Input.Like();                                        // pressed while nothing is in range
        Run(g, InputState.LikeLatchFrames + 2);
        MoveOnto(g, g.Wings[0]);
        Run(g, 5);
        Assert.Empty(liked);
    }

    [Fact]
    public void Tap_and_click_never_like()
    {
        var g = NewGame();
        var liked = new List<FeedPost>();
        g.OnLike = liked.Add;
        g.OfferPost(Post(1));
        WingmanInRange(g);
        g.Input.Tap(g.Layout.Col0 + 2, 5);                    // nowhere near the bomb button
        g.Step();
        Assert.Empty(liked);
    }

    [Fact]
    public void Logged_out_the_prompt_says_so_and_nothing_is_sent()
    {
        var g = NewGame(loggedIn: false);
        var liked = new List<FeedPost>();
        g.OnLike = liked.Add;
        g.OfferPost(Post(1));
        WingmanInRange(g);
        g.Input.Like(); g.Step();
        Assert.Empty(liked);
        Assert.Equal("sign in to like", g.Toast);
        Assert.False(g.Powered);
    }

    [Fact]
    public void Logged_out_a_like_press_with_no_wingman_near_still_shows_the_hint()
    {
        var g = NewGame(loggedIn: false);
        g.Input.Like(); g.Step();
        Assert.Equal("sign in to like", g.Toast);
    }

    [Fact]
    public void A_failed_like_can_be_tried_again()
    {
        var g = NewGame();
        var liked = new List<FeedPost>();
        g.OnLike = liked.Add;
        g.OfferPost(Post(1));
        var w = WingmanInRange(g);
        g.Input.Like(); g.Step();
        g.LikeFailed(w.Post.Uri);
        MoveOnto(g, w); g.Input.Like(); g.Step();
        Assert.Equal(2, liked.Count);
    }

    [Fact]
    public void The_power_up_lasts_its_time_then_ends()
    {
        var g = NewGame();
        g.OfferPost(Post(1));
        WingmanInRange(g);
        g.Input.Like(); g.Step();
        Assert.InRange(g.PowerLeft, Tuning.PowerUpFrames - 2, Tuning.PowerUpFrames);
        // the timer counts sim ticks, so a hit-stop pauses it; count ticks, not frames
        long start = g.Tick;
        while (g.Tick < start + Tuning.PowerUpFrames / 2) g.Step();
        Assert.True(g.Powered);
        while (g.Tick < start + Tuning.PowerUpFrames + 1) g.Step();
        Assert.False(g.Powered);
    }

    [Fact]
    public void Powered_fire_adds_angled_shots_and_damage()
    {
        var g = NewGame();
        g.Input.Key(GameKey.Fire, true);
        g.Step();
        var plain = g.Bullets.Where(b => b.Friendly).ToList();
        g.OfferPost(Post(1));
        var w = WingmanInRange(g);
        g.Input.Like(); g.Step();
        g.Bullets.Clear();
        g.Input.Key(GameKey.Fire, true);
        Run(g, Tuning.FireInterval + 1);
        var powered = g.Bullets.Where(b => b.Friendly && !b.Wing).ToList();
        Assert.Single(plain);                                    // level 1 is a single stream
        Assert.Contains(powered, b => b.VX != 0);
        Assert.True(powered.Max(b => b.Dmg) > plain.Max(b => b.Dmg));
    }

    // ---------------------------------------------------------------- darkness to waves

    [Fact]
    public void A_rude_event_becomes_one_gunship_with_its_text()
    {
        var w = WaveSpawner.FromPost(new DarkPost(DarknessKind.Rude, 9, "you are all wrong about everything"));
        Assert.True(w.Live);
        var e = Assert.Single(w.Enemies);
        Assert.Equal(EnemyKind.Gunship, e.Kind);
        Assert.False(string.IsNullOrEmpty(e.Text));
    }

    [Fact]
    public void A_spam_event_becomes_five_to_twelve_grunts_with_exactly_one_carrier()
    {
        for (ulong seed = 1; seed < 40; seed++)
        {
            var w = WaveSpawner.FromPost(new DarkPost(DarknessKind.Spam, seed, "free crypto giveaway dm me now please"));
            Assert.InRange(w.Enemies.Count, Tuning.SpamMin, Tuning.SpamMax);
            Assert.Equal(1, w.Enemies.Count(e => e.Kind == EnemyKind.Carrier));
            Assert.DoesNotContain(w.Enemies, e => e.Kind == EnemyKind.Gunship);
            Assert.Equal(1, w.Enemies.Count(e => e.Text is not null));
        }
    }

    [Fact]
    public void The_same_event_always_gives_the_same_wave_and_filler_never_carries_text()
    {
        var p = new DarkPost(DarknessKind.Spam, 77, "same text every time here");
        Assert.Equal(WaveSpawner.FromPost(p).Enemies, WaveSpawner.FromPost(p).Enemies);
        for (ulong s = 0; s < 30; s++)
        {
            var f = WaveSpawner.Filler(s);
            Assert.False(f.Live);
            Assert.All(f.Enemies, e => { Assert.Equal(EnemyKind.Grunt, e.Kind); Assert.Null(e.Text); });
        }
    }

    [Fact]
    public void A_darkness_event_puts_a_live_wave_on_screen_and_the_hud_tells_it_from_filler()
    {
        var g = NewGame();
        g.OfferDark(new DarkPost(DarknessKind.Rude, 4, "some rude words for the gunship"));
        for (int i = 0; i < 200 && !g.Enemies.Any(e => e.Kind == EnemyKind.Gunship); i++) g.Step();
        Assert.Contains(g.Enemies, e => e.Kind == EnemyKind.Gunship && e.Live);
        Assert.Equal(1, g.LiveWavesSpawned);
        Assert.Contains("LIVE", g.WaveLabel());

        var quiet = NewGame();
        Run(quiet, 400);
        Assert.True(quiet.WavesSpawned > 0);                    // filler keeps the screen busy
        Assert.Equal(0, quiet.LiveWavesSpawned);
        Assert.DoesNotContain("LIVE", quiet.WaveLabel());
    }

    // ---------------------------------------------------------------- impact feel

    [Fact]
    public void Shake_decays_to_nothing_and_hit_stop_freezes_the_sim_then_releases()
    {
        var g = NewGame();
        Run(g, 5);
        g.AddShake(Tuning.ShakeBomb, hitStop: 4);
        Assert.True(g.Shake > 0);
        long tick = g.Tick; double scroll = g.ScrollFrames;
        Run(g, 4);
        Assert.Equal(tick, g.Tick);                             // frozen
        Assert.Equal(scroll, g.ScrollFrames);
        Assert.True(g.Shake < Tuning.ShakeBomb);                // but the shake keeps decaying
        g.Step();
        Assert.Equal(tick + 1, g.Tick);                         // released
        double prev = g.Shake;
        for (int i = 0; i < 80 && g.Shake > 0; i++) { g.Step(); Assert.True(g.Shake <= prev); prev = g.Shake; }
        Assert.Equal(0, g.Shake);
        Assert.Equal((0, 0), g.ShakeOffset());
    }

    [Fact]
    public void Shake_is_capped()
    {
        var g = NewGame();
        for (int i = 0; i < 10; i++) g.AddShake(Tuning.ShakeBomb);
        Assert.True(g.Shake <= Tuning.ShakeMax);
    }

    [Fact]
    public void Killing_a_heavy_enemy_gives_hit_stop_flash_and_debris()
    {
        var g = NewGame();
        g.OfferDark(new DarkPost(DarknessKind.Rude, 4, "rude words for the gunship to say"));
        for (int i = 0; i < 200 && !g.Enemies.Any(e => e.Kind == EnemyKind.Gunship && e.Y > 6); i++) g.Step();
        var ship = g.Enemies.First(e => e.Kind == EnemyKind.Gunship);
        g.Input.Bomb(); g.Step();                               // a bomb hits everything for 30
        Assert.True(ship.Flash > 0 || ship.Hp < ship.MaxHp);
        Assert.True(g.HitStop > 0 || g.Shake > 0);
        Assert.NotEmpty(g.Rings);
    }

    // ---------------------------------------------------------------- firing

    [Fact]
    public void Holding_space_fires_and_releasing_stops()
    {
        var g = NewGame();
        Run(g, 10);
        Assert.DoesNotContain(g.Bullets, b => b.Friendly);
        g.Input.Key(GameKey.Fire, true);
        Run(g, 20);
        int held = g.Bullets.Count(b => b.Friendly);
        Assert.True(held >= 4, $"held space should fire repeatedly, got {held}");
        g.Input.Key(GameKey.Fire, false);
        Run(g, 120);
        Assert.DoesNotContain(g.Bullets, b => b.Friendly);         // all gone off the top, none new
    }

    [Fact]
    public void A_console_key_press_without_release_still_fires_for_a_moment()
    {
        var g = NewGame();
        g.Input.Press(GameKey.Fire);
        Run(g, 3);
        Assert.Contains(g.Bullets, b => b.Friendly);
    }

    [Fact]
    public void F_toggles_auto_fire_on_and_off()
    {
        var g = NewGame();
        g.Input.ToggleAutoFire();
        Run(g, 20);
        Assert.True(g.Bullets.Count(b => b.Friendly) >= 4);     // no key held at all
        g.Input.ToggleAutoFire();
        Run(g, 120);
        Assert.DoesNotContain(g.Bullets, b => b.Friendly);
    }

    [Fact]
    public void Pointer_and_touch_turn_auto_fire_on_and_keys_turn_it_back_off()
    {
        var g = NewGame();
        g.Input.Pointer(g.Layout.Col0 + 10, 20);
        Assert.True(g.Input.Firing);
        g.Input.Key(GameKey.Left, true);
        Assert.False(g.Input.Firing);
        g.Input.Drag(1, 0);
        Assert.True(g.Input.Firing);
    }

    [Fact]
    public void The_plane_follows_the_pointer_and_a_drag()
    {
        var g = NewGame();
        Run(g, 1);
        double x0 = g.PX;
        g.Input.Pointer(g.Layout.Col0 + 6, g.Layout.Rows / 2.0);
        Run(g, 60);
        Assert.True(g.PX < x0 - 10);
        double x1 = g.PX, y1 = g.PY;
        g.Input.Drag(3, -2);
        g.Step();
        Assert.True(g.PX > x1 && g.PY < y1);
    }

    [Fact]
    public void Tapping_the_bomb_button_bombs_instead_of_liking()
    {
        var g = NewGame();
        var (c, r, w, h) = g.BombButton;
        int bombs = g.Bombs;
        g.Input.Tap(c + 1, r);
        g.Step();
        Assert.Equal(bombs - 1, g.Bombs);
    }

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void The_same_seed_and_inputs_give_the_same_game()
    {
        static (int, long, int) Play()
        {
            var g = NewGame();
            g.OfferDark(new DarkPost(DarknessKind.Spam, 5, "buy now cheap followers here"));
            g.OfferPost(Post(1));
            g.Input.Key(GameKey.Fire, true);
            Run(g, 600);
            return (g.Score, g.Tick, g.Enemies.Count);
        }
        Assert.Equal(Play(), Play());
    }
}
