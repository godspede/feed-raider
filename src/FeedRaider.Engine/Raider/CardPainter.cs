using System.Runtime.CompilerServices;

namespace FeedRaider.Engine.Raider;

/// <summary>The colours of a card in the background. Everything here is dim on purpose: the fight draws over it, and no idle colour
/// is brighter than <see cref="MaxIdleLuminance"/> so the ships, shots, bolts and text always stand out of it.</summary>
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
    /// <summary>A button pressed while signed out: the line under its icon is in the lit colour at half strength, since nothing was sent.</summary>
    public static readonly uint LikeMarked = Surface.Mix(Slab, LikeLit, 0.5), RepostMarked = Surface.Mix(Slab, RepostLit, 0.5);
    public const double MaxIdleLuminance = 0.40;
    /// <summary>How far toward the slab a picture is mixed, and how far toward its icon a lit button's ground is.</summary>
    public const double AvatarDim = 0.35, ButtonTint = 0.35;
    /// <summary>The box of a post's picture that has not come, or never will: the edge colour, one the card has already.</summary>
    public const uint PictureWaiting = Edge;

    // An idle button has no ground of its own, only its icon: every extra colour behind the fight is one more pair for the terminal's
    // glyph atlas wherever a shot or a bolt crosses it (a tinted ground cost about 650 entries in a 60 second run).
    public static readonly uint LikeButtonLit = Surface.Mix(Slab, LikeLit, ButtonTint), RepostButtonLit = Surface.Mix(Slab, RepostLit, ButtonTint);

    /// <summary>The words of a card. Each reads on the slab at 4.5 to 1 or better, and is dim enough to stay under
    /// <see cref="MaxIdleLuminance"/>. Three fixed colours, not a gradient: each (letter, colour, ground) is one atlas entry.</summary>
    public const uint NameText = 0x9aa5ba, PostText = 0x8b95a9, DimText = 0x7a859b;

    /// <summary>A count on a lit button's ground, which is lighter than the slab: the lit icon colour made legible on it (4.5 to 1).</summary>
    public static readonly uint LikeCountLit = Surface.Legible(LikeLit, LikeButtonLit), RepostCountLit = Surface.Legible(RepostLit, RepostButtonLit);

    // The link card and the video mark are painted in the card's own colours. A colour of their own would be one more the encoder holds
    // exactly, and each near colour of the sky it took in cost about 100 atlas entries in a 60 second run with no link or video on it.

    /// <summary>A link card's slab: the edge colour, a step lighter than the card's slab, so the box reads as one thing inside the card.</summary>
    public const uint LinkSlab = Edge;
    /// <summary>A quote box's outline: the card's own edge colour, so the box costs the atlas no colour, and its words stay on the slab.</summary>
    public const uint QuoteEdge = Edge;
    /// <summary>A link card's thumbnail that has not come, or never will: the card's slab, a dark slot in the lighter link box.</summary>
    public const uint LinkThumbWaiting = Slab;
    /// <summary>A link card's title, and its host and description: the card's name and dim colours made legible on the link slab.</summary>
    public static readonly uint LinkTitle = Surface.Legible(NameText, LinkSlab), LinkDim = Surface.Legible(DimText, LinkSlab);
    /// <summary>The video mark's ground: the card's slab, darker than a picture's waiting box and dimmer than any dimmed thumbnail's light parts.</summary>
    public const uint VideoMark = Slab;
    /// <summary>The play sign on the video mark: the name colour, the brightest of the card's words.</summary>
    public const uint VideoPlay = NameText;

    /// <summary>Every colour a card is painted in while idle, each once: none is brighter than <see cref="MaxIdleLuminance"/>. The link
    /// card's text colours are the card's own where those already read on the link slab, so they are listed once.</summary>
    public static readonly uint[] Idle = [.. new[] { Slab, Edge, GoldEdge, LikeIdle, RepostIdle, NameText, PostText, DimText, GoldText, LikeIcon, RepostIcon, LinkTitle, LinkDim }.Distinct()];

    /// <summary>Every fixed colour of a card, for the encoder to hold exactly (see <see cref="ColourSnap.Prime"/>).</summary>
    public static readonly uint[] All = [.. Idle, LikeLit, RepostLit, LikeButtonLit, RepostButtonLit, LikeCountLit, RepostCountLit, LikeMarked, RepostMarked];
}

