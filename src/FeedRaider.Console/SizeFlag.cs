using System.Globalization;
using FeedRaider.Engine.Raider;

/// <summary>The headless runs' <c>--size</c> flag: a terminal size the field fits, no larger than <see cref="MaxCols"/> by <see cref="MaxRows"/>.</summary>
static class SizeFlag
{
    /// <summary>The largest size taken. It is far past any terminal, and a size with no upper limit overflows the screen's cell count.</summary>
    public const int MaxCols = 1000, MaxRows = 1000;

    /// <summary>What a refusal says a value must be.</summary>
    public static readonly string Rule = $"<cols>x<rows>, from {Tuning.FieldMinCols}x{Tuning.FieldMinRows} to {MaxCols}x{MaxRows}";

    public static bool TryParse(string? text, out int cols, out int rows)
    {
        cols = rows = 0;
        return text?.Split('x') is [var c, var r]
            && int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out cols) && cols <= MaxCols
            && int.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out rows) && rows <= MaxRows
            && FieldLayout.Compute(cols, rows).FitsMinimum;
    }
}
