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
    /// would knock the cell grid out of line, so they become '?' or vanish.</summary>
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
            lastSpace = false;
            if (char.IsHighSurrogate(ch)) { i++; sb.Append('?'); continue; }
            if (char.IsLowSurrogate(ch)) continue;
            if ((ch >= 0x300 && ch <= 0x36F) || (ch >= 0x200B && ch <= 0x200F) || (ch >= 0x202A && ch <= 0x202E) || (ch >= 0x2060 && ch <= 0x206F) || ch == 0xFEFF || (ch >= 0xFE00 && ch <= 0xFE0F)) continue;
            if (IsWide(ch)) { sb.Append('?'); continue; }
            sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    private static bool IsWide(char c) =>
        (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3) ||
        (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE6F) || (c >= 0xFF00 && c <= 0xFF60) ||
        (c >= 0xFFE0 && c <= 0xFFE6) || (c >= 0x231A && c <= 0x23FF) || (c >= 0x2600 && c <= 0x27BF);
}
