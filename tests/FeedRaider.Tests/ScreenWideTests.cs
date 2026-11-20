using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// A wide char (CJK, fullwidth) takes two cells: the glyph in the first, a continuation cell after it. The screen keeps the pair whole,
// and the encoder never lets one reach the terminal in a way that shifts the rest of the row.
public class ScreenWideTests
{
    private static readonly char Wide = (char)0x65E5;      // a CJK ideograph, two cells in xterm
    private static readonly char Wide2 = (char)0x672C;
    private static readonly char Mark = (char)0x20DD;      // a combining mark, no cell of its own

    private static Screen Blank(int cols = 12, int rows = 2)
    {
        var s = new Screen(cols, rows);
        s.Clear(0x101010);
        return s;
    }

    [Fact]
    public void A_wide_char_takes_its_cell_and_the_next_with_one_background()
    {
        var s = Blank();
        s.Bg[3] = 0x333333; s.Bg[4] = 0x444444;
        s.Text(2, 0, "a" + Wide + "b", 0xFFFFFF);
        Assert.Equal(new[] { 'a', Wide, Screen.Continue, 'b' }, s.Ch.Skip(2).Take(4).ToArray());
        Assert.Equal(s.Bg[3], s.Bg[4]);
        s.Text(0, 1, Wide.ToString(), 0xFFFFFF, 0x222222);
        Assert.Equal((0x222222u, 0x222222u), (s.Bg[12], s.Bg[13]));
    }

    [Fact]
    public void A_char_with_no_cell_is_dropped_and_never_a_question_mark()
    {
        var s = Blank();
        s.Text(0, 0, "A" + Mark + "B" + Wide + "C", 0xFFFFFF);
        Assert.Equal(new[] { 'A', 'B', Wide, Screen.Continue, 'C' }, s.Ch.Take(5).ToArray());
        Assert.DoesNotContain('?', s.Ch);
    }

    [Fact]
    public void A_wide_char_that_would_straddle_the_right_edge_is_not_drawn()
    {
        var s = Blank(6, 1);
        s.Text(0, 0, "abcde" + Wide, 0xFFFFFF);
        Assert.Equal("abcde ", new string(s.Ch));
        s.Put(-1, 0, Wide, 0xFFFFFF);
        Assert.Equal("abcde ", new string(s.Ch));
    }

    [Fact]
    public void Writing_over_half_of_a_wide_char_blanks_the_other_half()
    {
        var s = Blank();
        s.Text(2, 0, Wide.ToString(), 0xFFFFFF);
        s.Put(3, 0, 'x', 0xFFFFFF);
        Assert.Equal(new[] { ' ', 'x' }, s.Ch.Skip(2).Take(2).ToArray());

        s.Text(2, 0, Wide.ToString(), 0xFFFFFF);
        s.Put(2, 0, 'y', 0xFFFFFF);
        Assert.Equal(new[] { 'y', ' ' }, s.Ch.Skip(2).Take(2).ToArray());

        s.Text(2, 0, Wide.ToString() + Wide2, 0xFFFFFF);
        s.Text(3, 0, Wide2.ToString(), 0xFFFFFF);      // lands across the end of the first and the start of the second
        Assert.Equal(new[] { ' ', Wide2, Screen.Continue, ' ' }, s.Ch.Skip(2).Take(4).ToArray());
    }

    [Fact]
    public void The_encoder_sends_a_wide_char_once_and_the_row_stays_in_line()
    {
        var s = Blank();
        var enc = new DiffEncoder();
        var term = new MiniTerm(s.Cols, s.Rows);
        s.Text(0, 0, "ab" + Wide + Wide2 + "cd", 0xFFFFFF);
        term.Feed(enc.Encode(s));
        Assert.Equal("ab" + Wide + "\0" + Wide2 + "\0cd", new string(term.Ch, 0, 8));

        s.Text(2, 0, "xy", 0xFFFFFF);                  // narrow over the first wide char
        term.Feed(enc.Encode(s));
        Assert.Equal("abxy" + Wide2 + "\0cd", new string(term.Ch, 0, 8));

        s.Text(4, 0, "zz", 0xFFFFFF);                  // and over the second
        term.Feed(enc.Encode(s));
        Assert.Equal("abxyzzcd", new string(term.Ch, 0, 8));

        s.Text(5, 0, Wide.ToString(), 0xFFFFFF);       // wide over narrow again
        term.Feed(enc.Encode(s));
        Assert.Equal("abxyz" + Wide + "\0d", new string(term.Ch, 0, 8));
        for (int i = 0; i < s.Ch.Length; i++) Assert.Equal(MiniTerm.Look(s.Ch[i], s.Fg[i], s.Bg[i]), term.LookAt(i));
    }

    [Fact]
    public void A_broken_pair_in_the_grid_reaches_the_terminal_as_blanks_not_a_shifted_row()
    {
        var s = Blank();
        var enc = new DiffEncoder();
        var term = new MiniTerm(s.Cols, s.Rows);
        s.Text(0, 0, "abcdefgh", 0xFFFFFF);
        s.Ch[2] = Wide;                                  // a lead with no continuation after it
        s.Ch[5] = Screen.Continue;                       // a continuation with no lead before it
        s.Ch[11] = Wide;                                 // a lead in the last column
        term.Feed(enc.Encode(s));
        Assert.Equal(' ', term.Ch[2]);
        Assert.Equal('d', term.Ch[3]);
        Assert.Equal('e', term.Ch[4]);
        Assert.Equal(' ', term.Ch[5]);
        Assert.Equal('g', term.Ch[6]);
        Assert.Equal(' ', term.Ch[11]);
    }

    [Fact]
    public void A_wide_char_alone_in_a_changed_run_moves_the_cursor_two_cells()
    {
        var s = Blank();
        var enc = new DiffEncoder();
        var term = new MiniTerm(s.Cols, s.Rows);
        s.Text(0, 0, "abcdefgh", 0xFFFFFF);
        term.Feed(enc.Encode(s));
        s.Text(1, 0, Wide.ToString(), 0xFFFFFF);
        s.Put(5, 0, 'Q', 0xFFFFFF);
        term.Feed(enc.Encode(s));
        Assert.Equal("a" + Wide + "\0deQgh", new string(term.Ch, 0, 8));
    }

    [Fact]
    public void A_picture_cell_set_over_half_of_a_wide_char_blanks_the_other_half()
    {
        var s = Blank();
        s.Text(2, 0, Wide.ToString() + Wide2, 0xFFFFFF);
        s.Set(3, 0, '▘', 0xAA0000, 0x00AA00);        // a unit passing over the second half of the first char
        Assert.Equal(' ', s.Ch[2]);
        Assert.Equal('▘', s.Ch[3]);
        Assert.Equal((0xAA0000u, 0x00AA00u), (s.Fg[3], s.Bg[3]));
        Assert.Equal(Wide2, s.Ch[4]);                      // the next char is whole
        s.Set(4, 0, '▘', 0xAA0000, 0x00AA00);        // over the first half of the second
        Assert.Equal(' ', s.Ch[5]);
    }
}
