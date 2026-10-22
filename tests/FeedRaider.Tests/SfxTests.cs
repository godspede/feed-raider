using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Which game events emit which sound effect names, and the rate limit. The real sim with a recording bus.
public class SfxTests
{
    private const string LongText = "this is a perfectly ordinary post with enough words in it";

    private readonly record struct Ev(string Name, double Pan, long Frame, int Step, double Size);

    private static (RaiderGame Game, List<Ev> Played) New(bool loggedIn = true)
    {
        var g = new RaiderGame(5) { LoggedIn = loggedIn };
        g.SetLayout(FieldLayout.Compute(120, 46));
        var played = new List<Ev>();
        g.Sfx = new SfxBus((n, p, step, size) => played.Add(new Ev(n, p, g.Frame, step, size)));
        return (g, played);
    }

    private static FeedPost Post(int n) =>
        new($"at://did:plc:t{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:t{n}", $"t{n}.test", LongText, 10, 0, 0, null, null);

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    private static Enemy Add(RaiderGame g, EnemyKind kind, double x, double y, double hp, bool live = false)
    {
        var e = new Enemy { Kind = kind, Sprite = kind == EnemyKind.Gunship ? Art.Gunship : Art.Grunt, X = x, BaseX = x, Y = y, Hp = hp, MaxHp = hp, Live = live };
        g.Enemies.Add(e);
        return e;
    }

    private static void Settle(RaiderGame g, List<Ev> played)
    {
        Run(g, 5);
        played.Clear();
    }

    [Fact]
    public void The_bus_drops_a_repeat_inside_the_gap_and_keeps_sounds_apart()
    {
        var got = new List<(string, double)>();
        var bus = new SfxBus((n, p, _, _) => got.Add((n, p)));
        bus.Play("shot", 0, 10);
        bus.Play("shot", 0, 11);                       // 33 ms later: dropped
        bus.Play("hit", 0, 11);                        // another sound has its own clock
        bus.Play("shot", 0, 10 + SfxBus.MinGapFrames("shot"));
        Assert.Equal(["shot", "hit", "shot"], got.Select(x => x.Item1));
        Assert.True(SfxBus.MinGapFrames("shot") * 1000.0 / Tuning.Fps >= 60);
        bus.Play("shot", 5, 100);
        Assert.Equal(1, got[^1].Item2);                // pan is clamped to -1..1
    }

    private static List<(string Name, int Step, double Size)> Bus(Action<SfxBus> play)
    {
        var got = new List<(string, int, double)>();
        play(new SfxBus((n, _, step, size) => got.Add((n, step, size))));
        return got;
    }

