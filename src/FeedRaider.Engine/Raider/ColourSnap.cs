namespace FeedRaider.Engine.Raider;

/// <summary>
/// Folds the truecolour stream onto a small palette it learns as it goes. A colour within <see cref="Tolerance"/> of one
/// already in the palette (on every channel) becomes that one, and anything further off joins the palette as itself, so
/// the first colours seen, which are the theme's own, stay exact. Once the palette is full every colour takes its nearest.
/// <para>
/// A terminal renderer with a glyph atlas (xterm's WebGL addon, Windows Terminal) rasterises each (glyph, foreground,
/// background) once. The playfield's quadrant cells pair whatever colours sit next to each other, and the blends, fades
/// and averages mint new ones every frame, so unsnapped the set of keys never stops growing and a phone at DPR 3 spends
/// its frames rasterising glyphs and filling atlas pages. Snapped, the keys are pairs of palette colours and level off.
/// </para>
/// </summary>
public sealed class ColourSnap
{
    public const int DefaultTolerance = 20;
    public const int DefaultCapacity = 384;

    // A direct-mapped memo of colour -> palette colour: one hash and one compare per lookup on the hot path.
    private const int SlotBits = 13, Slots = 1 << SlotBits;
    private readonly uint[] _key = new uint[Slots];
    private readonly uint[] _val = new uint[Slots];
    private readonly List<uint> _pal = [];
    // The palette bucketed by 32-level cube cell (8 cells per colour axis), so a search looks at the cells within reach,
    // not the whole palette. A cell is 1 << CellShift levels wide, and a tolerance under that keeps every match in reach.
    private const int CellShift = 5, CellAxisBits = 3, CellsPerAxis = 1 << CellAxisBits;
    private readonly List<uint>[] _cell = new List<uint>[CellsPerAxis * CellsPerAxis * CellsPerAxis];

    public ColourSnap(int tolerance = DefaultTolerance, int capacity = DefaultCapacity)
    {
        Tolerance = Math.Clamp(tolerance, 0, (1 << CellShift) - 1);
        Capacity = Math.Max(1, capacity);
        Array.Fill(_key, uint.MaxValue);
    }

    public int Tolerance { get; }
    public int Capacity { get; }
    public int Count => _pal.Count;

    public uint this[uint c]
    {
        get
        {
            c &= 0xFFFFFF;
            int slot = (int)((c * 2654435761u) >> (32 - SlotBits));
            if (_key[slot] == c) return _val[slot];
            uint v = FindOrAdd(c);
            _key[slot] = c;
            _val[slot] = v;
            return v;
        }
    }

    /// <summary>The palette colour for <paramref name="c"/>; a colour no entry is near joins the palette while there is room.</summary>
    private uint FindOrAdd(uint c)
    {
        int r = (int)(c >> 16 & 255), g = (int)(c >> 8 & 255), b = (int)(c & 255);
        int best = int.MaxValue;
        uint pick = c;
        // with the tolerance under a cell's width, any match lies in this cell or a neighbour
        for (int dr = -1; dr <= 1; dr++)
            for (int dg = -1; dg <= 1; dg++)
                for (int db = -1; db <= 1; db++)
                {
                    int cr = (r >> CellShift) + dr, cg = (g >> CellShift) + dg, cb = (b >> CellShift) + db;
                    if ((uint)cr >= CellsPerAxis || (uint)cg >= CellsPerAxis || (uint)cb >= CellsPerAxis) continue;
                    if (_cell[CellIndex(cr, cg, cb)] is not { } list) continue;
                    foreach (var p in list)
                    {
                        int d = Distance(p, r, g, b);
                        if (d < best) { best = d; pick = p; }
                    }
                }
        if (best <= Tolerance) return pick;
        if (_pal.Count < Capacity)
        {
            _pal.Add(c);
            (_cell[CellIndex(r >> CellShift, g >> CellShift, b >> CellShift)] ??= []).Add(c);
            return c;
        }
        // full: the nearest anywhere, even past the tolerance
        if (best == int.MaxValue)
            foreach (var p in _pal)
            {
                int d = Distance(p, r, g, b);
                if (d < best) { best = d; pick = p; }
            }
        return pick;
    }

    private static int CellIndex(int cr, int cg, int cb) => cr << (2 * CellAxisBits) | cg << CellAxisBits | cb;

    private static int Distance(uint p, int r, int g, int b) =>
        Math.Max(Math.Abs((int)(p >> 16 & 255) - r), Math.Max(Math.Abs((int)(p >> 8 & 255) - g), Math.Abs((int)(p & 255) - b)));
}
