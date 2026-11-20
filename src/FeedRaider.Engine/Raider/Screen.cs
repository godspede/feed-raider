using System.Text;
using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>The terminal as a grid of cells: a glyph, a foreground and a background (0xRRGGBB).</summary>
public sealed class Screen
{
    /// <summary>The lowest code point that can be a wide character; everything below is one cell.</summary>
    internal const int FirstWideCodePoint = 0x300;
    /// <summary>The four quadrants of a cell, one bit each.</summary>
    internal const int AllQuadrants = 15;
    /// <summary>The cells a wide char takes.</summary>
    internal const int WideCells = 2;

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
        Array.Fill(Fg, Palette.White);
        Array.Fill(Bg, bg);
    }

    /// <summary>The second cell of a wide char: the glyph is in the cell before it, and this cell sends nothing of its own.</summary>
    public const char Continue = '\0';

    /// <summary>Puts a glyph. A null background keeps the cell's own (so text sits on the picture beneath it). A char with no cell of
    /// its own (a mark, a control) is dropped, never swapped for a question mark; a wide char takes this cell and the next, which gets
    /// the first one's background (a terminal paints both from the first), and one that would straddle the right edge is not drawn.
    /// Drawing over half of a wide char blanks the other half.</summary>
    public void Put(int col, int row, char ch, uint fg, uint? bg = null)
    {
        if ((uint)col >= (uint)Cols || (uint)row >= (uint)Rows) return;
        int cell = row * Cols + col;
        int width = Cells(ch);
        if (width == 0) return;
        if (width == WideCells && col + 1 >= Cols) return;
        Unpair(cell, col);
        if (width == WideCells) Unpair(cell + 1, col + 1);
        Ch[cell] = ch; Fg[cell] = fg;
        if (bg is { } ground) Bg[cell] = ground;
        if (width == WideCells) { Ch[cell + 1] = Continue; Fg[cell + 1] = fg; Bg[cell + 1] = Bg[cell]; }
    }

    /// <summary>Sets a cell to a picture glyph (a quadrant) and both colours, as <see cref="QuadComposer"/> does, keeping a wide char
    /// beside it whole: if the cell was half of one, the other half becomes a space.</summary>
    public void Set(int col, int row, char glyph, uint fg, uint bg)
    {
        if ((uint)col >= (uint)Cols || (uint)row >= (uint)Rows) return;
        int cell = row * Cols + col;
        Unpair(cell, col);
        Ch[cell] = glyph; Fg[cell] = fg; Bg[cell] = bg;
    }

    // the cells a char takes: half of a surrogate pair (a char outside the Basic Multilingual Plane) cannot be drawn, so it takes none
    private static int Cells(char ch) => char.IsSurrogate(ch) ? 0 : CellWidth.Of(ch);

    // a cell about to be overwritten that is half of a wide char: the other half becomes a space
    private void Unpair(int cell, int col)
    {
        char old = Ch[cell];
        if (old == Continue) { if (col > 0 && CellWidth.Of(Ch[cell - 1]) == WideCells) Ch[cell - 1] = ' '; }
        else if (old >= FirstWideCodePoint && col + 1 < Cols && CellWidth.Of(old) == WideCells && Ch[cell + 1] == Continue) Ch[cell + 1] = ' ';
    }

    /// <summary>Writes a string from cell (col, row): a wide char takes two cells, a char with no cell of its own takes none.</summary>
    public void Text(int col, int row, string text, uint fg, uint? bg = null)
    {
        foreach (char ch in text)
        {
            Put(col, row, ch, fg, bg);
            col += Cells(ch);
        }
    }

    /// <summary>Paints a run of <paramref name="width"/> spaces on a background: the allocation-free form of
    /// <c>Text(col, row, new string(' ', width), ...)</c>.</summary>
    public void Blank(int col, int row, int width, uint fg, uint bg)
    {
        for (int offset = 0; offset < width; offset++) Put(col + offset, row, ' ', fg, bg);
    }

    public char CharAt(int col, int row) => Ch[row * Cols + col];
    public uint BgAt(int col, int row) => Bg[row * Cols + col];
}

