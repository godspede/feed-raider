using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>Draws the wingmen's comms and chips under their ships. All the placement lives here and in
/// <see cref="CommPlacement"/>; <see cref="RaiderView"/> only calls <see cref="Draw"/> and reads <see cref="Occupied"/>.</summary>
public sealed class CommPainter
{
    private const uint PanelBg = 0x0c1830, Dim = 0x6f86ad, TextFg = 0xe6eefc;

    private readonly QuadComposer[] _avQuad = [new(), new()];
    private readonly Surface _avSurface = new(8, 8);
    private readonly List<CellRect> _avoid = new(16);
    private CellRect _comm;

    /// <summary>Cells the speaker's comm took this frame (W = 0 when nobody speaks); other overlays can steer clear.</summary>
    public CellRect Occupied => _comm;

    /// <summary>Cells other overlays have claimed this frame; a merge adds speech bubbles and enemy badges here.</summary>
    public void Avoid(CellRect r) => _avoid.Add(r);

    public void Draw(RaiderGame g, Screen scr, FieldLayout lay)
    {
        _comm = default;
        int c0 = lay.Col0, cols = lay.PCols;
        var field = new CellRect(c0, 0, cols, lay.Rows);

        if (g.Alive)
        {
            int pw = g.Plane.W / 2 + 2, ph = g.Plane.H / 2 + 2;
            _avoid.Add(new CellRect(c0 + (int)(g.PX / 2) - pw / 2 - 1, (int)(g.PY / 2) - ph / 2 - 1, pw + 2, ph + 2));
        }
        if (g.Input.PointerLast)
        {
            var (bc, br, bw, bh) = g.BombButton;
            _avoid.Add(new CellRect(bc, br, bw, bh));
        }

        if (g.Comms.Speaker is { } sp && OnScreen(sp, lay))
        {
            _comm = Place(sp, WingComms.CommW(sp), WingComms.CommH(sp), c0, field, out bool above);
            DrawComm(scr, sp, _comm, above);
            _avoid.Add(_comm);
        }
        foreach (var w in g.Wings)
        {
            if (w == g.Comms.Speaker || !OnScreen(w, lay)) continue;
            var r = Place(w, WingComms.ChipW, WingComms.ChipH, c0, field, out _);
            DrawChip(scr, w, r);
            _avoid.Add(r);
        }
        _avoid.Clear();
    }

    private static bool OnScreen(Wingman w, FieldLayout lay) => w.Y < lay.PH && w.Y > 8;

    private CellRect Place(Wingman w, int width, int height, int c0, CellRect field, out bool above)
    {
        int shipCol = c0 + (int)(w.X / 2);
        int top = (int)((w.Y - w.Plane.H / 2.0) / 2), bottom = (int)((w.Y + w.Plane.H / 2.0) / 2) + 1;
        return CommPlacement.Place(shipCol, top, bottom, width, height, field, 2, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_avoid), out above);
    }

    private void DrawComm(Screen scr, Wingman w, CellRect r, bool above)
    {
        uint ac = w.Accent, bg = Surface.Mix(PanelBg, ac, 0.22);
        double open = WingComms.Openness(w);
        int h = open >= 1 ? r.H : Math.Max(2, (int)Math.Round(r.H * open));
        int row = above ? r.Bottom - h : r.R;
        Frame(scr, r.C, row, r.W, h, ac, bg);
        // a pointer on the edge nearest the ship ties the comm to it
        int tip = Math.Clamp(r.C + r.W / 2, r.C + 1, r.Right - 2);
        scr.Put(tip, above ? row + h - 1 : row, above ? '▼' : '▲', ac, bg);
        if (h < r.H) return;

        FillAvatar(w);
        _avQuad[w.Slot & 1].Compose(_avSurface, 0, 0, scr, r.C + 1, r.R + 1);
        scr.Text(r.C + 6, r.R + 1, w.HandleTag, ac, bg);
        // an open thread reads down: stubs of the oldest posts, the previous post shortened, then the newest being typed out
        int y = r.R + 2, x = r.C + 6;
        foreach (var stub in WingComms.Stubs(w, w.CommTextW)) scr.Text(x, y++, stub, Dim, bg);
        uint prevFg = Surface.Mix(TextFg, bg, 0.45);
        foreach (var line in w.PrevLines) scr.Text(x, y++, line, prevFg, bg);
        var (page, shown) = WingComms.Progress(w, w.CommAge);
        bool thread = w.Earlier.Count > 0;
        for (int i = 0, first = page * w.PageLines; i < w.PageLines && first + i < w.CommLines.Length && shown > 0; i++)
        {
            string line = w.CommLines[first + i];
            int n = Math.Min(line.Length, shown);
            shown -= n;
            if (thread) scr.Put(x - 1, y + i, '▌', ac, bg);
            for (int k = 0; k < n; k++) scr.Put(x + k, y + i, line[k], TextFg, bg);
            if (n < line.Length) { scr.Put(x + n, y + i, '▌', ac, bg); break; }
        }
        if (WingComms.Pages(w) > 1) scr.Text(r.Right - 5, r.Bottom - 1, $"{page + 1}/{WingComms.Pages(w)}", Dim, bg);
    }

    private static void Frame(Screen scr, int c, int r, int w, int h, uint fg, uint bg)
    {
        for (int y = 0; y < h; y++) scr.Blank(c, r + y, w, fg, bg);
        for (int x = 1; x < w - 1; x++) { scr.Put(c + x, r, '─', fg, bg); scr.Put(c + x, r + h - 1, '─', fg, bg); }
        for (int y = 1; y < h - 1; y++) { scr.Put(c, r + y, '│', fg, bg); scr.Put(c + w - 1, r + y, '│', fg, bg); }
        scr.Put(c, r, '┌', fg, bg); scr.Put(c + w - 1, r, '┐', fg, bg);
        scr.Put(c, r + h - 1, '└', fg, bg); scr.Put(c + w - 1, r + h - 1, '┘', fg, bg);
    }

    private void DrawChip(Screen scr, Wingman w, CellRect r)
    {
        uint ac = w.Accent, bg = Surface.Mix(PanelBg, ac, 0.16);
        for (int y = 0; y < r.H; y++) scr.Blank(r.C, r.R + y, r.W, ac, bg);
        for (int y = 0; y < r.H; y++) scr.Put(r.C, r.R + y, '▌', ac, bg);
        FillAvatar(w);
        _avQuad[w.Slot & 1].Compose(_avSurface, 0, 0, scr, r.C + 1, r.R);
        scr.Text(r.C + 6, r.R + 1, w.ChipTag, ac, bg);
        scr.Text(r.C + 6, r.R + 2, Snippet(w.Text, r.W - 7), Dim, bg);
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
