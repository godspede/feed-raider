using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Deterministic, no-network tests of the Feed Raider sim: every run is the same seed and the same inputs.
public class RaiderTests
{
    private const string LongText = "this is a perfectly ordinary post with enough words in it";

    private static RaiderGame NewGame(bool signedIn = true)
    {
        var g = new RaiderGame(5) { SignedIn = signedIn };
        g.SetLayout(FieldLayout.Compute(120, 46));
        return g;
    }

    private static FeedPost Post(int n, string text = LongText, int likes = 10, bool media = false) =>
        new($"at://did:plc:t{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:t{n}", $"t{n}.test", text, likes, 0, 0, null, null, HasEmbed: media);

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    // ---------------------------------------------------------------- the feed filter

    private static FeedPage EmbedPage() => FeedMapper.ParsePage(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feed-embeds.json")), skipReposts: true);

    [Fact]
    public void Feed_mapper_flags_every_embed_and_keeps_the_avatar()
    {
        var posts = EmbedPage().Posts;
        Assert.Equal(8, posts.Count);
        Assert.Equal([false, true, true, true, true, true, true, false], posts.Select(p => p.HasEmbed));
        // text-only takes the plain posts and the one whose attachment is its own pictures, which its card draws
        Assert.Equal([true, true, false, false, false, false, false, true], posts.Select(p => PostRules.Shown(p, false, textOnly: true)));
        const string one = """{"feed":[{"post":{"uri":"at://did:plc:a/app.bsky.feed.post/1","cid":"c","author":{"did":"did:plc:a","handle":"a.test","avatar":"https://cdn.example/a.jpg"},"record":{"text":"hello there"},"embed":null}}]}""";
        var plain = FeedMapper.ParsePage(one).Posts.Single();
        Assert.False(plain.HasEmbed);
        Assert.Equal("https://cdn.example/a.jpg", plain.AvatarUrl);
    }

    [Fact]
    public void The_interleaver_takes_only_posts_without_an_embed_or_with_pictures_when_text_only()
    {
        var posts = EmbedPage().Posts;
        var mix = new FeedInterleaver(textOnly: true);
        mix.Add(posts);
        Assert.Equal(3, mix.Count);
        while (mix.TryTake(out var p, allowRepeat: true)) Assert.True(!p.HasEmbed || p.Pictures is { Count: > 0 });
    }

    // ---------------------------------------------------------------- darkness to waves

    [Fact]
    public void A_rude_event_makes_no_enemy_and_waits_for_a_speaker()
    {
        var w = WaveSpawner.FromPost(new DarkPost(DarknessKind.Rude, 9, "you are all wrong about everything"));
        Assert.Empty(w.Enemies);
        var g = NewGame();
        g.OfferDark(new DarkPost(DarknessKind.Rude, 9, "you are all wrong about everything"));
        Assert.Equal("you are all wrong about everything", Assert.Single(g.Rude.Waiting).Text);
        Assert.Equal(0, g.LiveWavesSpawned);
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
        g.OfferDark(new DarkPost(DarknessKind.Spam, 4, "some spam words for the carrier"));
        for (int i = 0; i < 200 && !g.Enemies.Any(e => e.Live); i++) g.Step();
        Assert.Contains(g.Enemies, e => e.Live);
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
    public void A_bomb_on_a_heavy_enemy_gives_hit_stop_flash_and_debris()
    {
        var g = NewGame();
        var ship = g.SpawnLesser();
        ship.Y = 40;
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
        Run(g, 30);
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
        g.Input.Press(GameKey.Fire, new KeyRepeatTiming(500, 40));
        Run(g, 3);
        Assert.Contains(g.Bullets, b => b.Friendly);
    }

    [Fact]
    public void F_toggles_auto_fire_on_and_off()
    {
        var g = NewGame();
        g.Input.ToggleAutoFire();
        Run(g, 30);
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
    public void The_ship_follows_the_pointer_and_a_drag()
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
            g.Posts.Offer(Post(1));
            g.Input.Key(GameKey.Fire, true);
            Run(g, 600);
            return (g.Score, g.Tick, g.Enemies.Count);
        }
        Assert.Equal(Play(), Play());
    }
}
