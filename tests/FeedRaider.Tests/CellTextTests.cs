using FeedRaider.Engine;

namespace FeedRaider.Tests;

// Post text on the way to cells, for every painter of it: a drawable char is kept (a wide one takes two cells), a styled letter is
// folded to its plain one, an undrawable char is dropped, and nothing is ever swapped for a "?". A text the drop leaves empty is empty.
public class CellTextTests
{
    private static string FromCodePoint(int codePoint) => char.ConvertFromUtf32(codePoint);
    private static readonly string Smile = FromCodePoint(0x1F600);       // outside the Basic Multilingual Plane: two UTF-16 chars
    private static readonly string Star = FromCodePoint(0x2B50);         // a browser draws it as an emoji
    private static readonly string Cjk = FromCodePoint(0x65E5) + FromCodePoint(0x672C) + FromCodePoint(0x8A9E);
    private static readonly string Keycap = "2" + FromCodePoint(0xFE0F) + FromCodePoint(0x20E3);
    /// <summary>The cells of <see cref="Cjk"/>, three wide chars.</summary>
    private static readonly int CjkCells = Cjk.Length * CellText.WideCells;
    /// <summary>Plain words either side of the wide chars.</summary>
    private const string Lead = "go ", Tail = " now";

    [Fact]
    public void An_undrawable_char_is_dropped_and_never_becomes_a_question_mark()
    {
        const string Word = "peas";
        Assert.Equal("hi there", CellText.Clean("hi " + Smile + "there"));
        Assert.Equal("a b", CellText.Clean("a " + Star + " b"));
        Assert.Equal(Word, CellText.Clean(Keycap.Substring(1) + Word));
        Assert.DoesNotContain('?', CellText.Clean(Smile + FromCodePoint(0x1F468) + FromCodePoint(0x200D) + FromCodePoint(0x1F469) + " family"));
    }

    [Fact]
    public void A_question_mark_the_author_typed_stays()
    {
        const string Typed = "why?";
        Assert.Equal(Typed, CellText.Clean(Typed));
    }

    [Fact]
    public void A_wide_char_is_kept_and_takes_two_cells()
    {
        string clean = CellText.Clean(Lead + Cjk + Tail);
        Assert.Equal(Lead + Cjk + Tail, clean);
        Assert.Equal(Lead.Length + CjkCells + Tail.Length, CellText.Width(clean));
        Assert.Equal(CellText.WideCells, CellText.Cells(Cjk[0]));
        Assert.Equal(1, CellText.Cells('a'));
        Assert.Equal(0, CellText.Cells(Smile[0]));
    }

    [Fact]
    public void A_single_cell_clean_drops_the_wide_chars_too()
    {
        Assert.Equal("go now", CellText.CleanSingle(Lead + Cjk + Tail));
        Assert.Empty(CellText.CleanSingle(Smile + Cjk));
    }

    [Fact]
    public void A_text_left_empty_is_empty_and_whitespace_collapses()
    {
        Assert.Empty(CellText.Clean(Smile + Smile));
        Assert.Empty(CellText.Clean(null));
        Assert.Empty(CellText.Clean("  \t\n "));
        Assert.Equal("a b c", CellText.Clean(" a \n\t b   c "));
    }