/// <summary>
/// Turns a pixel <see cref="Surface"/> into quadrant-glyph cells (2x2 pixels per cell) through the vendored
/// <see cref="QuadRenderer"/>. Only cells whose four pixels changed since the last frame are re-reduced; the reduction
/// is the expensive part under the browser's interpreter, and a scrolling dim sky leaves most cells alone.
/// </summary>
public sealed class QuadComposer
{
    private uint[] _prev = [];
    private char[] _glyph = [];
    private uint[] _fg = [], _bg = [];
    /// <summary>The pixels in one cell: <see cref="CardField.PixelsPerCell"/> each way.</summary>
    private const int PixelsInCell = CardField.PixelsPerCell * CardField.PixelsPerCell;
    /// <summary>Where each pixel of a cell sits among its four, and which bit of a quadrant mask it takes.</summary>
    private const int TopLeft = 0, TopRight = 1, BottomLeft = 2, BottomRight = 3;
    private readonly uint[] _quadrants = new uint[PixelsInCell];
    public int Reduced { get; private set; }

    /// <summary>Writes the surface into <paramref name="screen"/> at cell (col0, row0), reading it shifted by (shiftX, shiftY)
    /// pixels (the screen shake). Edge pixels the shift uncovers repeat the nearest row or column.</summary>
    public void Compose(Surface surface, int shiftX, int shiftY, Screen screen, int col0, int row0)
    {
        int cols = surface.W / CardField.PixelsPerCell, rows = surface.H / CardField.PixelsPerCell;
        if (_prev.Length != cols * rows * PixelsInCell)
        {
            _prev = new uint[cols * rows * PixelsInCell];
            Array.Fill(_prev, Surface.None);   // no pixel equals it, so the first frame reduces every cell
            _glyph = new char[cols * rows];
            _fg = new uint[cols * rows];
            _bg = new uint[cols * rows];
        }
        Reduced = 0;
        var pixels = surface.P;
        var prev = _prev;
        int width = surface.W, height = surface.H;
        bool still = shiftX == 0 && shiftY == 0;
        // the cells that land on the screen: the rest are still reduced (they stay cached) but not copied
        int firstShownCol = Math.Max(0, -col0), endShownCol = Math.Min(cols, screen.Cols - col0);
        for (int cellRow = 0; cellRow < rows; cellRow++)
        {
            int screenRow = row0 + cellRow;
            bool onScreen = (uint)screenRow < (uint)screen.Rows;
            int rowStart = screenRow * screen.Cols + col0;
            int cell = cellRow * cols;
            int topRow, bottomRow;
            if (still) { topRow = cellRow * CardField.PixelsPerCell * width; bottomRow = topRow + width; }
            else
            {
                topRow = Math.Clamp(cellRow * CardField.PixelsPerCell - shiftY, 0, height - 1) * width;
                bottomRow = Math.Clamp(cellRow * CardField.PixelsPerCell + 1 - shiftY, 0, height - 1) * width;
            }
            for (int cellCol = 0; cellCol < cols; cellCol++, cell++)
            {
                int leftX, rightX;
                if (still) { leftX = cellCol * CardField.PixelsPerCell; rightX = leftX + 1; }
                else { leftX = Math.Clamp(cellCol * CardField.PixelsPerCell - shiftX, 0, width - 1); rightX = Math.Clamp(cellCol * CardField.PixelsPerCell + 1 - shiftX, 0, width - 1); }
                uint topLeft = pixels[topRow + leftX], topRight = pixels[topRow + rightX], bottomLeft = pixels[bottomRow + leftX], bottomRight = pixels[bottomRow + rightX];
                int cached = cell * PixelsInCell;
                if (prev[cached + TopLeft] != topLeft || prev[cached + TopRight] != topRight || prev[cached + BottomLeft] != bottomLeft || prev[cached + BottomRight] != bottomRight)
                {
                    prev[cached + TopLeft] = topLeft; prev[cached + TopRight] = topRight; prev[cached + BottomLeft] = bottomLeft; prev[cached + BottomRight] = bottomRight;
                    Reduce(cell, topLeft, topRight, bottomLeft, bottomRight);
                    Reduced++;
                }
                if (onScreen && cellCol >= firstShownCol && cellCol < endShownCol)
                {
                    int screenCell = rowStart + cellCol;
                    screen.Ch[screenCell] = _glyph[cell]; screen.Fg[screenCell] = _fg[cell]; screen.Bg[screenCell] = _bg[cell];
                }
            }
        }
    }

