namespace FeedRaider.Engine;

/// <summary>Where each guy lives when idle. Pixel coordinates are the sprite's top-left corner.</summary>
public static class Layout
{
    public const int LabelRows = 1;
    public const int TopMarginPx = 2;
    /// <summary>Pixels of canvas per guy before the room is "full".</summary>
    private const int PixelsPerGuy = 120;

    public static int Capacity(int cols, int pixelHeight) =>
        Math.Max(1, cols * pixelHeight / PixelsPerGuy);

    /// <summary>Rows of guys, staggered like a crowd photo: back rows show heads, front rows show everything.</summary>
    public static (double X, double Y)[] Homes(int count, int cols, int pixelHeight)
    {
        var homes = new (double, double)[count];
        if (count == 0) return homes;
        double spanX = Math.Max(0, cols - GuyBuilder.Width);
        double spanY = Math.Max(0, pixelHeight - GuyBuilder.Height - LabelRows * 2 - TopMarginPx);
        int gcols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(count * (double)cols / pixelHeight)), 1, Math.Max(1, cols / GuyBuilder.Width));
        int grows = (count + gcols - 1) / gcols;
        for (int i = 0; i < count; i++)
        {
            int r = i / gcols, c = i % gcols;
            double x = gcols == 1 ? spanX / 2
                : spanX * (c + 0.5 * (grows > 1 ? (r & 1) : 0)) / (gcols - (grows > 1 ? 0.5 : 1.0));
            double y = TopMarginPx + (grows == 1 ? spanY / 2 : spanY * r / (grows - 1));
            homes[i] = (Math.Round(x), Math.Round(y));
        }
        return homes;
    }

    /// <summary>Wander amplitude in pixels, kept inside the guy's own cell so neighbours do not collide.</summary>
    public static (double X, double Y) Slack(int count, int cols, int pixelHeight) => (3, pixelHeight > 40 ? 2 : 1);
}
