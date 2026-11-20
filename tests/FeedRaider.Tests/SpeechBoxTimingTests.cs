using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The timing and paging of a speech box, on plain lines of text.
public class SpeechBoxTimingTests
{
    /// <summary>Lines to a page: the <c>pageLines</c> argument of <c>SpeechBoxText</c>.</summary>
    private const int PageLines = 4;

    private static string[] Lines(int count, int width) => Enumerable.Range(0, count).Select(i => new string((char)('a' + i % 26), width)).ToArray();

    [Fact]
    public void The_text_types_out_at_the_speech_box_rate_then_holds()
    {
        Assert.Equal(0, SpeechBoxText.Revealed(0, 40));
        Assert.Equal(Tuning.SpeechBoxCharsPerSecond, SpeechBoxText.Revealed(Tuning.Fps, 100));
        Assert.Equal(40, SpeechBoxText.Revealed(10 * Tuning.Fps, 40));            // never past the end
        var lines = Lines(3, 20);
        int reveal = SpeechBoxText.RevealFrames(60);
        Assert.Equal(60, SpeechBoxText.Revealed(reveal, 60));
        Assert.True(SpeechBoxText.Revealed(reveal - 2, 60) < 60);
        Assert.Equal(reveal + Tuning.SpeechBoxHoldFrames, SpeechBoxText.TotalFrames(lines, PageLines, 0));
        Assert.Equal(reveal + Tuning.SpeechBoxHoldFrames + 9, SpeechBoxText.TotalFrames(lines, PageLines, 9));
        double hold = Tuning.SpeechBoxHoldFrames / (double)Tuning.Fps;
        Assert.InRange(hold, 4, 5);                                          // "about 4-5 s"
    }

    [Fact]
    public void A_text_longer_than_a_page_is_paged_and_every_page_is_read()
    {
        var lines = Lines(10, 16);   // 10 lines of 16 chars: 4 + 4 + 2 lines make 3 pages
        Assert.Equal(3, SpeechBoxText.Pages(lines, PageLines));
        Assert.Equal(64, SpeechBoxText.PageChars(lines, PageLines, 0));   // a full page: PageLines * 16
        Assert.Equal(32, SpeechBoxText.PageChars(lines, PageLines, 2));                   // the last page holds what is left: 2 lines * 16
        var seen = new HashSet<int>();
        int total = SpeechBoxText.TotalFrames(lines, PageLines, 0);
        for (int age = 0; age < total; age++) seen.Add(SpeechBoxText.Progress(lines, PageLines, age).Page);
        Assert.Equal(3, seen.Count);
        var (last, shown) = SpeechBoxText.Progress(lines, PageLines, total - 1);
        Assert.Equal(2, last);
        Assert.Equal(SpeechBoxText.PageChars(lines, PageLines, last), shown);
    }

    [Fact]
    public void A_text_that_fits_one_page_has_one_page()
    {
        var lines = Lines(3, 20);
        Assert.Equal(1, SpeechBoxText.Pages(lines, PageLines));
        Assert.Equal(0, SpeechBoxText.Progress(lines, PageLines, 0).Page);
    }

    [Fact]
    public void A_narrow_field_gets_a_narrow_text_width()
    {
        Assert.Equal(SpeechBoxText.MinTextW, SpeechBoxText.TextWidth(30));
        Assert.Equal(SpeechBoxText.MaxTextW, SpeechBoxText.TextWidth(64));
        Assert.InRange(SpeechBoxText.TextWidth(40), SpeechBoxText.MinTextW, SpeechBoxText.MaxTextW);
    }
}