    private void Reduce(int cell, uint topLeft, uint topRight, uint bottomLeft, uint bottomRight)
    {
        // at most two distinct colours is the common case and exact: skip the general reduction
        uint other = topLeft;
        bool hasOther = false, moreThanTwo = false;
        if (topRight != topLeft) { other = topRight; hasOther = true; }
        if (bottomLeft != topLeft) { if (!hasOther) { other = bottomLeft; hasOther = true; } else if (bottomLeft != other) moreThanTwo = true; }
        if (bottomRight != topLeft) { if (!hasOther) { other = bottomRight; hasOther = true; } else if (bottomRight != other) moreThanTwo = true; }
        if (!moreThanTwo)
        {
            if (!hasOther) { _glyph[cell] = ' '; _fg[cell] = topLeft; _bg[cell] = topLeft; return; }
            int mask = 1 << TopLeft | (topRight == topLeft ? 1 << TopRight : 0) | (bottomLeft == topLeft ? 1 << BottomLeft : 0) | (bottomRight == topLeft ? 1 << BottomRight : 0);
            _glyph[cell] = QuadRenderer.GlyphFor(mask); _fg[cell] = topLeft; _bg[cell] = other;
            return;
        }
        var quadrants = _quadrants;
        quadrants[TopLeft] = topLeft; quadrants[TopRight] = topRight; quadrants[BottomLeft] = bottomLeft; quadrants[BottomRight] = bottomRight;
        _glyph[cell] = QuadRenderer.GlyphFor(SplitMask(quadrants, out uint fg, out uint bg));
        _fg[cell] = fg; _bg[cell] = bg;
    }

    /// <summary>The reduction of a block with three or more colours, as <see cref="QuadRenderer.Block"/> does it but on packed
    /// colours: the two farthest-apart pixels seed two groups, the rest join the nearer seed, each group is averaged.</summary>
    internal static int SplitMask(uint[] quadrants, out uint fg, out uint bg)
    {
        int seedA = 0, seedB = 1, farthest = -1;
        for (int first = 0; first < quadrants.Length; first++)
            for (int second = first + 1; second < quadrants.Length; second++)
            {
                int apart = DistanceSquared(quadrants[first], quadrants[second]);
                if (apart > farthest) { farthest = apart; seedA = first; seedB = second; }
            }
        int mask = 0;
        for (int quadrant = 0; quadrant < quadrants.Length; quadrant++)
            if (DistanceSquared(quadrants[quadrant], quadrants[seedA]) <= DistanceSquared(quadrants[quadrant], quadrants[seedB])) mask |= 1 << quadrant;
        fg = Average(quadrants, mask); bg = Average(quadrants, ~mask & Screen.AllQuadrants);
        return mask;
    }

    private static int DistanceSquared(uint first, uint second)
    {
        var (firstR, firstG, firstB) = Surface.Unpack(first);
        var (secondR, secondG, secondB) = Surface.Unpack(second);
        int diffR = firstR - secondR, diffG = firstG - secondG, diffB = firstB - secondB;
        return diffR * diffR + diffG * diffG + diffB * diffB;
    }

