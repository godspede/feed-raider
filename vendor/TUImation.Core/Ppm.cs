using System.Text;

namespace TUImation;

/// <summary>
/// Writes a Canvas to a raw PPM (P6) image. A debug escape hatch for eyeballing
/// a sprite outside a truecolor terminal: dump a frame, convert to PNG (see
/// tools/ppm2png.py), and look at it. Transparent pixels get the given
/// background color.
/// </summary>
public static class Ppm
{
    public static void Write(Canvas canvas, string path, Rgb background)
    {
        using var fs = File.Create(path);
        fs.Write(Encoding.ASCII.GetBytes($"P6\n{canvas.Width} {canvas.Height}\n255\n"));
        for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
            {
                var px = canvas[x, y] ?? background;
                fs.WriteByte(px.R);
                fs.WriteByte(px.G);
                fs.WriteByte(px.B);
            }
    }
}
