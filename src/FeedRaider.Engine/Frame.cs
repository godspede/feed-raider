using System.Text;
using TUImation;

namespace FeedRaider.Engine;

/// <summary>
/// A pixel layer (half-block, via Canvas) with a text layer on top. Speech needs real glyphs, which
/// the vendored Renderer cannot do, so frames are composed here: a cell with text shows the text,
/// any other cell shows the same half-block pair Renderer would emit.
/// </summary>
internal sealed class Frame
{
    private readonly record struct Cell(char Ch, Rgb Fg, Rgb? Bg);

    private static readonly Rgb Cream = new(0xfb, 0xf6, 0xe6);
    private static readonly Rgb Charcoal = new(0x10, 0x13, 0x16);
    private static readonly Rgb Khaki = new(0xd8, 0xcb, 0xa3);
    private static readonly Rgb Olive = new(0xa3, 0xb3, 0x5f);

    private readonly int _cols, _rows;
    private readonly Canvas _pix;
    private readonly Cell?[,] _text;
    // top layer: monsters and their text. It is composited over everything below, whatever the call order,
    // so no guy, label or speech bubble can ever hide a monster or its words.
    private readonly Canvas _topPix;
    private readonly Cell?[,] _topText;
    private string _status = "";

    public Frame(int cols, int rows)
    {
        _cols = cols;
        _rows = rows;
        _pix = new Canvas(cols, rows * 2);
        _text = new Cell?[cols, rows];
        _topPix = new Canvas(cols, rows * 2);
        _topText = new Cell?[cols, rows];
    }

    /// <summary>Draws a sprite on the top layer: it covers guys, labels and bubbles beneath it.</summary>
    public void BlitTop(Canvas sprite, int x0, int y0)
    {
        for (int y = 0; y < sprite.Height; y++)
            for (int x = 0; x < sprite.Width; x++)
                if (sprite[x, y] is { } c && x0 + x >= 0 && x0 + x < _cols && y0 + y >= 0 && y0 + y < _rows * 2)
                    _topPix[x0 + x, y0 + y] = c;
    }

    /// <summary>Draws a sprite; opaque pixels also wipe any text underneath, so a guy in front hides labels behind him.</summary>
    public void Blit(Canvas sprite, int x0, int y0)
    {
        for (int y = 0; y < sprite.Height; y++)
            for (int x = 0; x < sprite.Width; x++)
                if (sprite[x, y] is { } c && x0 + x >= 0 && x0 + x < _cols && y0 + y >= 0 && y0 + y < _rows * 2)
                {
                    _pix[x0 + x, y0 + y] = c;
                    _text[x0 + x, (y0 + y) / 2] = null;
                }
    }

    public void Label(string handle, int centerX, int row)
    {
        string s = BubbleText.Sanitize(handle);
        if (s.Length > GuyBuilder.Width) s = s[..(GuyBuilder.Width - 1)] + "…";
        int left = centerX - s.Length / 2;
        for (int i = 0; i < s.Length; i++)
            Put(left + i, row, new Cell(s[i], Khaki, null));
    }

    /// <summary>A speech bubble whose tail sits just above <paramref name="headRow"/>.</summary>
    public void Bubble(string[] lines, int centerX, int headRow)
    {
        int w = lines.Max(l => l.Length) + 2;
        int left = Math.Clamp(centerX - w / 2, 0, Math.Max(0, _cols - w));
        int tailRow = headRow - 1;
        int top = tailRow - lines.Length;
        if (top < 0) { top = 0; tailRow = lines.Length; }
        for (int i = 0; i < lines.Length; i++)
        {
            string l = " " + lines[i].PadRight(w - 2) + " ";
            for (int x = 0; x < w; x++) Put(left + x, top + i, new Cell(l[x], Charcoal, Cream));
        }
        Put(Math.Clamp(centerX, 0, _cols - 1), tailRow, new Cell('▼', Cream, null));
    }

    private static readonly Rgb Void = new(0x14, 0x0c, 0x1e);
    private static readonly Rgb Violet = new(0x4a, 0x2a, 0x70);
    private static readonly Rgb Bone = new(0xe3, 0xd9, 0xf0);
    private static readonly Rgb Moss = new(0x1c, 0x24, 0x10);
    private static readonly Rgb SickGreen = new(0xc6, 0xd8, 0x6c);

