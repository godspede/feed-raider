namespace TUImation;

/// <summary>A 24-bit RGB pixel. Two of these stack into one terminal cell.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public Rgb Darken(double factor) =>
        new((byte)(R * factor), (byte)(G * factor), (byte)(B * factor));

    public Rgb Lerp(Rgb other, double t) =>
        new(
            (byte)(R + (other.R - R) * t),
            (byte)(G + (other.G - G) * t),
            (byte)(B + (other.B - B) * t));
}
