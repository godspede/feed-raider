namespace FeedRaider.Engine;

/// <summary>Where each figure lives when idle. Pixel coordinates are the sprite's top-left corner.</summary>
public static class Layout
{
    public const int LabelRows = 1;
    public const int TopMarginPx = 2;
    /// <summary>Pixels of canvas per figure before the room is "full".</summary>
    private const int PixelsPerFigure = 120;

    public static int Capacity(int cols, int pixelHeight) =>
        Math.Max(1, cols * pixelHeight / PixelsPerFigure);

    /// <summary>Rows of figures, staggered like a crowd photo: back rows show heads, front rows show everything.</summary>
    public static (double X, double Y)[] Homes(int count, int cols, int pixelHeight)
    {
        var homes = new (double, double)[count];
        if (count == 0) return homes;
        double spanX = Math.Max(0, cols - FigureBuilder.Width);
        double spanY = Math.Max(0, pixelHeight - FigureBuilder.Height - LabelRows * 2 - TopMarginPx);
        int gcols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(count * (double)cols / pixelHeight)), 1, Math.Max(1, cols / FigureBuilder.Width));
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

    private const int SlackX = 3, TallSlackY = 2, ShortSlackY = 1;
    /// <summary>A canvas taller than this many pixels gets the larger vertical wander.</summary>
    private const int TallRoomPx = 40;

    /// <summary>Wander amplitude in pixels, kept inside the figure's own cell so neighbours do not collide.</summary>
    public static (double X, double Y) Slack(int pixelHeight) => (SlackX, pixelHeight > TallRoomPx ? TallSlackY : ShortSlackY);
}
