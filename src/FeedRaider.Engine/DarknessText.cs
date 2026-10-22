using System.Text.RegularExpressions;

namespace FeedRaider.Engine;

/// <summary>The anonymity rule lives here: whatever text a monster carries went through <see cref="Clean"/>.</summary>
public static partial class DarknessText
{
    public const int MaxChars = 60;

    // @mentions, URLs, hashtags, DIDs, and bare handles / domains (anything dotted that looks like a hostname)
    [GeneratedRegex(@"(?:https?://|www\.)\S+|@\S+|#\S+|\bdid:[a-z0-9]+:\S+|\b[a-z0-9][a-z0-9-]*(?:\.[a-z0-9-]+)*\.(?:[a-z]{2,})(?:/\S*)?(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Identifying();

    /// <summary>Strips mentions, URLs, hashtags and handle-like tokens, collapses whitespace, truncates to about 60 characters.</summary>
    public static string Clean(string? text, int max = MaxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string s = Identifying().Replace(text, " ");
        s = BubbleText.Sanitize(s);
        if (s.Length <= max) return s;
        int cut = s.LastIndexOf(' ', max - 1);
        if (cut < max / 2) cut = max - 1;
        return s[..cut].TrimEnd() + "…";
    }

    /// <summary>Splits clean text into short fragments of at most <paramref name="width"/> characters for spam motes.</summary>
    public static List<string> Fragments(string clean, int width = 14)
    {
        var parts = new List<string>();
        var cur = "";
        foreach (var w in clean.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string word = w.Length > width ? w[..width] : w;
            if (cur.Length == 0) cur = word;
            else if (cur.Length + 1 + word.Length <= width) cur += " " + word;
            else { parts.Add(cur); cur = word; }
        }
        if (cur.Length > 0) parts.Add(cur);
        return parts;
    }
}
