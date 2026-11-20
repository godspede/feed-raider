namespace FeedRaider.Engine.Raider;

/// <summary>A rectangle in terminal cells, relative to a card's top left.</summary>
public readonly record struct CardRect(int X, int Y, int W, int H);

/// <summary>A rectangle in field pixels (two pixels to a cell).</summary>
public readonly record struct PixelRect(int X, int Y, int W, int H);

/// <summary>A run of text at a cell, relative to a card's top left.</summary>
public readonly record struct CardText(int Col, int Row, string Text);

/// <summary>Where every part of one card sits, in cells from the card's top left. Pure data from <see cref="CardLayout.Measure"/>.
/// <see cref="Star"/> is the star and like count of a gold card, null on any other. <see cref="Pictures"/> holds one rectangle for
/// each picture of the post, in the post's order. <see cref="LikeIcon"/> and <see cref="RepostIcon"/> are the cells the two icons
/// are drawn at, and <see cref="LikeButton"/> and <see cref="RepostButton"/> the boxes the ship rests on, each larger than its icon.</summary>
public sealed record CardShape(
    int Cols, int Rows, int TextX, int TextWidth,
    CardText Name, CardText? Star, CardText Handle, IReadOnlyList<CardText> Lines, CardText? Note,
    CardText LikeIcon, CardText RepostIcon, CardText LikeCount, CardText RepostCount,
    CardRect Avatar, CardRect LikeButton, CardRect RepostButton, IReadOnlyList<CardRect> Pictures);

/// <summary>The geometry of a post card, in the proportions of a post in the Bluesky app: the avatar, one head row of name and
/// handle, the wrapped text, pictures, the attachment note, and one action row of two icons with their counts.</summary>
public static class CardLayout
{
    /// <summary>A card under this many columns is narrow: its action row is cut in halves and its pictures go two to a row.</summary>
    public const int NarrowBelow = 40;
    public const int AvatarCells = 4;
    /// <summary>The action row is one row of text: an icon is one character, as tall as a letter.</summary>
    public const int ActionRows = 1;
    /// <summary>Rows left clear between the last of the words, pictures or note and the action row. The app leaves less than a line,
    /// which a grid of text rows cannot draw; with none, the counts read as one more line of the words.</summary>
    public const int ActionAirRows = 1;
    /// <summary>The slab's rows above the head row and under the action row.</summary>
    public const int EdgeRows = 1;
    /// <summary>The foot of a card: the clear row, the action row and the slab's last row. It is also the height of the box the ship
    /// rests on, so a still ship stays on a passing button for as long as the hold's timing was tuned for.</summary>
    public const int FooterRows = ActionAirRows + ActionRows + EdgeRows;
    /// <summary>The widest a button's box gets, in cells.</summary>
    public const int ButtonColsMax = 12;
    /// <summary>Cells kept clear between the two boxes: the ship fits between them touching neither.</summary>
    public const int ButtonGapCols = (ShipFactory.W + CardField.PixelsPerCell - 1) / CardField.PixelsPerCell;
    /// <summary>The action row is cut in thirds, as the app's row of reply, repost and like is, where a third leaves a box at least
    /// this wide: 16 pixels, which on a 412 pixel page is about 59 by 47 CSS pixels, a finger's width. Under that it is cut in halves.</summary>
    public const int ThirdsBoxColsMin = 8;
    private const int Thirds = 3, Halves = 2;
    /// <summary>A box starts this many cells left of its icon, so the icon is not on the box's edge.</summary>
    private const int ButtonLeadCols = 1;
    /// <summary>The count starts this many cells after its icon's cell: the icon, one space, the count.</summary>
    public const int CountAfterIcon = 2;
    /// <summary>The column the head row and the words start at: beside the avatar, on a card of any width.</summary>
    public const int TextCol = 7;
    /// <summary>Cells a text line gives up to the card's edges and the avatar.</summary>
    private const int ChromeCols = 8;
    /// <summary>The row of the name and handle, and the row the words start on, right under it.</summary>
    private const int HeadRow = 1, FirstLine = 2;
    /// <summary>The cell the avatar's top left sits at, inside the card's border.</summary>
    private const int AvatarOrigin = 1;
    /// <summary>Cells between the name and the handle on the head row.</summary>
    private const int HandleGap = 1;
    /// <summary>The fewest cells a handle is shown in: an at sign, a letter and the ellipsis. With less room it is left out.</summary>
    private const int HandleColsMin = 3;
    /// <summary>The size in pixels of the heart and loop art a held button grows.</summary>
    public const int IconWidthPx = 7, IconHeightPx = 6;
    /// <summary>The icons of the action row, each one character: the heart of a button that is off, the heart of one that is
    /// marked, asked for or lit, and the repost loop. The feed log draws the same three.</summary>
    public const char HeartOff = '\u2661', HeartOn = '\u2665', LoopMark = '\u21bb';
    /// <summary>The note on the card of an author's own next reply, which a finished like brings in.</summary>
    public const string ThreadNote = "↳ thread";
    /// <summary>What stands before the like count at the right of a gold card's name row.</summary>
    public const string StarMark = "\u2605 ";
    /// <summary>Cells kept clear between a name and the star.</summary>
    private const int StarGap = 1;

