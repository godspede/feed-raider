using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using SfxSynth = FeedRaider.Console.Audio.SfxSynth;

namespace FeedRaider.Tests;

// Which game events emit which sound effect names, and the rate limit. The real sim with a recording bus.
public class SfxTests
{
    private readonly record struct Ev(string Name, double Pan, long Frame, int Step, double Size);

    private static (RaiderGame Game, List<Ev> Played) New()
    {
        var g = new RaiderGame(Enemies.EnemyKit.PlainSeed) { SignedIn = true };
        g.SetLayout(FieldLayout.Compute(PressRig.ScrCols, PressRig.ScrRows));
        var played = new List<Ev>();
        g.Sfx = new SfxBus((n, p, step, size) => played.Add(new Ev(n, p, g.Frame, step, size)));
        return (g, played);
    }

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    private static void Add(RaiderGame g, EnemyKind kind, double x, double y, double hp) =>
        g.Enemies.Add(new Enemy { Kind = kind, Sprite = kind == EnemyKind.Gunship ? Art.Gunship : Art.Grunt, X = x, BaseX = x, Y = y, Hp = hp, MaxHp = hp });

    /// <summary>The frames the opening of a game takes to make its own sounds, before a test listens.</summary>
    private const int SettleFrames = 5;
    /// <summary>How far above the ship an enemy is put to be shot, in pixels, and how far apart a column of them stands.</summary>
    private const double ShotRange = 14, ColumnGap = 16;
    /// <summary>Frames for a volley to reach an enemy at <see cref="ShotRange"/> and finish it.</summary>
    private const int VolleyFrames = 12;
    /// <summary>An enemy's hit points that one shot ends, and ones that take several.</summary>
    private const double OneShotHp = 0.5, SeveralShotsHp = 3;
    /// <summary>Long enough for anything a test waits on to happen.</summary>
    private const int MaxWaitFrames = 600;

    private static void Settle(RaiderGame g, List<Ev> played)
    {
        Run(g, SettleFrames);
        played.Clear();
    }

    [Fact]
    public void The_bus_drops_a_repeat_inside_the_gap_and_keeps_sounds_apart()
    {
        var got = new List<(string, double)>();
        var bus = new SfxBus((n, p, _, _) => got.Add((n, p)));
        const long frame = 10;
        bus.Play(SfxNames.Shot, 0, frame);
        bus.Play(SfxNames.Shot, 0, frame + 1);                // 33 ms later: dropped
        bus.Play(SfxNames.Hit, 0, frame + 1);                 // another sound has its own clock
        bus.Play(SfxNames.Shot, 0, frame + SfxBus.MinGapFrames(SfxNames.Shot));
        Assert.Equal([SfxNames.Shot, SfxNames.Hit, SfxNames.Shot], got.Select(x => x.Item1));
        const double minShotGapMs = 60, farRightPan = 5;
        Assert.True(SfxBus.MinGapFrames(SfxNames.Shot) * 1000.0 / Tuning.Fps >= minShotGapMs);
        bus.Play(SfxNames.Shot, farRightPan, 100);
        Assert.Equal(1, got[^1].Item2);                // pan is clamped to -1..1
    }

    private static List<(string Name, int Step, double Size)> Recorded(Action<SfxBus> play)
    {
        var got = new List<(string, int, double)>();
        play(new SfxBus((n, _, step, size) => got.Add((n, step, size))));
        return got;
    }

