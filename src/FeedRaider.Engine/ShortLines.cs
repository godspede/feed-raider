using System.Text;

namespace FeedRaider.Engine;

/// <summary>Turns post text into a few short terminal-safe lines.</summary>
public static class ShortLines
{
    public const int DefaultMaxWidth = 22, DefaultMaxLines = 3;

    public static string[] Format(string text, int maxWidth = DefaultMaxWidth, int maxLines = DefaultMaxLines)
    {
        string clean = CellText.CleanSingle(text);
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
}
