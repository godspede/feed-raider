using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The comm box under each wingman: the whole post (never a two-word chip), pinned below its ship at one offset,
// one size for the life of a post, cropped by the field's edge, and drawn beneath the ships and bullets.
public class CommBoxTests
{
    private const string Long = "this is a perfectly ordinary post with enough words in it to run well past three short lines of comm text";

    private static FeedPost Post(int n, string text = Long) =>
        new($"at://did:plc:b{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:b{n}", $"b{n}.test", text, 10, 0, 0, null, null);

    private static void RunUntil(RaiderGame g, Func<bool> done, int max = 3000) { for (int i = 0; i < max && !done(); i++) g.Step(); Assert.True(done()); }

    private static string Row(Screen s, int r) => new(Enumerable.Range(0, s.Cols).Select(c => s.CharAt(c, r)).ToArray());

    private static bool Shows(Screen s, string text) => Enumerable.Range(0, s.Rows).Any(r => Row(s, r).Contains(text));

    /// <summary>A game with <paramref name="posts"/> wingmen on screen; the player starts low in the field, out of their way.</summary>
    private static RaiderGame Game(int cols, int rows, params string[] posts)
    {
        var g = new RaiderGame(5) { LoggedIn = true, MaxWings = posts.Length };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        for (int i = 0; i < posts.Length; i++) g.OfferPost(Post(i + 1, posts[i]));
        RunUntil(g, () => g.Wings.Count == posts.Length && g.Comms.Speaker is not null);
        return g;
    }

    /// <summary>Renders with nothing else in the air (the shots, wrecks and sparks of the frames run so far are cleared) unless <paramref name="quiet"/> is false.</summary>
    private static Screen Render(RaiderGame g, int cols, int rows, bool quiet = true)
    {
        if (quiet) { g.Enemies.Clear(); g.Bullets.Clear(); g.Pickups.Clear(); g.Particles.Clear(); g.Rings.Clear(); }
        var scr = new Screen(cols, rows);
        new RaiderView().Render(g, scr);
        return scr;
    }

    // ---------------------------------------------------------------- the whole post, never two words

    [Theory]
    [InlineData(48, 60)]     // a phone
    [InlineData(130, 46)]    // a desktop
    public void A_waiting_wingman_shows_its_post_not_its_first_two_words(int cols, int rows)
    {
        var g = Game(cols, rows, Long, "a second wingman with its own post to read out in full");
        var sp = g.Comms.Speaker!;
        var other = g.Wings.Single(w => w != sp);
        Assert.Equal(CommState.Waiting, other.Comm);
        sp.X = g.Layout.PW / 2.0; sp.Y = 16;
        other.X = g.Layout.PW / 2.0; other.Y = 44;   // clear of the player's ship, low in the field
        var scr = Render(g, cols, rows);
        Assert.True(Shows(scr, other.HandleTag), "the waiting wingman's box carries its handle");
        foreach (var line in other.CommLines) Assert.True(Shows(scr, line), $"the waiting wingman's box shows \"{line}\"");
    }

    [Fact]
    public void A_wingman_that_has_spoken_still_shows_its_whole_post()
    {
        var g = Game(48, 60, Long);
        var w = g.Wings.Single();
        RunUntil(g, () => w.Comm == CommState.Spoken || w.Phase == WingPhase.Leave);
        w.X = g.Layout.PW / 2.0; w.Y = 24;
        var scr = Render(g, 48, 60);
        foreach (var line in w.CommLines) Assert.True(Shows(scr, line), $"\"{line}\" is still on show");
        Assert.DoesNotContain(Enumerable.Range(0, scr.Rows), r => Row(scr, r).Contains("thi…"));
    }