    [Fact]
    public void Kills_in_a_chain_raise_the_pitch_a_semitone_each_up_to_a_cap_then_a_pause_resets()
    {
        long f = 100;
        const int chainKills = 10;
        var got = Recorded(bus =>
        {
            for (int i = 0; i < chainKills; i++) { bus.Play(SfxNames.Kill, 0, f); bus.Play(SfxNames.Hit, 0, f + 1); f += SfxBus.ComboFrames - 2; }   // quick: a chain
            f += SfxBus.ComboFrames + 1;                                                                                    // a pause
            bus.Play(SfxNames.Kill, 0, f);
            bus.Play(SfxNames.Hit, 0, f + 1);
        });
        var kills = got.Where(x => x.Name == SfxNames.Kill).Select(x => x.Step).ToList();
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 7, 7, 0], kills);
        Assert.Equal(7, SfxBus.MaxStep);
        Assert.Equal(0, got.Last(x => x.Name == SfxNames.Hit).Step);                       // hits ride the same chain
    }

    [Fact]
    public void A_hit_takes_the_pitch_of_the_chain_and_hurting_the_ship_resets_it()
    {
        const long chainHit = 30;
        var got = Recorded(bus =>
        {
            bus.Play(SfxNames.Hit, 0, 10);                    // no kill yet: no rise
            bus.Play(SfxNames.Kill, 0, 20); bus.Play(SfxNames.Kill, 0, 24); bus.Play(SfxNames.Kill, 0, 28);
            bus.Play(SfxNames.Hit, 0, chainHit);              // inside the chain: 2 semitones up
            bus.Play(SfxNames.Hit, 0, chainHit + SfxBus.ComboFrames + 1);   // the chain has lapsed
            bus.Play(SfxNames.Kill, 0, 100); bus.Play(SfxNames.Kill, 0, 104);
            bus.Play(SfxNames.Hurt, 0, 105);                  // ouch: the chain is gone
            bus.Play(SfxNames.Kill, 0, 108);
            bus.Play(SfxNames.Kill, 0, 112); bus.Play(SfxNames.Death, 0, 113); bus.Play(SfxNames.Kill, 0, 116);
        });
        Assert.Equal([0, 2, 0], got.Where(x => x.Name == SfxNames.Hit).Select(x => x.Step));
        Assert.Equal([0, 1, 2, 0, 1, 0, 1, 0], got.Where(x => x.Name == SfxNames.Kill).Select(x => x.Step));
    }

    [Fact]
    public void A_kill_the_gap_drops_still_counts_for_the_chain_and_the_enemy_size_comes_through()
    {
        var got = Recorded(bus =>
        {
            bus.Play(SfxNames.Kill, 0, 10, 0);
            bus.Play(SfxNames.Kill, 0, 11, 0.5);              // inside the 2-frame gap: not played, but it is a kill
            bus.Play(SfxNames.Kill, 0, 14, 1);
            bus.Play(SfxNames.Boom, 0, 18, 1);
        });
        Assert.Equal([(SfxNames.Kill, 0, 0.0), (SfxNames.Kill, 2, 1.0), (SfxNames.Boom, 3, 1.0)], got);
    }

    [Fact]
    public void The_game_chains_kills_and_passes_the_size_of_what_died()
    {
        var (g, played) = New();
        Settle(g, played);
        Add(g, EnemyKind.Grunt, g.PX, g.PY - ShotRange, OneShotHp);
        Add(g, EnemyKind.Carrier, g.PX, g.PY - ShotRange - ColumnGap, OneShotHp);
        Add(g, EnemyKind.Gunship, g.PX, g.PY - ShotRange - 2 * ColumnGap, OneShotHp);
        g.Input.Key(GameKey.Fire, true);
        var three = g.Enemies.ToList();
        const double followDeadbandPx = 2;
        for (int i = 0; i < MaxWaitFrames && three.Any(g.Enemies.Contains); i++)
        {
            g.Enemies.RemoveAll(e => !three.Contains(e));                           // the waves keep coming; only these three count
            var t = three.Where(g.Enemies.Contains).OrderByDescending(e => e.Y).First();
            g.Input.Key(GameKey.Left, t.X < g.PX - followDeadbandPx); g.Input.Key(GameKey.Right, t.X > g.PX + followDeadbandPx);   // they sidestep a lone lane: follow
            g.Step();
        }
        var kills = played.Where(p => p.Name is SfxNames.Kill or SfxNames.Boom).ToList();
        Assert.Equal(three.Count, kills.Count);
        // grunts and carriers sidestep a shot lined up on them, so the order they fall in is not fixed; what died sets the size
        Assert.Equal(Sizes, kills.Select(k => k.Size).Order());
        Assert.All(kills, k => Assert.Equal(k.Size == 1 ? SfxNames.Boom : SfxNames.Kill, k.Name));
        Assert.Equal(0, kills[0].Step);
    }

    [Fact]
    public void Silence_without_a_bus_and_nothing_while_idle()
    {
        var g = new RaiderGame(Enemies.EnemyKit.PlainSeed);
        g.Input.Key(GameKey.Fire, true);
        const int firingFrames = 30, idleFrames = 120;
        Run(g, firingFrames);                           // no bus: no crash
        var (g2, played) = New();
        Run(g2, idleFrames);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Shot or SfxNames.Bomb or SfxNames.Like or SfxNames.NoLike or SfxNames.SpeechBox);
    }

    [Fact]
    public void Firing_plays_a_shot_per_volley_panned_by_the_ship()
    {
        var (g, played) = New();
        const int firing = 40;
        g.Input.Key(GameKey.Fire, true);
        Run(g, firing);
        var shots = played.Where(p => p.Name == SfxNames.Shot).ToList();
        const int minShots = 5;
        Assert.InRange(shots.Count, minShots, firing / Tuning.FireInterval + 1);
        Assert.All(shots, s => Assert.Equal(0, s.Pan, 0.1));      // starts mid-field
        for (int i = 1; i < shots.Count; i++) Assert.True(shots[i].Frame - shots[i - 1].Frame >= SfxBus.MinGapFrames(SfxNames.Shot));
        g.Input.Key(GameKey.Fire, false);
        const int leftFrames = 60, refireFrames = 8;
        const double leftPan = -0.3;
        for (int i = 0; i < leftFrames; i++) { g.Input.Key(GameKey.Left, true); g.Step(); }
        g.Input.Key(GameKey.Left, false);
        played.Clear();
        g.Input.Key(GameKey.Fire, true);
        Run(g, refireFrames);
        Assert.True(played.First(p => p.Name == SfxNames.Shot).Pan < leftPan);   // left of centre pans left
    }

    [Fact]
    public void A_hit_then_a_small_kill()
    {
        var (g, played) = New();
        Settle(g, played);
        Add(g, EnemyKind.Grunt, g.PX, g.PY - ShotRange, SeveralShotsHp);
        g.Input.Key(GameKey.Fire, true);
        Run(g, VolleyFrames);
        Assert.Contains(played, p => p.Name == SfxNames.Hit);
        Assert.Contains(played, p => p.Name == SfxNames.Kill);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Boom);
    }

    [Fact]
    public void A_gunship_going_down_is_the_big_boom_not_the_small_kill()
    {
        var (g, played) = New();
        Settle(g, played);
        Add(g, EnemyKind.Gunship, g.PX, g.PY - ShotRange, OneShotHp);
        g.Input.Key(GameKey.Fire, true);
        Run(g, VolleyFrames);
        Assert.Contains(played, p => p.Name == SfxNames.Boom);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Kill);
    }

    [Fact]
    public void A_ship_hit_with_the_shield_up_pings_and_one_without_it_is_death()
    {
        var (g, played) = New();
        Settle(g, played);
        for (int round = 0; round < 2; round++)
        {
            const int maxInvulnFrames = 400;
            for (int i = 0; i < maxInvulnFrames && g.Invuln > 0; i++) g.Step();
            g.Enemies.Clear();
            g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY });
            int before = played.Count;
            g.Step();
            var now = played.Skip(before).Where(p => p.Name is SfxNames.Hurt or SfxNames.Death or SfxNames.ShieldHit).Select(p => p.Name).ToList();
            Assert.Equal(round == 0 ? [SfxNames.ShieldHit] : [SfxNames.Death], now);
        }
        Assert.Equal(1, g.Deaths);
    }

    [Fact]
    public void A_bomb_has_its_own_sound_and_silences_the_kills_it_causes()
    {
        var (g, played) = New();
        Settle(g, played);
        const double farFromShipX = 30, farFromShipY = 12;   // high in the field: in the one frame run, only the bomb reaches it
        Add(g, EnemyKind.Grunt, farFromShipX, farFromShipY, SeveralShotsHp);
        g.Input.Bomb();
        g.Step();
        Assert.Single(played, p => p.Name == SfxNames.Bomb);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Kill or SfxNames.Hit);
        Assert.Empty(g.Enemies);
    }

    [Fact]
    public void A_live_wave_swells_once_when_it_arrives_and_clearing_it_rings()
    {
        var (g, played) = New();
        const ulong darknessSeed = 9;
        g.OfferDarkness(new DarknessPost(DarknessKind.Spam, darknessSeed, "free crypto giveaway dm me now please"));
        for (int i = 0; i < MaxWaitFrames && !g.Enemies.Any(e => e.Live); i++) g.Step();
        Assert.Single(played, p => p.Name == SfxNames.Darkness);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Clear);
        const int fileInFrames = 60;
        const double nearTopY = 10;
        for (int i = 0; i < fileInFrames; i++) g.Step();   // the rest of the wave files in
        foreach (var live in g.Enemies.Where(e => e.Live)) { live.Hp = 1; live.Y = nearTopY; }
        played.Clear();
        g.Input.Bomb(); g.Step();
        Assert.Equal(1, g.LiveWavesCleared);
        Assert.Contains(played, p => p.Name == SfxNames.Clear);
        Assert.Contains(played, p => p.Name == SfxNames.Bomb);   // grunts and a carrier are small: a bomb has its own sound, no boom
    }

    [Fact]
    public void Filler_waves_do_not_swell_or_ring()
    {
        var (g, played) = New();
        const int fillerFrames = 900;
        Run(g, fillerFrames);
        Assert.True(g.WavesSpawned > 0);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Darkness or SfxNames.Clear);
    }

    /// <summary>The seed every synth render here uses, so a noisy cue renders the same each run.</summary>
    private const int SynthSeed = 1;
    /// <summary>The shortest a cue may be, in seconds.</summary>
    private const double MinCueSeconds = 0.02;
    /// <summary>How far past its stated length a cue may ring, for the tail of its envelope.</summary>
    private const double LengthSlackSeconds = 0.03;
    /// <summary>A peak above this is heard; one above the clear peak is heard plainly.</summary>
    private const float AudiblePeak = 0.01f, ClearPeak = 0.05f;
    /// <summary>The smallest, a middling and the largest thing a cue can be played for.</summary>
    private static readonly double[] Sizes = [0.0, 0.5, 1.0];

    /// <summary>Upward zero crossings between two samples: the more, the higher the pitch.</summary>
    private static int Crossings(float[] s, int from, int to)
    {
        int n = 0;
        for (int i = from + 1; i < to; i++) if (s[i - 1] < 0 && s[i] >= 0) n++;
        return n;
    }

    private static int Crossings(float[] s) => Crossings(s, 0, s.Length);

    /// <summary>A pitch step sent with a cue, inside every cap, so it comes through unchanged where it is carried.</summary>
    private const int SentStep = 5;

    /// <summary>The page's table of sounds in sfx.js, up to the page's calls.</summary>
    private static string Sounds(string js) =>
        js.Split("export const sounds = {")[1].Split("// ---------------------------------------------------------------- the page's calls")[0];

    // ---- the twelve enemy cues: name, longest length in seconds
    private static readonly (string Name, double Seconds)[] EnemyCues =
    [
        (SfxNames.Warn, 0.6), (SfxNames.Ping, 0.2), (SfxNames.Open, 0.3), (SfxNames.Shut, 0.3), (SfxNames.Charge, 1.6),
        (SfxNames.Beam, 0.6), (SfxNames.Lob, 0.3), (SfxNames.Blast, 0.6), (SfxNames.Hatch, 0.2), (SfxNames.Tick, 0.15),
        (SfxNames.Part, 0.8), (SfxNames.BossDown, 1.5),
    ];

    [Fact]
    public void The_twelve_enemy_cue_names_are_these()
    {
        // literals, not the constants: these are the names sfx.js and the synth answer to
        Assert.Equal(["warn", "ping", "open", "shut", "charge", "beam", "lob", "blast", "hatch", "tick", "part", "boss_down"], EnemyCues.Select(c => c.Name));
    }

    [Fact]
    public void Every_enemy_cue_renders_a_non_empty_finite_buffer_no_longer_than_its_length()
    {
        const int midStep = 3;
        foreach (var (name, seconds) in EnemyCues)
            foreach (double size in Sizes)
            {
                var s = SfxSynth.Render(name, midStep, size, new Random(SynthSeed));
                Assert.True(s.Length > MinCueSeconds * SfxSynth.SampleRate, name);
                Assert.All(s, x => Assert.True(float.IsFinite(x), name));
                Assert.True(s.Max(Math.Abs) > AudiblePeak, name + " is silent");
                Assert.True(s.Length <= (seconds + LengthSlackSeconds) * SfxSynth.SampleRate, $"{name} is {s.Length / (double)SfxSynth.SampleRate:F2} s");
            }
    }

    [Fact]
    public void Charge_lasts_as_long_as_its_size_says_from_0_4_to_1_6_seconds()
    {
        int Len(double size) => SfxSynth.Render(SfxNames.Charge, 0, size, new Random(SynthSeed)).Length;
        int small = Len(Sizes[0]), middling = Len(Sizes[1]), large = Len(Sizes[^1]);
        double longest = EnemyCues.Single(c => c.Name == SfxNames.Charge).Seconds;
        const double shortest = 0.4, slack = 0.05;
        Assert.InRange(small / (double)SfxSynth.SampleRate, shortest, shortest + slack);
        Assert.InRange(large / (double)SfxSynth.SampleRate, longest, longest + slack);
        Assert.True(middling > small && large > middling);
    }

    [Fact]
    public void Tick_rises_in_pitch_with_its_step()
    {
        const int risen = 7;   // a step well up the tick's climb
        int low = Crossings(SfxSynth.Render(SfxNames.Tick, 0, 0, new Random(SynthSeed)));
        int high = Crossings(SfxSynth.Render(SfxNames.Tick, risen, 0, new Random(SynthSeed)));
        Assert.True(high > low);
    }

    [Fact]
    public void Ping_and_tick_respect_their_minimum_gaps_on_the_bus()
    {
        const int pingGapFrames = 4, tickGapFrames = 2;
        Assert.True(SfxBus.MinGapFrames(SfxNames.Ping) >= pingGapFrames);
        Assert.True(SfxBus.MinGapFrames(SfxNames.Tick) >= tickGapFrames);
        const int gaps = 4;   // played every frame across this many gaps, one sound gets through per gap
        foreach (string name in new[] { SfxNames.Ping, SfxNames.Tick })
        {
            int gap = SfxBus.MinGapFrames(name);
            var got = Recorded(bus => { for (long f = 0; f < gaps * gap; f++) bus.Play(name, 0, f); });
            Assert.Equal(gaps, got.Count);
        }
    }

    [Fact]
    public void Tick_carries_its_step_through_the_bus_and_the_others_do_not()
    {
        const long frame = 10;
        var got = Recorded(bus => { bus.Play(SfxNames.Tick, 0, frame, 0, SentStep); bus.Play(SfxNames.Ping, 0, frame, 0, SentStep); });
        Assert.Equal([(SfxNames.Tick, SentStep, 0.0), (SfxNames.Ping, 0, 0.0)], got);
    }

    [Fact]
    public void Boss_down_ducks_the_music_on_the_page_and_the_small_cues_do_not()
    {
        string js = SfxJs();
        string ducks = JsConst(js, DucksConst);
        Assert.Contains(SfxNames.BossDown, ducks);
        foreach (string n in new[] { SfxNames.Ping, SfxNames.Tick, SfxNames.Open, SfxNames.Hatch, SfxNames.Lob }) Assert.DoesNotContain(n, ducks);
    }

    [Fact]
    public void Every_enemy_cue_has_a_constant_a_page_function_a_trim_a_gap_and_a_synth_case()
    {
        string js = SfxJs();
        string sounds = Sounds(js);
        var synth = SfxSynth.Names.ToHashSet();
        foreach (var (name, _) in EnemyCues)
        {
            AssertOnPage(js, name);
            Assert.Contains(name, synth);
        }
        // and the page's table is exactly the C# constants
        var consts = typeof(SfxNames).GetFields().Select(f => (string)f.GetRawConstantValue()!).Order();
        Assert.Equal(consts, synth.Order());
        var pageNames = System.Text.RegularExpressions.Regex.Matches(sounds, @"(?m)^  ([a-z_]+)\(v").Select(m => m.Groups[1].Value).Order();
        Assert.Equal(consts, pageNames);
    }

    // ---- a press on a post's button: the like at once, and resting on the button sounds nothing
    [Fact]
    public void A_like_key_plays_like_once_and_resting_on_the_heart_first_plays_nothing()
    {
        var (g, c, played) = PressRig.Scene();
        g.OnLike = _ => { };
        PressRig.RestOn(g, c, PostAction.Like, Tuning.Fps);
        Assert.Empty(played);
        PressRig.Press(g, c, PostAction.Like);
        Assert.Equal(SfxNames.Like, played[^1].Name);
        Assert.Single(played, p => p.Name == SfxNames.Like);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Charge);   // the bosses' wind-up is not borrowed
    }

    // ---- the repost cue: a rising two-note sweep, clearly not the like chime
    [Fact]
    public void A_repost_key_plays_repost_once_and_never_like()
    {
        var (g, c, played) = PressRig.Scene();
        g.OnRepost = _ => { };
        PressRig.Press(g, c, PostAction.Repost);
        Assert.Equal(SfxNames.Repost, played[^1].Name);
        Assert.Single(played, p => p.Name == SfxNames.Repost);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Like);
        Assert.Equal("repost", SfxNames.Repost);   // the literal pins the name sfx.js and the synth answer to
        Assert.Equal(SfxBus.MinGapFrames(SfxNames.Like), SfxBus.MinGapFrames(SfxNames.Repost));
    }

    [Fact]
    public void Repost_is_two_notes_each_sweeping_up_the_second_higher_and_it_is_not_the_like_chime()
    {
        static double Rate(float[] s, int from, int to) => Crossings(s, from, to) / (double)(to - from);
        const double shortestSeconds = 0.2, longestSeconds = 0.6, firstNoteSeconds = 0.12, likeBrighterBy = 1.5;
        int rate = SfxSynth.SampleRate;
        var s = SfxSynth.Render(SfxNames.Repost, 0, 0, new Random(SynthSeed));
        var like = SfxSynth.Render(SfxNames.Like, 0, 0, new Random(SynthSeed));
        Assert.All(s, x => Assert.True(float.IsFinite(x)));
        Assert.True(s.Max(Math.Abs) > ClearPeak);
        Assert.InRange(s.Length, (int)(shortestSeconds * rate), (int)(longestSeconds * rate));
        // the second note starts where the first ends: each rises, and the second sits above the first
        int note = (int)(firstNoteSeconds * rate), half = note / 2, bothNotes = 2 * note;
        Assert.True(Rate(s, half, note) > Rate(s, 0, half), "the first note sweeps up");
        Assert.True(Rate(s, note + half, bothNotes) > Rate(s, note, note + half), "the second note sweeps up");
        Assert.True(Rate(s, note, bothNotes) > Rate(s, 0, note), "the second note is the higher");
        // the like is a bell run far above it
        Assert.True(Rate(like, 0, bothNotes) > likeBrighterBy * Rate(s, 0, bothNotes), "the like chime is brighter");
        AssertOnPage(SfxJs(), SfxNames.Repost);
    }

    /// <summary>The page's sound table, sfx.js, as the repo holds it.</summary>
    internal static string SfxJs() => File.ReadAllText(Path.Combine(RepoRoot(), "src", "FeedRaider.Web", "wwwroot", "sfx.js"));

    /// <summary>The constants of sfx.js that list each sound: its trim, its least gap in milliseconds, and the sounds that duck the music.</summary>
    private const string TrimConst = "TRIM", GapConst = "GAP_MS", DucksConst = "DUCKS";

    private static string JsConst(string js, string name) => js.Split($"const {name} =")[1].Split(';')[0];

    /// <summary>Checks that sfx.js has a function for the cue in its table of sounds, a trim for it and a least gap.</summary>
    private static void AssertOnPage(string js, string name)
    {
        Assert.Matches($@"(?m)^  {name}\(v", Sounds(js));
        Assert.Matches($@"\b{name}: [0-9.]+", JsConst(js, TrimConst));
        Assert.Matches($@"\b{name}: [0-9]+", JsConst(js, GapConst));
    }

    private static string RepoRoot()
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "FeedRaider.slnx"))) d = Path.GetDirectoryName(d);
        return d ?? throw new InvalidOperationException("repo root not found");
    }

    // ---- the thirteenth cue, the sting: a visitor arrives, the music goes on
    [Fact]
    public void The_sting_has_a_constant_a_page_function_a_trim_a_gap_a_duck_and_a_synth_case()
    {
        Assert.Equal("sting", SfxNames.Sting);   // the literal pins the name sfx.js and the synth answer to
        string js = SfxJs();
        AssertOnPage(js, SfxNames.Sting);
        Assert.Contains(SfxNames.Sting, JsConst(js, DucksConst));
        Assert.Contains(SfxNames.Sting, SfxSynth.Names);
        const int stingGapFrames = 3;
        Assert.True(SfxBus.MinGapFrames(SfxNames.Sting) >= stingGapFrames);
    }

    [Fact]
    public void The_sting_renders_a_finite_audible_buffer_no_longer_than_half_a_second_at_every_size()
    {
        const double shortestSting = 0.2, longestSting = 0.5;
        foreach (double size in Sizes)
        {
            var s = SfxSynth.Render(SfxNames.Sting, 0, size, new Random(SynthSeed));
            Assert.True(s.Length > shortestSting * SfxSynth.SampleRate);
            Assert.All(s, x => Assert.True(float.IsFinite(x)));
            Assert.True(s.Max(Math.Abs) > ClearPeak, "sting is silent");
            Assert.True(s.Length <= (longestSting + LengthSlackSeconds) * SfxSynth.SampleRate, $"sting is {s.Length / (double)SfxSynth.SampleRate:F2} s");
        }
    }

    [Fact]
    public void The_sting_is_two_notes_so_its_second_half_is_not_a_copy_of_its_first()
    {
        var s = SfxSynth.Render(SfxNames.Sting, 0, 0, new Random(SynthSeed));
        int half = s.Length / 2;
        Assert.NotEqual(Crossings(s, 0, half), Crossings(s, half, s.Length));
    }
}
