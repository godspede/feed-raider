namespace FeedRaider.Engine.Raider;

/// <summary>A rectangle of terminal cells.</summary>
public readonly record struct CellRect(int C, int R, int W, int H)
{
    public int Right => C + W;
    public int Bottom => R + H;
    public bool Overlaps(CellRect o) => C < o.Right && o.C < Right && R < o.Bottom && o.R < Bottom;
}

/// <summary>The timing and wrapping of a speech box: how wide its text is, how many characters are typed after so many frames, and
/// how the text pages. <see cref="EnemySpeechBox"/> uses it for an enemy's box; <see cref="SpeechBoxPainter"/> draws the result.</summary>
public static class SpeechBoxText
{
    public const int MaxTextW = 30, MinTextW = 16, TextLines = 11, MinPageLines = 4, SpeechBoxSide = 7;

    /// <summary>Text columns for a field <paramref name="fieldCols"/> wide: as wide as <see cref="MaxTextW"/> where there is room, never so wide the speech box covers the field.</summary>
    public static int TextWidth(int fieldCols) => Math.Clamp(fieldCols - 2 * SpeechBoxSide, MinTextW, MaxTextW);

    public static int RevealFrames(int chars) => (chars * Tuning.Fps + Tuning.SpeechBoxCharsPerSecond - 1) / Tuning.SpeechBoxCharsPerSecond;

    /// <summary>Characters on page <paramref name="page"/> of <paramref name="lines"/> read <paramref name="pageLines"/> at a time.</summary>
    public static int PageChars(string[] lines, int pageLines, int page)
    {
        int n = 0;
        for (int i = page * pageLines; i < Math.Min(lines.Length, (page + 1) * pageLines); i++) n += lines[i].Length;
        return n;
    }

    public static int Pages(string[] lines, int pageLines) => (lines.Length + pageLines - 1) / pageLines;

    /// <summary>Frames to read every page out, hold the last, and then <paramref name="tail"/> more.</summary>
    public static int TotalFrames(string[] lines, int pageLines, int tail)
    {
        int pages = Pages(lines, pageLines), total = tail;
        for (int p = 0; p < pages; p++) total += RevealFrames(PageChars(lines, pageLines, p)) + (p == pages - 1 ? Tuning.SpeechBoxHoldFrames : Tuning.SpeechBoxPageHoldFrames);
        return total;
    }

    /// <summary>The page on show and the characters of it typed so far, <paramref name="age"/> frames into the speech box.</summary>
    public static (int Page, int Shown) Progress(string[] lines, int pageLines, int age)
    {
        int pages = Pages(lines, pageLines);
        for (int p = 0; p < pages - 1; p++)
        {
            int chars = PageChars(lines, pageLines, p), span = RevealFrames(chars) + Tuning.SpeechBoxPageHoldFrames;
            if (age < span) return (p, Revealed(age, chars));
            age -= span;
        }
        return (Math.Max(0, pages - 1), Revealed(age, PageChars(lines, pageLines, Math.Max(0, pages - 1))));
    }

    /// <summary>Characters shown after <paramref name="age"/> frames of speaking.</summary>
    public static int Revealed(int age, int chars) => Math.Min(chars, age * Tuning.SpeechBoxCharsPerSecond / Tuning.Fps);
}
