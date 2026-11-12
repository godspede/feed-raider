using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// A darkness enemy that carries text speaks in the wingmen's comm box, above it, with its label where a name would be.
public class EnemyCommTests
{
    private const string Words = "you are all idiots and nobody likes your takes";

    [Theory]
    [InlineData(DarknessKind.Spam, null, DarkSource.Label, "SPAM")]
    [InlineData(DarknessKind.Rude, null, DarkSource.Label, "RUDE")]
    [InlineData(DarknessKind.Rude, "rude", DarkSource.Label, "RUDE")]
    [InlineData(DarknessKind.Spam, "scam", DarkSource.Label, "SCAM")]
    [InlineData(DarknessKind.Rude, null, DarkSource.Hidden, "HIDDEN REPLY")]
    public void Header_is_the_upper_case_label_else_the_kind(DarknessKind kind, string? label, DarkSource src, string expected) =>
        Assert.Equal(expected, EnemyComm.Header(label, kind, src));

    private static RaiderGame Game(DarknessKind kind, string label, int cols = 48, int rows = 60, DarkSource src = DarkSource.Label)
    {
        var g = new RaiderGame(3);
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.OfferDark(new DarkPost(kind, 5, Words, src, label));
        for (int i = 0; i < 1500 && !g.Enemies.Any(EnemyComm.IsOpen); i++) g.Step();
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
        var e = g.Enemies.First(EnemyComm.IsOpen);
        var r = EnemyComm.Box(e, g.Layout);
        bool below = EnemyComm.IsBelow(e, r);   // right under the top there is no room above, so the box goes under the enemy
        Assert.True(below ? r.R >= (int)Math.Ceiling((e.Y + e.Sprite.H / 2.0) / 2) : r.Bottom <= (int)Math.Floor((e.Y - e.Sprite.H / 2.0) / 2) || r.R == EnemyComm.MinRow);
        var scr = Render(g, 48, 60);
        Assert.Contains(label.ToUpperInvariant(), Row(scr, r.R + 1));
        int pr = below ? r.R : r.Bottom - 1;
        Assert.Equal(below ? '▲' : '▼', scr.CharAt(r.C + r.W / 2, pr));
        Assert.Equal(EnemyComm.Edge, scr.Fg[pr * scr.Cols + r.C + r.W / 2]);
    }


    [Fact]
    public void The_box_is_red_on_near_black_and_a_solid_panel()
    {
        var g = Game(DarknessKind.Rude, "rude");
        var e = g.Enemies.First(EnemyComm.IsOpen);
        var r = EnemyComm.Box(e, g.Layout);
        var scr = Render(g, 48, 60);
        Assert.Equal(EnemyComm.Edge, scr.Fg[r.R * scr.Cols + r.C]);
        for (int y = r.R; y < r.Bottom; y++)
            for (int x = r.C; x < r.Right; x++) Assert.Equal(EnemyComm.PanelBg, scr.Bg[y * scr.Cols + x]);
        Assert.Equal(EnemyComm.HeaderFg, scr.Fg[(r.R + 1) * scr.Cols + r.C + 2]);
        Assert.True(EnemyComm.Edge >> 16 > 0xc0 && (EnemyComm.Edge & 0xffff) < 0x4000, "the border is red");
        Assert.True(EnemyComm.PanelBg >> 16 < 0x20 && (EnemyComm.PanelBg & 0xff) < 0x20, "the panel is near black");
    }

    [Fact]
    public void No_tag_is_left_over_the_sprite_and_no_handle_or_did_shows()
    {
        var g = Game(DarknessKind.Spam, "spam");
        g.Enemies.RemoveAll(e => !EnemyComm.IsOpen(e));
        var e = g.Enemies[0];
        var r = EnemyComm.Box(e, g.Layout);
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
        EnemyComm.Update(e, 48);
        Assert.False(EnemyComm.IsOpen(e));
    }

    [Fact]
    public void The_box_is_clamped_inside_the_field_and_below_the_hud()
    {
        var g = Game(DarknessKind.Rude, "rude");
        var e = g.Enemies.First(EnemyComm.IsOpen);
        foreach (double x in new[] { 0.0, 1000.0 })
        {
            e.X = x; e.Y = 8;
            var r = EnemyComm.Box(e, g.Layout);
            Assert.True(r.C >= g.Layout.Col0 && r.Right <= g.Layout.Col0 + g.Layout.PCols);
            Assert.True(r.R >= EnemyComm.MinRow);
        }
    }

