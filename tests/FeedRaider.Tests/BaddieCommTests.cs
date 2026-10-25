using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// A darkness enemy that carries text speaks in the wingmen's comm box, above it, with its label where a name would be.
public class BaddieCommTests
{
    private const string Words = "you are all idiots and nobody likes your takes";

    [Theory]
    [InlineData(DarknessKind.Spam, null, DarkSource.Label, "SPAM")]
    [InlineData(DarknessKind.Rude, null, DarkSource.Label, "RUDE")]
    [InlineData(DarknessKind.Rude, "rude", DarkSource.Label, "RUDE")]
    [InlineData(DarknessKind.Spam, "scam", DarkSource.Label, "SCAM")]
    [InlineData(DarknessKind.Rude, null, DarkSource.Hidden, "HIDDEN REPLY")]
    public void Header_is_the_upper_case_label_else_the_kind(DarknessKind kind, string? label, DarkSource src, string expected) =>
        Assert.Equal(expected, BaddieComm.Header(label, kind, src));

    private static RaiderGame Game(DarknessKind kind, string label, int cols = 48, int rows = 60, DarkSource src = DarkSource.Label)
    {
        var g = new RaiderGame(3);
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.OfferDark(new DarkPost(kind, 5, Words, src, label));
        for (int i = 0; i < 1500 && !g.Enemies.Any(BaddieComm.IsOpen); i++) g.Step();
        return g;
    }

    private static Screen Render(RaiderGame g, int cols, int rows)
    {
        var scr = new Screen(cols, rows);
        new RaiderView().Render(g, scr);
        return scr;
    }

    private static string Row(Screen s, int r) => new(Enumerable.Range(0, s.Cols).Select(c => s.CharAt(c, r)).ToArray());

    [Theory]
    [InlineData(DarknessKind.Rude, "rude")]
    [InlineData(DarknessKind.Spam, "spam")]
    public void The_box_is_above_the_enemy_with_the_label_in_its_header_and_the_pointer_down(DarknessKind kind, string label)
    {
        var g = Game(kind, label);
        var e = g.Enemies.First(BaddieComm.IsOpen);
        var r = BaddieComm.Box(e, g.Layout);
        Assert.True(r.Bottom <= (int)Math.Floor((e.Y - e.Sprite.H / 2.0) / 2) || r.R == BaddieComm.MinRow);
        var scr = Render(g, 48, 60);
        Assert.Contains(label.ToUpperInvariant(), Row(scr, r.R + 1));
        Assert.Equal('\u25bc', scr.CharAt(r.C + r.W / 2, r.Bottom - 1));
        Assert.Equal(BaddieComm.Edge, scr.Fg[(r.Bottom - 1) * scr.Cols + r.C + r.W / 2]);
    }

    [Fact]
    public void The_box_is_red_on_near_black_and_a_solid_panel()
    {
        var g = Game(DarknessKind.Rude, "rude");
        var e = g.Enemies.First(BaddieComm.IsOpen);
        var r = BaddieComm.Box(e, g.Layout);
        var scr = Render(g, 48, 60);
        Assert.Equal(BaddieComm.Edge, scr.Fg[r.R * scr.Cols + r.C]);
        for (int y = r.R; y < r.Bottom; y++)
            for (int x = r.C; x < r.Right; x++) Assert.Equal(BaddieComm.PanelBg, scr.Bg[y * scr.Cols + x]);
        Assert.Equal(BaddieComm.HeaderFg, scr.Fg[(r.R + 1) * scr.Cols + r.C + 2]);
        Assert.True(BaddieComm.Edge >> 16 > 0xc0 && (BaddieComm.Edge & 0xffff) < 0x4000, "the border is red");
        Assert.True(BaddieComm.PanelBg >> 16 < 0x20 && (BaddieComm.PanelBg & 0xff) < 0x20, "the panel is near black");
    }

    [Fact]
    public void No_tag_is_left_over_the_sprite_and_no_handle_or_did_shows()
    {
        var g = Game(DarknessKind.Spam, "spam");
        g.Enemies.RemoveAll(e => !BaddieComm.IsOpen(e));
        var e = g.Enemies[0];
        var r = BaddieComm.Box(e, g.Layout);
        var scr = Render(g, 48, 60);
        for (int y = 0; y < scr.Rows; y++)
        {
            string row = Row(scr, y);
            Assert.DoesNotContain("@", row);
            Assert.DoesNotContain("did:", row);
            if (y < r.R || y >= r.Bottom) Assert.DoesNotContain("SPAM", row);
        }
    }

    [Fact]
    public void An_enemy_with_no_text_shows_no_box()
    {
        var e = new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = 40, Y = 30, Header = "SPAM", Live = true };
        BaddieComm.Update(e, 48);
        Assert.False(BaddieComm.IsOpen(e));
    }

    [Fact]
    public void The_box_is_clamped_inside_the_field_and_below_the_hud()
    {
        var g = Game(DarknessKind.Rude, "rude");
        var e = g.Enemies.First(BaddieComm.IsOpen);
        foreach (double x in new[] { 0.0, 1000.0 })
        {
            e.X = x; e.Y = 8;
            var r = BaddieComm.Box(e, g.Layout);
            Assert.True(r.C >= g.Layout.Col0 && r.Right <= g.Layout.Col0 + g.Layout.PCols);
            Assert.True(r.R >= BaddieComm.MinRow);
        }
    }

    [Fact]
    public void The_box_never_covers_the_players_ship_and_closes_after_its_hold()
    {
        var g = Game(DarknessKind.Rude, "rude");
        var e = g.Enemies.First(BaddieComm.IsOpen);
        e.X = g.PX; e.Y = g.PY + 2;   // level with the ship: its box would sit on it
        Assert.DoesNotContain(e, BaddieComm.Visible(g, g.Layout, 3));
        e.CommAge = BaddieComm.Frames(e);
        Assert.False(BaddieComm.IsOpen(e));
    }

    [Fact]
    public void At_most_the_wingmens_cap_of_boxes_show_at_once()
    {
        var g = Game(DarknessKind.Rude, "rude");
        var e = g.Enemies.First(BaddieComm.IsOpen);
        for (int i = 0; i < 4; i++)
        {
            var c = new Enemy { Kind = e.Kind, Sprite = e.Sprite, X = 20 + i * 12, Y = 20, Text = e.Text, Header = "RUDE", Live = true };
            BaddieComm.Update(c, 48); c.CommAge = 1; g.Enemies.Add(c);
        }
        Assert.True(BaddieComm.Visible(g, g.Layout, 2).Count <= 2);
    }
}
