using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The wide screen's left column is a feed log: every wingman's post stays, the newest at the bottom pushing the older
// ones up and off the top. It moves only when a post comes in (never on a timer), and a post dims once its wingman has gone.
public class FeedLogTests
{
    private const int Cols = 130, Rows = 46;

    private static string Words(int n) =>
        $"post number {n} opens here and keeps going with plenty of ordinary words so that it wraps over several rows and ends with tail{n}word";

    private static FeedPost Post(int n, string? text = null) =>
        new($"at://did:plc:b{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:b{n}", $"b{n}.test", text ?? Words(n), 10, 0, 0, null, null);

    private static void RunUntil(RaiderGame g, Func<bool> done, int max = 8000) { for (int i = 0; i < max && !done(); i++) g.Step(); Assert.True(done()); }

    private static RaiderGame Game(int posts)
    {
        var g = new RaiderGame(5) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(Cols, Rows));
        for (int i = 1; i <= posts; i++) g.OfferPost(Post(i));
        return g;
    }

    private static int C0(RaiderGame g) => g.Layout.Col0 - 28;

    private static string ColRow(Screen s, RaiderGame g, int r) => new(Enumerable.Range(C0(g), 26).Select(c => s.CharAt(c, r)).ToArray());

    /// <summary>The row of the log column showing <paramref name="text"/>, or -1.</summary>
    private static int RowOf(Screen s, RaiderGame g, string text) => Enumerable.Range(0, s.Rows).FirstOrDefault(r => ColRow(s, g, r).Contains(text), -1);

    /// <summary>The foreground of the first cell of <paramref name="text"/> in the log column.</summary>
    private static uint FgOf(Screen s, RaiderGame g, string text)
    {
        int r = RowOf(s, g, text);
        Assert.True(r >= 0, $"\"{text}\" is in the log");
        int c = C0(g) + ColRow(s, g, r).IndexOf(text, StringComparison.Ordinal);
        return s.Fg[r * s.Cols + c];
    }

    private static int Luma(uint c) => (int)((c >> 16 & 0xff) * 3 + (c >> 8 & 0xff) * 6 + (c & 0xff));

    private static Screen Render(RaiderView view, RaiderGame g)
    {
        var scr = new Screen(Cols, Rows);
        view.Render(g, scr);
        return scr;
    }

    private static void Settle(RaiderGame g) { for (int i = 0; i < 30; i++) g.Step(); }

    [Fact]
    public void New_posts_enter_at_the_bottom_and_push_the_old_ones_up_and_off_the_top()
    {
        var g = Game(8);
        var view = new RaiderView();
        RunUntil(g, () => g.Log.Entries.Count == 3);
        Settle(g);
        var scr = Render(view, g);
        int r1 = RowOf(scr, g, "@b1.test"), r2 = RowOf(scr, g, "@b2.test"), r3 = RowOf(scr, g, "@b3.test");
        Assert.True(r1 >= 0 && r1 < r2 && r2 < r3, $"oldest on top: {r1} {r2} {r3}");
        Assert.True(RowOf(scr, g, "tail3word") > r3, "the newest post ends at the bottom");

        RunUntil(g, () => g.Log.Entries.Count == 4);
        Settle(g);
        scr = Render(view, g);
        Assert.True(RowOf(scr, g, "@b2.test") < r2, "an arrival pushes the earlier posts up");
        Assert.True(RowOf(scr, g, "@b4.test") > RowOf(scr, g, "@b3.test"));

        RunUntil(g, () => g.Log.Entries.Count == 8);
        Settle(g);
        scr = Render(view, g);
        Assert.Equal(-1, RowOf(scr, g, "@b1.test"));     // scrolled off the top
        Assert.True(RowOf(scr, g, "@b8.test") > RowOf(scr, g, "@b7.test"));
        Assert.True(RowOf(scr, g, "@b7.test") > RowOf(scr, g, "@b6.test"));
    }

    [Fact]
    public void Nothing_moves_while_no_new_post_comes_in()
    {
        var g = Game(2);
        var view = new RaiderView();
        RunUntil(g, () => g.Log.Entries.Count == 2);
        Settle(g);
        var before = Render(view, g);
        var rows = Enumerable.Range(0, Rows).Select(r => ColRow(before, g, r)).ToArray();
        // the second wingman fights, leaves and is gone; no third post arrives
        for (int i = 0; i < 600; i++) { g.Step(); Render(view, g); }
        Assert.Empty(g.Wings);
        var after = Render(view, g);
        Assert.Equal(rows, Enumerable.Range(0, Rows).Select(r => ColRow(after, g, r)).ToArray());
    }

    [Fact]
    public void A_post_dims_once_its_wingman_has_left_and_the_speaker_is_brightest()
    {
        var g = Game(2);
        var view = new RaiderView();
        RunUntil(g, () => g.Log.Entries.Count == 1 && g.Comms.Speaker is not null);
        var w1 = g.Wings.Single();
        uint speaking = FgOf(Render(view, g), g, "tail1word");
        RunUntil(g, () => !g.Wings.Contains(w1));
        RunUntil(g, () => g.Log.Entries.Count == 2 && g.Comms.Speaker is not null);
        Settle(g);
        var scr = Render(view, g);
        uint gone = FgOf(scr, g, "tail1word"), now = FgOf(scr, g, "tail2word");
        Assert.True(Luma(gone) < Luma(speaking), "the post of a wingman that has gone dims");
        Assert.True(Luma(now) > Luma(gone), "the current speaker is brighter than a gone wingman's post");
        Assert.True(Luma(FgOf(scr, g, "@b2.test")) > Luma(FgOf(scr, g, "@b1.test")), "and so is its handle");
    }

    [Fact]
    public void A_post_dims_as_soon_as_its_wingman_flies_off_the_top()
    {
        var g = Game(1);
        var view = new RaiderView();
        RunUntil(g, () => g.Wings.Count == 1);
        var w = g.Wings.Single();
        uint on = FgOf(Render(view, g), g, "tail1word");
        RunUntil(g, () => w.Y + w.Plane.H / 2.0 < 0 || !g.Wings.Contains(w));
        Assert.True(Luma(FgOf(Render(view, g), g, "tail1word")) < Luma(on));
    }

    [Fact]
    public void Each_entry_shows_its_handle_likes_and_whole_post()
    {
        string text = string.Join(' ', Enumerable.Repeat("tomatoes ripen slowly in cool spring weather", 5)) + " and the very last word zebra";
        var g = new RaiderGame(5) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(Cols, Rows));
        g.OfferPost(Post(1, text));
        RunUntil(g, () => g.Wings.Count == 1);
        var w = g.Wings.Single();
        var scr = Render(new RaiderView(), g);
        Assert.True(RowOf(scr, g, "@b1.test") >= 0);
        Assert.True(RowOf(scr, g, w.LikesTag) >= 0);
        var lines = BubbleText.Format(text, 24, 200);
        Assert.True(lines.Length > 5);
        foreach (var l in lines) Assert.True(RowOf(scr, g, l) >= 0, $"\"{l}\" is shown");
        Assert.DoesNotContain(Enumerable.Range(0, Rows), r => ColRow(scr, g, r).Contains('…'));
    }

    [Fact]
    public void A_thread_continuation_grows_its_wingmans_entry()
    {
        var g = new RaiderGame(5) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(Cols, Rows));
        g.OfferPost(Post(1));
        RunUntil(g, () => g.Wings.Count == 1);
        var w = g.Wings.Single();
        g.ThreadOpened(w.Post.Uri, Post(1, "the reply in the same thread carries on and ends in giraffe") with { Uri = "at://did:plc:b1/app.bsky.feed.post/1b", Cid = "cid1b" });
        var scr = Render(new RaiderView(), g);
        Assert.Single(g.Log.Entries);
        int first = RowOf(scr, g, "tail1word"), reply = RowOf(scr, g, "giraffe");
        Assert.True(first >= 0 && reply > first, $"the reply is under the first post: {first} {reply}");
    }

    [Fact]
    public void A_narrow_screen_keeps_its_one_line_panel()
    {
        var g = new RaiderGame(5) { LoggedIn = true };
        g.SetLayout(FieldLayout.Compute(48, 60));
        g.OfferPost(Post(1));
        RunUntil(g, () => g.Wings.Count == 1 && g.Comms.Speaker is not null);
        var scr = new Screen(48, 60);
        new RaiderView().Render(g, scr);
        var row2 = new string(Enumerable.Range(0, 48).Select(c => scr.CharAt(c, 2)).ToArray());
        Assert.Contains(g.Wings[0].LikesTag, row2);
    }
}
