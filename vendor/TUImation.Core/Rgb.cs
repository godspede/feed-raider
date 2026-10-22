namespace TUImation;

/// <summary>A 24-bit RGB pixel. Two of these stack into one terminal cell (see Renderer).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public Rgb Darken(double factor) =>
        new((byte)(R * factor), (byte)(G * factor), (byte)(B * factor));

    /// <summary>Desaturate to gray at the same perceived brightness (Rec.601
    /// luma), so a figure keeps its exact form and only its hue drains away.</summary>
    public Rgb Grayscale()
    {
        byte l = (byte)Math.Clamp(0.299 * R + 0.587 * G + 0.114 * B, 0, 255);
        return new Rgb(l, l, l);
    }

    public Rgb Lerp(Rgb other, double t) =>
        new(
            (byte)(R + (other.R - R) * t),
            (byte)(G + (other.G - G) * t),
            (byte)(B + (other.B - B) * t));
}
