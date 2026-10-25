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
    private const int Slots = 1 << 13;
    private readonly uint[] _key = new uint[Slots];
    private readonly uint[] _val = new uint[Slots];
    private readonly List<uint> _pal = [];
    // the palette bucketed by 32-level cube cell, so a search looks at the cells within reach, not the whole palette
    private readonly List<uint>[] _cell = new List<uint>[512];

    public ColourSnap(int tolerance = DefaultTolerance, int capacity = DefaultCapacity)
    {
        Tolerance = Math.Clamp(tolerance, 0, 31);
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
            int slot = (int)((c * 2654435761u) >> 19);
            if (_key[slot] == c) return _val[slot];
            uint v = Find(c);
            _key[slot] = c;
            _val[slot] = v;
            return v;
        }
    }

    private uint Find(uint c)
    {
        int r = (int)(c >> 16 & 255), g = (int)(c >> 8 & 255), b = (int)(c & 255);
        int best = int.MaxValue;
        uint pick = c;
        // with the tolerance under a cell's width (32), any match lies in this cell or a neighbour
        for (int dr = -1; dr <= 1; dr++)
            for (int dg = -1; dg <= 1; dg++)
                for (int db = -1; db <= 1; db++)
                {
                    int cr = (r >> 5) + dr, cg = (g >> 5) + dg, cb = (b >> 5) + db;
                    if ((uint)cr > 7 || (uint)cg > 7 || (uint)cb > 7) continue;
                    if (_cell[cr << 6 | cg << 3 | cb] is not { } list) continue;
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
            (_cell[(r >> 5) << 6 | (g >> 5) << 3 | (b >> 5)] ??= []).Add(c);
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

    private static int Distance(uint p, int r, int g, int b) =>
        Math.Max(Math.Abs((int)(p >> 16 & 255) - r), Math.Max(Math.Abs((int)(p >> 8 & 255) - g), Math.Abs((int)(p & 255) - b)));
}
