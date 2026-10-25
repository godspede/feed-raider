using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The cell grid the view draws into: what Clear leaves, and how Put, Text and Blank place a glyph and its colours.
public class ScreenTests
{
    private const uint Bg = 0x123456;

    [Fact]
    public void A_fresh_screen_is_all_zero_cells()
    {
        var s = new Screen(3, 2);
        Assert.All(s.Ch, c => Assert.Equal('\0', c));
        Assert.All(s.Fg, c => Assert.Equal(0u, c));
        Assert.All(s.Bg, c => Assert.Equal(0u, c));
    }

    [Fact]
    public void Clear_resets_every_cell_to_a_space_in_white_on_the_given_background()
    {
        var s = new Screen(5, 4);
        s.Text(0, 0, "hello", 0x112233, 0x445566);
        s.Put(4, 3, 'x', 0xabcdef, 0x778899);
        s.Clear(Bg);
        Assert.Equal(20, s.Ch.Length);
        for (int i = 0; i < s.Ch.Length; i++)
        {
            Assert.Equal(' ', s.Ch[i]);
            Assert.Equal(0xFFFFFFu, s.Fg[i]);
            Assert.Equal(Bg, s.Bg[i]);
        }
    }

    [Fact]
    public void Clear_takes_the_new_background_each_time()
    {
        var s = new Screen(2, 2);
        s.Clear(Bg);
        s.Clear(0x0a0b0c);
        Assert.All(s.Bg, c => Assert.Equal(0x0a0b0cu, c));
    }

    [Fact]
    public void Put_sets_the_glyph_and_both_colours_in_the_cell_it_names()
    {
        var s = new Screen(4, 3);
        s.Clear(Bg);
        s.Put(2, 1, 'q', 0x112233, 0x445566);
        Assert.Equal('q', s.CharAt(2, 1));
        Assert.Equal(0x445566u, s.BgAt(2, 1));
        Assert.Equal(0x112233u, s.Fg[1 * 4 + 2]);
        Assert.Equal(' ', s.CharAt(1, 1));      // the neighbours, left and above, are untouched
        Assert.Equal(' ', s.CharAt(2, 0));
        Assert.Equal(Bg, s.BgAt(2, 0));
    }

    [Fact]
    public void Put_without_a_background_keeps_the_one_beneath()
    {
        var s = new Screen(2, 1);
        s.Clear(Bg);
        s.Put(1, 0, 'a', 0x112233);
        Assert.Equal(Bg, s.BgAt(1, 0));
        Assert.Equal(0x112233u, s.Fg[1]);
    }

    [Fact]
    public void Put_off_the_grid_changes_nothing()
    {
        var s = new Screen(3, 2);
        s.Clear(Bg);
        foreach (var (c, r) in new[] { (-1, 0), (0, -1), (3, 0), (0, 2), (3, 2) }) s.Put(c, r, 'x', 1, 2);
        Assert.All(s.Ch, c => Assert.Equal(' ', c));
        Assert.All(s.Bg, c => Assert.Equal(Bg, c));
        s.Put(2, 1, 'z', 1, 2);                 // the last cell is on the grid
        Assert.Equal('z', s.CharAt(2, 1));
    }

    [Fact]
    public void A_glyph_xterm_would_draw_wide_or_zero_wide_is_put_as_a_question_mark()
    {
        var s = new Screen(5, 1);
        s.Put(0, 0, '中', 1);               // a wide CJK glyph
        s.Put(1, 0, '́', 1);               // a combining accent
        s.Put(2, 0, '█', 1);                    // a single-width block is fine
        s.Put(3, 0, 'é', 1);               // below U+0300 nothing is checked
        Assert.Equal("??█é", new string(s.Ch, 0, 4));
    }

    [Fact]
    public void Text_runs_along_the_row_and_is_cut_at_the_right_edge()
    {
        var s = new Screen(6, 2);
        s.Clear(Bg);
        s.Text(3, 1, "abcdef", 0x112233, 0x445566);
        Assert.Equal("abc", new string(Enumerable.Range(3, 3).Select(c => s.CharAt(c, 1)).ToArray()));
        Assert.Equal(0x445566u, s.BgAt(5, 1));
        Assert.Equal(Bg, s.BgAt(2, 1));         // the run starts at column 3
        Assert.Equal(' ', s.CharAt(0, 0));      // and stays in its row
    }

