using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The speech box paints post text: a drawable char is drawn (a wide one in two cells, never across the box edge), an undrawable
// one is dropped and never a "?", and a post left with no text opens no box.
public class SpeechBoxTextTests
{
    private const int Cols = 48, Rows = 60;

    /// <summary>The box's first text row, under its top border and title; the text takes every row but the <see cref="EnemySpeechBox.Chrome"/>.</summary>
    private const int FirstTextRow = 2;

    /// <summary>Frames a box is aged past the end of its typing, so the whole first page shows.</summary>
    private const int FramesPastTyping = 2;

    /// <summary>The box's frame is one cell thick on every side.</summary>
    private const int Border = 1;

    private const int GameSeed = 3;
    private const ulong PostSeed = 5;

    /// <summary>Frames given for the enemy to arrive and open its box.</summary>
    private const int FramesToOpen = 1500;

    private static string U(int cp) => char.ConvertFromUtf32(cp);
    private static readonly string Smile = U(0x1F600);
    private static readonly string Cjk = U(0x65E5) + U(0x672C) + U(0x8A9E) + U(0x306E) + U(0x6295) + U(0x7A3F);

    private static RaiderGame Offered(string text, int steps = FramesToOpen)
    {
        var g = new RaiderGame(GameSeed) { BossGates = false };
        g.SetLayout(FieldLayout.Compute(Cols, Rows));
        g.OfferDarkness(new DarknessPost(DarknessKind.Spam, PostSeed, text, DarknessSource.Label, "x"));
        for (int i = 0; i < steps && !g.Enemies.Any(EnemySpeechBox.IsOpen); i++) g.Step();
        return g;
    }

    private static Screen Render(RaiderGame g)
    {
        var scr = new Screen(Cols, Rows);
        new RaiderView().FitAndRender(g, scr);
        return scr;
    }

    private static (Screen Scr, CellRect Box) FullyTyped(RaiderGame g)
    {
        var e = g.Enemies.First(EnemySpeechBox.IsOpen);
        e.SpeechBoxAge = SpeechBoxText.RevealFrames(SpeechBoxText.PageChars(e.SpeechLines!, e.PageLines, 0)) + FramesPastTyping;
        return (Render(g), EnemySpeechBox.Box(e, g.Layout));
    }

    [Fact]
    public void An_emoji_in_a_post_is_dropped_from_the_box_and_never_a_question_mark()
    {
        var g = Offered("good " + Smile + Smile + " morning " + Smile + " all");
        var (scr, box) = FullyTyped(g);
        var rows = Enumerable.Range(box.R + FirstTextRow, box.H - EnemySpeechBox.Chrome).Select(r => new string(Enumerable.Range(box.C, box.W).Select(c => scr.CharAt(c, r)).ToArray())).ToList();
        Assert.Contains("good morning all", string.Join(" ", rows.Select(r => r.Trim('\0', ' ', '│'))).Replace("  ", " "));
        Assert.DoesNotContain(rows, r => r.Contains('?'));
    }

    [Fact]
    public void Wide_text_takes_two_cells_a_char_and_stays_whole_inside_the_box()
    {
        var g = Offered("news " + Cjk + Cjk + " today");
        var (scr, box) = FullyTyped(g);
        int wide = 0;
        for (int r = box.R + FirstTextRow; r < box.Bottom - Border; r++)
            for (int c = box.C + Border; c < box.Right - Border; c++)
            {
                char ch = scr.CharAt(c, r);
                if (CellWidth.Of(ch) == 2)
                {
                    wide++;
                    Assert.True(c + 1 < box.Right - Border, "a wide char stops short of the right border");
                    Assert.Equal(Screen.Continue, scr.CharAt(c + 1, r));
                    Assert.Equal(scr.Bg[r * scr.Cols + c], scr.Bg[r * scr.Cols + c + 1]);
                }
                else if (ch == Screen.Continue) Assert.Equal(2, CellWidth.Of(scr.CharAt(c - 1, r)));
            }
        Assert.Equal(Cjk.Length * 2, wide);
        Assert.Equal('│', scr.CharAt(box.Right - Border, box.R + FirstTextRow));
    }

    [Fact]
    public void A_box_of_wide_text_is_wide_enough_for_its_cells_and_its_lines_wrap_by_cells()
    {
        const int copies = 6;
        var g = Offered(string.Concat(Enumerable.Repeat(Cjk, copies)));
        var e = g.Enemies.First(EnemySpeechBox.IsOpen);
        var box = EnemySpeechBox.Box(e, g.Layout);
        int widest = e.SpeechLines!.Max(CellText.Width);
        Assert.InRange(widest, 2, SpeechBoxText.TextWidth(g.Layout.PCols));
        Assert.True(box.W >= widest + EnemySpeechBox.TextSide);
        Assert.All(e.SpeechLines!, l => Assert.InRange(CellText.Width(l), 1, SpeechBoxText.TextWidth(g.Layout.PCols)));
        Assert.Equal(Cjk.Length * copies, e.SpeechLines!.Sum(l => l.Length));
    }

    [Fact]
    public void A_post_with_nothing_drawable_opens_no_box_and_draws_no_empty_one()
    {
        // time enough for the enemy to arrive and a box to have opened; the watch after it draws one frame in every forty
        const int framesBefore = 700, framesWatched = 400, drawEvery = 40;
        var g = Offered(Smile + " " + Smile + Smile, steps: framesBefore);
        Assert.DoesNotContain(g.Enemies, EnemySpeechBox.IsOpen);
        for (int i = 0; i < framesWatched; i++)
        {
            g.Step();
            if (i % drawEvery == 0)
            {
                var scr = Render(g);
                for (int r = 0; r < scr.Rows; r++)
                    for (int c = 0; c < scr.Cols; c++)
                        Assert.NotEqual('┌', scr.CharAt(c, r));
            }
        }
        Assert.All(g.Enemies.Where(EnemySpeechBox.Speaks), e => Assert.Empty(e.SpeechLines ?? []));
    }

    [Fact]
    public void A_label_with_an_emoji_or_wide_chars_is_cleaned_like_the_text()
    {
        Assert.Equal("SCAM", EnemySpeechBox.Header(Smile + " scam", DarknessKind.Spam, DarknessSource.Label));
        Assert.Equal("SPAM", EnemySpeechBox.Header(Smile, DarknessKind.Spam, DarknessSource.Label));
        // 48 cells of wide chars, twice the widest label
        const int copies = 4;
        string wide = EnemySpeechBox.Header(string.Concat(Enumerable.Repeat(Cjk, copies)), DarknessKind.Spam, DarknessSource.Label);
        Assert.InRange(CellText.Width(wide), 1, EnemySpeechBox.MaxLabel);
    }
}
