using System.Runtime.CompilerServices;

namespace FeedRaider.Engine.Raider;

/// <summary>The colours of a card in the background. Everything here is dim on purpose: the fight draws over it, and no idle colour
/// is brighter than <see cref="MaxIdleLuminance"/> so the ships, bullets and text always stand out of it.</summary>
public static class CardColours
{
    public const uint Slab = 0x131a2b, Edge = 0x1e2942;
    /// <summary>The top edge of a gold card; and the colour of its star and like count on the name row, which is the edge's
    /// colour lifted until it reads on the slab.</summary>
    public const uint GoldEdge = 0x8a7020;
    public static readonly uint GoldText = Surface.Legible(GoldEdge, Slab);
    /// <summary>The icon of a button that is off: the idle colour lifted until a mark one letter tall reads on the slab.</summary>
    public static readonly uint LikeIcon = Surface.Legible(LikeIdle, Slab), RepostIcon = Surface.Legible(RepostIdle, Slab);
    /// <summary>The thread note: the colour of the heart of a button that is off, so the note points back at the like that brought the card.</summary>
    public static readonly uint ThreadText = LikeIcon;
    public const uint LikeIdle = 0x6b3348, RepostIdle = 0x2f5a45;
    public const uint LikeLit = 0xe0456f, RepostLit = 0x35c28a;
    /// <summary>A button held to the end while signed out: the line under its icon is in the lit colour at half strength, since nothing was sent.</summary>
    public static readonly uint LikeMarked = Surface.Mix(Slab, LikeLit, 0.5), RepostMarked = Surface.Mix(Slab, RepostLit, 0.5);
    public const double MaxIdleLuminance = 0.40;
    /// <summary>How far toward the slab a picture is mixed, and how far toward its icon a lit button's ground is.</summary>
    public const double AvatarDim = 0.35, ButtonTint = 0.35;
    /// <summary>The box of a post's picture that has not come, or never will: the edge colour, one the card has already.</summary>
    public const uint PictureWaiting = Edge;

    // An idle button has no ground of its own, only its icon: every extra colour behind the fight is one more pair for the terminal's
    // glyph atlas wherever a bullet crosses it (a tinted ground cost about 650 entries in a 60 second run).
    public static readonly uint LikeButtonLit = Surface.Mix(Slab, LikeLit, ButtonTint), RepostButtonLit = Surface.Mix(Slab, RepostLit, ButtonTint);

    /// <summary>The words of a card. Each reads on the slab at 4.5 to 1 or better, and is dim enough to stay under
    /// <see cref="MaxIdleLuminance"/>. Three fixed colours, not a gradient: each (letter, colour, ground) is one atlas entry.</summary>
    public const uint NameText = 0x9aa5ba, PostText = 0x8b95a9, DimText = 0x7a859b;

    /// <summary>A count on a lit button's ground, which is lighter than the slab: the lit icon colour made legible on it (4.5 to 1).</summary>
    public static readonly uint LikeCountLit = Surface.Legible(LikeLit, LikeButtonLit), RepostCountLit = Surface.Legible(RepostLit, RepostButtonLit);

    /// <summary>Every colour a card is painted in while idle: none is brighter than <see cref="MaxIdleLuminance"/>.</summary>
    public static readonly uint[] Idle = [Slab, Edge, GoldEdge, LikeIdle, RepostIdle, NameText, PostText, DimText, GoldText, LikeIcon, RepostIcon];

    /// <summary>Every fixed colour of a card, for the encoder to hold exactly (see <see cref="ColourSnap.Prime"/>).</summary>
    public static readonly uint[] All = [.. Idle, LikeLit, RepostLit, LikeButtonLit, RepostButtonLit, LikeCountLit, RepostCountLit, LikeMarked, RepostMarked];
}

/// <summary>Draws the posts of the feed into the background pass of the field: each card's slab, its top edge, its avatar, the
/// pictures of its post and its two buttons. The layer is under the units, the bullets and the text.</summary>
public static class CardPainter
{
    /// <summary>Pixel rows 0 to 3 of the screen belong to the HUD; the cards never paint there.</summary>
    public const int HudPixelRows = 4;
    /// <summary>The avatar's side in pixels.</summary>
    private const int AvatarPx = CardLayout.AvatarCells * CardField.PixelsPerCell;

