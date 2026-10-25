using FeedRaider.Engine.Raider;
using TUImation;

namespace FeedRaider.Tests;

// The exact bytes DiffEncoder sends. The replay tests beside it only see the picture a terminal ends up with, so a
// stream that is longer than it needs to be, or that skips a reset, still passes them; these pin the stream itself.
public class DiffEncoderBytesTests
{
    private const uint Ink = 0x010203, Paper = 0x0a0b0c;
    private const string Sgr = "\x1b[38;2;1;2;3;48;2;10;11;12m";   // Ink on Paper

    private static Screen Blank(int cols, int rows)
    {
        var s = new Screen(cols, rows);
        s.Clear(Paper);
        return s;
    }

    /// <summary>An encoder that has already sent a blank <paramref name="s"/>, so the next Encode is a diff.</summary>
    private static DiffEncoder Primed(Screen s)
    {
        var e = new DiffEncoder();
        e.Encode(s);
        return e;
    }

    [Fact]
    public void The_first_frame_clears_the_terminal_and_every_frame_with_output_ends_with_a_reset()
    {
        var s = Blank(2, 1);
        var e = new DiffEncoder();
        var first = e.Encode(s);
        Assert.StartsWith("\x1b[0m\x1b[2J", first);
        Assert.EndsWith("\x1b[0m", first);
        s.Put(0, 0, '#', Ink, Paper);
        Assert.Equal("\x1b[1;1H" + Sgr + "#\x1b[0m", e.Encode(s));
    }

    [Fact]
    public void A_frame_that_changes_nothing_is_empty_and_counts_no_cells()
    {
        var s = Blank(3, 2);
        var e = Primed(s);
        Assert.Equal("", e.Encode(s));
        Assert.Equal(0, e.LastChanged);
    }

    [Fact]
    public void Two_changed_cells_with_one_unchanged_cell_between_are_sent_as_one_run()
    {
        var s = Blank(8, 1);
        var e = Primed(s);
        s.Put(0, 0, '#', Ink, Paper);
        s.Put(2, 0, '#', Ink, Paper);
        Assert.Equal("\x1b[1;1H" + Sgr + "# #\x1b[0m", e.Encode(s));
        Assert.Equal(3, e.LastChanged);     // the cell between is sent again
    }

    [Fact]
    public void Two_changed_cells_with_two_unchanged_cells_between_are_sent_as_two_runs_joined_by_a_relative_move()
    {
        var s = Blank(8, 1);
        var e = Primed(s);
        s.Put(0, 0, '#', Ink, Paper);
        s.Put(3, 0, '#', Ink, Paper);
        Assert.Equal("\x1b[1;1H" + Sgr + "#\x1b[2C#\x1b[0m", e.Encode(s));
        Assert.Equal(2, e.LastChanged);
    }

    [Fact]
    public void A_change_on_the_next_row_is_reached_with_an_absolute_move()
    {
        var s = Blank(4, 2);
        var e = Primed(s);
        s.Put(1, 0, '#', Ink, Paper);
        s.Put(2, 1, '#', Ink, Paper);
        Assert.Equal("\x1b[1;2H" + Sgr + "#\x1b[2;3H#\x1b[0m", e.Encode(s));
    }

    [Fact]
    public void A_space_is_sent_without_a_foreground_and_a_full_block_without_a_background()
    {
        var s = Blank(2, 1);
        var e = new DiffEncoder();
        s.Put(0, 0, ' ', Ink, Paper);
        s.Put(1, 0, '█', Ink, 0x333333);
        // a fresh encoder knows no colours: the space needs only the background, then the block only the foreground (its own background never shows)
        Assert.Equal("\x1b[0m\x1b[2J\x1b[1;1H\x1b[48;2;10;11;12m \x1b[38;2;1;2;3m█\x1b[0m", e.Encode(s));
    }

    [Fact]
    public void A_quadrant_glyph_whose_colours_both_differ_either_way_keeps_its_glyph()
    {
        var s = Blank(1, 1);
        var e = Primed(s);
        s.Put(0, 0, QuadRenderer.GlyphFor(1), Ink, Paper);
        // flipping would cost two colour codes as well, so there is no reason to
        Assert.Equal("\x1b[1;1H" + Sgr + QuadRenderer.GlyphFor(1) + "\x1b[0m", e.Encode(s));
    }

    [Fact]
    public void A_quadrant_glyph_is_flipped_to_its_complement_when_that_saves_a_colour_code()
    {
        var s = Blank(2, 1);
        var e = Primed(s);
        s.Put(0, 0, '#', Ink, Paper);
        s.Put(1, 0, QuadRenderer.GlyphFor(1), Paper, Ink);   // the colours as the last cell left them, swapped
        Assert.Equal("\x1b[1;1H" + Sgr + "#" + QuadRenderer.GlyphFor(14) + "\x1b[0m", e.Encode(s));
    }

    [Fact]
    public void A_changed_foreground_under_a_space_is_no_change()
    {
        var s = Blank(2, 1);
        var e = Primed(s);
        s.Put(0, 0, ' ', Ink, Paper);
        Assert.Equal("", e.Encode(s));
        Assert.Equal(0, e.LastChanged);
    }

    [Fact]
    public void A_changed_background_under_a_full_block_is_no_change()
    {
        var s = Blank(2, 1);
        s.Put(0, 0, '█', Ink, Paper);
        var e = Primed(s);
        s.Put(0, 0, '█', Ink, 0x333333);
        Assert.Equal("", e.Encode(s));
    }

    [Fact]
    public void A_changed_background_under_a_space_and_a_changed_foreground_under_a_block_are_sent()
    {
        var s = Blank(3, 1);
        s.Put(2, 0, '█', Ink, Paper);
        var e = Primed(s);
        s.Put(0, 0, ' ', Ink, 0x333333);
        s.Put(2, 0, '█', 0x444444, Paper);
        var o = e.Encode(s);
        Assert.Contains("48;2;51;51;51m ", o);
        Assert.Contains("38;2;68;68;68m█", o);
    }

    [Fact]
    public void A_screen_resized_by_rows_alone_is_redrawn_in_full()
    {
        var e = Primed(Blank(4, 2));
        Assert.StartsWith("\x1b[0m\x1b[2J", e.Encode(Blank(4, 3)));
    }

    [Fact]
    public void A_screen_resized_by_columns_alone_is_redrawn_in_full()
    {
        var e = Primed(Blank(4, 2));
        Assert.StartsWith("\x1b[0m\x1b[2J", e.Encode(Blank(5, 2)));
    }

    [Fact]
    public void Invalidate_makes_the_next_frame_a_full_redraw_and_the_one_after_a_diff_again()
    {
        var s = Blank(3, 1);
        var e = Primed(s);
        e.Invalidate();
        var full = e.Encode(s);
        Assert.StartsWith("\x1b[0m\x1b[2J", full);
        Assert.Equal(3, e.LastChanged);
        Assert.Equal("", e.Encode(s));
    }
}
