using System.Text;
using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>The terminal as a grid of cells: a glyph, a foreground and a background (0xRRGGBB).</summary>
public sealed class Screen
{
    public int Cols { get; }
    public int Rows { get; }
    public readonly char[] Ch;
    public readonly uint[] Fg, Bg;

    public Screen(int cols, int rows)
    {
        Cols = cols; Rows = rows;
        Ch = new char[cols * rows];
        Fg = new uint[cols * rows];
        Bg = new uint[cols * rows];
    }

    public void Clear(uint bg)
    {
        Array.Fill(Ch, ' ');
        Array.Fill(Fg, 0xFFFFFFu);
        Array.Fill(Bg, bg);
    }

    /// <summary>Puts a glyph. A null background keeps the cell's own (so text sits on the picture beneath it).</summary>
    public void Put(int c, int r, char ch, uint fg, uint? bg = null)
    {
        if ((uint)c >= (uint)Cols || (uint)r >= (uint)Rows) return;
        int i = r * Cols + c;
        // a char xterm draws 0 or 2 cells wide would shift the rest of the row; text should come through BubbleText.Sanitize
        Ch[i] = ch < 0x300 || CellWidth.Single(ch) ? ch : '?'; Fg[i] = fg;
        if (bg is { } b) Bg[i] = b;
    }

    public void Text(int c, int r, string s, uint fg, uint? bg = null)
    {
        for (int i = 0; i < s.Length; i++) Put(c + i, r, s[i], fg, bg);
    }

    /// <summary>Paints a w-cell run of spaces on a background: the allocation-free form of <c>Text(c, r, new string(' ', w), ...)</c>.</summary>
    public void Blank(int c, int r, int w, uint fg, uint bg)
    {
        for (int i = 0; i < w; i++) Put(c + i, r, ' ', fg, bg);
    }

    public char CharAt(int c, int r) => Ch[r * Cols + c];
    public uint BgAt(int c, int r) => Bg[r * Cols + c];
}

/// <summary>
/// Turns a pixel <see cref="Surface"/> into quadrant-glyph cells (2x2 pixels per cell) through the vendored
/// <see cref="QuadRenderer"/>. Only cells whose four pixels changed since the last frame are re-reduced; the reduction
/// is the expensive part under the browser's interpreter, and a scrolling dim backdrop leaves most cells alone.
/// </summary>
public sealed class QuadComposer
{
    private uint[] _prev = [];
    private char[] _glyph = [];
    private uint[] _fg = [], _bg = [];
    private readonly uint[] _px = new uint[4];
    public int Reduced { get; private set; }

    /// <summary>Writes the surface into <paramref name="scr"/> at cell (col0, row0), reading it shifted by (ox, oy) pixels
    /// (the screen shake). Edge pixels the shift uncovers repeat the nearest row or column.</summary>
    public void Compose(Surface s, int ox, int oy, Screen scr, int col0, int row0)
    {
        int cols = s.W / 2, rows = s.H / 2;
        if (_prev.Length != cols * rows * 4)
        {
            _prev = new uint[cols * rows * 4];
            Array.Fill(_prev, 0xFF000000u);
            _glyph = new char[cols * rows];
            _fg = new uint[cols * rows];
            _bg = new uint[cols * rows];
        }
        Reduced = 0;
        var p = s.P;
        var prev = _prev;
        int w = s.W, h = s.H;
        bool still = ox == 0 && oy == 0;
        // the cells that land on the screen: the rest are still reduced (they stay cached) but not copied
        int cxLo = Math.Max(0, -col0), cxHi = Math.Min(cols, scr.Cols - col0);
        for (int cy = 0; cy < rows; cy++)
        {
            int sr = row0 + cy;
            bool onScreen = (uint)sr < (uint)scr.Rows;
            int o = sr * scr.Cols + col0;
            int i = cy * cols;
            int rowA, rowB;
            if (still) { rowA = cy * 2 * w; rowB = rowA + w; }
            else
            {
                rowA = Math.Clamp(cy * 2 - oy, 0, h - 1) * w;
                rowB = Math.Clamp(cy * 2 + 1 - oy, 0, h - 1) * w;
            }
            for (int cx = 0; cx < cols; cx++, i++)
            {
                int x0, x1;
                if (still) { x0 = cx * 2; x1 = x0 + 1; }
                else { x0 = Math.Clamp(cx * 2 - ox, 0, w - 1); x1 = Math.Clamp(cx * 2 + 1 - ox, 0, w - 1); }
                uint a = p[rowA + x0], b = p[rowA + x1], c = p[rowB + x0], d = p[rowB + x1];
                int j = i * 4;
                if (prev[j] != a || prev[j + 1] != b || prev[j + 2] != c || prev[j + 3] != d)
                {
                    prev[j] = a; prev[j + 1] = b; prev[j + 2] = c; prev[j + 3] = d;
                    Reduce(i, a, b, c, d);
                    Reduced++;
                }
                if (onScreen && cx >= cxLo && cx < cxHi)
                {
                    int t = o + cx;
                    scr.Ch[t] = _glyph[i]; scr.Fg[t] = _fg[i]; scr.Bg[t] = _bg[i];
                }
            }
        }
    }

