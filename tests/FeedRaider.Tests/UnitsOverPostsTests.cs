using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Ships, enemies, pickups and shots draw above the post boxes: where their pixels cross a box the cell shows them, not the text.
public class UnitsOverPostsTests
{
    private const int Cols = 48, Rows = 60;
    private const string Post = "this is a perfectly ordinary post with enough words in it to run well past three short lines of comm text";

    private static (RaiderGame g, Wingman w) Game()
    {
        var g = new RaiderGame(5) { LoggedIn = true, MaxWings = 1 };
        g.SetLayout(FieldLayout.Compute(Cols, Rows));
        g.Queue.Offer(new FeedPost("at://did:plc:b1/app.bsky.feed.post/1", "cid1", "did:plc:b1", "b1.test", Post, 10, 0, 0, null, null));
        for (int i = 0; i < 3000 && !(g.Wings.Count == 1 && g.Comms.Speaker is not null); i++) g.Step();
        var w = g.Wings.Single();
        w.X = g.Layout.PW / 2.0; w.Y = 16;
        SetPlayer(g, g.Layout.PW - 8, g.Layout.PH - 8);   // the player's ship out of the way
        Clear(g);
        return (g, w);
    }

    private static void SetPlayer(RaiderGame g, double x, double y)
    {
        typeof(RaiderGame).GetProperty("PX")!.GetSetMethod(true)!.Invoke(g, [x]);
        typeof(RaiderGame).GetProperty("PY")!.GetSetMethod(true)!.Invoke(g, [y]);
    }

    private static void Clear(RaiderGame g) { g.Enemies.Clear(); g.Bullets.Clear(); g.Pickups.Clear(); g.Particles.Clear(); g.Rings.Clear(); }

    private static Screen Render(RaiderGame g)
    {
        var scr = new Screen(Cols, Rows);
        new RaiderView().Render(g, scr);
        return scr;
    }

    /// <summary>A cell inside the box that carries a letter of the post.</summary>
    private static (int c, int r) LetterCell(Screen scr, CellRect box)
    {
        for (int r = box.R + 2; r < box.Bottom - 1; r++)
            for (int c = box.C + 8; c < box.Right - 2; c++)
                if (char.IsLetterOrDigit(scr.CharAt(c, r))) return (c, r);
        throw new Xunit.Sdk.XunitException("no letter in the box");
    }

    [Fact]
    public void A_friendly_bullet_over_a_comm_box_shows_instead_of_the_text()
    {
        var (g, w) = Game();
        var box = CommPainter.Box(w, g.Layout);
        var (c, r) = LetterCell(Render(g), box);
        g.Bullets.Add(new Bullet { X = (c - g.Layout.Col0) * 2, Y = r * 2 + 2, Friendly = true });
        var scr = Render(g);
        Assert.False(char.IsLetterOrDigit(scr.CharAt(c, r)), "the shot's cell is no longer a letter");
        Assert.True(scr.Fg[r * Cols + c] == Palette.Ice || scr.Bg[r * Cols + c] == Palette.Ice, "the cell carries the shot's colour");
        Assert.Contains(Enumerable.Range(0, Rows).Select(y => new string(Enumerable.Range(0, Cols).Select(x => scr.CharAt(x, y)).ToArray())), l => l.Contains(w.HandleTag));
    }

    [Fact]
    public void An_enemy_bullet_over_a_comm_box_shows_instead_of_the_text()
    {
        var (g, w) = Game();
        var box = CommPainter.Box(w, g.Layout);
        var (c, r) = LetterCell(Render(g), box);
        g.Bullets.Add(new Bullet { X = (c - g.Layout.Col0) * 2 + 1, Y = r * 2 + 1, Friendly = false });
        var scr = Render(g);
        Assert.False(char.IsLetterOrDigit(scr.CharAt(c, r)));
        Assert.True(scr.Fg[r * Cols + c] == Palette.Crimson || scr.Bg[r * Cols + c] == Palette.Crimson);
    }

    [Fact]
    public void The_players_ship_over_a_comm_box_shows_instead_of_the_text()
    {
        var (g, w) = Game();
        var box = CommPainter.Box(w, g.Layout);
        var control = Render(g);
        var (c, r) = LetterCell(control, box);
        SetPlayer(g, (c - g.Layout.Col0) * 2 + 1, r * 2 + 1);
        var scr = Render(g);
        Assert.False(char.IsLetterOrDigit(scr.CharAt(c, r)), "the ship's cell is not a letter");
        Assert.NotEqual(control.Fg[r * Cols + c], scr.Fg[r * Cols + c]);
        // the box is still there around the ship: its top edge and pointer
        Assert.Equal(control.CharAt(box.C, box.R), scr.CharAt(box.C, box.R));
    }

    [Fact]
    public void Cells_with_no_unit_in_them_keep_the_text()
    {
        var (g, w) = Game();
        var box = CommPainter.Box(w, g.Layout);
        var control = Render(g);
        var (c, r) = LetterCell(control, box);
        g.Bullets.Add(new Bullet { X = (c - g.Layout.Col0) * 2 + 40, Y = 100, Friendly = true });   // far from the box
        var scr = Render(g);
        for (int y = box.R; y < box.Bottom; y++)
            for (int x = box.C; x < box.Right; x++) Assert.Equal(control.CharAt(x, y), scr.CharAt(x, y));
    }
}