    [Fact]
    public void The_box_never_covers_the_players_ship_and_closes_after_its_hold()
    {
        var g = Game(DarknessKind.Rude, "rude");
        var e = g.Enemies.First(EnemyComm.IsOpen);
        e.X = g.PX; e.Y = g.PY + 2;   // level with the ship: its box would sit on it
        Assert.DoesNotContain(e, EnemyComm.Visible(g, g.Layout, 3));
        e.CommAge = EnemyComm.Frames(e);
        Assert.False(EnemyComm.IsOpen(e));
    }

    [Fact]
    public void At_most_the_wingmens_cap_of_boxes_show_at_once()
    {
        var g = Game(DarknessKind.Rude, "rude");
        var e = g.Enemies.First(EnemyComm.IsOpen);
        for (int i = 0; i < 4; i++)
        {
            var c = new Enemy { Kind = e.Kind, Sprite = e.Sprite, X = 20 + i * 12, Y = 20, Text = e.Text, Header = "RUDE", Live = true };
            EnemyComm.Update(c, 48); c.CommAge = 1; g.Enemies.Add(c);
        }
        Assert.True(EnemyComm.Visible(g, g.Layout, 2).Count <= 2);
    }

    private const string LongPost = "honestly i cannot believe how little you people understand about anything at all, every single take you post is worse than the last and the worst part is you think everyone is impressed by how loud you are";

    private static string Squash(IEnumerable<string> lines) => string.Join(' ', lines);

    [Theory]
    [InlineData(DarknessKind.Rude)]
    [InlineData(DarknessKind.Spam)]
    public void A_long_post_comes_through_the_wave_and_shows_in_full_with_no_ellipsis(DarknessKind kind)
    {
        var spec = WaveSpawner.FromPost(new DarkPost(kind, 5, LongPost, DarkSource.Label, "x"));
        Assert.Contains(spec.Enemies, s => s.Text == LongPost);
        var g = new RaiderGame(3);
        g.SetLayout(FieldLayout.Compute(80, 40));
        g.OfferDark(new DarkPost(kind, 5, LongPost, DarkSource.Label, "x"));
        for (int i = 0; i < 1500 && !g.Enemies.Any(EnemyComm.IsOpen); i++) g.Step();
        var e = g.Enemies.First(EnemyComm.IsOpen);
        Assert.Equal(LongPost, Squash(e.CommLines!));
        Assert.DoesNotContain("…", Squash(e.CommLines!));
        Assert.Equal(1, WingComms.Pages(e.CommLines!, e.PageLines));
        e.CommAge = EnemyComm.Frames(e) - 1;   // typed out
        g.Enemies.RemoveAll(x => x != e);      // units draw over boxes; clear the escort so the words are readable
        g.Bullets.Clear();
        var scr = Render(g, 80, 40);
        var rows = Enumerable.Range(0, scr.Rows).Select(r => Row(scr, r)).ToArray();
        foreach (var line in e.CommLines!) Assert.Contains(rows, row => row.Contains(line));
        Assert.DoesNotContain(rows, row => row.Contains('…'));
    }