    [Fact]
    public void Kills_in_a_chain_raise_the_pitch_a_semitone_each_up_to_a_cap_then_a_pause_resets()
    {
        long f = 100;
        var got = Bus(bus =>
        {
            for (int i = 0; i < 10; i++) { bus.Play("kill", 0, f); bus.Play("hit", 0, f + 1); f += SfxBus.ComboFrames - 2; }   // quick: a chain
            f += SfxBus.ComboFrames + 1;                                                                                    // a pause
            bus.Play("kill", 0, f);
            bus.Play("hit", 0, f + 1);
        });
        var kills = got.Where(x => x.Name == "kill").Select(x => x.Step).ToList();
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 7, 7, 0], kills);
        Assert.Equal(7, SfxBus.MaxStep);
        Assert.Equal(0, got.Last(x => x.Name == "hit").Step);                       // hits ride the same chain
    }

    [Fact]
    public void A_hit_takes_the_pitch_of_the_chain_and_hurting_the_ship_resets_it()
    {
        var got = Bus(bus =>
        {
            bus.Play("hit", 0, 10);                    // no kill yet: no rise
            bus.Play("kill", 0, 20); bus.Play("kill", 0, 24); bus.Play("kill", 0, 28);
            bus.Play("hit", 0, 30);                    // inside the chain: 2 semitones up
            bus.Play("hit", 0, 30 + SfxBus.ComboFrames + 1);   // the chain has lapsed
            bus.Play("kill", 0, 100); bus.Play("kill", 0, 104);
            bus.Play("hurt", 0, 105);                  // ouch: the chain is gone
            bus.Play("kill", 0, 108);
            bus.Play("kill", 0, 112); bus.Play("death", 0, 113); bus.Play("kill", 0, 116);
        });
        Assert.Equal([0, 2, 0], got.Where(x => x.Name == "hit").Select(x => x.Step));
        Assert.Equal([0, 1, 2, 0, 1, 0, 1, 0], got.Where(x => x.Name == "kill").Select(x => x.Step));
    }

    [Fact]
    public void A_kill_the_gap_drops_still_counts_for_the_chain_and_the_enemy_size_comes_through()
    {
        var got = Bus(bus =>
        {
            bus.Play("kill", 0, 10, 0);
            bus.Play("kill", 0, 11, 0.5);              // inside the 2-frame gap: not played, but it is a kill
            bus.Play("kill", 0, 14, 1);
            bus.Play("boom", 0, 18, 1);
        });
        Assert.Equal([("kill", 0, 0.0), ("kill", 2, 1.0), ("boom", 3, 1.0)], got);
    }

    [Fact]
    public void The_game_chains_kills_and_passes_the_size_of_what_died()
    {
        var (g, played) = New();
        Settle(g, played);
        Add(g, EnemyKind.Grunt, g.PX, g.PY - 14, Tuning.PlayerBulletDamage * 0.5);
        Add(g, EnemyKind.Carrier, g.PX, g.PY - 30, Tuning.PlayerBulletDamage * 0.5);
        Add(g, EnemyKind.Gunship, g.PX, g.PY - 46, Tuning.PlayerBulletDamage * 0.5);
        g.Input.Key(GameKey.Fire, true);
        Run(g, 60);
        var kills = played.Where(p => p.Name is "kill" or "boom").ToList();
        Assert.Equal(3, kills.Count);
        Assert.Equal([0, 0.5, 1], kills.Select(k => k.Size));
        Assert.Equal(["kill", "kill", "boom"], kills.Select(k => k.Name));
        Assert.Equal(0, kills[0].Step);
    }

    [Fact]
    public void Silence_without_a_bus_and_nothing_while_idle()
    {
        var g = new RaiderGame(5);
        g.Input.Key(GameKey.Fire, true);
        Run(g, 30);                                     // no bus: no crash
        var (g2, played) = New();
        Run(g2, 120);
        Assert.DoesNotContain(played, p => p.Name is "shot" or "bomb" or "like" or "nolike" or "comm");
    }

    [Fact]
    public void Firing_plays_a_shot_per_volley_panned_by_the_ship()
    {
        var (g, played) = New();
        g.Input.Key(GameKey.Fire, true);
        Run(g, 40);
        var shots = played.Where(p => p.Name == "shot").ToList();
        Assert.InRange(shots.Count, 8, 40 / Tuning.FireInterval + 1);
        Assert.All(shots, s => Assert.Equal(0, s.Pan, 0.1));      // starts mid-field
        for (int i = 1; i < shots.Count; i++) Assert.True(shots[i].Frame - shots[i - 1].Frame >= SfxBus.MinGapFrames("shot"));
        g.Input.Key(GameKey.Fire, false);
        for (int i = 0; i < 60; i++) { g.Input.Key(GameKey.Left, true); g.Step(); }
        g.Input.Key(GameKey.Left, false);
        played.Clear();
        g.Input.Key(GameKey.Fire, true);
        Run(g, 8);
        Assert.True(played.First(p => p.Name == "shot").Pan < -0.3);   // left of centre pans left
    }

    [Fact]
    public void A_hit_then_a_small_kill()
    {
        var (g, played) = New();
        Settle(g, played);
        Add(g, EnemyKind.Grunt, g.PX, g.PY - 14, 3);
        g.Input.Key(GameKey.Fire, true);
        Run(g, 12);
        Assert.Contains(played, p => p.Name == "hit");
        Assert.Contains(played, p => p.Name == "kill");
        Assert.DoesNotContain(played, p => p.Name == "boom");
    }

    [Fact]
    public void A_gunship_going_down_is_the_big_boom_not_the_small_kill()
    {
        var (g, played) = New();
        Settle(g, played);
        Add(g, EnemyKind.Gunship, g.PX, g.PY - 14, 0.5);
        g.Input.Key(GameKey.Fire, true);
        Run(g, 12);
        Assert.Contains(played, p => p.Name == "boom");
        Assert.DoesNotContain(played, p => p.Name == "kill");
    }

    [Fact]
    public void A_player_hit_hurts_and_the_last_one_is_death()
    {
        var (g, played) = New();
        Settle(g, played);
        int hurts = 0;
        for (int hp = Tuning.PlayerHp; hp > 0; hp--)
        {
            for (int i = 0; i < 400 && g.Invuln > 0; i++) g.Step();
            g.Enemies.Clear();
            g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY });
            int before = played.Count;
            g.Step();
            var now = played.Skip(before).Where(p => p.Name is "hurt" or "death").Select(p => p.Name).ToList();
            if (hp > 1) { Assert.Equal(["hurt"], now); hurts++; }
            else Assert.Equal(["death"], now);
        }
        Assert.Equal(Tuning.PlayerHp - 1, hurts);
        Assert.Equal(1, g.Deaths);
    }

    [Fact]
    public void A_bomb_has_its_own_sound_and_silences_the_kills_it_causes()
    {
        var (g, played) = New();
        Settle(g, played);
        Add(g, EnemyKind.Grunt, 30, 12, 3);
        g.Input.Bomb();
        g.Step();
        Assert.Single(played, p => p.Name == "bomb");
        Assert.DoesNotContain(played, p => p.Name is "kill" or "hit");
        Assert.Empty(g.Enemies);
    }

    [Fact]
    public void A_wingmans_comm_opens_with_one_chirp_panned_to_its_ship()
    {
        var (g, played) = New();
        g.OfferPost(Post(1));
        for (int i = 0; i < 400 && g.Comms.Speaker is null; i++) g.Step();
        var w = Assert.IsType<Wingman>(g.Comms.Speaker);
        var chirp = Assert.Single(played, p => p.Name == "comm");
        Assert.Equal(w.X / g.PW * 2 - 1, chirp.Pan, 0.01);
        Assert.True(chirp.Pan < 0);                      // the first wingman takes the left slot
        Run(g, 20);
        Assert.Single(played, p => p.Name == "comm");   // speaking is not opening again
    }

    [Fact]
    public void Like_chimes_logged_in_and_blips_logged_out_or_when_the_call_fails()
    {
        foreach (bool loggedIn in new[] { true, false })
        {
            var (g, played) = New(loggedIn);
            g.OfferPost(Post(1));
            for (int i = 0; i < 400 && !g.Wings.Any(w => w.Age > Tuning.WingEnterFrames); i++) g.Step();
            var w = g.Wings[0];
            for (int i = 0; i < 200 && (Math.Abs(g.PX - w.X) > 3 || Math.Abs(g.PY - w.Y) > 3); i++)
            {
                g.Input.Key(GameKey.Left, g.PX > w.X + 1); g.Input.Key(GameKey.Right, g.PX < w.X - 1);
                g.Input.Key(GameKey.Up, g.PY > w.Y + 1); g.Input.Key(GameKey.Down, g.PY < w.Y - 1);
                g.Step();
            }
            foreach (var k in Enum.GetValues<GameKey>()) g.Input.Key(k, false);
            played.Clear();
            g.Input.Like(); g.Step();
            Assert.Equal(loggedIn ? ["like"] : ["nolike"], played.Select(p => p.Name));
            if (!loggedIn) continue;
            played.Clear();
            g.LikeFailed(w.Post.Uri);
            Assert.Equal(["nolike"], played.Select(p => p.Name));
        }
    }

    [Fact]
    public void A_darkness_wave_swells_once_when_it_arrives_and_clearing_it_rings()
    {
        var (g, played) = New();
        g.OfferDark(new DarkPost(DarknessKind.Rude, 9, "you are all wrong about everything"));
        for (int i = 0; i < 600 && !g.Enemies.Any(e => e.Live); i++) g.Step();
        Assert.Single(played, p => p.Name == "darkness");
        Assert.DoesNotContain(played, p => p.Name == "clear");
        var boss = g.Enemies.First(e => e.Live);
        boss.Hp = 1; boss.Y = 10;
        played.Clear();
        g.Input.Bomb(); g.Step();
        Assert.Equal(1, g.LiveWavesCleared);
        Assert.Contains(played, p => p.Name == "clear");
        Assert.Contains(played, p => p.Name == "boom");
    }

    [Fact]
    public void Filler_waves_do_not_swell_or_ring()
    {
        var (g, played) = New();
        Run(g, 900);
        Assert.True(g.WavesSpawned > 0);
        Assert.DoesNotContain(played, p => p.Name is "darkness" or "clear");
    }
}