    [Fact]
    public void A_post_with_line_breaks_and_emoji_is_shown_whole()
    {
        string text = "first line\nsecond line \U0001F600 here\n\nthird line after a gap and then a few more words";
        var g = Game(48, 60, text);
        var w = g.Wings.Single();
        w.X = g.Layout.PW / 2.0; w.Y = 24;
        w.CommAge = WingComms.TotalFrames(w) - Tuning.CommCollapseFrames - 1;   // typed out
        var scr = Render(g, 48, 60);
        Assert.True(w.CommLines.Length >= 2);
        foreach (var line in w.CommLines) Assert.True(Shows(scr, line), $"\"{line}\" is on show");
        Assert.Contains("third line after", string.Join(' ', w.CommLines));
    }

    // ---------------------------------------------------------------- pinned below the ship

    [Fact]
    public void The_box_sits_at_one_offset_below_its_ship_wherever_the_ship_is()
    {
        var g = Game(48, 60, Long);
        var w = g.Wings.Single();
        var lay = g.Layout;
        (int, int)? offset = null;
        // middle, both side edges, the bottom edge and the corners
        foreach (var (x, y) in new[] { (lay.PW / 2.0, 30.0), (2.0, 30.0), (lay.PW - 2.0, 30.0), (lay.PW / 2.0, lay.PH - 12.0),
                                       (2.0, lay.PH - 4.0), (lay.PW - 2.0, lay.PH - 20.0), (lay.PW / 2.0, 12.0), (31.0, 47.0) })
        {
            w.X = x; w.Y = y;
            var r = CommPainter.Box(w, lay);
            int shipCol = lay.Col0 + (int)Math.Floor(x / 2), shipRow = (int)Math.Floor(y / 2);
            var o = (r.C - shipCol, r.R - shipRow);
            offset ??= o;
            Assert.Equal(offset, o);
            Assert.True(r.R > (int)((y + w.Plane.H / 2.0) / 2), "below the ship, never flipped above it");

            var scr = Render(g, 48, 60);
            // the box's top edge is drawn on its row wherever that row is in the field; it never moves to stay on screen
            if (r.R < scr.Rows)
                for (int c = Math.Max(r.C + 1, lay.Col0); c < Math.Min(r.Right - 1, lay.Col0 + lay.PCols); c++)
                    Assert.True(scr.CharAt(c, r.R) is '─' or '▲', $"top edge at ({c},{r.R}) for a ship at ({x},{y}) reads '{scr.CharAt(c, r.R)}'");
            // and nothing of it lands above its own row (a flipped or clamped box would)
            for (int row = 3; row < Math.Min(r.R, scr.Rows); row++)   // row 2 is the phone panel line
                Assert.DoesNotContain(w.HandleTag, Row(scr, row));
        }
    }

    [Fact]
    public void The_field_edge_crops_the_box()
    {
        var g = Game(48, 60, Long);
        var w = g.Wings.Single();
        var lay = g.Layout;
        w.X = 2; w.Y = lay.PH - 6;
        var r = CommPainter.Box(w, lay);
        Assert.True(r.C < lay.Col0, "the box hangs off the left edge");
        Assert.True(r.Bottom > lay.Rows, "and off the bottom");
        var scr = Render(g, 48, 60);
        Assert.DoesNotContain(Enumerable.Range(3, scr.Rows - 3), row => Row(scr, row).Contains(w.HandleTag));   // the cropped header is not drawn elsewhere
    }

    // ---------------------------------------------------------------- one size for a post's life

