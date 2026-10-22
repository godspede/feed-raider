using System.Text;

namespace TUImation;

/// <summary>
/// Renders a Canvas to a 24-bit-truecolor ANSI string using the half-block
/// technique: each terminal cell shows the ▀ (upper half block) glyph with
/// its foreground color set to the top pixel and its background color set to
/// the bottom pixel. That doubles the vertical resolution per cell — one row
/// of text cells carries two rows of "pixels" — which is the same trick tools
/// like chafa/viu use to show real photos in a terminal. It's the practical
/// ceiling for plain ANSI text: real bitmap fidelity needs a graphics protocol
/// (Sixel / Kitty / iTerm2 inline images) instead, which this deliberately does
/// not depend on, so it degrades gracefully over plain SSH in any terminal that
/// understands 24-bit color.
///
/// Rows join with newlines (none trailing) and each ends with \x1b[K, so the
/// caller can redraw in place without a full-screen clear (no black flash —
/// see each sprite's Program.cs).
/// </summary>
public static class Renderer
{
    public static string Render(Canvas canvas)
    {
        var sb = new StringBuilder();
        bool firstRow = true;
        for (int y = 0; y < canvas.Height; y += 2)
        {
            if (!firstRow)
                sb.Append('\n');
            firstRow = false;

            Rgb? lastFg = null;
            Rgb? lastBg = null;
            bool first = true;

            for (int x = 0; x < canvas.Width; x++)
            {
                Rgb? top = canvas[x, y];
                Rgb? bottom = y + 1 < canvas.Height ? canvas[x, y + 1] : null;

                if (top is null && bottom is null)
                {
                    if (lastFg is not null || lastBg is not null || first)
                        sb.Append("\x1b[0m");
                    sb.Append(' ');
                    lastFg = null;
                    lastBg = null;
                }
                else
                {
                    if (top != lastFg)
                        sb.Append(top is { } t ? $"\x1b[38;2;{t.R};{t.G};{t.B}m" : "\x1b[39m");
                    if (bottom != lastBg)
                        sb.Append(bottom is { } b ? $"\x1b[48;2;{b.R};{b.G};{b.B}m" : "\x1b[49m");
                    sb.Append('▀'); // ▀ upper half block
                    lastFg = top;
                    lastBg = bottom;
                }

                first = false;
            }

            sb.Append("\x1b[0m\x1b[K");
        }

        return sb.ToString();
    }
}