    /// <summary>The heart and the repost loop a held button grows, centred on the button's box. A button at rest draws no art: its
    /// icon is one character on the action row.</summary>
    public static readonly string[] Heart =
    [
        ".##.##.",
        "#######",
        "#######",
        ".#####.",
        "..###..",
        "...#...",
    ];

    public static readonly string[] Loop =
    [
        "..#....",
        ".#####.",
        "..#...#",
        "#...#..",
        ".#####.",
        "....#..",
    ];

    /// <summary>Cells of lit ground left and right of a lit button's icon and count.</summary>
    public const int LitPadCols = 1;

    /// <summary>A pending button's icon is lit for this many frames, then idle for as many, until the write is answered.</summary>
    public const int BlinkFrames = 6;

    // The tints of the disc that stands in for an avatar that has not come: dim, so it stays behind the fight.
    private static readonly uint[] DiscColours =
    [
        0x3a4f7a, 0x4d3f7a, 0x6a3f6e, 0x7a3f4d, 0x7a5a3a, 0x5a6e3a, 0x2f6e5a, 0x2f5f7a,
    ];

    private static readonly ConditionalWeakTable<uint[], uint[]> Dimmed = new();
    private static readonly uint[][] Discs = Enumerable.Range(0, DiscColours.Length).Select(BakeDisc).ToArray();

    /// <summary>Draws every card of <paramref name="g"/> into <paramref name="s"/>, moved by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// pixels, and never above pixel row <paramref name="clipTop"/> of the field.</summary>
    public static void DrawCards(Surface s, RaiderGame g, int dx, int dy, int clipTop)
    {
        int x0 = g.Cards.CardLeft * CardField.PixelsPerCell + dx;
        foreach (var c in g.Cards.Cards)
        {
            int y0 = c.Top * CardField.PixelsPerCell + dy;
            var shape = c.Shape;
            int h = shape.Rows * CardField.PixelsPerCell;
            if (y0 >= s.H || y0 + h <= clipTop) continue;
            Fill(s, x0, y0, shape.Cols * CardField.PixelsPerCell, h, CardColours.Slab, clipTop);
            Fill(s, x0, y0, shape.Cols * CardField.PixelsPerCell, 1, c.Gold ? CardColours.GoldEdge : CardColours.Edge, clipTop);
            DrawAvatar(s, g, c, x0, y0, clipTop);
            DrawPictures(s, g, c, x0, y0, clipTop);
            DrawButton(s, g, c, PostAction.Like, x0, y0, clipTop);
            DrawButton(s, g, c, PostAction.Repost, x0, y0, clipTop);
        }
    }

    /// <summary>The shake of the cards: the pixel shake rounded to an even number of pixels, so a card moves a whole cell at a time (one
    /// pixel stays put, two is a cell).</summary>
    internal static int EvenShake(int px) =>
        CardField.PixelsPerCell * (int)Math.Round(px / (double)CardField.PixelsPerCell, MidpointRounding.ToEven);

