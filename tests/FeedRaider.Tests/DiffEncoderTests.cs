using System.Text;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using TUImation;

namespace FeedRaider.Tests;

/// <summary>A tiny terminal: just the sequences DiffEncoder may emit (CUP, CUF, CHA, CR/LF, SGR truecolor, ED 2), enough to replay a frame stream.</summary>
sealed class MiniTerm(int cols, int rows)
{
    public readonly char[] Ch = Enumerable.Repeat(' ', cols * rows).ToArray();
    public readonly uint[] Fg = new uint[cols * rows], Bg = new uint[cols * rows];
    private int _r, _c;
    private uint _fg = 0xFFFFFF, _bg;
    private bool _wrap;

    public void Feed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '\x1b' && i + 1 < s.Length && s[i + 1] == '[')
            {
                int j = i + 2;
                while (j < s.Length && (char.IsDigit(s[j]) || s[j] == ';')) j++;
                var p = s[(i + 2)..j].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                Csi(s[j], p);
                i = j;
            }
            else if (ch == '\r') { _c = 0; _wrap = false; }
            else if (ch == '\n') { _r = Math.Min(rows - 1, _r + 1); }
            else
            {
                if (_wrap) { _c = 0; _r = Math.Min(rows - 1, _r + 1); _wrap = false; }
                int o = _r * cols + _c;
                Ch[o] = ch; Fg[o] = _fg; Bg[o] = _bg;
                if (_c == cols - 1) _wrap = true; else _c++;
            }
        }
    }

    private void Csi(char f, int[] p)
    {
        _wrap = false;
        int Arg(int k, int d) => k < p.Length && p[k] != 0 ? p[k] : d;
        switch (f)
        {
            case 'H': _r = Math.Clamp(Arg(0, 1) - 1, 0, rows - 1); _c = Math.Clamp(Arg(1, 1) - 1, 0, cols - 1); break;
            case 'C': _c = Math.Min(cols - 1, _c + Arg(0, 1)); break;
            case 'G': _c = Math.Clamp(Arg(0, 1) - 1, 0, cols - 1); break;
            case 'J' when Arg(0, 0) == 2: Array.Fill(Ch, ' '); Array.Clear(Fg); Array.Clear(Bg); break;
            case 'm':
                if (p.Length == 0) p = [0];
                for (int k = 0; k < p.Length; k++)
                {
                    if (p[k] == 0) { _fg = 0xFFFFFF; _bg = 0; }
                    else if (p[k] is 38 or 48 && k + 4 < p.Length + 0 && p[k + 1] == 2)
                    {
                        uint c = (uint)(p[k + 2] << 16 | p[k + 3] << 8 | p[k + 4]);
                        if (p[k] == 38) _fg = c; else _bg = c;
                        k += 4;
                    }
                    else throw new InvalidOperationException("unexpected SGR " + p[k]);
                }
                break;
            default: throw new InvalidOperationException("unexpected CSI " + f);
        }
    }

    /// <summary>What the eye sees per cell: a quadrant glyph is its four colours (so an inverted glyph with swapped colours
    /// is the same picture), a space ignores the foreground, anything else is glyph + both colours.</summary>
    public static string Look(char ch, uint fg, uint bg)
    {
        int m = Array.IndexOf(Quads, ch);
        if (ch == ' ') return $"q{bg:x6}{bg:x6}{bg:x6}{bg:x6}";
        if (m >= 0)
            return "q" + string.Concat(Enumerable.Range(0, 4).Select(b => ((m >> b & 1) != 0 ? fg : bg).ToString("x6")));
        return $"{ch}{fg:x6}{bg:x6}";
    }

    public static readonly char[] Quads = Enumerable.Range(0, 16).Select(QuadRenderer.GlyphFor).ToArray();

    public string LookAt(int i) => Look(Ch[i], Fg[i], Bg[i]);
}

public class DiffEncoderTests
{
    private static void AssertSame(Screen scr, MiniTerm t, string where)
    {
        for (int i = 0; i < scr.Ch.Length; i++)
            Assert.True(MiniTerm.Look(scr.Ch[i], scr.Fg[i], scr.Bg[i]) == t.LookAt(i), $"{where}: cell {i} (col {i % scr.Cols}, row {i / scr.Cols}) differs");
    }