    /// <summary>The size cap of a picture, in cells: one picture alone, each of two side by side, and each of three or four.</summary>
    public const int OnePictureCols = 24, OnePictureRows = 8;
    public const int TwoPictureCols = 16, TwoPictureRows = 6;
    public const int ManyPictureCols = 12, ManyPictureRows = 5;
    /// <summary>Cells left clear between two pictures, each way.</summary>
    public const int PictureGap = 1;
    /// <summary>A cell is about twice as tall as it is wide, so a square picture is twice as many cells wide as it is rows high.</summary>
    public const int CellTallness = 2;

    /// <summary>Measures the card for a card <paramref name="cardCols"/> wide. A <paramref name="thread"/> card carries the thread note
    /// in place of the attachment note, on the line under its words: above its pictures, where the note still reads as part of
    /// the words. A gold card's head row ends in the star and the post's like count, and the name and handle give way to them.</summary>
    public static CardShape Measure(FeedPost post, int cardCols, bool thread)
    {
        bool narrow = cardCols < NarrowBelow;
        int textWidth = Math.Max(1, cardCols - ChromeCols);
        string? star = PostRules.Gold(post) ? StarMark + ShortCount(post.LikeCount) : null;
        int headWidth = star is null ? textWidth : Math.Max(1, textWidth - star.Length - StarGap);

        var lines = CellText.Wrap(CellText.Clean(post.Text), textWidth);
        int underWords = FirstLine + lines.Count;
        var pictures = Pictures(post, TextCol, thread ? underWords + 1 : underWords, textWidth, narrow, out int pictureRows);
        string? note = thread ? ThreadNote : NoteText(post, pictures.Count > 0);
        int noteRows = note is null ? 0 : 1;

        var texts = new List<CardText>(lines.Count);
        for (int i = 0; i < lines.Count; i++) texts.Add(new CardText(TextCol, FirstLine + i, lines[i]));
        CardText? noteAt = note is null ? null : new CardText(TextCol, thread ? underWords : underWords + pictureRows, CellText.Clip(note, textWidth, true));

        // never above the avatar's last row: a one-line post's action row is level with the avatar's foot
        int actionRow = Math.Max(AvatarOrigin + AvatarCells - 1, underWords + pictureRows + noteRows + ActionAirRows);
        int rows = actionRow + ActionRows + EdgeRows;

        string display = CellText.Clean(post.DisplayName);
        string handle = "@" + post.AuthorHandle;
        var name = new CardText(TextCol, HeadRow, CellText.Clip(display.Length == 0 ? handle : display, headWidth, false));
        int handleCol = TextCol + CellText.Width(name.Text) + HandleGap, handleRoom = TextCol + headWidth - handleCol;
        var handleAt = new CardText(handleCol, HeadRow, display.Length == 0 || handleRoom < HandleColsMin ? "" : CellText.Clip(handle, handleRoom, true));
        CardText? starAt = star is null ? null : new CardText(Math.Max(TextCol, TextCol + textWidth - star.Length), HeadRow, star);

        int pitch = textWidth / Thirds;
        if (narrow || pitch - ButtonGapCols < ThirdsBoxColsMin) pitch = Math.Max(1, textWidth / Halves);
        int buttonW = Math.Clamp(pitch - ButtonGapCols, 1, ButtonColsMax);
        var likeIcon = new CardText(TextCol, actionRow, HeartOff.ToString());
        var repostIcon = new CardText(TextCol + pitch, actionRow, LoopMark.ToString());
        var like = new CardRect(likeIcon.Col - ButtonLeadCols, actionRow - ActionAirRows, buttonW, FooterRows);
        var repost = new CardRect(repostIcon.Col - ButtonLeadCols, actionRow - ActionAirRows, buttonW, FooterRows);

        return new CardShape(cardCols, rows, TextCol, textWidth, name, starAt, handleAt, texts, noteAt,
            likeIcon, repostIcon, CountAt(likeIcon, ShortCount(post.LikeCount)), CountAt(repostIcon, ShortCount(post.RepostCount)),
            new CardRect(AvatarOrigin, AvatarOrigin, AvatarCells, AvatarCells), like, repost, pictures);
    }