    [Theory]
    [InlineData(Long)]
    [InlineData("a short post, one line")]
    public void The_box_keeps_one_size_from_arrival_to_departure(string text)
    {
        var g = new RaiderGame(5) { LoggedIn = true, MaxWings = 1 };
        g.SetLayout(FieldLayout.Compute(48, 60));
        g.OfferPost(Post(1, text));
        RunUntil(g, () => g.Wings.Count == 1);
        var w = g.Wings[0];
        var view = new RaiderView();
        var scr = new Screen(48, 60);
        var lay = g.Layout;
        var size = (CommPainter.Box(w, lay).W, CommPainter.Box(w, lay).H);
        int corners = 0;
        for (int f = 0; f < 3000 && g.Wings.Contains(w); f++)
        {
            g.Step();
            g.Enemies.Clear(); g.Bullets.Clear(); g.Particles.Clear(); g.Rings.Clear();   // nothing in the air but the ships
            view.Render(g, scr);
            var r = CommPainter.Box(w, lay);
            Assert.Equal(size, (r.W, r.H));
            // every corner in the field is drawn where the box says, from the first frame to the last: no opening or collapsing
            foreach (var (c, row, ch) in new[] { (r.C, r.R, '┌'), (r.Right - 1, r.R, '┐'), (r.C, r.Bottom - 1, '└'), (r.Right - 1, r.Bottom - 1, '┘') })
            {
                if (c < lay.Col0 || c >= lay.Col0 + lay.PCols || row < 2 || row >= lay.Rows) continue;
                char got = scr.CharAt(c, row);
                uint boxBg = Surface.Mix(0x0a1426, w.Accent, 0.14);
                if (got != ch && scr.BgAt(c, row) != boxBg) continue;   // a ship over the corner shows through
                Assert.True(got == ch, $"frame {f}: '{ch}' expected at ({c},{row}), got '{got}' bg {scr.BgAt(c, row):x6} box bg {boxBg:x6}");
                corners++;
            }
        }
        Assert.True(corners > 50, $"{corners} corners checked");
    }

    // ---------------------------------------------------------------- beneath the action

    [Fact]
    public void Bullets_and_ships_inside_the_box_show_over_it()
    {
        var g = Game(48, 60, Long);
        var w = g.Wings.Single();
        var lay = g.Layout;
        w.X = lay.PW / 2.0; w.Y = 20;
        w.CommAge = WingComms.TotalFrames(w) - Tuning.CommCollapseFrames - 1;
        var r = CommPainter.Box(w, lay);
        // an enemy bullet filling the one cell at (r.C + 8, r.R + 2), a text cell of the box
        int bc = r.C + 8 - lay.Col0, br = r.R + 2;
        g.Enemies.Clear(); g.Bullets.Clear(); g.Pickups.Clear(); g.Particles.Clear(); g.Rings.Clear();
        g.Bullets.Add(new Bullet { X = bc * 2 + 1, Y = br * 2 + 1 });
        var scr = Render(g, 48, 60, quiet: false);
        int i = br * scr.Cols + r.C + 8;
        Assert.True(scr.Fg[i] is 0xff3d6e or 0xffffff || scr.Bg[i] is 0xff3d6e or 0xffffff,
            $"the bullet's cell shows the bullet, not the box (fg {scr.Fg[i]:x6} bg {scr.Bg[i]:x6} '{scr.Ch[i]}')");
        // the box's own text still reads where nothing covers it
        Assert.Contains(w.HandleTag, Row(scr, r.R + 1));

        // a second wingman flying through the box shows its ship, not the box
        var g2 = Game(48, 60, Long, "another post to fly over the first box");
        var a = g2.Comms.Speaker!; var b = g2.Wings.Single(x => x != a);
        a.X = lay.PW / 2.0; a.Y = 20;
        var ra = CommPainter.Box(a, g2.Layout);
        b.X = lay.PW / 2.0; b.Y = (ra.R + 3) * 2;
        var s2 = Render(g2, 48, 60);
        var boxBg = s2.BgAt(ra.C + 1, ra.R + 1);
        int sc = g2.Layout.Col0 + (int)(b.X / 2), sr = (int)(b.Y / 2);
        Assert.True(sr > ra.R && sr < ra.Bottom - 1, "the ship's centre is inside the box");
        Assert.False(s2.BgAt(sc, sr) == boxBg && s2.CharAt(sc, sr) == ' ', "the ship's centre cell shows the ship, not the box");
    }
}