    /// <summary>The brute's speech: near-black with a sawtooth top and bottom edge.</summary>
    public void JaggedBubble(string[] lines, int centerX, int headRow)
    {
        int w = lines.Max(l => l.Length) + 2;
        int left = Math.Clamp(centerX - w / 2, 0, Math.Max(0, _cols - w));
        int top = Math.Max(0, headRow - 1 - lines.Length - 2);
        for (int x = 0; x < w; x++) PutTop(left + x, top, new Cell(x % 2 == 0 ? '▲' : ' ', Violet, null));
        for (int i = 0; i < lines.Length; i++)
        {
            string l = " " + lines[i].PadRight(w - 2) + " ";
            for (int x = 0; x < w; x++) PutTop(left + x, top + 1 + i, new Cell(l[x], Bone, Void));
        }
        int bottom = top + 1 + lines.Length;
        for (int x = 0; x < w; x++) PutTop(left + x, bottom, new Cell(x % 2 == 0 ? ' ' : '▼', Violet, null));
    }

    /// <summary>A mote's text fragment: one dark-green line over its head.</summary>
    public void Whisper(string text, int centerX, int headRow)
    {
        string s = " " + text + " ";
        int left = Math.Clamp(centerX - s.Length / 2, 0, Math.Max(0, _cols - s.Length));
        for (int i = 0; i < s.Length; i++) PutTop(left + i, Math.Max(0, headRow - 1), new Cell(s[i], SickGreen, Moss));
    }

    public void Status(string text) => _status = BubbleText.Sanitize(text);

    private void PutTop(int x, int y, Cell c)
    {
        if (x >= 0 && x < _cols && y >= 0 && y < _rows) _topText[x, y] = c;
    }

    private void Put(int x, int y, Cell c)
    {
        if (x >= 0 && x < _cols && y >= 0 && y < _rows) _text[x, y] = c;
    }

    public string ToAnsi()
    {
        var sb = new StringBuilder();
        sb.Append("\x1b[H");
        for (int r = 0; r < _rows; r++)
        {
            Rgb? lastFg = null, lastBg = null;
            bool reset = true;
            sb.Append("\x1b[0m");
            for (int x = 0; x < _cols; x++)
            {
                Rgb? fg, bg;
                char ch;
                var topUp = _topPix[x, r * 2];
                var topLo = _topPix[x, r * 2 + 1];
                if (_topText[x, r] is { } top) { fg = top.Fg; bg = top.Bg; ch = top.Ch; }
                else if (topUp is not null || topLo is not null)
                {
                    // a top sprite pixel hides whatever is under it; an empty half shows the layer below
                    // (a guy's text in this cell is covered, since the sprite is in front of it)
                    fg = topUp ?? _pix[x, r * 2];
                    bg = topLo ?? _pix[x, r * 2 + 1];
                    ch = fg is null && bg is null ? ' ' : '▀';
                }
                else if (_text[x, r] is { } cell) { fg = cell.Fg; bg = cell.Bg; ch = cell.Ch; }
                else
                {
                    fg = _pix[x, r * 2];
                    bg = _pix[x, r * 2 + 1];
                    ch = fg is null && bg is null ? ' ' : '▀';
                }
                if (ch == '▀' && fg is null) { ch = '▄'; fg = bg; bg = null; }   // only the lower half is lit
                if (fg != lastFg || reset)
                    sb.Append(fg is { } f ? $"\x1b[38;2;{f.R};{f.G};{f.B}m" : "\x1b[39m");
                if (bg != lastBg || reset)
                    sb.Append(bg is { } b ? $"\x1b[48;2;{b.R};{b.G};{b.B}m" : "\x1b[49m");
                reset = false;
                lastFg = fg;
                lastBg = bg;
                sb.Append(ch);
            }
            sb.Append("\x1b[0m\x1b[K\r\n");
        }
        // status line has no trailing newline, so the last row never scrolls the screen
        string s = _status.Length > _cols ? _status[.._cols] : _status;
        sb.Append($"\x1b[38;2;{Olive.R};{Olive.G};{Olive.B}m{s}\x1b[0m\x1b[K");
        return sb.ToString();
    }
}
