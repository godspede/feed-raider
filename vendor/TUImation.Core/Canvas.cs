namespace TUImation;

/// <summary>
/// A small RGB pixel grid with flat-shaded drawing primitives. Null pixels are
/// "transparent" — the renderer lets the terminal's own background show through
/// instead of painting a hard box, so a sprite reads as a figure on the
/// terminal rather than a bounded card.
/// </summary>
public sealed class Canvas
{
    private readonly Rgb?[,] _pixels;

    public int Width { get; }
    public int Height { get; }

    public Canvas(int width, int height)
    {
        Width = width;
        Height = height;
        _pixels = new Rgb?[width, height];
    }

    public Rgb? this[int x, int y]
    {
        get => x >= 0 && x < Width && y >= 0 && y < Height ? _pixels[x, y] : null;
        set
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
                _pixels[x, y] = value;
        }
    }

    public void FillRect(double x0, double y0, double w, double h, Rgb color)
    {
        int xi0 = (int)Math.Round(x0), yi0 = (int)Math.Round(y0);
        int xi1 = (int)Math.Round(x0 + w), yi1 = (int)Math.Round(y0 + h);
        for (int y = yi0; y < yi1; y++)
            for (int x = xi0; x < xi1; x++)
                this[x, y] = color;
    }

    public void FillEllipse(double cx, double cy, double rx, double ry, Rgb color)
    {
        int minX = (int)Math.Floor(cx - rx), maxX = (int)Math.Ceiling(cx + rx);
        int minY = (int)Math.Floor(cy - ry), maxY = (int)Math.Ceiling(cy + ry);
        for (int y = minY; y <= maxY; y++)
        {
            double dy = (y + 0.5 - cy) / ry;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = (x + 0.5 - cx) / rx;
                if (dx * dx + dy * dy <= 1.0)
                    this[x, y] = color;
            }
        }
    }

    /// <summary>A rounded thick line between two points — the workhorse for limbs.
    /// Steps along the segment stamping filled disks, so joints (two segments
    /// sharing an endpoint) meet without a gap.</summary>
    public void FillThickLine(double x0, double y0, double x1, double y1, double thickness, Rgb color)
    {
        double dx = x1 - x0, dy = y1 - y0;
        double len = Math.Sqrt(dx * dx + dy * dy);
        int steps = (int)Math.Ceiling(len) + 1;
        double r = thickness / 2;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            FillEllipse(x0 + dx * t, y0 + dy * t, r, r, color);
        }
    }

    /// <summary>A short horizontal bar with a linear slope — used for angled brows.</summary>
    public void FillSlopedBar(double xCenter, double yCenter, double halfWidth, double thickness, double slope, Rgb color)
    {
        int x0 = (int)Math.Round(xCenter - halfWidth);
        int x1 = (int)Math.Round(xCenter + halfWidth);
        for (int x = x0; x <= x1; x++)
        {
            double y = yCenter + slope * (x - xCenter);
            FillRect(x, y - thickness / 2, 1, thickness, color);
        }
    }
}
