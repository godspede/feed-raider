using System.Buffers;
using System.Text;

namespace FeedRaider.Engine;

/// <summary>Post text on the way to terminal cells, for every painter of it (the cards, the speech boxes, the feed log). The grid
/// holds one UTF-16 char per cell, and a wide char takes its cell and the one after it. A styled letter or digit from outside the
/// Basic Multilingual Plane becomes its plain one. A char the terminal can draw is kept; one it cannot is dropped. Nothing is ever swapped for a <c>?</c>: a text the drop leaves empty is empty, and shows nothing.</summary>
public static class CellText
{
    /// <summary>The cells a wide char takes: its own and the one after it.</summary>
    public const int WideCells = 2;

    /// <summary>True when the terminal draws <paramref name="ch"/> in one cell or two and the grid can hold it: a char of the Basic
    /// Multilingual Plane (a pair of surrogates is one char outside it, which the grid cannot hold), not a control, a mark or a
    /// joiner with no cell of its own, not the soft hyphen, and not one of the symbols a browser draws as a wide colour emoji.</summary>
    public static bool CanDraw(char ch) =>
        !char.IsSurrogate(ch) && ch != '\u00ad' && CellWidth.Of(ch) != 0 && !IsEmoji(ch);

    /// <summary>The cells <paramref name="ch"/> takes: 1 or 2 for a char that <see cref="CanDraw"/>, else 0.</summary>
    public static int Cells(char ch) => CanDraw(ch) ? CellWidth.Of(ch) : 0;

    /// <summary>The symbols a browser draws as emoji, wider than the cell the terminal's table gives them.</summary>
    internal static bool IsEmoji(char ch) =>
        (ch >= 0x231A && ch <= 0x23FF) || (ch >= 0x2600 && ch <= 0x27BF) ||
        ch is '\u25fd' or '\u25fe' or '\u2b1b' or '\u2b1c' or '\u2b50' or '\u2b55';

    /// <summary>The post's text as cells: runs of whitespace and controls become one space, a styled letter or digit is folded to
    /// its plain one (<see cref="PlainLetter"/>), every char that cannot be drawn is dropped, wide chars stay. Trimmed.</summary>
    public static string Clean(string? text) => Clean(text, true);

    /// <summary>Like <see cref="Clean(string)"/> but for a grid of one cell per char: the wide chars are dropped too.</summary>
    public static string CleanSingle(string? text) => Clean(text, false);

    private const string Capitals = "ABCDEFGHIJKLMNOPQRSTUVWXYZ", Letters = Capitals + "abcdefghijklmnopqrstuvwxyz", Digits = "0123456789";

    /// <summary>The styled Latin letters and digits outside the Basic Multilingual Plane that names and posts spell words with, each
    /// run with the plain letters it repeats from its first code point on, style after style.</summary>
    private static readonly (int First, int Last, string Plain)[] StyledRuns =
    [
        (0x1F130, 0x1F149, Capitals),   // squared
        (0x1F150, 0x1F169, Capitals),   // negative circled
        (0x1F170, 0x1F189, Capitals),   // negative squared
        (0x1D400, 0x1D6A3, Letters),    // bold, italic, script, fraktur, double-struck, sans-serif, monospace and their bold and italic mixes
        (0x1D7CE, 0x1D7FF, Digits),     // bold, double-struck, sans-serif and monospace
    ];

    /// <summary>The plain letter or digit a styled one of <see cref="StyledRuns"/> stands for, or null for any other code point.</summary>
    private static char? PlainLetter(Rune rune)
    {
        foreach (var (first, last, plain) in StyledRuns)
            if (rune.Value >= first && rune.Value <= last) return plain[(rune.Value - first) % plain.Length];
        return null;
    }

    private static string Clean(string? text, bool keepWide)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var kept = new StringBuilder(text.Length);
        bool lastSpace = true;
        for (int at = 0, used; at < text.Length; at += used)
        {
            // a lone surrogate is half of a char, which no cell can hold: it is dropped with the rest of what cannot be drawn
            if (Rune.DecodeFromUtf16(text.AsSpan(at), out Rune rune, out used) != OperationStatus.Done) continue;
            // outside the plane the grid holds no char, so only a styled letter, folded, gets a cell there
            if ((rune.IsBmp ? (char)rune.Value : PlainLetter(rune)) is not { } ch) continue;
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (!lastSpace) kept.Append(' ');
                lastSpace = true;
                continue;
            }
            int cells = Cells(ch);
            if (cells == 0 || (cells == WideCells && !keepWide)) continue;
            lastSpace = false;
            kept.Append(ch);
        }
        return kept.ToString().Trim();
    }

    /// <summary>The cells a string takes.</summary>
    public static int Width(string text)
    {
        int cells = 0;
        foreach (char ch in text) cells += CellWidth.Of(ch);
        return cells;
    }

    /// <summary><paramref name="text"/> padded with spaces on the right to <paramref name="width"/> cells.</summary>
    public static string PadRight(string text, int width) => Width(text) >= width ? text : text + new string(' ', width - Width(text));

    /// <summary>Cuts to <paramref name="width"/> cells without cutting a wide char in two; with <paramref name="ellipsis"/> a cut
    /// line ends in an ellipsis.</summary>
    public static string Clip(string text, int width, bool ellipsis)
    {
        if (Width(text) <= width) return text;
        int room = ellipsis ? width - 1 : width, used = 0, kept = 0;
        foreach (char ch in text)
        {
            int cells = CellWidth.Of(ch);
            if (used + cells > room) break;
            used += cells;
            kept++;
        }
        return text[..kept] + (ellipsis ? "\u2026" : "");
    }

    /// <summary>Wraps clean text on spaces by cell width; a wide char counts two; a word wider than the line is split, and never in the
    /// middle of a wide char. No line is wider than <paramref name="width"/> (a wide char alone in a line of one cell is dropped).</summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        int lineCells = 0;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int wordCells = Width(word), spacedCells = lineCells + " ".Length + wordCells;
            if (lineCells > 0 && spacedCells <= width) { line.Append(' ').Append(word); lineCells = spacedCells; continue; }
            if (lineCells > 0) { lines.Add(line.ToString()); line.Clear(); lineCells = 0; }
            if (wordCells <= width) { line.Append(word); lineCells = wordCells; continue; }
            foreach (char ch in word)
            {
                int cells = CellWidth.Of(ch);
                if (cells > width) continue;
                if (lineCells + cells > width) { lines.Add(line.ToString()); line.Clear(); lineCells = 0; }
                line.Append(ch);
                lineCells += cells;
            }
        }
        if (lineCells > 0) lines.Add(line.ToString());
        return lines;
    }
}