/// <summary>Draws the posts of the feed into the background pass of the field: each card's slab, its top edge, its avatar, the
/// pictures of its post and its two buttons. The layer is under the units, the shots, the bolts and the text.</summary>
public static class CardPainter
{
    /// <summary>Pixel rows 0 to 3 of the screen belong to the HUD; the cards never paint there.</summary>
    public const int HudPixelRows = 4;
    /// <summary>The avatar's side in pixels.</summary>
    private const int AvatarPx = CardLayout.AvatarCells * CardField.PixelsPerCell;

    /// <summary>The heart and the repost loop a pressed button grows, centred on the button's box. A button at rest draws no art: its
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
            DrawQuote(s, c, x0, y0, clipTop);
            DrawPictures(s, g, c, x0, y0, clipTop);
            DrawLink(s, g, c, x0, y0, clipTop);
            DrawVideo(s, g, c, x0, y0, clipTop);
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
    /// (a ship, a shot, a bolt, a pickup, a ring, a particle) changes a pixel, and a cell the HUD cuts off the slab is not the slab. A wide
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
            if (s.Link is { } link)
            {
                foreach (var l in link.Title) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, l, CardColours.LinkTitle, CardColours.LinkSlab);
                DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, link.Host, CardColours.LinkDim, CardColours.LinkSlab);
                if (link.Description is { } d) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, d, CardColours.LinkDim, CardColours.LinkSlab);
            }
            if (s.Video is { } video) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, video.Play, CardColours.VideoPlay, CardColours.VideoMark, own: true);
            if (s.Quote is { } quote)
            {
                if (quote.Head is { } head) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, head, CardColours.DimText, CardColours.Slab);
                foreach (var l in quote.Lines) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, l, CardColours.PostText, CardColours.Slab);
                if (quote.Note is { } qn) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, qn, CardColours.DimText, CardColours.Slab);
                if (quote.Notice is { } notice) DrawTextRun(scr, before, after, pw, ph, col0, sx, sy, left, top, notice, CardColours.DimText, CardColours.Slab, own: true);
            }
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

    /// <summary>What a press shows on the cards, drawn over the slabs and under every unit: while the keyboard was the last device,
    /// an outline round both buttons of the card the like and repost keys would act on (<see cref="RaiderGame.KeyCard"/>; none on the start page's tour, whose pilot clicks); and for
    /// the frames of a press the icon drawn at its widest (<see cref="PressLook.MaxIconHeight"/> tall, the footer) and near white
    /// in a halo, then falling back. A pressed repost's loop turns once as it does.</summary>
    public static void DrawPressGlow(Surface s, RaiderGame g, int dx, int dy, int clipTop)
    {
        var target = g.Input.PointerLast || g.Charmed ? null : g.KeyCard;
        foreach (var c in g.Cards.Cards)
            foreach (var a in (ReadOnlySpan<PostAction>)[PostAction.Like, PostAction.Repost])
            {
                long sincePress = g.LastPress.Since(c.Marks, a, g.Frame);
                int settle = (int)sincePress - PressLook.HitStop - 1;
                bool flash = sincePress <= PressLook.HitStop, settling = settle >= 0 && settle < PressLook.SettleWidths.Length;
                bool aimed = ReferenceEquals(target, c);
                if (!flash && !settling && !aimed) continue;
                var box = g.Cards.ButtonBox(c, a);
                box = new PixelRect(box.X + dx, box.Y + dy, box.W, box.H);
                if (aimed) Outline(s, box, PressLook.Dim(a), clipTop);
                string[] art = a == PostAction.Like ? Heart : Loop;
                bool loop = a == PostAction.Repost;
                if (flash)
                {
                    Halo(s, box, PressLook.MaxIconWidth, PressLook.HaloColour(a), clipTop);
                    if (loop) LoopIcon(s, box, PressLook.MaxIconWidth, PressLook.FlashColour(a), SpinPhase(PressLook.MaxIconWidth, sincePress), clipTop);
                    else Icon(s, box, art, PressLook.MaxIconWidth, PressLook.FlashColour(a), clipTop);
                }
                else if (settling)
                {
                    int sw = PressLook.SettleWidths[settle];
                    if (loop) LoopIcon(s, box, sw, PressLook.Lit(a), SpinPhase(sw, sincePress), clipTop);
                    else Icon(s, box, art, sw, PressLook.Lit(a), clipTop);
                }
            }
    }

    /// <summary>The loop of a pressed repost is not the button's small picture scaled up but drawn for its size: the outline of a
    /// rounded box <paramref name="w"/> pixels wide, as the pixels of one walk round it, clockwise from the top left.</summary>
    public static (int X, int Y)[] LoopPath(int w) => Loops.GetOrAdd(w, BuildLoop).Path;

    /// <summary>A pressed repost spins its loop once: how far round the arrowheads are this many frames after the press.</summary>
    public static int SpinPhase(int w, long sinceDone) =>
        (int)Math.Round(LoopPath(w).Length * Math.Clamp(sinceDone, 0, SpinFrames) / (double)SpinFrames);

    /// <summary>The frames a pressed repost's loop takes to turn once.</summary>
    public const int SpinFrames = 6;

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
        int h = PressLook.IconHeight(w);
        var path = new List<(int, int)>();
        var inward = new List<(int, int)>();
        void Add(int x, int y, int ix, int iy) { path.Add((x, y)); inward.Add((ix, iy)); }
        // the corners are cut one pixel deep
        for (int x = 1; x <= w - 2; x++) Add(x, 0, 0, 1);
        for (int y = 1; y <= h - 2; y++) Add(w - 1, y, -1, 0);
        for (int x = w - 2; x >= 1; x--) Add(x, h - 1, 0, -1);
        for (int y = h - 2; y >= 1; y--) Add(0, y, 1, 0);
        return new LoopShape([.. path], [.. inward]);
    }

    /// <summary>The loop <paramref name="w"/> pixels wide, centred on the button, in <paramref name="colour"/>: two arrows chasing
    /// each other round it, their heads <paramref name="phase"/> pixels round from the top left, a gap ahead of each.</summary>
    private static void LoopIcon(Surface s, PixelRect button, int w, uint colour, int phase, int clipTop)
    {
        var shape = Loops.GetOrAdd(w, BuildLoop);
        var (path, inward) = shape;
        int n = path.Length, h = PressLook.IconHeight(w);
        int x0 = button.X + (int)Math.Floor((button.W - w) / 2.0), y0 = button.Y + (int)Math.Floor((button.H - h) / 2.0);
        int gap = Math.Max(1, n / LoopGapDivisor);
        int head0 = (phase % n + n) % n, head1 = (head0 + n / 2) % n;
        for (int i = 0; i < n; i++)
        {
            // the pixels just ahead of a head are left out: the space between one arrow's point and the other's tail
            if ((i - head0 - 1 + n) % n < gap || (i - head1 - 1 + n) % n < gap) continue;
            Put(s, x0 + path[i].X, y0 + path[i].Y, colour, clipTop);
        }
        // each head is a one-pixel barb on the inside of the line, a pixel back from the point: the icon stays as wide as its step
        foreach (int head in (ReadOnlySpan<int>)[head0, head1])
        {
            int j = (head - 1 + n) % n;
            Put(s, x0 + path[j].X + inward[j].X, y0 + path[j].Y + inward[j].Y, colour, clipTop);
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
    /// button's ground or the sky, so it is one colour.</summary>
    private static void Halo(Surface s, PixelRect button, int w, uint colour, int clipTop)
    {
        int h = PressLook.IconHeight(w);
        double cx = button.X + button.W / 2.0 - 0.5, cy = button.Y + button.H / 2.0 - 0.5;
        double rx = w / 2.0 + PressLook.HaloMargin, ry = Math.Min(h / 2.0 + PressLook.HaloMargin, button.H / 2.0);
        for (int y = (int)Math.Floor(cy - ry); y <= (int)Math.Ceiling(cy + ry); y++)
            for (int x = (int)Math.Floor(cx - rx); x <= (int)Math.Ceiling(cx + rx); x++)
            {
                double ux = (x - cx) / rx, uy = (y - cy) / ry;
                if (ux * ux + uy * uy <= 1 && y >= clipTop && s.Get(x, y) == CardColours.Slab) s.Set(x, y, colour);
            }
    }

    /// <summary>The icon art scaled to <paramref name="w"/> pixels wide and centred on the button, in <paramref name="colour"/>.</summary>
    private static void Icon(Surface s, PixelRect button, string[] art, int w, uint colour, int clipTop)
    {
        int h = PressLook.IconHeight(w);
        int x0 = button.X + (int)Math.Floor((button.W - w) / 2.0), y0 = button.Y + (int)Math.Floor((button.H - h) / 2.0);
        for (int y = 0; y < h; y++)
        {
            string row = art[y * CardLayout.IconHeightPx / h];
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
        if (boxes.Count == 0 || c.Shape.Media.Pictures is not { } pictures) return;
        for (int i = 0; i < boxes.Count && i < pictures.Count; i++) DrawPicture(s, g, pictures[i].Thumb, boxes[i], x0, y0, clipTop);
    }

    /// <summary>The quote box's one-pixel outline, on the outer pixels of its border cells; its words are cells, written by <see cref="DrawText"/>.</summary>
    private static void DrawQuote(Surface s, Card c, int x0, int y0, int clipTop)
    {
        if (c.Shape.Quote is not { } quote) return;
        const int px = CardField.PixelsPerCell;
        var b = quote.Box;
        int x = x0 + b.X * px, y = y0 + b.Y * px, w = b.W * px, h = b.H * px;
        Fill(s, x, y, w, 1, CardColours.QuoteEdge, clipTop);
        Fill(s, x, y + h - 1, w, 1, CardColours.QuoteEdge, clipTop);
        Fill(s, x, y, 1, h, CardColours.QuoteEdge, clipTop);
        Fill(s, x + w - 1, y, 1, h, CardColours.QuoteEdge, clipTop);
    }

    /// <summary>The link card's slab, and its thumbnail drawn as a picture is, in <see cref="CardColours.LinkThumbWaiting"/> until it comes.
    /// Its words are cells, written by <see cref="DrawText"/>.</summary>
    private static void DrawLink(Surface s, RaiderGame g, Card c, int x0, int y0, int clipTop)
    {
        if (c.Shape.Link is not { } link) return;
        var b = link.Box;
        Fill(s, x0 + b.X * CardField.PixelsPerCell, y0 + b.Y * CardField.PixelsPerCell, b.W * CardField.PixelsPerCell, b.H * CardField.PixelsPerCell, CardColours.LinkSlab, clipTop);
        if (link.Thumb is { } thumb && c.Shape.Media.Link?.ThumbUrl is { } url) DrawPicture(s, g, url, thumb, x0, y0, clipTop, CardColours.LinkThumbWaiting);
    }

    /// <summary>The video's thumbnail drawn as a picture is, with the dark mark over its bottom left corner whether or not the picture
    /// has come, so a video reads as one before it arrives, and when it never does. The play sign is a cell, written by <see cref="DrawText"/>.</summary>
    private static void DrawVideo(Surface s, RaiderGame g, Card c, int x0, int y0, int clipTop)
    {
        if (c.Shape.Video is not { } box || c.Shape.Media.Video is not { } video) return;
        DrawPicture(s, g, RaiderGame.VideoKey(video), box.Picture, x0, y0, clipTop);
        var m = box.Mark;
        Fill(s, x0 + m.X * CardField.PixelsPerCell, y0 + m.Y * CardField.PixelsPerCell, m.W * CardField.PixelsPerCell, m.H * CardField.PixelsPerCell, CardColours.VideoMark, clipTop);
    }

    private static void DrawPicture(Surface s, RaiderGame g, string key, CardRect b, int x0, int y0, int clipTop, uint waiting = CardColours.PictureWaiting)
    {
        int px = x0 + b.X * CardField.PixelsPerCell, py = y0 + b.Y * CardField.PixelsPerCell, w = b.W * CardField.PixelsPerCell, h = b.H * CardField.PixelsPerCell;
        if (py >= s.H || py + h <= clipTop) return;
        if (g.PictureOf(key) is not { } pic)
        {
            Fill(s, px, py, w, h, waiting, clipTop);
            return;
        }
        for (int y = Math.Max(0, clipTop - py); y < h && py + y < s.H; y++)
        {
            int row = y * pic.H / h * pic.W;
            for (int x = 0; x < w; x++) s.Set(px + x, py + y, pic.Px[row + x * pic.W / w]);
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
