namespace FeedRaider.Engine.Raider;

/// <summary>
/// Folds the truecolour stream onto a small palette it learns as it goes. A colour within <see cref="Tolerance"/> of one
/// already in the palette (on every channel) becomes that one, and anything further off joins the palette as itself, so
/// the first colours seen, which are the theme's own, stay exact. Once the palette is full every colour takes its nearest.
/// <para>
/// A terminal renderer with a glyph atlas (xterm's WebGL addon, Windows Terminal) rasterises each (glyph, foreground,
/// background) once. The field's quadrant cells pair whatever colours sit next to each other, and the blends, fades
/// and averages mint new ones every frame, so unsnapped the set of keys never stops growing and a phone at DPR 3 spends
/// its frames rasterising glyphs and filling atlas pages. Snapped, the keys are pairs of palette colours and level off.
/// </para>
/// </summary>
public sealed class ColourSnap
{
    public const int DefaultTolerance = 20;
    public const int DefaultCapacity = 384;

    /// <summary>The 24 colour bits of a pixel: every bit <see cref="Surface.None"/> leaves clear, as no real colour sets its top byte.</summary>
    private const uint RgbMask = ~Surface.None;
    /// <summary>A memo key no colour can be, its top byte set, which marks a slot as holding nothing.</summary>
    private const uint NoKey = uint.MaxValue;
    /// <summary>Knuth's multiplicative hash constant, 2^32 divided by the golden ratio, which spreads nearby colours across the memo.</summary>
    private const uint GoldenRatioHash = 2654435761u;
    private const int SlotBits = 13, Slots = 1 << SlotBits;
    /// <summary>The bits of the <c>uint</c> the hash is taken in; its top <see cref="SlotBits"/> pick the slot.</summary>
    private const int HashBits = 32;
    // A direct-mapped memo of colour -> the colour it is sent as: one hash and one compare per lookup on the hot path.
    private readonly uint[] _memoColour = new uint[Slots];
    private readonly uint[] _memoSent = new uint[Slots];
    private readonly List<uint> _palette = [];
    // The palette bucketed by cube cell, so a search looks at the cells within reach, not the whole palette. A cell is
    // 1 << CellShift levels wide, and a tolerance under that keeps every match in reach.
    private const int CellShift = 5, CellAxisBits = 3, CellsPerAxis = 1 << CellAxisBits;
    private readonly List<uint>[] _byCell = new List<uint>[CellsPerAxis * CellsPerAxis * CellsPerAxis];
    private readonly Dictionary<uint, uint[]> _divertedTo = [];
    private readonly HashSet<uint> _exempt = [];

    public ColourSnap(int tolerance = DefaultTolerance, int capacity = DefaultCapacity)
    {
        Tolerance = Math.Clamp(tolerance, 0, (1 << CellShift) - 1);
        Capacity = Math.Max(1, capacity);
        Array.Fill(_memoColour, NoKey);
    }

    public int Tolerance { get; }
    public int Capacity { get; }
    public int Count => _palette.Count;

    public uint this[uint pixel]
    {
        get
        {
            uint colour = pixel & RgbMask;
            int slot = (int)((colour * GoldenRatioHash) >> (HashBits - SlotBits));
            if (_memoColour[slot] == colour) return _memoSent[slot];
            uint sent = _exempt.Contains(colour) ? colour : Diverted(FindOrAdd(colour), colour);
            _memoColour[slot] = colour;
            _memoSent[slot] = sent;
            return sent;
        }
    }

    /// <summary>Puts each colour into the palette as itself, even where one already there is within the tolerance, for colours that
    /// must come through exact and lie close to one another (the three greys of a card's text). A colour the palette holds is left
    /// alone, and so is whatever does not fit once it is full. Nearest-wins then returns each of them unchanged.</summary>
    public void Prime(IEnumerable<uint> colours)
    {
        foreach (uint pixel in colours)
        {
            uint colour = pixel & RgbMask;
            if (_palette.Count >= Capacity) break;
            if (!_palette.Contains(colour)) AddToPalette(colour);
        }
        Array.Fill(_memoColour, NoKey);   // colours looked up before may have folded into a neighbour
    }