    [Fact]
    public void An_overlong_post_pages_and_is_never_cut()
    {
        string text = string.Join(' ', Enumerable.Range(1, 120).Select(i => "word" + i));
        var e = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = 40, Y = 30, Header = "RUDE", Live = true, Text = text };
        EnemyComm.Update(e, 48, 24);
        Assert.Equal(text, Squash(e.CommLines!));
        int pages = WingComms.Pages(e.CommLines!, e.PageLines);
        Assert.True(pages > 1);
        Assert.InRange(e.PageLines, WingComms.MinPageLines, WingComms.TextLines);
        // read page by page, to the last
        Assert.Equal(0, WingComms.Progress(e.CommLines!, e.PageLines, 0).Page);
        Assert.Equal(pages - 1, WingComms.Progress(e.CommLines!, e.PageLines, EnemyComm.Frames(e) - 1).Page);
        Assert.True(EnemyComm.Frames(e) > WingComms.TotalFrames(e.CommLines!, e.PageLines, 0) - 1);
    }

    [Theory]
    [InlineData(80, 24)]
    [InlineData(40, 70)]   // phone portrait
    public void The_box_of_a_long_post_fits_the_smallest_fields(int cols, int rows)
    {
        var g = new RaiderGame(3);
        g.SetLayout(FieldLayout.Compute(cols, rows));
        string text = string.Join(' ', Enumerable.Repeat(LongPost, 3));
        var e = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = g.Layout.PW / 2.0, Y = g.Layout.PH / 2.0, Header = "RUDE", Live = true, Text = text };
        EnemyComm.Update(e, g.Layout.PCols, g.Layout.PRows);
        var r = EnemyComm.Box(e, g.Layout);
        Assert.True(r.C >= g.Layout.Col0 && r.Right <= g.Layout.Col0 + g.Layout.PCols);
        Assert.True(r.R >= EnemyComm.MinRow && r.Bottom <= g.Layout.PRows, $"box {r} in {g.Layout.PRows} rows");
        Assert.True(r.H <= g.Layout.PRows / 2 + 1, "no taller than a wingman's box");
    }

    private static readonly string Post300 = string.Join(' ', Enumerable.Range(1, 300).Select(i => "w" + i)).Substring(0, 300).TrimEnd();

    [Theory]
    [InlineData(DarknessKind.Rude, 40, 70)]
    [InlineData(DarknessKind.Rude, 120, 40)]
    [InlineData(DarknessKind.Spam, 40, 70)]
    [InlineData(DarknessKind.Spam, 120, 40)]
    public void A_300_character_post_shows_every_word_across_its_pages_with_no_ellipsis(DarknessKind kind, int cols, int rows)
    {
        string post = DarknessText.Clean(Post300, DarknessText.MaxPostChars);
        Assert.Equal(Post300, post);
        var g = new RaiderGame(3);
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.OfferDark(new DarkPost(kind, 5, Post300, DarkSource.Label, "x"));
        for (int i = 0; i < 1500 && !g.Enemies.Any(EnemyComm.IsOpen); i++) g.Step();
        var e = g.Enemies.First(EnemyComm.IsOpen);
        Assert.Equal(kind == DarknessKind.Rude ? EnemyKind.Gunship : EnemyKind.Carrier, e.Kind);
        Assert.Equal(Post300, Squash(e.CommLines!));
        Assert.DoesNotContain("…", Squash(e.CommLines!));
        // read every page in turn: each line is painted on its page
        int pages = WingComms.Pages(e.CommLines!, e.PageLines);
        g.Enemies.RemoveAll(x => x != e); g.Bullets.Clear();
        var seen = new List<string>();
        for (int p = 0; p < pages; p++)
        {
            e.CommAge = p == pages - 1
                ? EnemyComm.Frames(e) - 1
                : WingComms.TotalFrames(e.CommLines!.Take((p + 1) * e.PageLines).ToArray(), e.PageLines, 0) - Tuning.CommPageHoldFrames - 1;
            var scr = Render(g, cols, rows);
            var rws = Enumerable.Range(0, scr.Rows).Select(r => Row(scr, r)).ToArray();
            Assert.DoesNotContain(rws, row => row.Contains('…'));
            for (int i = p * e.PageLines; i < Math.Min(e.CommLines!.Length, (p + 1) * e.PageLines); i++)
            {
                Assert.Contains(rws, row => row.Contains(e.CommLines[i]));
                seen.Add(e.CommLines[i]);
            }
        }
        Assert.Equal(Post300, Squash(seen));
    }

    [Fact]
    public void A_boss_shot_down_mid_post_keeps_its_box_until_every_page_is_read()
    {
        var g = new RaiderGame(3);
        g.SetLayout(FieldLayout.Compute(40, 70));
        g.OfferDark(new DarkPost(DarknessKind.Rude, 5, Post300, DarkSource.Label, "x"));
        for (int i = 0; i < 1500 && !g.Enemies.Any(EnemyComm.IsOpen); i++) g.Step();
        var e = g.Enemies.First(EnemyComm.IsOpen);
        int total = EnemyComm.Frames(e);
        e.Hp = 0; g.Enemies.Remove(e); EnemyComm.Bury(g, e);
        Assert.Contains(e, g.Echoes);
        g.Enemies.Clear();
        long stop = g.Tick + total - e.CommAge - 3;
        for (int i = 0; i < 100000 && g.Tick < stop; i++) { g.Step(); g.Enemies.Clear(); }
        Assert.Contains(e, g.Echoes);
        stop = g.Tick + 10;
        for (int i = 0; i < 100000 && g.Tick < stop; i++) { g.Step(); g.Enemies.Clear(); }
        Assert.DoesNotContain(e, g.Echoes);
    }
}
