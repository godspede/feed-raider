using TUImation;

namespace FeedRaider.Engine.Raider;

/// <summary>Draws each wingman's comm under its ship. The box is a solid panel over the field:
/// ships and bullets pass beneath it, and the field's edge crops it.</summary>
public sealed class CommPainter
{
    public const uint PanelBg = 0x0c1830, TextFg = 0xe6eefc;
    private const uint Dim = Palette.Dim;

    private readonly QuadComposer[] _avQuad = [new(), new()];
    // The avatar is a square of AvatarCells cells, each two pixels across, drawn one cell in from the box's left edge and
    // followed by a one-cell gap; the handle and the text sit past that.
    private const int AvatarCells = 4, TextInset = AvatarCells + 2;
    private readonly Surface _avSurface = new(AvatarCells * 2, AvatarCells * 2);
    private readonly Screen _avCells = new(AvatarCells, AvatarCells);
    private Screen _scr = null!;
    private CellRect _clip;

    /// <summary>The boxes drawn by the last <see cref="Draw"/>, so the view can lay the units back over them.</summary>
    public List<CellRect> Boxes { get; } = [];

    public void Draw(RaiderGame g, Screen scr, FieldLayout lay)
    {
        _scr = scr;
        Boxes.Clear();
        _clip = new CellRect(lay.Col0, 2, lay.PCols, Math.Min(lay.PRows, scr.Rows) - 2);
        // the darkness speaks first, so a wingman's box is whole where the two meet
        foreach (var e in EnemyComm.Visible(g, lay, g.MaxWings)) DrawEnemyComm(e, EnemyComm.Box(e, lay));
        // the speaker last, so where two boxes overlap the one being read is whole
        foreach (var w in g.Wings) if (w != g.Comms.Speaker) DrawComm(w, Box(w, lay), speaking: false);
        if (g.Comms.Speaker is { } sp && g.Wings.Contains(sp)) DrawComm(sp, Box(sp, lay), speaking: true);
    }

    /// <summary>The cells of <paramref name="w"/>'s comm: centred under the ship at one fixed offset, wherever the ship is.
    /// It is never moved to stay on screen; the field's edge crops it.</summary>
    public static CellRect Box(Wingman w, FieldLayout lay) =>
        new(lay.Col0 + (int)Math.Floor(w.X / 2) - WingComms.CommW(w) / 2, (int)Math.Floor(w.Y / 2) + (w.Plane.H + 3) / 4 + 2, WingComms.CommW(w), WingComms.CommH(w));

    /// <summary>One cell of a comm: dropped outside the field (the crop).</summary>
    private void Put(int c, int r, char ch, uint fg, uint bg)
    {
        if (c < _clip.C || c >= _clip.Right || r < _clip.R || r >= _clip.Bottom) return;
        _scr.Put(c, r, ch, fg, bg);
    }

    private void Text(int c, int r, string s, uint fg, uint bg)
    {
        for (int i = 0; i < s.Length; i++) Put(c + i, r, s[i], fg, bg);
    }

    private void DrawComm(Wingman w, CellRect r, bool speaking)
    {
        if (!r.Overlaps(_clip)) return;
        Boxes.Add(r);
        uint ac = w.Accent, bg = Surface.Mix(PanelBg, ac, 0.22);
        Frame(r, ac, bg);
        // a pointer on the top edge ties the comm to the ship above it
        Put(r.C + r.W / 2, r.R, '▲', ac, bg);

        RaiderView.FillAvatar(w, _avSurface);
        _avQuad[w.Slot & 1].Compose(_avSurface, 0, 0, _avCells, 0, 0);
        for (int y = 0; y < _avCells.Rows && y < r.H - 2; y++)
            for (int x = 0; x < _avCells.Cols; x++)
            {
                int i = y * _avCells.Cols + x;
                Put(r.C + 1 + x, r.R + 1 + y, _avCells.Ch[i], _avCells.Fg[i], _avCells.Bg[i]);
            }
        Text(r.C + TextInset, r.R + 1, w.HandleTag, Surface.Legible(ac, bg), bg);
        // an open thread reads down: stubs of the oldest posts, the previous post shortened, then the newest. The whole page is
        // always on show; the typewriter only brightens it, so nothing appears or vanishes as it is read out
        int y0 = r.R + 2, x0 = r.C + TextInset;
        uint dim = Surface.Legible(Dim, bg);
        foreach (var stub in WingComms.Stubs(w, w.CommTextW)) Text(x0, y0++, stub, dim, bg);
        uint prevFg = Surface.Legible(Surface.Mix(TextFg, bg, 0.45), bg), unread = Surface.Legible(Surface.Mix(TextFg, bg, 0.4), bg);
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
        if (WingComms.Pages(w) > 1) Text(r.Right - 5, r.Bottom - 1, $"{page + 1}/{WingComms.Pages(w)}", dim, bg);
    }

    /// <summary>An enemy's comm: the wingmen's box with the label for a name, red on near-black, a pointer facing down at the enemy.</summary>
    private void DrawEnemyComm(Enemy e, CellRect r)
    {
        if (!r.Overlaps(_clip)) return;
        Boxes.Add(r);
        const uint bg = EnemyComm.PanelBg, ac = EnemyComm.Edge;
        Frame(r, ac, bg);
        if (EnemyComm.IsBelow(e, r)) Put(r.C + r.W / 2, r.R, '▲', ac, bg); else Put(r.C + r.W / 2, r.Bottom - 1, '▼', ac, bg);
        Text(r.C + 2, r.R + 1, e.Header!, EnemyComm.HeaderFg, bg);
        var (page, shown) = WingComms.Progress(e.CommLines!, e.PageLines, e.CommAge);
        bool cursor = true;
        for (int i = 0, first = page * e.PageLines; i < e.PageLines && first + i < e.CommLines!.Length; i++)
        {
            string line = e.CommLines[first + i];
            int n = Math.Min(line.Length, shown);
            shown -= n;
            for (int k = 0; k < line.Length; k++) Put(r.C + 2 + k, r.R + 2 + i, line[k], k < n ? EnemyComm.TextFg : EnemyComm.UnreadFg, bg);
            if (cursor && n < line.Length) { Put(r.C + 2 + n, r.R + 2 + i, '▌', ac, bg); cursor = false; }
        }
        int pages = WingComms.Pages(e.CommLines!, e.PageLines);
        if (pages > 1) Text(r.Right - 5, r.Bottom - 1, $"{page + 1}/{pages}", EnemyComm.UnreadFg, bg);
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
}