    /// <summary>Writes the words of every card into <paramref name="scr"/> as cells, over what <see cref="QuadComposer"/> made of the
    /// slab. A letter goes in only where the four pixels of its cell are the same in <paramref name="after"/> (the field as drawn) as in
    /// <paramref name="before"/> (the field just after the slabs), and are the ground the letter is for: anything the fight drew there
    /// (a ship, a bullet, a pickup, a ring, a particle) changes a pixel, and a cell the HUD cuts off the slab is not the slab. A wide
    /// letter needs both of its cells clear. The cards move by the shake rounded to a cell, so the words move with them.</summary>
    internal static void DrawText(Screen scr, RaiderGame g, uint[] before, uint[] after, int pw, int ph, int col0, int sx, int sy)
    {
        int left = col0 + g.Cards.CardLeft + EvenShake(sx) / CardField.PixelsPerCell, dy = EvenShake(sy) / CardField.PixelsPerCell;
        foreach (var c in g.Cards.Cards)
        {
            var s = c.Shape;
            int top = c.Top + dy;
            if (top >= scr.Rows || top + s.Rows <= 0) continue;
            DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, s.Name, CardColours.NameText, CardColours.Slab);
            if (s.Star is { } star) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, star, CardColours.GoldText, CardColours.Slab, own: true);
            DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, s.Handle, CardColours.DimText, CardColours.Slab);
            foreach (var l in s.Lines) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, l, CardColours.PostText, CardColours.Slab);
            if (s.Note is { } n) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, n, c.Thread ? CardColours.ThreadText : CardColours.DimText, CardColours.Slab);
            DrawAction(scr, g, c, PostAction.Like, before, after, pw, ph, col0, sx, sy, left, top);
            DrawAction(scr, g, c, PostAction.Repost, before, after, pw, ph, col0, sx, sy, left, top);
        }
    }

    /// <summary>One button of the action row as text: its icon, and right after it its count, unless the lock stands there.</summary>
    private static void DrawAction(Screen scr, RaiderGame g, Card c, PostAction action, uint[] before, uint[] after, int pw, int ph,
        int col0, int sx, int sy, int left, int top)
    {
        bool like = action == PostAction.Like;
        var state = c.Marks.Of(action);
        bool lit = LitGround(state);
        uint ground = !lit ? CardColours.Slab : like ? CardColours.LikeButtonLit : CardColours.RepostButtonLit;
        DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, IconAt(c, action), IconColour(state, action, g.Frame), ground, own: true);
        if (Locked(g, c, action)) return;
        uint count = !lit ? CardColours.DimText : like ? CardColours.LikeCountLit : CardColours.RepostCountLit;
        DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, CountAt(c, action), count, ground);
    }

    private static readonly string HeartOn = CardLayout.HeartOn.ToString();

    /// <summary>The icon a button shows and where: the heart is full only for a like that is asked for or made, and the outline
    /// while the button is off or marked; the loop is one shape, told apart by its colour and its ground.</summary>
    public static CardText IconAt(Card c, PostAction action)
    {
        if (action == PostAction.Repost) return c.Shape.RepostIcon;
        return LitGround(c.Marks.Like) ? c.Shape.LikeIcon with { Text = HeartOn } : c.Shape.LikeIcon;
    }

    /// <summary>The colour of a button's icon: dim while off, the lit colour on the slab once marked, and on a lit ground the
    /// colour its count has there. A pending icon blinks between that and the idle colour until the write is answered.</summary>
    public static uint IconColour(ButtonState state, PostAction action, long frame)
    {
        bool like = action == PostAction.Like;
        uint lit = like ? CardColours.LikeCountLit : CardColours.RepostCountLit;
        return state switch
        {
            ButtonState.Lit => lit,
            ButtonState.Pending => frame / BlinkFrames % 2 == 0 ? lit : like ? CardColours.LikeIdle : CardColours.RepostIdle,
            ButtonState.Marked => like ? CardColours.LikeLit : CardColours.RepostLit,
            _ => like ? CardColours.LikeIcon : CardColours.RepostIcon,
        };
    }

    /// <summary>A button whose write is asked for or made stands on the lit ground.</summary>
    private static bool LitGround(ButtonState s) => s is ButtonState.Lit or ButtonState.Pending;

    /// <summary>The count a button shows: the post's own, and one more once the player's like or repost of this run is on it.</summary>
    public static string CountText(Card c, PostAction action) => CountText(c.Post, c.Marks, action);

    /// <summary>The same count for a post that is not on a card, a feed-log entry.</summary>
    public static string CountText(FeedPost post, PostMarks marks, PostAction action) =>
        CardLayout.ShortCount(Math.Max(0, (action == PostAction.Like ? post.LikeCount : post.RepostCount) + marks.Delta(action)));

    /// <summary>Where a button's count is drawn: right after its icon, whatever the count has grown to.</summary>
    public static CardText CountAt(Card c, PostAction action)
    {
        bool like = action == PostAction.Like;
        if (c.Marks.Delta(action) == 0) return like ? c.Shape.LikeCount : c.Shape.RepostCount;
        return CardLayout.CountAt(like ? c.Shape.LikeIcon : c.Shape.RepostIcon, CountText(c, action));
    }

    /// <summary>The cells a button's icon and count take on the action row, in cells of the card: the first column and how many.</summary>
    public static (int Col, int Cols) ActionSpan(Card c, PostAction action)
    {
        var icon = action == PostAction.Like ? c.Shape.LikeIcon : c.Shape.RepostIcon;
        var count = CountAt(c, action);
        return (icon.Col, count.Col + CellText.Width(count.Text) - icon.Col);
    }

    /// <summary>The part of a button that is pixels and not text. Off, there is none: the icon and count stand on the bare slab.
    /// Lit or pending, the lit ground lies under the icon and count, one row tall. Marked, a line runs under them, in the marked
    /// colour, on the first pixel row under the action row. Locked, the padlock stands where the count would.</summary>
    private static void DrawButton(Surface s, RaiderGame g, Card c, PostAction action, int x0, int y0, int clipTop)
    {
        bool like = action == PostAction.Like;
        var icon = like ? c.Shape.LikeIcon : c.Shape.RepostIcon;
        var (col, cols) = ActionSpan(c, action);
        int px = x0 + col * CardField.PixelsPerCell, py = y0 + icon.Row * CardField.PixelsPerCell, w = cols * CardField.PixelsPerCell;
        switch (c.Marks.Of(action))
        {
            case ButtonState.Lit or ButtonState.Pending:
                int pad = LitPadCols * CardField.PixelsPerCell, right = Math.Min(px + w + pad, x0 + (c.Shape.Cols - 1) * CardField.PixelsPerCell);
                Fill(s, px - pad, py, right - (px - pad), CardLayout.ActionRows * CardField.PixelsPerCell, like ? CardColours.LikeButtonLit : CardColours.RepostButtonLit, clipTop);
                break;
            case ButtonState.Marked:
                Fill(s, px, py + CardLayout.ActionRows * CardField.PixelsPerCell, w, 1, like ? CardColours.LikeMarked : CardColours.RepostMarked, clipTop);
                break;
            default:
                if (Locked(g, c, action)) DrawLock(s, like ? c.Shape.LikeButton : c.Shape.RepostButton, like ? c.Shape.LikeCount : c.Shape.RepostCount, x0, y0, clipTop);
                break;
        }
    }

    /// <summary>The build-up of every held or draining button, drawn over the slabs and under every unit: the button's outline while
    /// the ship is on it, a halo on the slab round the icon, and the icon grown to its step's width. The heart fills from the
    /// bottom as far as progress has come; the loop is drawn round, its arrows a quarter turn on with every beat. For the frames
    /// of a finish the icon is drawn at its widest and near white, then falls back; the loop turns once as it does.</summary>
    public static void DrawHoldGlow(Surface s, RaiderGame g, int dx, int dy, int clipTop)
    {
        var touch = HoldTracker.Touching(g);
        foreach (var c in g.Cards.Cards)
            foreach (var a in (ReadOnlySpan<PostAction>)[PostAction.Like, PostAction.Repost])
            {
                var m = c.Marks;
                int progress = m.Progress(a);
                long finished = g.Hold.SinceFinished(m, a, g.Frame);
                int settle = (int)finished - HoldBeats.FinishedHitStop - 1;
                bool flash = finished <= HoldBeats.FinishedHitStop, settling = settle >= 0 && settle < HoldBeats.SettleWidths.Length;
                bool on = touch is { } t && ReferenceEquals(t.Card, c) && t.Action == a;
                if (progress <= 0 && !flash && !settling && !on) continue;
                var box = g.Cards.ButtonBox(c, a);
                box = new PixelRect(box.X + dx, box.Y + dy, box.W, box.H);
                uint[] steps = HoldBeats.Colours(a);
                if (on) Outline(s, box, steps[0], clipTop);
                string[] art = a == PostAction.Like ? Heart : Loop;
                uint idle = a == PostAction.Like ? CardColours.LikeIdle : CardColours.RepostIdle;
                bool loop = a == PostAction.Repost;
                if (flash)
                {
                    Halo(s, box, HoldBeats.FinishedWidth, HoldBeats.HaloColour(a, HoldBeats.Ticks.Length - 1), clipTop);
                    if (loop) LoopIcon(s, box, HoldBeats.FinishedWidth, HoldBeats.Finished(a), idle, 1, SpinPhase(HoldBeats.FinishedWidth, finished), clipTop);
                    else Icon(s, box, art, HoldBeats.FinishedWidth, HoldBeats.Finished(a), idle, 1, clipTop);
                }
                else if (settling)
                {
                    int sw = HoldBeats.SettleWidths[settle];
                    if (loop) LoopIcon(s, box, sw, steps[^1], idle, 1, SpinPhase(sw, finished), clipTop);
                    else Icon(s, box, art, sw, steps[^1], idle, 1, clipTop);
                }
                else if (progress > 0)
                {
                    int step = HoldBeats.StepAt(progress);
                    long beat = g.Hold.SinceBeat(m, a, g.Frame);
                    int w = HoldBeats.Widths[step] + (beat < HoldBeats.ThumpFrames ? HoldBeats.ThumpPixels : 0);
                    double filled = progress / (double)Tuning.HoldFrames;
                    Halo(s, box, w, HoldBeats.HaloColour(a, step), clipTop);
                    if (loop) LoopIcon(s, box, w, steps[step], idle, filled, LoopPhase(w, step, beat), clipTop);
                    else Icon(s, box, art, w, steps[step], idle, filled, clipTop);
                }
            }
    }

    /// <summary>The loop of a held repost button is not the button's small picture scaled up but drawn for its size: the outline of a
    /// rounded box <paramref name="w"/> pixels wide, as the pixels of one walk round it, clockwise from the top left. The lit part
    /// of a hold is the first so many of them.</summary>
    public static (int X, int Y)[] LoopPath(int w) => Loops.GetOrAdd(w, BuildLoop).Path;

    /// <summary>How far round the loop its arrowheads are, in pixels of <see cref="LoopPath"/>: a quarter turn for every beat
    /// passed, and on a beat they travel that quarter over the frames of the thump instead of jumping it.</summary>
    public static int LoopPhase(int w, int step, long sinceBeat)
    {
        int quarter = LoopPath(w).Length / 4;
        if (step > 0 && sinceBeat >= 0 && sinceBeat < HoldBeats.ThumpFrames)
            return (step - 1) * quarter + quarter * ((int)sinceBeat + 1) / (HoldBeats.ThumpFrames + 1);
        return step * quarter;
    }

    /// <summary>A finished repost spins its loop once: how far round the arrowheads are this many frames after the hold ended.</summary>
    public static int SpinPhase(int w, long sinceDone) =>
        (int)Math.Round(LoopPath(w).Length * Math.Clamp(sinceDone, 0, SpinFrames) / (double)SpinFrames);

    /// <summary>The frames a finished loop takes to turn once.</summary>
    public const int SpinFrames = 6;

    /// <summary>Loop icon widths, in pixels, from which the corners are cut deep and the arrow barbs grow, from which the barbs grow
    /// longest, and from which the line is drawn thick. A narrower loop has corners cut one pixel deep, one-pixel barbs and a one-pixel line.</summary>
    private const int LoopThickCornerWidth = 13, LoopLongBarbWidth = 19, LoopThickLineWidth = 15;
    /// <summary>In pixels: the deep corner cut, the barb from <see cref="LoopThickCornerWidth"/>, the barb from
    /// <see cref="LoopLongBarbWidth"/>, and the thick line.</summary>
    private const int LoopDeepCorner = 2, LoopMidBarb = 2, LoopLongBarb = 3, LoopThickLine = 2;
    /// <summary>The gap left ahead of each arrow is this fraction of the outline: one part in this many.</summary>
    private const int LoopGapDivisor = 12;
    /// <summary>How much further toward the slab a picture is pulled on each pass while it is still too bright.</summary>
    private const double DimStep = 0.05;
    /// <summary>How far toward the card's edge colour the slab is mixed for the ground round an avatar's disc.</summary>
    private const double DiscGroundMix = 0.6;
    /// <summary>The colour bits of a pixel, without its alpha.</summary>
    private const uint RgbMask = 0xffffff;

    private sealed record LoopShape((int X, int Y)[] Path, (int X, int Y)[] Inward);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, LoopShape> Loops = new();

    private static LoopShape BuildLoop(int w)
    {
        int h = HoldBeats.IconHeight(w), r = w >= LoopThickCornerWidth ? LoopDeepCorner : 1;
        var path = new List<(int, int)>();
        var inward = new List<(int, int)>();
        void Add(int x, int y, int ix, int iy) { path.Add((x, y)); inward.Add((ix, iy)); }
        for (int x = r; x <= w - 1 - r; x++) Add(x, 0, 0, 1);
        for (int i = 1; i < r; i++) Add(w - 1 - r + i, i, -1, 1);
        for (int y = r; y <= h - 1 - r; y++) Add(w - 1, y, -1, 0);
        for (int i = 1; i < r; i++) Add(w - 1 - i, h - 1 - r + i, -1, -1);
        for (int x = w - 1 - r; x >= r; x--) Add(x, h - 1, 0, -1);
        for (int i = 1; i < r; i++) Add(r - i, h - 1 - i, 1, -1);
        for (int y = h - 1 - r; y >= r; y--) Add(0, y, 1, 0);
        for (int i = 1; i < r; i++) Add(i, r - i, 1, 1);
        return new LoopShape([.. path], [.. inward]);
    }

    /// <summary>The loop <paramref name="w"/> pixels wide, centred on the button: two arrows chasing each other round it, their heads
    /// <paramref name="phase"/> pixels round from the top left, a gap ahead of each. The first <paramref name="filled"/> of the
    /// outline, clockwise from the top left, is in <paramref name="lit"/> and the rest in <paramref name="idle"/>: where the heart
    /// fills like a glass, the loop is drawn round.</summary>
    private static void LoopIcon(Surface s, PixelRect button, int w, uint lit, uint idle, double filled, int phase, int clipTop)
    {
        var shape = Loops.GetOrAdd(w, BuildLoop);
        var (path, inward) = shape;
        int n = path.Length, h = HoldBeats.IconHeight(w);
        int x0 = button.X + (int)Math.Floor((button.W - w) / 2.0), y0 = button.Y + (int)Math.Floor((button.H - h) / 2.0);
        int count = (int)Math.Round(n * Math.Clamp(filled, 0, 1));
        int gap = Math.Max(1, n / LoopGapDivisor), barb = w >= LoopLongBarbWidth ? LoopLongBarb : w >= LoopThickCornerWidth ? LoopMidBarb : 1;
        int thick = w >= LoopThickLineWidth ? LoopThickLine : 1;
        int head0 = (phase % n + n) % n, head1 = (head0 + n / 2) % n;
        for (int i = 0; i < n; i++)
        {
            // the pixels just ahead of a head are left out: the space between one arrow's point and the other's tail
            if ((i - head0 - 1 + n) % n < gap || (i - head1 - 1 + n) % n < gap) continue;
            uint colour = i < count ? lit : idle;
            for (int t = 0; t < thick; t++) Put(s, x0 + path[i].X + inward[i].X * t, y0 + path[i].Y + inward[i].Y * t, colour, clipTop);
        }
        // each head is a barb on the inside of the line, longer the further back from the point: the icon stays as wide as its step
        foreach (int head in (ReadOnlySpan<int>)[head0, head1])
        {
            uint colour = head < count ? lit : idle;
            for (int k = 1; k <= barb; k++)
            {
                int j = (head - k + n) % n;
                for (int d = thick; d < thick + k; d++) Put(s, x0 + path[j].X + inward[j].X * d, y0 + path[j].Y + inward[j].Y * d, colour, clipTop);
            }
        }
    }

    /// <summary>A small padlock, drawn where a button's count would be when the player's sign-in may not use the button.</summary>
    public static readonly string[] Lock =
    [
        ".###.",
        "#...#",
        "#####",
        "##.##",
        "#####",
    ];

    /// <summary>Whether this button is drawn locked: the player is signed in, the sign-in may not make this write, and the button is
    /// not lit already.</summary>
    public static bool Locked(RaiderGame g, Card c, PostAction action) => !g.MayWrite(action) && c.Marks.Of(action) == ButtonState.Off;

    /// <summary>The padlock at the count's column, centred on the height of the button's box: it is taller than the action row.</summary>
    private static void DrawLock(Surface s, CardRect button, CardText count, int x0, int y0, int clipTop)
    {
        int lx = x0 + count.Col * CardField.PixelsPerCell;
        int ly = y0 + button.Y * CardField.PixelsPerCell + (button.H * CardField.PixelsPerCell - Lock.Length) / 2;
        for (int y = 0; y < Lock.Length; y++)
            for (int x = 0; x < Lock[y].Length; x++)
                if (Lock[y][x] == '#') Put(s, lx + x, ly + y, CardColours.DimText, clipTop);
    }

    private static void Outline(Surface s, PixelRect b, uint colour, int clipTop)
    {
        for (int x = b.X; x < b.X + b.W; x++) { Put(s, x, b.Y, colour, clipTop); Put(s, x, b.Y + b.H - 1, colour, clipTop); }
        for (int y = b.Y; y < b.Y + b.H; y++) { Put(s, b.X, y, colour, clipTop); Put(s, b.X + b.W - 1, y, colour, clipTop); }
    }

    /// <summary>A soft disc round an icon <paramref name="w"/> wide, laid only where the slab shows: it never tints a picture, a
    /// button's ground or the sky, so it is one colour a step.</summary>
    private static void Halo(Surface s, PixelRect button, int w, uint colour, int clipTop)
    {
        int h = HoldBeats.IconHeight(w);
        double cx = button.X + button.W / 2.0 - 0.5, cy = button.Y + button.H / 2.0 - 0.5;
        double rx = w / 2.0 + HoldBeats.HaloMargin, ry = h / 2.0 + HoldBeats.HaloMargin;
        for (int y = (int)Math.Floor(cy - ry); y <= (int)Math.Ceiling(cy + ry); y++)
            for (int x = (int)Math.Floor(cx - rx); x <= (int)Math.Ceiling(cx + rx); x++)
            {
                double ux = (x - cx) / rx, uy = (y - cy) / ry;
                if (ux * ux + uy * uy <= 1 && y >= clipTop && s.Get(x, y) == CardColours.Slab) s.Set(x, y, colour);
            }
    }

    /// <summary>The icon art scaled to <paramref name="w"/> pixels wide and centred on the button; the bottom <paramref name="filled"/>
    /// of its rows in <paramref name="lit"/>, the rest in <paramref name="idle"/>.</summary>
    private static void Icon(Surface s, PixelRect button, string[] art, int w, uint lit, uint idle, double filled, int clipTop)
    {
        int h = HoldBeats.IconHeight(w);
        int x0 = button.X + (int)Math.Floor((button.W - w) / 2.0), y0 = button.Y + (int)Math.Floor((button.H - h) / 2.0);
        int rows = (int)Math.Round(h * Math.Clamp(filled, 0, 1));
        for (int y = 0; y < h; y++)
        {
            string row = art[y * CardLayout.IconHeightPx / h];
            uint colour = y >= h - rows ? lit : idle;
            for (int x = 0; x < w; x++)
                if (row[x * CardLayout.IconWidthPx / w] == '#') Put(s, x0 + x, y0 + y, colour, clipTop);
        }
    }

    private static void DrawTextRun(Screen scr, uint[] before, uint[] after, int pw, int ph, int col0, int sx, int sy,
        int left, int top, CardText t, uint fg, uint ground, bool own = false)
    {
        int row = top + t.Row, col = left + t.Col;
        if (row < 0 || row >= scr.Rows) return;
        foreach (char ch in t.Text)
        {
            // The game's own marks are measured as the HUD's are: the filter that keeps a post's emoji off the grid would drop the star.
            int w = own ? CellWidth.Of(ch) : CellText.Cells(ch);
            if (ch != ' ' && w > 0)
            {
                bool clear = true;
                for (int k = 0; k < w && clear; k++) clear = IsGroundClear(before, after, pw, ph, col + k - col0, row, sx, sy, ground);
                if (clear) scr.Put(col, row, ch, fg, ground);
            }
            col += w;
        }
    }

    /// <summary>True when the four field pixels the cell (cx, cy) is made from, read as <see cref="QuadComposer"/> reads them, are
    /// <paramref name="ground"/> before and after the world was drawn.</summary>
    private static bool IsGroundClear(uint[] before, uint[] after, int pw, int ph, int cx, int cy, int sx, int sy, uint ground)
    {
        if (cx < 0 || cx >= pw / CardField.PixelsPerCell || cy < 0 || cy >= ph / CardField.PixelsPerCell) return false;
        int x0 = Math.Clamp(cx * CardField.PixelsPerCell - sx, 0, pw - 1), x1 = Math.Clamp(cx * CardField.PixelsPerCell + 1 - sx, 0, pw - 1);
        int y0 = Math.Clamp(cy * CardField.PixelsPerCell - sy, 0, ph - 1), y1 = Math.Clamp(cy * CardField.PixelsPerCell + 1 - sy, 0, ph - 1);
        return IsGround(before, after, y0 * pw + x0, ground) && IsGround(before, after, y0 * pw + x1, ground) &&
               IsGround(before, after, y1 * pw + x0, ground) && IsGround(before, after, y1 * pw + x1, ground);
    }

    private static bool IsGround(uint[] before, uint[] after, int i, uint ground) => before[i] == ground && after[i] == ground;

    private static void Fill(Surface s, int x, int y, int w, int h, uint colour, int clipTop)
    {
        int top = Math.Max(y, clipTop);
        s.Rect(x, top, w, y + h - top, colour);
    }

    private static void Put(Surface s, int x, int y, uint colour, int clipTop)
    {
        if (y >= clipTop) s.Set(x, y, colour);
    }

    private static void DrawAvatar(Surface s, RaiderGame g, Card c, int x0, int y0, int clipTop)
    {
        var a = c.Shape.Avatar;
        var px = g.AvatarOf(c.Post.AuthorDid);
        var picture = px is null ? Discs[DiscIndex(c.Post.AuthorDid)] : Dimmed.GetValue(px, Dim);
        int ax = x0 + a.X * CardField.PixelsPerCell, ay = y0 + a.Y * CardField.PixelsPerCell;
        for (int y = 0; y < AvatarPx; y++)
            for (int x = 0; x < AvatarPx; x++)
                Put(s, ax + x, ay + y, picture[y * AvatarPx + x], clipTop);
    }

    /// <summary>The pictures of the card's post, each in its box: two pixels to a cell each way and dimmed, as the avatar is. A
    /// picture the game holds at another size than the box (the field was resized since it came) is sampled to fit. A box whose
    /// picture has not come is filled with <see cref="CardColours.PictureWaiting"/>.</summary>
    private static void DrawPictures(Surface s, RaiderGame g, Card c, int x0, int y0, int clipTop)
    {
        var boxes = c.Shape.Pictures;
        if (boxes.Count == 0 || c.Post.Pictures is not { } pictures) return;
        for (int i = 0; i < boxes.Count && i < pictures.Count; i++)
        {
            var b = boxes[i];
            int px = x0 + b.X * CardField.PixelsPerCell, py = y0 + b.Y * CardField.PixelsPerCell, w = b.W * CardField.PixelsPerCell, h = b.H * CardField.PixelsPerCell;
            if (py >= s.H || py + h <= clipTop) continue;
            if (g.PictureOf(pictures[i].Thumb) is not { } pic)
            {
                Fill(s, px, py, w, h, CardColours.PictureWaiting, clipTop);
                continue;
            }
            for (int y = Math.Max(0, clipTop - py); y < h && py + y < s.H; y++)
            {
                int row = y * pic.H / h * pic.W;
                for (int x = 0; x < w; x++) s.Set(px + x, py + y, pic.Px[row + x * pic.W / w]);
            }
        }
    }

    /// <summary>A picture pulled toward the slab, and further if it would still be brighter than the cap.</summary>
    internal static uint[] Dim(uint[] px)
    {
        var o = new uint[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            uint p = px[i] & RgbMask;
            double t = CardColours.AvatarDim;
            uint c = Surface.Mix(p, CardColours.Slab, t);
            while (Surface.Luminance(c) > CardColours.MaxIdleLuminance && t < 1)
            {
                t = Math.Min(1, t + DimStep);
                c = Surface.Mix(p, CardColours.Slab, t);
            }
            o[i] = c;
        }
        return o;
    }

    private static int DiscIndex(string did)
    {
        return (int)(Hashing.MixedFnv64(did) % (ulong)DiscColours.Length);
    }

    private static uint[] BakeDisc(int index)
    {
        var o = new uint[AvatarPx * AvatarPx];
        uint ground = Surface.Mix(CardColours.Slab, CardColours.Edge, DiscGroundMix), disc = DiscColours[index];
        double mid = (AvatarPx - 1) / 2.0;
        for (int y = 0; y < AvatarPx; y++)
            for (int x = 0; x < AvatarPx; x++)
            {
                double dx = x - mid, dy = y - mid;
                o[y * AvatarPx + x] = dx * dx + dy * dy <= mid * mid ? disc : ground;
            }
        return o;
    }
}
