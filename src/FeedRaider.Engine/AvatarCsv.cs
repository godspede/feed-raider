namespace FeedRaider.Engine;

/// <summary>The avatar the page hands over: 8x8 pixels as comma-separated numbers.</summary>
public static class AvatarCsv
{
    /// <summary>The number of pixels in an avatar.</summary>
    public const int Pixels = 64;

    /// <summary>The pixels, or null when there are not exactly <see cref="Pixels"/> of them. A value that is not a number throws
    /// <see cref="FormatException"/> (or <see cref="OverflowException"/>), which the caller reports.</summary>
    public static uint[]? Parse(string csv)
    {
        var px = csv.Split(',').Select(uint.Parse).ToArray();
        return px.Length == Pixels ? px : null;
    }
}