    [Fact]
    public void DiffedStreamReplaysToTheFullFrame()
    {
        const int cols = 100, rows = 46;
        var game = new RaiderGame(42);
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        var enc = new DiffEncoder();
        var term = new MiniTerm(cols, rows);
        game.LoggedIn = true;
        game.OfferPost(new FeedPost("at://did:plc:a/app.bsky.feed.post/1", "c1", "did:plc:a", "a.bsky.social", "just shipped the thing we have been working on for months, tell me what you think", 240, 3, 1, null, null));
        game.OfferDark(new DarkPost(DarknessKind.Spam, 11, "buy followers cheap click my profile now"));
        var inp = game.Input;
        for (int f = 0; f < 500; f++)
        {
            inp.Key(GameKey.Fire, true);
            inp.Key(GameKey.Left, f / 40 % 2 == 0); inp.Key(GameKey.Right, f / 40 % 2 == 1);
            game.Step();
            view.Render(game, scr);
            term.Feed(enc.Encode(scr));
            AssertSame(scr, term, $"frame {f}");
        }
    }

    [Fact]
    public void FirstFrameAndAFrameAfterInvalidateAreComplete()
    {
        var scr = new Screen(30, 10);
        scr.Clear(0x112233);
        scr.Text(2, 3, "hello", 0xffeedd, 0x445566);
        var enc = new DiffEncoder();
        var term = new MiniTerm(30, 10);
        term.Feed(enc.Encode(scr));
        AssertSame(scr, term, "first");
        scr.Text(2, 3, "J", 0x010203, 0x040506);
        var stale = new MiniTerm(30, 10); // a terminal that lost its contents
        enc.Invalidate();
        stale.Feed(enc.Encode(scr));
        AssertSame(scr, stale, "after invalidate");
    }

    [Fact]
    public void UnchangedScreenEncodesToNothing()
    {
        var scr = new Screen(30, 10);
        scr.Clear(0x112233);
        var enc = new DiffEncoder();
        enc.Encode(scr);
        Assert.Equal("", enc.Encode(scr));
    }

    [Fact]
    public void QuadrantCellsReplayWithEitherGlyphPolarity()
    {
        // alternating two-tone cells are where the encoder may flip a glyph and swap its colours to save a colour code
        var scr = new Screen(40, 6);
        scr.Clear(0);
        var rng = new Random(5);
        uint[] pal = [0x102030, 0x405060, 0x708090];
        var enc = new DiffEncoder();
        var term = new MiniTerm(40, 6);
        for (int round = 0; round < 50; round++)
        {
            for (int i = 0; i < scr.Ch.Length; i++)
            {
                if (rng.Next(3) != 0) continue;
                scr.Ch[i] = MiniTerm.Quads[rng.Next(16)];
                scr.Fg[i] = pal[rng.Next(3)]; scr.Bg[i] = pal[rng.Next(3)];
            }
            term.Feed(enc.Encode(scr));
            AssertSame(scr, term, $"round {round}");
        }
    }
}

public class QuadComposerTests
{
    [Fact]
    public void PackedReductionMatchesTheVendoredRenderer()
    {
        var rng = new Random(11);
        for (int n = 0; n < 20000; n++)
        {
            // few distinct palette entries so near and tied distances show up
            var px = new uint[4];
            for (int i = 0; i < 4; i++)
                px[i] = rng.Next(3) == 0 ? (uint)rng.Next(0x1000000) : (uint)((rng.Next(4) * 60) << 16 | (rng.Next(3) * 90) << 8 | rng.Next(2) * 200);
            if (px.Distinct().Count() < 3) continue;
            var rgb = px.Select(v => (Rgb?)Surface.Unpack(v)).ToArray();
            var (glyph, fg, bg) = QuadRenderer.Block(rgb);
            int mask = QuadComposer.SplitMask(px, out uint f, out uint b);
            Assert.Equal(glyph, QuadRenderer.GlyphFor(mask));
            Assert.Equal(Surface.Pack(fg!.Value), f);
            Assert.Equal(Surface.Pack(bg!.Value), b);
        }
    }

    [Fact]
    public void ComposeMatchesPerCellReductionUnderShakeAndOffset()
    {
        var rng = new Random(3);
        var s = new Surface(16, 12);
        var quad = new QuadComposer();
        var scr = new Screen(12, 8);
        for (int round = 0; round < 40; round++)
        {
            for (int i = 0; i < s.P.Length; i++) if (rng.Next(3) == 0) s.P[i] = (uint)rng.Next(4) * 0x303030;
            int ox = round % 3 == 0 ? 0 : rng.Next(-3, 4), oy = round % 3 == 0 ? 0 : rng.Next(-3, 4);
            int col0 = rng.Next(-2, 6), row0 = rng.Next(-1, 3);
            scr.Clear(0);
            quad.Compose(s, ox, oy, scr, col0, row0);
            for (int cy = 0; cy < 6; cy++)
                for (int cx = 0; cx < 8; cx++)
                {
                    int sc = col0 + cx, sr = row0 + cy;
                    if (sc < 0 || sc >= scr.Cols || sr < 0 || sr >= scr.Rows) continue;
                    uint P(int dx, int dy) => s.P[Math.Clamp(cy * 2 + dy - oy, 0, 11) * 16 + Math.Clamp(cx * 2 + dx - ox, 0, 15)];
                    var px = new Rgb?[] { Surface.Unpack(P(0, 0)), Surface.Unpack(P(1, 0)), Surface.Unpack(P(0, 1)), Surface.Unpack(P(1, 1)) };
                    var (g, fg, bg) = QuadRenderer.Block(px);
                    int t = sr * scr.Cols + sc;
                    Assert.Equal(MiniTerm.Look(g, Surface.Pack(fg!.Value), Surface.Pack(bg!.Value)), MiniTerm.Look(scr.Ch[t], scr.Fg[t], scr.Bg[t]));
                }
        }
    }
}