    private static uint Average(uint[] quadrants, int mask)
    {
        int count = 0, sumR = 0, sumG = 0, sumB = 0;
        for (int quadrant = 0; quadrant < quadrants.Length; quadrant++)
            if ((mask >> quadrant & 1) != 0) { var (r, g, b) = Surface.Unpack(quadrants[quadrant]); count++; sumR += r; sumG += g; sumB += b; }
        // a block of one colour leaves the other group empty, and that side of the glyph never shows
        return count == 0 ? 0 : Surface.Pack(sumR / count, sumG / count, sumB / count);
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
    /// <summary>The output builder's starting capacity in chars. It is only where the builder starts: a frame that needs
    /// more grows it.</summary>
    private const int StartCapacity = 1 << 16;
    /// <summary>A cell that is all foreground: its background never shows.</summary>
    private const char FullBlock = '█';
    /// <summary>ESC and '[', which open every sequence the encoder writes, and the sequence that sets the colours back to the terminal's own.</summary>
    private const string Csi = "\x1b[", ResetColours = Csi + "0m";
    private readonly StringBuilder _out = new(StartCapacity);
    private const int GapMerge = 1;
    private static readonly string[] DecimalText = Enumerable.Range(0, byte.MaxValue + 1).Select(value => value.ToString()).ToArray();
    // quadrant glyph -> mask, so a glyph can be flipped to its complement (mask ^ Screen.AllQuadrants); -1 for anything else
    private static readonly sbyte[] QuadMask = BuildQuadMask();
    private static readonly char[] QuadGlyph = Enumerable.Range(0, Screen.AllQuadrants + 1).Select(QuadRenderer.GlyphFor).ToArray();

    public int LastChanged { get; private set; }

    /// <summary>When set, every colour sent goes through it first (see <see cref="ColourSnap"/>). Change detection still
    /// compares the colours the screen holds.</summary>
    public ColourSnap? Snap { get; init; }

    /// <summary>The encoder the game sends with: colours snapped to a <see cref="ColourSnap"/> with its defaults.</summary>
    public static DiffEncoder Snapped()
    {
        var snap = new ColourSnap();
        // the card text greys are within the tolerance of one another and would fold into one
        snap.Prime(CardColours.All);
        // the sky's fade passes within the tolerance of a card's slab and edge and would fold into them
        foreach (uint card in CardColours.All) snap.Divert(card, Background.SkySteps);
        // the grounds of the HUD and the feed log lie within the tolerance of the slab too, and nearer parts of the fade than any
        // step does: exempt, not offered as targets, so they keep their colour and the sky never takes theirs
        snap.Exempt([RaiderView.HudBg, RaiderView.PanelBg]);
        return new DiffEncoder { Snap = snap };
    }

    public void Invalidate() => _full = true;

    private static sbyte[] BuildQuadMask()
    {
        var masks = new sbyte[char.MaxValue + 1];
        Array.Fill(masks, (sbyte)-1);
        for (int mask = 0; mask <= Screen.AllQuadrants; mask++) masks[QuadRenderer.GlyphFor(mask)] = (sbyte)mask;
        return masks;
    }

    public string Encode(Screen screen)
    {
        _out.Clear();
        bool full = _full || screen.Cols != _cols || screen.Rows != _rows;
        if (full)
        {
            _cols = screen.Cols; _rows = screen.Rows;
            _ch = new char[screen.Ch.Length]; _fg = new uint[screen.Ch.Length]; _bg = new uint[screen.Ch.Length];
            _out.Append(ResetColours).Append(Csi).Append("2J");
            _full = false;
        }
        uint lastFg = uint.MaxValue, lastBg = uint.MaxValue;
        int changed = 0, cursorRow = -1, cursorCol = -1;
        int cols = screen.Cols;
        var chars = screen.Ch; var fgs = screen.Fg; var bgs = screen.Bg;
        for (int row = 0; row < screen.Rows; row++)
        {
            int col = 0;
            while (col < cols)
            {
                if (!full && Unchanged(screen, row * cols + col, col)) { col++; continue; }
                int start = col, end = col, gap = 0;
                for (int ahead = col; ahead < cols; ahead++)
                {
                    if (full || !Unchanged(screen, row * cols + ahead, ahead)) { end = ahead; gap = 0; }
                    else if (++gap > GapMerge) break;
                }
                if (cursorRow == row && cursorCol < start) { _out.Append(Csi); AppendInt(start - cursorCol); _out.Append('C'); }
                else if (cursorRow != row || cursorCol != start) { _out.Append(Csi); AppendInt(row + 1); _out.Append(';'); AppendInt(start + 1); _out.Append('H'); }
                int next = start;
                for (int runCol = start; runCol <= end; runCol = next)
                {
                    int cell = row * cols + runCol;
                    char ch = chars[cell]; uint fg = fgs[cell], bg = bgs[cell];
                    if (ch >= Screen.FirstWideCodePoint || ch == Screen.Continue) ResolveWide(screen, cell, runCol, ref ch, ref fg, ref bg);
                    next = runCol + 1;
                    if (ch == Screen.Continue) continue;      // the second half of a wide char sent with the first
                    if (ch >= Screen.FirstWideCodePoint && CellWidth.Of(ch) == Screen.WideCells) next = runCol + Screen.WideCells;
                    fg = SentColour(fg); bg = SentColour(bg);
                    uint sentFg = fg, sentBg = bg;
                    char glyph = ch;
                    bool needFg = true, needBg = true;
                    if (ch == ' ') needFg = false;
                    else if (ch == FullBlock) needBg = false;
                    else if (QuadMask[ch] is var mask and >= 0)
                    {
                        if (Snap is not null)
                        {
                            // one orientation per colour pair, and a cell of one colour is a space, so the atlas holds each look once
                            if (fg == bg) { glyph = ' '; needFg = false; }
                            else if (fg < bg) { glyph = QuadGlyph[mask ^ Screen.AllQuadrants]; (fg, bg) = (bg, fg); }
                        }
                        else
                        {
                            int asIs = (fg != lastFg ? 1 : 0) + (bg != lastBg ? 1 : 0);
                            int flipped = (bg != lastFg ? 1 : 0) + (fg != lastBg ? 1 : 0);
                            if (flipped < asIs) { glyph = QuadGlyph[mask ^ Screen.AllQuadrants]; (fg, bg) = (bg, fg); }
                        }
                    }
                    needFg = needFg && fg != lastFg; needBg = needBg && bg != lastBg;
                    if (needFg || needBg)
                    {
                        _out.Append(Csi);
                        if (needFg) { _out.Append("38;2;"); AppendRgb(fg); lastFg = fg; }
                        if (needFg && needBg) _out.Append(';');
                        if (needBg) { _out.Append("48;2;"); AppendRgb(bg); lastBg = bg; }
                        _out.Append('m');
                    }
                    _out.Append(glyph);
                    _ch[cell] = ch; _fg[cell] = sentFg; _bg[cell] = sentBg;
                    if (next > runCol + 1) { _ch[cell + 1] = Screen.Continue; _fg[cell + 1] = sentFg; _bg[cell + 1] = sentBg; }
                    changed++;
                }
                cursorRow = row; cursorCol = next;
                col = next;
            }
        }
        LastChanged = changed;
        if (_out.Length > 0) _out.Append(ResetColours);
        return _out.ToString();
    }

    // the snapped value, so a colour that moves within the palette's reach is no change
    private uint SentColour(uint colour) => Snap is { } snap ? snap[colour] : colour;

    // What a cell really holds. A wide char with no continuation after it (or in the last column) and a continuation with no wide char
    // before it are each a space: sent as they are they would shift the rest of the row. A continuation has its wide char's colours.
    private static void ResolveWide(Screen screen, int cell, int col, ref char ch, ref uint fg, ref uint bg)
    {
        if (ch == Screen.Continue)
        {
            if (col > 0 && screen.Ch[cell - 1] >= Screen.FirstWideCodePoint && CellWidth.Of(screen.Ch[cell - 1]) == Screen.WideCells) { fg = screen.Fg[cell - 1]; bg = screen.Bg[cell - 1]; }
            else ch = ' ';
        }
        else if (CellWidth.Of(ch) == Screen.WideCells && !(col + 1 < screen.Cols && screen.Ch[cell + 1] == Screen.Continue)) ch = ' ';
    }

    private bool Unchanged(Screen screen, int cell, int col)
    {
        char ch = screen.Ch[cell]; uint fg = screen.Fg[cell], bg = screen.Bg[cell];
        if (ch >= Screen.FirstWideCodePoint || ch == Screen.Continue) ResolveWide(screen, cell, col, ref ch, ref fg, ref bg);
        return MatchesSent(ch, SentColour(fg), SentColour(bg), cell);
    }

    // a glyph on a space-like cell hides its foreground (and a full block its background), so those changes are no change
    private bool MatchesSent(char ch, uint fg, uint bg, int cell) =>
        _ch[cell] == ch && (_bg[cell] == bg || ch == FullBlock) && (_fg[cell] == fg || ch == ' ' || ch == Screen.Continue);

    private void AppendRgb(uint colour)
    {
        var (r, g, b) = Surface.Unpack(colour);
        _out.Append(DecimalText[r]).Append(';').Append(DecimalText[g]).Append(';').Append(DecimalText[b]);
    }

    private void AppendInt(int value)
    {
        if (value < DecimalText.Length) { _out.Append(DecimalText[value]); return; }
        _out.Append(value.ToString());
    }
}
