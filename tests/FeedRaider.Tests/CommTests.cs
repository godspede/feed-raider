using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The Star Fox style wingman comms: who holds the mic, the typewriter, the wrap, the picture. Where the box sits: CommBoxTests.
public class CommTests
{
    private const string Long = "this is a perfectly ordinary post with enough words in it to run well past three short lines of comm text";

    private static RaiderGame NewGame(int cols = 48, int rows = 60, int wings = 2)
    {
        // two at once, so the comm queue (one speaker at a time, in arrival order) is still exercised
        var g = new RaiderGame(5) { LoggedIn = true, MaxWings = wings };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        return g;
    }

    private static FeedPost Post(int n, string text = Long) =>
        new($"at://did:plc:t{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:t{n}", $"t{n}.test", text, 10, 0, 0, null, null);

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    private static void RunUntil(RaiderGame g, Func<bool> done, int max = 3000) { for (int i = 0; i < max && !done(); i++) g.Step(); Assert.True(done()); }

    private static string Row(Screen s, int r) => new(Enumerable.Range(0, s.Cols).Select(c => s.CharAt(c, r)).ToArray());

    private static RaiderGame TwoWingmen()
    {
        var g = NewGame();
        g.OfferPost(Post(1)); g.OfferPost(Post(2));
        RunUntil(g, () => g.Wings.Count == 2 && g.Comms.Speaker is not null);
        return g;
    }

    // ---------------------------------------------------------------- the queue

    [Fact]
    public void One_wingman_speaks_at_a_time_in_arrival_order_and_the_next_follows_the_collapse()
    {
        var g = TwoWingmen();
        var first = g.Comms.Speaker!;
        Assert.Equal(0, first.CommSeq);
        var second = g.Wings.Single(w => w != first);
        Assert.Equal(CommState.Speaking, first.Comm);
        Assert.Equal(CommState.Waiting, second.Comm);
        while (first.CommAge < WingComms.TotalFrames(first) - 2)
        {
            g.Step();
            Assert.Same(first, g.Comms.Speaker);
            Assert.Equal(1, g.Wings.Count(w => w.Comm == CommState.Speaking));
        }
        RunUntil(g, () => first.Comm == CommState.Spoken);
        Assert.Same(second, g.Comms.Speaker);
        Assert.Equal(CommState.Speaking, second.Comm);
    }

    [Fact]
    public void A_wingman_leaving_mid_comm_frees_the_slot()
    {
        var g = TwoWingmen();
        var first = g.Comms.Speaker!;
        var second = g.Wings.Single(w => w != first);
        Run(g, 10);
        g.Wings.Remove(first);
        g.Step();
        Assert.Equal(CommState.Spoken, first.Comm);
        RunUntil(g, () => g.Comms.Speaker is not null);
        Assert.Same(second, g.Comms.Speaker);                   // it takes the mic as soon as it is on screen
    }

    // ---------------------------------------------------------------- typewriter and wrap

    [Fact]
    public void The_text_types_out_at_the_comm_rate_then_holds_then_hands_over()
    {
        Assert.Equal(0, WingComms.Revealed(0, 40));
        Assert.Equal(Tuning.CommCharsPerSecond, WingComms.Revealed(Tuning.Fps, 100));
        Assert.Equal(40, WingComms.Revealed(10 * Tuning.Fps, 40));            // never past the end
        var g = TwoWingmen();
        var w = g.Comms.Speaker!;
        int reveal = WingComms.RevealFrames(w.CommChars);
        Assert.Equal(w.CommChars, WingComms.Revealed(reveal, w.CommChars));
        Assert.True(WingComms.Revealed(reveal - 2, w.CommChars) < w.CommChars);
        Assert.Equal(reveal + Tuning.CommHoldFrames + Tuning.CommCollapseFrames, WingComms.TotalFrames(w));
        double hold = Tuning.CommHoldFrames / (double)Tuning.Fps;
        Assert.InRange(hold, 4, 5);                                          // "about 4-5 s"
    }

    [Fact]
    public void A_post_of_the_full_length_fits_one_page_at_the_widest_comm()
    {
        var g = NewGame();
        string text = string.Join(' ', Enumerable.Repeat("word wordier wordiest", 20))[..300];
        g.OfferPost(Post(1, text));
        RunUntil(g, () => g.Comms.Speaker is not null);
        var w = g.Comms.Speaker!;
        Assert.Equal(WingComms.MaxTextW, w.CommTextW);
        Assert.InRange(w.CommLines.Length, 1, WingComms.TextLines);
        Assert.Equal(1, WingComms.Pages(w));
        Assert.All(w.CommLines, l => Assert.True(l.Length <= w.CommTextW));
        Assert.DoesNotContain(w.CommLines, l => l.Contains('…'));
        Assert.Equal(w.CommLines.Sum(l => l.Length), w.CommChars);
        Assert.Equal("@t1.test", w.HandleTag);
    }

    [Fact]
    public void A_post_of_long_words_is_paged_and_every_word_is_read()
    {
        var g = NewGame();
        string text = string.Join(' ', Enumerable.Repeat("sixteencharwords", 18))[..300];
        g.OfferPost(Post(1, text));
        RunUntil(g, () => g.Comms.Speaker is not null);
        var w = g.Comms.Speaker!;
        Assert.True(WingComms.Pages(w) > 1);
        Assert.Equal(text.Replace(" ", ""), string.Concat(w.CommLines).Replace(" ", ""));
        var seen = new HashSet<int>();
        for (int age = 0; age < WingComms.TotalFrames(w); age++) seen.Add(WingComms.Progress(w, age).Page);
        Assert.Equal(WingComms.Pages(w), seen.Count);
        var (last, shown) = WingComms.Progress(w, WingComms.TotalFrames(w) - 1);
        Assert.Equal(WingComms.Pages(w) - 1, last);
        Assert.Equal(WingComms.PageChars(w, last), shown);
        Assert.True(WingComms.CommH(w) <= WingComms.CommChrome + WingComms.TextLines);
    }

