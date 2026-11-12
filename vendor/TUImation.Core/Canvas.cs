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
}
