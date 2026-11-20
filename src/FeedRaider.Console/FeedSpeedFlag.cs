/// <summary>The terminal's <c>--feed-speed</c> flag: a feed speed step from 1 to 5, nothing else.</summary>
static class FeedSpeedFlag
{
    public static bool TryParse(string? text, out int step)
    {
        step = 0;
        return int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out step)
            && step is >= FeedRaider.Engine.Raider.FeedSpeed.Min and <= FeedRaider.Engine.Raider.FeedSpeed.Max;
    }
}
