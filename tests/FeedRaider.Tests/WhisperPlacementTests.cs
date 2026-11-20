using System.Text.RegularExpressions;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

// Where a mote's text fragment lands: the row over its head, a column kept on the frame, and a blank cell of padding at each end.
public class WhisperPlacementTests
{
    private const string Moss = "48;2;28;36;16";

    /// <summary>The frame as rows of (character, background code) cells.</summary>
    private static List<(char Ch, string Bg)[]> Cells(Frame f)
    {
        var rows = new List<(char, string)[]>();
        foreach (string line in f.ToAnsi().Split("\r\n").SkipLast(1).Select(l => l.Replace("\u001b[H", "")))
        {
            var cells = new List<(char, string)>();
            string bg = "";
            foreach (Match m in Regex.Matches(line, "\u001b\\[([0-9;]*)([A-Za-z])|(.)", RegexOptions.Singleline))
            {
                if (m.Groups[3].Success) { cells.Add((m.Groups[3].Value[0], bg)); continue; }
                string code = m.Groups[1].Value;
                if (m.Groups[2].Value != "m") continue;
                if (code.StartsWith("48;2;")) bg = code; else if (code is "49" or "0" or "") bg = "";
            }
            rows.Add([.. cells]);
        }
        return rows;
    }

    private static string Text(Frame f, int row) => new(Cells(f)[row].Select(c => c.Ch).ToArray());

    private static int[] MossColumns(Frame f, int row) => [.. Cells(f)[row].Select((c, i) => (c, i)).Where(p => p.c.Bg == Moss).Select(p => p.i)];

    [Fact]
    public void The_fragment_is_on_the_row_above_the_head()
    {
        var f = new Frame(30, 10);
        f.Whisper("hello", 15, 5);
        Assert.Contains("hello", Text(f, 4));
        Assert.DoesNotContain("hello", Text(f, 5));
    }

    [Fact]
    public void A_head_on_the_top_row_keeps_the_fragment_on_the_top_row()
    {
        var f = new Frame(30, 10);
        f.Whisper("hello", 15, 0);
        Assert.Contains("hello", Text(f, 0));
    }

    [Fact]
    public void The_fragment_is_padded_by_one_blank_cell_at_each_end_and_centred_on_the_mote()
    {
        var f = new Frame(30, 10);
        f.Whisper("hello", 15, 5);   // " hello " is 7 cells, so it starts at 15 - 3
        Assert.Equal(Enumerable.Range(12, 7), MossColumns(f, 4));
        Assert.Equal(" hello ", Text(f, 4).Substring(12, 7));
    }

    [Fact]
    public void A_fragment_near_the_left_edge_starts_on_the_first_column()
    {
        var f = new Frame(30, 10);
        f.Whisper("hello", 1, 5);
        Assert.Equal(Enumerable.Range(0, 7), MossColumns(f, 4));
        Assert.Equal(" hello ", Text(f, 4).Substring(0, 7));
    }

    [Fact]
    public void A_fragment_near_the_right_edge_ends_on_the_last_column()
    {
        var f = new Frame(30, 10);
        f.Whisper("hello", 29, 5);
        Assert.Equal(Enumerable.Range(23, 7), MossColumns(f, 4));
        Assert.Equal(" hello ", Text(f, 4).Substring(23, 7));
    }

    [Fact]
    public void A_fragment_wider_than_the_frame_is_cut_at_the_right_edge_and_starts_at_the_left()
    {
        var f = new Frame(6, 4);
        f.Whisper("abcdefghij", 3, 2);
        Assert.Equal(" abcde", Text(f, 1));
    }
}