    private void Reduce(int i, uint a, uint b, uint c, uint d)
    {
        // at most two distinct colours is the common case and exact: skip the general reduction
        uint other = a;
        bool have = false, many = false;
        if (b != a) { other = b; have = true; }
        if (c != a) { if (!have) { other = c; have = true; } else if (c != other) many = true; }
        if (d != a) { if (!have) { other = d; have = true; } else if (d != other) many = true; }
        if (!many)
        {
            if (!have) { _glyph[i] = ' '; _fg[i] = a; _bg[i] = a; return; }
            int mask = 1 | (b == a ? 2 : 0) | (c == a ? 4 : 0) | (d == a ? 8 : 0);
            _glyph[i] = QuadRenderer.GlyphFor(mask); _fg[i] = a; _bg[i] = other;
            return;
        }
        var px = _px;
        px[0] = a; px[1] = b; px[2] = c; px[3] = d;
        int m = SplitMask(px, out uint fg, out uint bg);
        _glyph[i] = QuadRenderer.GlyphFor(m);
        _fg[i] = fg; _bg[i] = bg;
    }

    /// <summary>The reduction of a block with three or more colours, as <see cref="QuadRenderer.Block"/> does it but on packed
    /// colours: the two farthest-apart pixels seed two groups, the rest join the nearer seed, each group is averaged.</summary>
    internal static int SplitMask(uint[] px, out uint fg, out uint bg)
    {
        int si = 0, sj = 1, best = -1;
        for (int i = 0; i < 4; i++)
            for (int j = i + 1; j < 4; j++)
            {
                int dd = Dist(px[i], px[j]);
                if (dd > best) { best = dd; si = i; sj = j; }
            }
        int mask = 0;
        for (int i = 0; i < 4; i++)
            if (Dist(px[i], px[si]) <= Dist(px[i], px[sj])) mask |= 1 << i;
        fg = Average(px, mask); bg = Average(px, ~mask & 15);
        return mask;
    }

    private static int Dist(uint p, uint q)
    {
        int dr = (int)(p >> 16 & 255) - (int)(q >> 16 & 255), dg = (int)(p >> 8 & 255) - (int)(q >> 8 & 255), db = (int)(p & 255) - (int)(q & 255);
        return dr * dr + dg * dg + db * db;
    }

    private static uint Average(uint[] px, int mask)
    {
        int n = 0, r = 0, g = 0, b = 0;
        for (int i = 0; i < 4; i++)
            if ((mask >> i & 1) != 0) { uint c = px[i]; n++; r += (int)(c >> 16 & 255); g += (int)(c >> 8 & 255); b += (int)(c & 255); }
        return n == 0 ? 0 : (uint)((r / n) << 16 | (g / n) << 8 | (b / n));
    }
}

/// <summary>
/// Emits only the cells that changed since the last frame, as cursor moves plus colour changes. Changed cells that touch are sent
/// as one run (a longer gap measured larger: a cursor move costs less than re-sending the cells in between). <see cref="Invalidate"/> forces a full redraw (first frame, resize).
/// Bytes are what xterm.js has to parse and the interpreter has to build, so the stream is kept short without changing the
/// picture: a space sends no foreground and a full block no background (neither shows), a quadrant glyph is flipped to its
/// complement with the colours swapped when that saves a colour code, both colours go in one SGR, and a move within the row
/// is a relative one.
/// </summary>
public sealed class DiffEncoder
{
    private char[] _ch = [];
    private uint[] _fg = [], _bg = [];
    private int _cols, _rows;
    private bool _full = true;
    private readonly StringBuilder _sb = new(1 << 16);
    private const int GapMerge = 1;
    private static readonly string[] Num = Enumerable.Range(0, 256).Select(i => i.ToString()).ToArray();
    // quadrant glyph -> mask, so a glyph can be flipped to its complement (mask ^ 15); -1 for anything else
    private static readonly sbyte[] QuadMask = BuildQuadMask();
    private static readonly char[] QuadGlyph = Enumerable.Range(0, 16).Select(TUImation.QuadRenderer.GlyphFor).ToArray();

