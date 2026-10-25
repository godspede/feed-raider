using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using TUImation;

namespace FeedRaider.Tests;

// xterm's WebGL renderer rasterises every (glyph, foreground, background) it meets once, into an atlas that only grows. On a
// phone at DPR 3 each new key is a slow draw and measure, and a full atlas means a renewal and the whole screen drawn again.
// Truecolour quadrant cells used to mint keys without end (about 670 a second in play), which is what made the phone build lag.
public class AtlasKeyTests
{
    private const uint Charcoal = 0x101316, Khaki = 0xd8cba3, Olive = 0xa3b35f, Navy = 0x4f5f8c, Wine = 0xa8324d;

    [Fact]
    public void The_first_colours_stay_exact_and_near_ones_fold_onto_them()
    {
        var snap = new ColourSnap();
        foreach (var c in new[] { Charcoal, Khaki, Olive, Navy, Wine }) Assert.Equal(c, snap[c]);
        Assert.Equal(Charcoal, snap[Charcoal + 0x050505]);
        Assert.Equal(Khaki, snap[Khaki - 0x0a0a0a]);
        Assert.Equal(5, snap.Count);
        // further than the tolerance on one channel is a colour of its own
        uint far = Olive + ((uint)ColourSnap.DefaultTolerance + 1 << 16);
        Assert.Equal(far, snap[far]);
        Assert.Equal(6, snap.Count);
    }

    [Fact]
    public void A_full_palette_gives_the_nearest_colour_and_grows_no_more()
    {
        var snap = new ColourSnap(tolerance: 4, capacity: 3);
        _ = snap[0x000000]; _ = snap[0x808080]; _ = snap[0xffffff];
        Assert.Equal(0x808080u, snap[0x9a9a9a]);
        Assert.Equal(0xffffffu, snap[0xe0e0e0]);
        Assert.Equal(3, snap.Count);
    }

    [Fact]
    public void A_snapped_encoder_sends_each_colour_pair_in_one_orientation_and_a_one_colour_quad_as_a_space()
    {
        var s = new Screen(3, 1);
        s.Clear(Charcoal);
        char left = QuadRenderer.GlyphFor(0b0101), right = QuadRenderer.GlyphFor(0b1010);
        s.Put(0, 0, left, Khaki, Navy);
        s.Put(1, 0, right, Navy, Khaki);
        s.Put(2, 0, left, Olive, Olive);
        var keys = Keys(DiffEncoder.Snapped().Encode(s), 3, 1);
        Assert.Equal(keys[0], keys[1]);
        Assert.Equal((' ', Olive), (keys[2].Ch, keys[2].Bg));
    }

    [Fact]
    public void The_game_sends_through_a_snapped_encoder()
    {
        var runner = new RaiderRunner(new RaiderGame(5), () => (60, 20), new NullSink());
        Assert.NotNull(runner.Encoder.Snap);
    }

    [Fact]
    public void Play_at_phone_size_mints_a_sixth_of_the_atlas_keys_it_did_unsnapped()
    {
        // a Pixel 9 Pro in portrait: 412 x 915 CSS pixels at the 12 px font
        const int cols = 56, rows = 58, frames = 90 * Tuning.Fps;
        var (snapped, plain) = (new HashSet<(char, uint, uint)>(), new HashSet<(char, uint, uint)>());
        int atTwoThirds = 0;
        Play(cols, rows, frames, DiffEncoder.Snapped(), snapped, f => { if (f == frames * 2 / 3) atTwoThirds = snapped.Count; });
        Play(cols, rows, frames, new DiffEncoder(), plain, _ => { });
        int lastThird = snapped.Count - atTwoThirds;
        // The bounds sit just above a measured 11.3k keys snapped (3.2k in the last 30 s) against 71k unsnapped. Snapped, a phone
        // at DPR 3 renews its atlas about every 40 s of full-speed play; unsnapped it was every 10 s, and the frames went
        // to rasterising. Past these bounds the phone build is heading back there.
        var seen = $"snapped {snapped.Count} ({lastThird} in the last 30 s), unsnapped {plain.Count}";
        Assert.True(plain.Count > 5 * snapped.Count, seen);
        Assert.True(snapped.Count < 14_000, seen);
        Assert.True(lastThird < 4_000, seen);
    }

    private static FeedPost Post(int n) =>
        new($"at://did:plc:p{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:p{n % 40}", $"p{n % 40}.test",
            $"post number {n} with a few words of text to read out loud", 10, 0, 0, null, null);

    private static void Play(int cols, int rows, int frames, DiffEncoder enc, HashSet<(char, uint, uint)> keys, Action<int> tick)
    {
        var g = new RaiderGame(5) { LoggedIn = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        var rnd = new Random(3);
        // avatars of every colour, the worst a feed can send
        g.AvatarWanted = p =>
        {
            var px = new uint[64];
            for (int i = 0; i < px.Length; i++) px[i] = (uint)rnd.Next(0x1000000);
            g.SetAvatar(p.AuthorDid, px);
        };
        var scr = new Screen(cols, rows);
        var view = new RaiderView(7);
        var term = new Term(cols, rows, keys);
        var dirs = new[] { GameKey.Left, GameKey.Right, GameKey.Up, GameKey.Down };
        int n = 0;
        for (int f = 0; f < frames; f++)
        {
            if (g.Queue.Count < 4) g.Queue.Offer(Post(++n));
            if (f % 12 == 0) foreach (var d in dirs) g.Input.Key(d, rnd.NextDouble() < 0.3);
            g.Input.Key(GameKey.Fire, true);
            g.Step();
            view.Render(g, scr);
            term.Feed(enc.Encode(scr));
            tick(f);
        }
    }

    private static (char Ch, uint Fg, uint Bg)[] Keys(string stream, int cols, int rows)
    {
        var keys = new List<(char, uint, uint)>();
        var t = new Term(cols, rows, null, keys);
        t.Feed(stream);
        return [.. keys];
    }

    private sealed class NullSink : IFrameSink
    {
        public void Write(string frame) { }
    }

    /// <summary>Just enough of a terminal to see what each printed cell's atlas key is: the glyph with the colours in force.</summary>
    private sealed class Term(int cols, int rows, HashSet<(char, uint, uint)>? seen, List<(char, uint, uint)>? order = null)
    {
        private int _r, _c;
        private uint _fg = uint.MaxValue, _bg = uint.MaxValue;

        public void Feed(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\x1b')
                {
                    if (_r < rows && _c < cols)
                    {
                        seen?.Add((s[i], _fg, _bg));
                        order?.Add((s[i], _fg, _bg));
                    }
                    _c++;
                    continue;
                }
                int j = i + 2;
                while (!char.IsLetter(s[j])) j++;
                var a = s[(i + 2)..j].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                switch (s[j])
                {
                    case 'H': _r = a[0] - 1; _c = a[1] - 1; break;
                    case 'C': _c += a.Length > 0 ? a[0] : 1; break;
                    case 'm': Sgr(a); break;
                }
                i = j;
            }
        }

        private void Sgr(int[] a)
        {
            if (a.Length == 0) { _fg = _bg = uint.MaxValue; return; }
            for (int k = 0; k < a.Length; k++)
            {
                switch (a[k])
                {
                    case 0: _fg = _bg = uint.MaxValue; break;
                    case 38: _fg = (uint)(a[k + 2] << 16 | a[k + 3] << 8 | a[k + 4]); k += 4; break;
                    case 48: _bg = (uint)(a[k + 2] << 16 | a[k + 3] << 8 | a[k + 4]); k += 4; break;
                }
            }
        }
    }
}