    [Fact]
    public void A_narrow_field_gets_a_narrow_comm_and_a_short_post_a_short_one()
    {
        Assert.Equal(WingComms.MinTextW, WingComms.TextWidth(30));
        Assert.Equal(WingComms.MaxTextW, WingComms.TextWidth(64));
        var g = NewGame(30, 40);
        g.OfferPost(Post(1));
        RunUntil(g, () => g.Comms.Speaker is not null);
        var w = g.Comms.Speaker!;
        Assert.True(WingComms.CommW(w) <= 30);
        var g2 = NewGame();
        g2.OfferPost(Post(1, "just a short little post here"));
        RunUntil(g2, () => g2.Comms.Speaker is not null);
        Assert.Equal(WingComms.CommChrome + 1, WingComms.CommH(g2.Comms.Speaker!));
    }

    [Fact]
    public void A_short_post_keeps_its_words_without_an_ellipsis()
    {
        var g = NewGame();
        g.OfferPost(Post(1, "just a short little post here"));
        RunUntil(g, () => g.Comms.Speaker is not null);
        Assert.DoesNotContain(g.Comms.Speaker!.CommLines, l => l.Contains('…'));
    }

    // ---------------------------------------------------------------- the picture

    [Fact]
    public void A_rendered_phone_screen_shows_a_comm_under_each_wingman()
    {
        var g = TwoWingmen();
        g.Input.Key(GameKey.Down, true);                          // the player starts mid-field; park it at the bottom
        Run(g, WingComms.RevealFrames(g.Comms.Speaker!.CommChars) + 1);
        g.Input.Key(GameKey.Down, false);
        g.Enemies.Clear(); g.Bullets.Clear(); g.Particles.Clear(); g.Rings.Clear();   // the comm is background: shots would show over it
        var view = new RaiderView();
        var scr = new Screen(48, 60);
        view.Render(g, scr);
        var sp = g.Comms.Speaker!;
        var other = g.Wings.Single(w => w != sp);
        sp.X = other.X = g.Layout.PW / 2.0; sp.Y = 24; other.Y = 64;   // mid-field: a ship near a side has its comm cropped
        view.Render(g, scr);
        int shipBottom = (int)((sp.Y + sp.Plane.H / 2.0) / 2) + 1;

        int handleRow = -1;
        for (int r = 0; r < scr.Rows; r++) if (Row(scr, r).Contains(sp.HandleTag)) handleRow = r;
        Assert.True(handleRow > shipBottom, $"handle row {handleRow} should be under the ship (bottom {shipBottom})");
        Assert.InRange(handleRow, shipBottom + 1, shipBottom + 3);
        Assert.Contains(sp.CommLines[0], Row(scr, handleRow + 1));
        Assert.Contains(sp.CommLines[2], Row(scr, handleRow + 3));
        Assert.Contains('┌', Row(scr, handleRow - 1));

        var ro = CommPainter.Box(other, g.Layout);
        if (ro.R + 1 >= 2 && ro.R + 1 < scr.Rows && ro.C + 6 >= g.Layout.Col0 && ro.C + 6 + other.HandleTag.Length <= g.Layout.Col0 + g.Layout.PCols)
            Assert.Contains(other.HandleTag, Row(scr, ro.R + 1));   // the waiting wingman has its own comm, not a chip
    }

    [Fact]
    public void The_phone_panel_drops_to_one_line_and_the_wide_panel_marks_the_speaker()
    {
        var g = TwoWingmen();
        var sp = g.Comms.Speaker!;
        var view = new RaiderView();
        var phone = new Screen(48, 60);
        view.Render(g, phone);
        Assert.Contains(sp.LikesTag, Row(phone, 2));
        Assert.DoesNotContain("\u2665", Row(phone, 4));   // \u2665 heart suit
        var wide = new Screen(130, 46);
        var gw = NewGame(130, 46);
        gw.OfferPost(Post(1)); gw.OfferPost(Post(2));
        RunUntil(gw, () => gw.Wings.Count == 2 && gw.Comms.Speaker is not null);
        view.Render(gw, wide);
        var s2 = gw.Comms.Speaker!;
        int mx = gw.Layout.Col0 - 26 - 2;
        int top = Enumerable.Range(2, 40).First(r => wide.CharAt(mx, r) == '▌' && wide.Fg[r * wide.Cols + mx] == s2.Accent);
        Assert.Equal('▌', wide.CharAt((gw.Layout.Col0 - 26 - 2), top));
        Assert.Equal(s2.Accent, wide.Fg[top * wide.Cols + (gw.Layout.Col0 - 26 - 2)]);
    }

    [Fact]
    public void ChipShowsThePostsOpeningWordsNotAStatus()
    {
        Assert.Equal("gm everyone", CommPainter.Snippet("gm \n\t everyone", 20));
        Assert.Equal("the quick…", CommPainter.Snippet("the quick brown fox", 10));
        Assert.Equal("", CommPainter.Snippet("anything", 0));
    }
}
