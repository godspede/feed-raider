using System.Text;

namespace FeedRaider.Engine;

/// <summary>Turns post text into a few short terminal-safe lines.</summary>
public static class BubbleText
{
    public static string[] Format(string text, int maxWidth = 22, int maxLines = 3)
    {
        string clean = Sanitize(text);
        var lines = new List<string>();
        var cur = new StringBuilder();
        bool truncated = false;

        foreach (string raw in clean.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string word = raw;
            while (word.Length > 0)
            {
                int need = cur.Length == 0 ? word.Length : cur.Length + 1 + word.Length;
                if (need <= maxWidth)
                {
                    if (cur.Length > 0) cur.Append(' ');
                    cur.Append(word);
                    word = "";
                }
                else if (cur.Length > 0)
                {
                    lines.Add(cur.ToString());
                    cur.Clear();
                    if (lines.Count == maxLines) { truncated = true; word = ""; break; }
                }
                else
                {
                    cur.Append(word, 0, maxWidth);
                    word = word[maxWidth..];
                    lines.Add(cur.ToString());
                    cur.Clear();
                    if (lines.Count == maxLines) { truncated = true; word = ""; break; }
                }
            }
            if (truncated) break;
        }
        if (!truncated && cur.Length > 0) lines.Add(cur.ToString());

        if (lines.Count == 0) lines.Add("...");
        if (truncated)
        {
            string last = lines[^1];
            lines[^1] = (last.Length >= maxWidth ? last[..(maxWidth - 1)] : last) + "…";
        }
        return lines.ToArray();
    }

    /// <summary>Single-cell-wide printable text only: wide (CJK, emoji) and combining characters
    /// would knock the cell grid out of line, so they become '?' or vanish. Widths are xterm's (<see cref="CellWidth"/>).</summary>
    public static string Sanitize(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool lastSpace = true;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            if (char.IsLowSurrogate(ch)) continue;
            // marks such as U+20E3 (the combining keycap, as in "2" + U+FE0F + U+20E3) have no cell of their own in xterm
            if (!char.IsHighSurrogate(ch) && (CellWidth.Of(ch) == 0 || ch == '­')) continue;   // a browser draws the soft hyphen 0 wide
            lastSpace = false;
            if (char.IsHighSurrogate(ch)) { if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++; sb.Append('?'); continue; }
            if (IsWide(ch)) { sb.Append('?'); continue; }
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    // xterm's wide chars, and the symbols a browser draws as emoji, wider than the cell xterm gives them
    private static bool IsWide(char c) =>
        CellWidth.Of(c) == 2 || (c >= 0x231A && c <= 0x23FF) || (c >= 0x2600 && c <= 0x27BF) ||
        c is '\u25fd' or '\u25fe' or '\u2b1b' or '\u2b1c' or '\u2b50' or '\u2b55';
}