public class AdaptiveQualityTests
{
    // One 0.5 s window as the runner would report it: `fps` frames drawn per second, all ticks, `pushed` of them pushed back.
    private static void Win(AdaptiveQuality q, double fps, double pushedShare = 0, int n = 1)
    {
        for (int i = 0; i < n; i++) q.Update((int)Math.Round(fps / 2), 15, (int)Math.Round(15 * pushedShare), 0.5);
    }

    [Fact]
    public void StaysAtFullRateWhenFramesKeepUp()
    {
        var q = new AdaptiveQuality();
        Win(q, 30, 0, 100);
        Assert.Equal(1, q.Divisor);
        Win(q, 28, 0.1, 100); // slightly short but nobody is pushing back
        Assert.Equal(1, q.Divisor);
    }

    [Fact]
    public void IdleThreadIsNeverThrottledForSlowFramesAlone()
    {
        // work ms is not an input at all: only drawn throughput and pushback are
        var q = new AdaptiveQuality();
        Win(q, 30, 0.05, 50);
        Assert.Equal(1, q.Divisor);
    }

    [Fact]
    public void StepsDownOneLevelOnSustainedBacklogThatLowerRateRelieves()
    {
        var q = new AdaptiveQuality();
        Win(q, 9, 0.8, AdaptiveQuality.DownWindows);
        Assert.Equal(2, q.Divisor);          // one level only
        Win(q, 15, 0, AdaptiveQuality.TrialWindows);
        Assert.Equal(2, q.Divisor);          // trial passed: 15 fps beats 9
    }

    [Fact]
    public void RevertsWhenTheLowerLevelDrawsNoMoreAndBacksOff()
    {
        var q = new AdaptiveQuality();
        Win(q, 9, 0.8, AdaptiveQuality.DownWindows);
        Assert.Equal(2, q.Divisor);
        Win(q, 8, 0.8, AdaptiveQuality.TrialWindows); // fewer fps than before
        Assert.Equal(1, q.Divisor);
        Win(q, 9, 0.8, AdaptiveQuality.MinCooldown - 1); // held off while cooling down
        Assert.Equal(1, q.Divisor);
        Win(q, 9, 0.8, AdaptiveQuality.DownWindows + 1);
        Assert.Equal(2, q.Divisor);          // tries again
        Win(q, 8, 0.8, AdaptiveQuality.TrialWindows);
        Assert.Equal(1, q.Divisor);
    }

    [Fact]
    public void NeverSettlesOnADividerThatDrawsFewerFpsThanFullRate()
    {
        // a device where drawing is the bottleneck at any divider: fps is 9 whatever we ask for
        var q = new AdaptiveQuality();
        int atOne = 0, total = 400;
        for (int i = 0; i < total; i++) { Win(q, 9, 0.8); if (q.Divisor == 1) atOne++; }
        Assert.True(atOne > total * 0.8, $"spent {atOne}/{total} windows at /1");
    }

    [Fact]
    public void ProbesBackUpWithinSecondsOnceThereIsHeadroom()
    {
        var q = new AdaptiveQuality();
        Win(q, 9, 0.8, AdaptiveQuality.DownWindows);
        Win(q, 15, 0, AdaptiveQuality.TrialWindows);
        Assert.Equal(2, q.Divisor);
        for (int i = 0; i < AdaptiveQuality.ProbeWindows + AdaptiveQuality.TrialWindows; i++) Win(q, 30.0 / q.Divisor); // fast again: full rate is reachable
        Assert.Equal(1, q.Divisor);
        Assert.True((AdaptiveQuality.ProbeWindows + AdaptiveQuality.TrialWindows) * 0.5 <= 5);
    }

    [Fact]
    public void ProbeThatFailsGoesBackDownAndCapsAtMaxDivisor()
    {
        var q = new AdaptiveQuality();
        for (int i = 0; i < 400; i++) Win(q, q.Divisor == 1 ? 6 : 30.0 / q.Divisor, q.Divisor == 1 ? 0.9 : 0);
        Assert.InRange(q.Divisor, 2, AdaptiveQuality.MaxDivisor);
    }
}
