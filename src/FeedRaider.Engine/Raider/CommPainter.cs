using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>Which field cells the action (ships, enemies, bullets, pickups, effects) covers this frame: a cell is covered when
/// any of its four pixels, read with the shake offset as <see cref="QuadComposer"/> reads them, differs from the backdrop.</summary>
public readonly struct Foreground(uint[] world, uint[] backdrop, int w, int h, int ox, int oy, int col0)
{
    public bool Covers(int col, int row)
    {
        if (world is null) return false;
        int cx = col - col0;
        if (cx < 0 || cx * 2 >= w || row < 0 || row * 2 >= h) return false;
        int x0 = Math.Clamp(cx * 2 - ox, 0, w - 1), x1 = Math.Clamp(cx * 2 + 1 - ox, 0, w - 1);
        int y0 = Math.Clamp(row * 2 - oy, 0, h - 1) * w, y1 = Math.Clamp(row * 2 + 1 - oy, 0, h - 1) * w;
        return world[y0 + x0] != backdrop[y0 + x0] || world[y0 + x1] != backdrop[y0 + x1]
            || world[y1 + x0] != backdrop[y1 + x0] || world[y1 + x1] != backdrop[y1 + x1];
    }
}

/// <summary>Draws each wingman's comm under its ship. The box is background: <see cref="RaiderView"/> paints it before every
/// other overlay, and it leaves the cells the action covers alone, so ships and bullets show through it.</summary>
public sealed class CommPainter
{
    private const uint PanelBg = 0x0a1426, Dim = 0x6f86ad, TextFg = 0xe6eefc;

    private readonly QuadComposer[] _avQuad = [new(), new()];
    private readonly Surface _avSurface = new(8, 8);
    private readonly Screen _avCells = new(4, 4);
    private Screen _scr = null!;
    private CellRect _clip;
    private Foreground _fore;

    public void Draw(RaiderGame g, Screen scr, FieldLayout lay, Foreground fore)
    {
        _scr = scr; _fore = fore;
        _clip = new CellRect(lay.Col0, 2, lay.PCols, Math.Min(lay.PRows, scr.Rows) - 2);
        // the speaker last, so where two boxes overlap the one being read is whole
        foreach (var w in g.Wings) if (w != g.Comms.Speaker) DrawComm(w, Box(w, lay), speaking: false);
        if (g.Comms.Speaker is { } sp && g.Wings.Contains(sp)) DrawComm(sp, Box(sp, lay), speaking: true);
    }

    /// <summary>The cells of <paramref name="w"/>'s comm: centred under the ship at one fixed offset, wherever the ship is.
    /// It is never moved to stay on screen; the field's edge crops it.</summary>
    public static CellRect Box(Wingman w, FieldLayout lay) =>
        new(lay.Col0 + (int)Math.Floor(w.X / 2) - WingComms.CommW(w) / 2, (int)Math.Floor(w.Y / 2) + (w.Plane.H + 3) / 4 + 2, WingComms.CommW(w), WingComms.CommH(w));

    /// <summary>One cell of a comm: dropped outside the field (the crop) and where the action covers it (it shows through).</summary>
    private void Put(int c, int r, char ch, uint fg, uint bg)
    {
        if (c < _clip.C || c >= _clip.Right || r < _clip.R || r >= _clip.Bottom || _fore.Covers(c, r)) return;
        _scr.Put(c, r, ch, fg, bg);
    }

    private void Text(int c, int r, string s, uint fg, uint bg)
    {
        for (int i = 0; i < s.Length; i++) Put(c + i, r, s[i], fg, bg);
    }

    private void DrawComm(Wingman w, CellRect r, bool speaking)
    {
        if (!r.Overlaps(_clip)) return;
        // dim and low-contrast, so it reads as part of the backdrop; the words keep their contrast
        uint ac = w.Accent, bg = Surface.Mix(PanelBg, ac, 0.14), edge = Surface.Mix(ac, bg, 0.5);
        Frame(r, edge, bg);
        // a pointer on the top edge ties the comm to the ship above it
        Put(r.C + r.W / 2, r.R, '▲', edge, bg);

        FillAvatar(w);
        _avQuad[w.Slot & 1].Compose(_avSurface, 0, 0, _avCells, 0, 0);
        for (int y = 0; y < 4 && y < r.H - 2; y++)
            for (int x = 0; x < 4; x++)
            {
                int i = y * _avCells.Cols + x;
                Put(r.C + 1 + x, r.R + 1 + y, _avCells.Ch[i], _avCells.Fg[i], _avCells.Bg[i]);
            }
        Text(r.C + 6, r.R + 1, w.HandleTag, ac, bg);
        // an open thread reads down: stubs of the oldest posts, the previous post shortened, then the newest. The whole page is
        // always on show; the typewriter only brightens it, so nothing appears or vanishes as it is read out
        int y0 = r.R + 2, x0 = r.C + 6;
        foreach (var stub in WingComms.Stubs(w, w.CommTextW)) Text(x0, y0++, stub, Dim, bg);
        uint prevFg = Surface.Mix(TextFg, bg, 0.45), unread = Surface.Mix(TextFg, bg, 0.4);
        foreach (var line in w.PrevLines) Text(x0, y0++, line, prevFg, bg);
        var (page, shown) = WingComms.Progress(w, w.CommAge);
        bool thread = w.Earlier.Count > 0, cursor = speaking;
        for (int i = 0, first = page * w.PageLines; i < w.PageLines && first + i < w.CommLines.Length; i++)
        {
            string line = w.CommLines[first + i];
            int n = w.Comm == CommState.Waiting ? 0 : Math.Min(line.Length, shown);   // not yet read out: whole, in the unread colour
            shown -= n;
            if (thread) Put(x0 - 1, y0 + i, '▌', ac, bg);
            for (int k = 0; k < line.Length; k++) Put(x0 + k, y0 + i, line[k], k < n ? TextFg : unread, bg);
            if (cursor && n < line.Length) { Put(x0 + n, y0 + i, '▌', ac, bg); cursor = false; }
        }
        if (WingComms.Pages(w) > 1) Text(r.Right - 5, r.Bottom - 1, $"{page + 1}/{WingComms.Pages(w)}", Dim, bg);
    }

    private void Frame(CellRect r, uint fg, uint bg)
    {
        for (int y = 0; y < r.H; y++) for (int x = 0; x < r.W; x++) Put(r.C + x, r.R + y, ' ', fg, bg);
        for (int x = 1; x < r.W - 1; x++) { Put(r.C + x, r.R, '─', fg, bg); Put(r.C + x, r.Bottom - 1, '─', fg, bg); }
        for (int y = 1; y < r.H - 1; y++) { Put(r.C, r.R + y, '│', fg, bg); Put(r.Right - 1, r.R + y, '│', fg, bg); }
        Put(r.C, r.R, '┌', fg, bg); Put(r.Right - 1, r.R, '┐', fg, bg);
        Put(r.C, r.Bottom - 1, '└', fg, bg); Put(r.Right - 1, r.Bottom - 1, '┘', fg, bg);
    }

    /// <summary>The post's opening words on one line, whitespace collapsed, cut with an ellipsis to fit <paramref name="w"/> cells.</summary>
    internal static string Snippet(string text, int w)
    {
        if (w <= 0) return "";
        var s = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= w ? s : s[..Math.Max(0, w - 1)] + "…";
    }

    private void FillAvatar(Wingman w) => RaiderView.FillAvatar(w, _avSurface);
}