    [Fact]
    public void Wrap_never_breaks_a_line_wider_than_the_box_or_splits_a_wide_char()
    {
        const int EveryWideCharFits = 4;
        string text = Cjk + Cjk + Cjk + " abc defghij " + Cjk;
        foreach (int width in new[] { 2, 3, 5, 7, 8, 11 })
        {
            var lines = CellText.Wrap(text, width);
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.InRange(CellText.Width(line), 1, width));
        }
        Assert.Equal(text.Count(ch => CellText.Cells(ch) == CellText.WideCells),
            CellText.Wrap(text, EveryWideCharFits).Sum(line => line.Count(ch => CellText.Cells(ch) == CellText.WideCells)));
    }

    [Fact]
    public void Clip_cuts_by_cells_and_never_through_a_wide_char()
    {
        const string Head = "ab";
        const int HeadAndOneWideChar = 4, OneShortOfTwoWideChars = 5, RoomToSpare = 20;
        string text = Head + Cjk + "cd";
        Assert.Equal(text, CellText.Clip(text, RoomToSpare, true));
        Assert.Equal(Head + Cjk[0], CellText.Clip(text, HeadAndOneWideChar, false));
        Assert.Equal(Head + Cjk[0], CellText.Clip(text, OneShortOfTwoWideChars, false));
        string cut = CellText.Clip(text, OneShortOfTwoWideChars, true);
        Assert.True(CellText.Width(cut) <= OneShortOfTwoWideChars);
        Assert.EndsWith(FromCodePoint(0x2026), cut);
        Assert.Equal(Head + "   ", CellText.PadRight(Head, OneShortOfTwoWideChars));
        Assert.Equal(Cjk, CellText.PadRight(Cjk, CjkCells));
    }

    [Fact]
    public void A_soft_hyphen_is_dropped()
    {
        Assert.False(CellText.CanDraw('\u00ad'));
        Assert.Equal("coop", CellText.Clean("co\u00adop"));
        Assert.Equal(0, CellText.Cells('\u00ad'));
    }

    // the first and the last code point of each run of styled letters or digits, and where two styles meet in a run
    [Theory]
    [InlineData(0x1F130, 'A')]   // squared
    [InlineData(0x1F149, 'Z')]
    [InlineData(0x1F150, 'A')]   // negative circled
    [InlineData(0x1F169, 'Z')]
    [InlineData(0x1F170, 'A')]   // negative squared
    [InlineData(0x1F189, 'Z')]
    [InlineData(0x1D400, 'A')]   // mathematical bold, the first style of letters
    [InlineData(0x1D433, 'z')]
    [InlineData(0x1D434, 'A')]   // mathematical italic, the second
    [InlineData(0x1D670, 'A')]   // mathematical monospace, the last
    [InlineData(0x1D6A3, 'z')]
    [InlineData(0x1D7CE, '0')]   // mathematical bold, the first style of digits
    [InlineData(0x1D7FF, '9')]   // mathematical monospace, the last
    public void A_styled_letter_or_digit_becomes_its_plain_one(int codePoint, char plain) =>
        Assert.Equal(plain.ToString(), CellText.Clean(FromCodePoint(codePoint)));

    // beside the runs: circled italic C, squared HV, negative circled CC, mathematical italic dotless i and the code point before
    // the first digit, none of them one plain letter
    [Theory]
    [InlineData(0x1F12B)]
    [InlineData(0x1F14A)]
    [InlineData(0x1F16D)]
    [InlineData(0x1D6A4)]
    [InlineData(0x1D7CD)]
    public void A_code_point_beside_the_styled_runs_is_dropped(int codePoint) => Assert.Empty(CellText.Clean(FromCodePoint(codePoint)));

    [Fact]
    public void Styled_letters_spell_a_plain_word_and_keep_the_spaces_between_words()
    {
        string bold = FromCodePoint(0x1D41B) + FromCodePoint(0x1D428) + FromCodePoint(0x1D425) + FromCodePoint(0x1D41D);
        Assert.Equal("bold move", CellText.Clean(bold + " move"));
        Assert.Equal("Professor Kiosk", CellText.Clean(FromCodePoint(0x1F17F) + "\uFE0Frofessor Kiosk " + FromCodePoint(0x1F48A)));
    }

    [Fact]
    public void A_lone_surrogate_is_dropped_wherever_it_stands()
    {
        Assert.Equal("on", CellText.Clean("o\uD83Cn"));
        Assert.Equal("up", CellText.Clean("\uDD7Fup"));
        Assert.Equal("it", CellText.Clean("it\uD83C"));
    }
}