    /// <summary>Sends a colour that would fold onto <paramref name="from"/> as whichever of <paramref name="to"/> lies nearer it, if
    /// one does. The colours in <paramref name="to"/> never join the palette, so every other colour folds exactly as it would
    /// without them: for colours that lie within the tolerance of a primed one but must not take it, as the sky's fade lies beside
    /// a card's slab and edge.</summary>
    public void Divert(uint from, IEnumerable<uint> to)
    {
        _divertedTo[from & RgbMask] = [.. to.Select(target => target & RgbMask)];
        Array.Fill(_memoColour, NoKey);   // colours looked up before may now go elsewhere
    }

    /// <summary>Sends each of <paramref name="colours"/> as itself, and no other colour as any of them. None joins the palette, so
    /// every other colour folds exactly as it would without them: for flat colours drawn only as they are that lie within the
    /// tolerance of a primed one, as the grounds of the HUD and the feed log lie beside a card's slab and the sky's fade.</summary>
    public void Exempt(IEnumerable<uint> colours)
    {
        foreach (uint pixel in colours) _exempt.Add(pixel & RgbMask);
        Array.Fill(_memoColour, NoKey);   // colours looked up before may have folded onto another
    }

    /// <summary><paramref name="folded"/>, or the colour it is diverted to that lies nearest <paramref name="colour"/> when one lies
    /// nearer than <paramref name="folded"/> does.</summary>
    private uint Diverted(uint folded, uint colour)
    {
        if (!_divertedTo.TryGetValue(folded, out var targets)) return folded;
        var (r, g, b) = Surface.Unpack(colour);
        uint nearest = folded;
        int nearestDistance = Distance(folded, r, g, b);
        foreach (uint target in targets)
        {
            int distance = Distance(target, r, g, b);
            if (distance < nearestDistance) { nearestDistance = distance; nearest = target; }
        }
        return nearest;
    }

    /// <summary>The palette colour for <paramref name="colour"/>; a colour no entry is near joins the palette while there is room.</summary>
    private uint FindOrAdd(uint colour)
    {
        var (r, g, b) = Surface.Unpack(colour);
        int nearestDistance = int.MaxValue;
        uint nearest = colour;
        // with the tolerance under a cell's width, any match lies in this cell or a neighbour
        for (int stepR = -1; stepR <= 1; stepR++)
            for (int stepG = -1; stepG <= 1; stepG++)
                for (int stepB = -1; stepB <= 1; stepB++)
                {
                    int cellR = (r >> CellShift) + stepR, cellG = (g >> CellShift) + stepG, cellB = (b >> CellShift) + stepB;
                    if ((uint)cellR >= CellsPerAxis || (uint)cellG >= CellsPerAxis || (uint)cellB >= CellsPerAxis) continue;
                    if (_byCell[CellIndex(cellR, cellG, cellB)] is not { } entries) continue;
                    foreach (uint entry in entries)
                    {
                        int distance = Distance(entry, r, g, b);
                        if (distance < nearestDistance) { nearestDistance = distance; nearest = entry; }
                    }
                }
        if (nearestDistance <= Tolerance) return nearest;
        if (_palette.Count < Capacity)
        {
            AddToPalette(colour);
            return colour;
        }
        // full: the nearest anywhere, even past the tolerance
        if (nearestDistance == int.MaxValue)
            foreach (uint entry in _palette)
            {
                int distance = Distance(entry, r, g, b);
                if (distance < nearestDistance) { nearestDistance = distance; nearest = entry; }
            }
        return nearest;
    }

    private void AddToPalette(uint colour)
    {
        var (r, g, b) = Surface.Unpack(colour);
        _palette.Add(colour);
        (_byCell[CellIndex(r >> CellShift, g >> CellShift, b >> CellShift)] ??= []).Add(colour);
    }

    private static int CellIndex(int cellR, int cellG, int cellB) => cellR << (2 * CellAxisBits) | cellG << CellAxisBits | cellB;

    /// <summary>How far <paramref name="candidate"/> lies from the colour (r, g, b): its largest difference on any channel.</summary>
    internal static int Distance(uint candidate, int r, int g, int b)
    {
        var (candidateR, candidateG, candidateB) = Surface.Unpack(candidate);
        return Math.Max(Math.Abs(candidateR - r), Math.Max(Math.Abs(candidateG - g), Math.Abs(candidateB - b)));
    }
}
