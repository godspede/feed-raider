using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The Star Fox style wingman comms: who holds the mic, the typewriter, the wrap, where it sits, what the rest wear.
public class CommTests
{
    private const string Long = "this is a perfectly ordinary post with enough words in it to run well past three short lines of comm text";

    private static RaiderGame NewGame(int cols = 48, int rows = 60)
    {
        var g = new RaiderGame(5) { LoggedIn = true };
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
    public void The_text_types_out_at_the_comm_rate_then_holds_then_collapses()
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
        w.CommAge = WingComms.TotalFrames(w) - 1;
        Assert.True(WingComms.Openness(w) < 0.2);
        w.CommAge = reveal + 5;
        Assert.Equal(1, WingComms.Openness(w));
    }

    [Fact]
    public void The_comm_text_wraps_once_to_three_lines_with_an_ellipsis()
    {
        var g = TwoWingmen();
        var w = g.Comms.Speaker!;
        Assert.Equal(WingComms.TextLines, w.CommLines.Length);
        Assert.All(w.CommLines, l => Assert.True(l.Length <= WingComms.TextW));
        Assert.EndsWith("…", w.CommLines[^1]);
        Assert.Equal(w.CommLines.Sum(l => l.Length), w.CommChars);
        Assert.Equal("@t1.test", w.HandleTag);
    }

    [Fact]
    public void A_short_post_keeps_its_words_without_an_ellipsis()
    {
        var g = NewGame();
        g.OfferPost(Post(1, "just a short little post here"));
        RunUntil(g, () => g.Comms.Speaker is not null);
        Assert.DoesNotContain(g.Comms.Speaker!.CommLines, l => l.Contains('…'));
    }

    // ---------------------------------------------------------------- placement

    private static readonly CellRect Field = new(0, 0, 48, 60);

    [Fact]
    public void The_comm_sits_just_below_the_ship()
    {
        var r = CommPlacement.Place(24, 20, 26, WingComms.CommW, WingComms.CommH, Field, 2, [], out bool above);
        Assert.False(above);
        Assert.Equal(27, r.R);
        Assert.Equal(24 - WingComms.CommW / 2, r.C);
    }

    [Fact]
    public void The_comm_flips_above_when_it_would_run_off_the_bottom()
    {
        var r = CommPlacement.Place(24, 50, 56, WingComms.CommW, WingComms.CommH, Field, 2, [], out bool above);
        Assert.True(above);
        Assert.Equal(50 - WingComms.CommH - 1, r.R);
        Assert.True(r.Bottom <= 50);
    }

    [Fact]
    public void The_comm_is_clamped_to_the_field_and_clear_of_the_hud()
    {
        var left = CommPlacement.Place(0, 20, 26, WingComms.CommW, WingComms.CommH, Field, 2, [], out _);
        var right = CommPlacement.Place(47, 20, 26, WingComms.CommW, WingComms.CommH, Field, 2, [], out _);
        Assert.Equal(0, left.C);
        Assert.Equal(48, right.Right);
        var top = CommPlacement.Place(24, -4, 1, WingComms.CommW, WingComms.CommH, Field, 2, [], out _);
        Assert.True(top.R >= 2);
    }

    [Fact]
    public void The_comm_steers_clear_of_the_player_and_other_overlays()
    {
        var player = new CellRect(16, 26, 12, 8);
        var r = CommPlacement.Place(24, 20, 26, WingComms.CommW, WingComms.CommH, Field, 2, [player], out bool above);
        Assert.False(r.Overlaps(player));
        Assert.True(above || r.R > 26 || r.Right <= player.C || r.C >= player.Right);
        var wall = new[] { new CellRect(0, 27, 48, 6), new CellRect(0, 12, 48, 7) };
        var s = CommPlacement.Place(24, 20, 26, WingComms.CommW, WingComms.CommH, Field, 2, wall, out _);
        Assert.True(s.C >= 0 && s.Right <= 48 && s.R >= 2 && s.Bottom <= 60);   // boxed in: still inside the field
    }

    // ---------------------------------------------------------------- the picture

    [Fact]
    public void A_rendered_phone_screen_shows_the_comm_under_the_speaker_and_a_chip_on_the_other()
    {
        var g = TwoWingmen();
        g.Input.Key(GameKey.Down, true);                          // the player starts mid-field; park it at the bottom
        Run(g, WingComms.RevealFrames(g.Comms.Speaker!.CommChars) + 1);
        g.Input.Key(GameKey.Down, false);
        var view = new RaiderView();
        var scr = new Screen(48, 60);
        view.Render(g, scr);
        var sp = g.Comms.Speaker!;
        var other = g.Wings.Single(w => w != sp);
        int shipBottom = (int)((sp.Y + sp.Plane.H / 2.0) / 2) + 1;

        int handleRow = -1;
        for (int r = 0; r < scr.Rows; r++) if (Row(scr, r).Contains(sp.HandleTag)) handleRow = r;
        Assert.True(handleRow > shipBottom, $"handle row {handleRow} should be under the ship (bottom {shipBottom})");
        Assert.InRange(handleRow, shipBottom + 1, shipBottom + 3);
        Assert.Contains(sp.CommLines[0], Row(scr, handleRow + 1));
        Assert.Contains(sp.CommLines[2], Row(scr, handleRow + 3));
        Assert.Contains('┌', Row(scr, handleRow - 1));

        bool chip = false;
        for (int r = 0; r < scr.Rows; r++) chip |= Row(scr, r).Contains(other.ChipTag);
        Assert.True(chip, "the waiting wingman wears a chip");
        Assert.DoesNotContain(Enumerable.Range(0, scr.Rows), r => Row(scr, r).Contains(other.HandleTag));
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
        int top = 2 + s2.Slot * 13;
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