    /// <summary>Where the post's pictures sit, under its words: one alone, two side by side, three or four in one row on a wide card
    /// and two to a row on a narrow one. Each keeps its shape inside its cap, so a wide picture stays wide and a tall one tall, and
    /// the size comes from the shape the post states, never from the picture itself: the card is as tall before the picture arrives
    /// as after. <paramref name="rows"/> is how many rows they take in all.</summary>
    private static IReadOnlyList<CardRect> Pictures(FeedPost post, int x, int y, int width, bool narrow, out int rows)
    {
        rows = 0;
        if (!PostRules.HasPictures(post)) return [];
        var pictures = post.Pictures!;
        int n = Math.Min(pictures.Count, FeedMapper.MaxPictures);
        int perRow = n <= 2 ? n : narrow ? 2 : n;
        int capCols = n == 1 ? OnePictureCols : n == 2 ? TwoPictureCols : ManyPictureCols;
        int capRows = n == 1 ? OnePictureRows : n == 2 ? TwoPictureRows : ManyPictureRows;
        int slot = Math.Max(1, Math.Min(capCols, (width - (perRow - 1) * PictureGap) / perRow));
        var rects = new List<CardRect>(n);
        for (int first = 0; first < n; first += perRow)
        {
            if (first > 0) rows += PictureGap;
            int tallest = 0;
            for (int i = first; i < Math.Min(n, first + perRow); i++)
            {
                var (w, h) = PictureCells(pictures[i], slot, capRows);
                rects.Add(new CardRect(x + (i - first) * (slot + PictureGap), y + rows, w, h));
                tallest = Math.Max(tallest, h);
            }
            rows += tallest;
        }
        return rects;
    }

    /// <summary>The cells a picture takes inside a box <paramref name="maxCols"/> by <paramref name="maxRows"/>: as large as fits
    /// with its shape kept. A picture that does not state its shape is taken as square.</summary>
    public static (int Cols, int Rows) PictureCells(PostPicture picture, int maxCols, int maxRows)
    {
        double aspect = picture.Width > 0 && picture.Height > 0 ? picture.Width / (double)picture.Height : 1;
        int rows = maxRows, cols = (int)Math.Round(CellTallness * aspect * rows);
        if (cols > maxCols)
        {
            cols = maxCols;
            rows = (int)Math.Round(cols / (CellTallness * aspect));
        }
        return (Math.Clamp(cols, 1, maxCols), Math.Clamp(rows, 1, maxRows));
    }

    /// <summary>The short count for a button: <c>999</c>, <c>1.2K</c>, <c>34K</c>, <c>1.2M</c>. Rounds down.</summary>
    public static string ShortCount(int n)
    {
        if (n < 1000) return Math.Max(0, n).ToString();
        if (n < 10_000) return Tenths(n, 1000, "K");
        if (n < 1_000_000) return n / 1000 + "K";
        if (n < 10_000_000) return Tenths(n, 1_000_000, "M");
        return n / 1_000_000 + "M";
    }

    private static string Tenths(int n, int unit, string suffix)
    {
        int tenths = n / (unit / 10);
        return tenths % 10 == 0 ? tenths / 10 + suffix : $"{tenths / 10}.{tenths % 10}{suffix}";
    }

    /// <summary>Where a count sits: on its icon's row, right after the icon.</summary>
    internal static CardText CountAt(CardText icon, string s) => new(icon.Col + CountAfterIcon, icon.Row, s);

    /// <summary>The one-line note of what is attached. A post whose pictures are <paramref name="drawn"/> on the card keeps the note
    /// only for its alt text: the bare <c>[image]</c> would say what the card already shows.</summary>
    internal static string? NoteText(FeedPost p, bool drawn = false)
    {
        string text = CellText.Clean(p.AttachmentText);
        if (drawn && text.Length == 0) return null;
        string Tagged(string tag) => text.Length == 0 ? $"[{tag}]" : $"[{tag}: {text}]";
        return p.Attachment switch
        {
            AttachmentKind.Picture => Tagged("image"),
            AttachmentKind.Video => Tagged("video"),
            AttachmentKind.Link => Tagged("link"),
            AttachmentKind.Quote => "[quote]",
            AttachmentKind.Other => "[attachment]",
            _ => null,
        };
    }
}
