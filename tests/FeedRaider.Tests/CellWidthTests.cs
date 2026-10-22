using System.Text.Json;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// The engine keeps one char per terminal cell. A char xterm draws 0 or 2 cells wide moves xterm's cursor off the engine's
// grid, and every later cell on that row lands in the wrong place, leaving stale blocks over the text.
public class CellWidthTests
{
    // posts of the kinds that broke a live run: keycaps, enclosing marks, right-to-left and Indic marks, emoji, ZWJ, CJK
    public static TheoryData<string, string> Posts => new()
    {
        { "2\ufe0f\u20e3 peas", "2 peas" },
        { "A⃝ THE TIME", "A THE TIME" },
        { "\u2139\ufe0f I. Gerald enjoys museums", "\u2139 I. Gerald enjoys museums" },
        { "שָׁלוֹם friends", "שלום friends" },
        { "مَرْحَبًا hi", "مرحبا hi" },
        { "สวัสดี thai", "สวสด thai" },
        { "नमस्ते hindi", "नमसत hindi" },
        { "café e҉ Z̤ͧalgo", "cafe e Zalgo" },
        { "\u2b50 star \u2b55 ring \u2b1b box \u2705 ok", "? star ? ring ? box ? ok" },
        { "\U0001f468\u200d\U0001f469\u200d\U0001f467 family", "??? family" },
        { "\U0001f3f3\ufe0f\u200d\U0001f308 flag", "?? flag" },
        { "日本語 ＡＢ　wide", "??? ?? wide" },
        { "word⁠joiner ​ zero ­soft", "wordjoiner zero soft" },
        { "⃣⃣ lead", "lead" },
    };

    [Theory, MemberData(nameof(Posts))]
    public void Sanitized_post_text_is_one_xterm_cell_per_char(string text, string expected)
    {
        string clean = BubbleText.Sanitize(text);
        Assert.Equal(expected, clean);
        Assert.All(clean, c => Assert.Equal(1, CellWidth.Of(c)));
    }

    [Fact]
    public void Widths_match_what_xterm_reports_for_every_BMP_char()
    {
        // dumped from xterm.js 5.5.0 in a browser: unicodeService.wcwidth for 0..FFFF, runs of width 0 or 2
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xterm-widths.json")));
        var want = new int[0x10000];
        Array.Fill(want, 1);
        foreach (var run in doc.RootElement.GetProperty("runs").EnumerateArray())
            for (int cp = run[0].GetInt32(); cp <= run[1].GetInt32(); cp++) want[cp] = run[2].GetInt32();
        for (int cp = 0; cp < 0x10000; cp++) Assert.True(want[cp] == CellWidth.Of((char)cp), $"U+{cp:X4}: xterm {want[cp]}, engine {CellWidth.Of((char)cp)}");
    }

    [Fact]
    public void A_char_that_would_shift_the_row_never_reaches_the_screen()
    {
        var s = new Screen(12, 1);
        s.Text(0, 0, "A⃝B日C", 0xFFFFFF);
        Assert.Equal("A?B?C", new string(s.Ch, 0, 5));
        s.Text(0, 0, "\u2588\u258c\u2026\u2665\u2598", 0xFFFFFF);
        Assert.Equal("\u2588\u258c\u2026\u2665\u2598", new string(s.Ch, 0, 5));
    }
}