    [Fact]
    public void Blank_paints_a_run_of_spaces_of_exactly_the_given_width()
    {
        var s = new Screen(8, 2);
        s.Clear(Bg);
        s.Text(0, 1, "abcdefgh", 1, 2);
        s.Blank(2, 1, 4, 0xfedcba, 0x998877);
        Assert.Equal("ab    gh", new string(Enumerable.Range(0, 8).Select(c => s.CharAt(c, 1)).ToArray()));
        for (int c = 0; c < 8; c++)
        {
            bool inside = c >= 2 && c < 6;
            Assert.Equal(inside ? 0x998877u : 2u, s.BgAt(c, 1));
            Assert.Equal(inside ? 0xfedcbau : 1u, s.Fg[8 + c]);
        }
    }

    // ---------------------------------------------------------------- QuadComposer

    private const uint Red = 0xff0000, Blue = 0x0000ff;

    /// <summary>A surface of 2x2-pixel cells, each one flat colour: <paramref name="cells"/> across, <paramref name="rows"/> down, alternating red and blue.</summary>
    private static Surface Flat(int cells, int rows)
    {
        var s = new Surface(cells * 2, rows * 2);
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++) s.Set(x, y, (x / 2 + y / 2) % 2 == 0 ? Red : Blue);
        return s;
    }

    [Fact]
    public void Compose_writes_a_flat_cell_as_a_space_in_its_colour_at_the_given_corner()
    {
        var scr = new Screen(6, 4);
        scr.Clear(Bg);
        new QuadComposer().Compose(Flat(2, 1), 0, 0, scr, 3, 2);
        Assert.Equal(' ', scr.CharAt(3, 2));
        Assert.Equal(Red, scr.BgAt(3, 2));
        Assert.Equal(Red, scr.Fg[2 * 6 + 3]);
        Assert.Equal(Blue, scr.BgAt(4, 2));
        Assert.Equal(Bg, scr.BgAt(2, 2));       // nothing beside or above the cells
        Assert.Equal(Bg, scr.BgAt(3, 1));
        Assert.Equal(Bg, scr.BgAt(5, 2));
    }

    [Fact]
    public void Compose_cuts_what_runs_past_the_right_edge_instead_of_wrapping_it_to_the_next_row()
    {
        var scr = new Screen(3, 2);
        scr.Clear(Bg);
        new QuadComposer().Compose(Flat(2, 1), 0, 0, scr, 2, 0);
        Assert.Equal(Red, scr.BgAt(2, 0));      // the first cell is the last column
        Assert.Equal(Bg, scr.BgAt(0, 1));       // the second would be the first column of the next row
    }

    [Fact]
    public void Compose_cuts_what_starts_left_of_the_screen_and_what_runs_below_it()
    {
        var scr = new Screen(3, 2);
        scr.Clear(Bg);
        new QuadComposer().Compose(Flat(2, 2), 0, 0, scr, -1, 1);   // the left cell is off the screen
        Assert.Equal(Blue, scr.BgAt(0, 1));     // the second cell of the first row lands on column 0
        Assert.Equal(Bg, scr.BgAt(1, 1));
        Assert.Equal(Bg, scr.BgAt(0, 0));       // the row above the corner is untouched
    }

    [Fact]
    public void Compose_counts_only_the_cells_whose_pixels_changed_since_the_last_frame()
    {
        var q = new QuadComposer();
        var scr = new Screen(4, 2);
        var s = Flat(4, 2);
        q.Compose(s, 0, 0, scr, 0, 0);
        Assert.Equal(8, q.Reduced);
        q.Compose(s, 0, 0, scr, 0, 0);
        Assert.Equal(0, q.Reduced);
        s.Set(3, 1, 0x00ff00);                  // one pixel, in the second cell of the first row
        q.Compose(s, 0, 0, scr, 0, 0);
        Assert.Equal(1, q.Reduced);
    }
}