    public int LastChanged { get; private set; }

    public void Invalidate() => _full = true;

    private static sbyte[] BuildQuadMask()
    {
        var m = new sbyte[0x10000];
        Array.Fill(m, (sbyte)-1);
        for (int i = 0; i < 16; i++) m[TUImation.QuadRenderer.GlyphFor(i)] = (sbyte)i;
        return m;
    }

    public string Encode(Screen s)
    {
        _sb.Clear();
        bool full = _full || s.Cols != _cols || s.Rows != _rows;
        if (full)
        {
            _cols = s.Cols; _rows = s.Rows;
            _ch = new char[s.Ch.Length]; _fg = new uint[s.Ch.Length]; _bg = new uint[s.Ch.Length];
            _sb.Append("\x1b[0m\x1b[2J");
            _full = false;
        }
        uint lastFg = uint.MaxValue, lastBg = uint.MaxValue;
        int changed = 0, curR = -1, curC = -1;
        int cols = s.Cols;
        var sch = s.Ch; var sfg = s.Fg; var sbg = s.Bg;
        for (int r = 0; r < s.Rows; r++)
        {
            int c = 0;
            while (c < cols)
            {
                int i = r * cols + c;
                if (!full && Same(sch[i], sfg[i], sbg[i], i)) { c++; continue; }
                // a run: extend over short unchanged gaps
                int start = c, end = c, gap = 0;
                for (int k = c; k < cols; k++)
                {
                    int ik = r * cols + k;
                    if (full || !Same(sch[ik], sfg[ik], sbg[ik], ik)) { end = k; gap = 0; }
                    else if (++gap > GapMerge) break;
                }
                if (curR == r && curC < start) { _sb.Append("\x1b["); AppendInt(start - curC); _sb.Append('C'); }
                else if (curR != r || curC != start) { _sb.Append("\x1b["); AppendInt(r + 1); _sb.Append(';'); AppendInt(start + 1); _sb.Append('H'); }
                for (int k = start; k <= end; k++)
                {
                    int ik = r * cols + k;
                    char ch = sch[ik]; uint fg = sfg[ik], bg = sbg[ik];
                    char glyph = ch;
                    bool needFg = true, needBg = true;
                    if (ch == ' ') needFg = false;
                    else if (ch == '█') needBg = false;
                    else if (QuadMask[ch] is var m and >= 0)
                    {
                        int asIs = (fg != lastFg ? 1 : 0) + (bg != lastBg ? 1 : 0);
                        int flipped = (bg != lastFg ? 1 : 0) + (fg != lastBg ? 1 : 0);
                        if (flipped < asIs) { glyph = QuadGlyph[m ^ 15]; (fg, bg) = (bg, fg); }
                    }
                    needFg = needFg && fg != lastFg; needBg = needBg && bg != lastBg;
                    if (needFg || needBg)
                    {
                        _sb.Append("\x1b[");
                        if (needFg) { _sb.Append("38;2;"); Rgb(fg); lastFg = fg; }
                        if (needFg && needBg) _sb.Append(';');
                        if (needBg) { _sb.Append("48;2;"); Rgb(bg); lastBg = bg; }
                        _sb.Append('m');
                    }
                    _sb.Append(glyph);
                    _ch[ik] = ch; _fg[ik] = sfg[ik]; _bg[ik] = sbg[ik];
                    changed++;
                }
                curR = r; curC = end + 1;
                c = end + 1;
            }
        }
        LastChanged = changed;
        if (_sb.Length > 0) _sb.Append("\x1b[0m");
        return _sb.ToString();
    }

    // a glyph on a space-like cell hides its foreground (and a full block its background), so those changes are no change
    private bool Same(char ch, uint fg, uint bg, int i) =>
        _ch[i] == ch && (_bg[i] == bg || ch == '█') && (_fg[i] == fg || ch == ' ');

    private void Rgb(uint c) =>
        _sb.Append(Num[c >> 16 & 255]).Append(';').Append(Num[c >> 8 & 255]).Append(';').Append(Num[c & 255]);

    private void AppendInt(int n)
    {
        if (n < 256) { _sb.Append(Num[n]); return; }
        _sb.Append(n.ToString());
    }
}
