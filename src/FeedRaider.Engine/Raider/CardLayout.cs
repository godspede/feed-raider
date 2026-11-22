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
/// are drawn at, and <see cref="LikeButton"/> and <see cref="RepostButton"/> the boxes the ship rests on, each larger than its icon.
/// <see cref="Media"/> is what <see cref="Pictures"/>, <see cref="Link"/> and <see cref="Video"/> were laid out for, so the painter and the
/// picture asks read the same pictures the boxes were measured from; when it is <see cref="PostRules.CardMedia.Quoted"/> they lie inside
/// <see cref="Quote"/>.</summary>
public sealed record CardShape(
    int Cols, int Rows, int TextX, int TextWidth,
    CardText Name, CardText? Star, CardText Handle, IReadOnlyList<CardText> Lines, CardText? Note,
    CardText LikeIcon, CardText RepostIcon, CardText LikeCount, CardText RepostCount,
    CardRect Avatar, CardRect LikeButton, CardRect RepostButton, IReadOnlyList<CardRect> Pictures,
    LinkBox? Link = null, VideoBox? Video = null, PostRules.CardMedia Media = default, QuoteBox? Quote = null);

/// <summary>A quoted post on a card, in cells from the card's top left. The edge runs round the outer pixels of <see cref="Box"/>, and inside
/// it stand either the quoted name and handle (<see cref="Head"/>), up to a few lines of the quoted words and a <c>[quote]</c> note for a
/// quote inside the quoted post, or, for a quoted post that cannot be shown, one centred <see cref="Notice"/> in Bluesky's own words. The
/// quoted post's media, when the card shows them, are the card's <see cref="CardShape.Pictures"/>, <see cref="CardShape.Link"/> or
/// <see cref="CardShape.Video"/>, laid inside the box.</summary>
public sealed record QuoteBox(CardRect Box, QuoteState State, CardText? Head, IReadOnlyList<CardText> Lines, CardText? Note, CardText? Notice)
{
    /// <summary>The quoted post is gone (deleted, blocked or removed by its author): its notice becomes mines.</summary>
    public bool Dead => State is QuoteState.Deleted or QuoteState.Blocked or QuoteState.Detached;
}

/// <summary>A link card on a card, in cells from the card's top left: its slab, its thumbnail's box (null when the link has none), up to two
/// rows of title, the host, and on a wide card one row of description (null when there is none or no room).</summary>
public sealed record LinkBox(CardRect Box, CardRect? Thumb, IReadOnlyList<CardText> Title, CardText Host, CardText? Description);

/// <summary>A video on a card, in cells from the card's top left: its thumbnail's box, and the mark in that box's bottom left corner with
/// the play sign at its middle.</summary>
public sealed record VideoBox(CardRect Picture, CardRect Mark, CardText Play);

/// <summary>Where one set of media sits (see <see cref="CardLayout.MediaAt"/>): the pictures, the link card or the video, and the rows they take.</summary>
public sealed record MediaLayout(IReadOnlyList<CardRect> Pictures, LinkBox? Link, VideoBox? Video, int Rows)
{
    public static readonly MediaLayout None = new([], null, null, 0);
}

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
    /// <summary>The foot of a card: the clear row, the action row and the slab's last row. It is also the height of a
    /// button's box.</summary>
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
    /// <summary>The size in pixels of the heart and loop art a pressed button grows.</summary>
    public const int IconWidthPx = 7, IconHeightPx = 6;
    /// <summary>The icons of the action row, each one character: the heart of a button that is off, the heart of one that is
    /// marked, asked for or lit, and the repost loop. The feed log draws the same three.</summary>
    public const char HeartOff = '\u2661', HeartOn = '\u2665', LoopMark = '\u21bb';
    /// <summary>The note on the card of an author's own next reply, which a like brings in.</summary>
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

    /// <summary>A link card's thumbnail beside its words on a wide card: the size of the app's, and as tall as the title, host and description it stands by.</summary>
    public const int LinkThumbCols = 12, LinkThumbRows = 5;
    /// <summary>The cap of a link card's thumbnail across the top of its box on a narrow card, where there is no room beside it.</summary>
    public const int NarrowLinkThumbCols = 16, NarrowLinkThumbRows = 6;
    /// <summary>The shape a link card's thumbnail is taken as, width over height: Open Graph's 1200 by 630, the shape almost every site gives,
    /// since the view states none. The box is sized from it, so the card does not change height when the picture comes.</summary>
    public const double LinkThumbAspect = 1200 / 630.0;
    /// <summary>The most rows a link card's title takes; a longer one ends in an ellipsis.</summary>
    public const int LinkTitleRows = 2;
    /// <summary>Cells between a link card's slab and its words, and between a thumbnail and the words beside it.</summary>
    public const int LinkInsetCols = 1;
    /// <summary>The slab's last row, under a link card's words, so the next line does not read as part of the card.</summary>
    public const int LinkEdgeRows = 1;
    /// <summary>The fewest cells of words beside a thumbnail; a box with less room puts its thumbnail on top, as on a narrow card.</summary>
    private const int LinkWordsColsMin = 12;
    /// <summary>The shape a video is taken as when the post states none: 16:9, the shape of almost every video.</summary>
    public const double VideoAspectDefault = 16 / 9.0;
    /// <summary>The side of the video mark, in cells: room for the play sign with a cell of dark around it.</summary>
    public const int VideoMarkCells = 3;
    /// <summary>The play sign on a video's mark.</summary>
    public const char PlayMark = '▶';

    /// <summary>The cells the quote box's edge takes on each side: the edge is the cell's outer pixel, and its inner pixel keeps the edge off
    /// the words.</summary>
    public const int QuoteEdgeCells = 1;
    /// <summary>The most lines of the quoted words on a wide card and on a narrow one; a longer quote ends in an ellipsis. A quote is read
    /// in passing, and a narrow card wraps to more lines for the same words, so it keeps fewer.</summary>
    public const int QuoteLinesWide = 4, QuoteLinesNarrow = 3;
    /// <summary>Bluesky's own notices for a quoted post it will not show (social-app, <c>src/components/Post/Embed/index.tsx</c>), the
    /// slashed circle standing for the app's icon, and the game's for a quoted post the feed's rules hide.</summary>
    public const string DeletedNotice = "⊘  Deleted", BlockedNotice = "⊘  Blocked", DetachedNotice = "Removed by author", HiddenNotice = "Hidden";
    /// <summary>The note of a quote the card does not draw: one inside a quoted post, and a post whose quote did not come with the view.</summary>
    public const string QuoteNote = "[quote]";

    /// <summary>Measures the card for a card <paramref name="cardCols"/> wide. A <paramref name="thread"/> card carries the thread note
    /// in place of the attachment note, on the line under its words: above its pictures, where the note still reads as part of
    /// the words. A gold card's head row ends in the star and the post's like count, and the name and handle give way to them. A quote
    /// stands in its box under the words, the post's own media and the note; for a viewer who is <paramref name="signedOut"/> a quoted
    /// author who opted out of signed-out viewers is hidden.</summary>
    public static CardShape Measure(FeedPost post, int cardCols, bool thread, bool signedOut = true)
    {
        bool narrow = cardCols < NarrowBelow;
        int textWidth = Math.Max(1, cardCols - ChromeCols);
        string? star = PostRules.Gold(post) ? StarMark + ShortCount(post.LikeCount) : null;
        int headWidth = star is null ? textWidth : Math.Max(1, textWidth - star.Length - StarGap);

        var lines = CellText.Wrap(CellText.Clean(post.Text), textWidth);
        int underWords = FirstLine + lines.Count;
        // a quote's media belong in its quote box, which lays them out itself
        var shown = PostRules.MediaShown(post);
        var laid = shown.Quoted ? MediaLayout.None : MediaAt(shown, TextCol, thread ? underWords + 1 : underWords, textWidth, narrow);
        int pictureRows = laid.Rows;
        // a quote that is read draws its box, which says all the QuoteNote would
        string? note = thread ? ThreadNote : post.Quote is null ? NoteText(post, laid.Rows > 0) : null;
        int noteRows = note is null ? 0 : 1;

        var texts = new List<CardText>(lines.Count);
        for (int i = 0; i < lines.Count; i++) texts.Add(new CardText(TextCol, FirstLine + i, lines[i]));
        CardText? noteAt = note is null ? null : new CardText(TextCol, thread ? underWords : underWords + pictureRows, CellText.Clip(note, textWidth, true));

        QuoteBox? quote = null;
        int quoteRows = 0;
        if (!thread && post.Quote is { } q)
        {
            var state = PostRules.QuoteStateFor(q, signedOut);
            (quote, var inner) = QuoteAt(q, state, state == QuoteState.Shown && shown.Quoted ? shown : default, TextCol, underWords + pictureRows + noteRows, textWidth, narrow);
            if (inner.Rows > 0) laid = inner;
            quoteRows = quote.Box.H;
        }
        var media = laid.Rows > 0 ? shown : default;

        // never above the avatar's last row: a one-line post's action row is level with the avatar's foot
        int actionRow = Math.Max(AvatarOrigin + AvatarCells - 1, underWords + pictureRows + noteRows + quoteRows + ActionAirRows);
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
            new CardRect(AvatarOrigin, AvatarOrigin, AvatarCells, AvatarCells), like, repost, laid.Pictures, laid.Link, laid.Video, media, quote);
    }

    /// <summary>The quote box with its top left at (<paramref name="x"/>, <paramref name="y"/>), <paramref name="width"/> cells wide, and the
    /// quoted post's <paramref name="media"/> laid inside it (none when the quote is not shown, or the quoting post has media of its own).
    /// A shown quote is the head row, its words cut to <see cref="QuoteLinesWide"/> or <see cref="QuoteLinesNarrow"/> lines, the media and
    /// the note of a quote inside it; any other is one row, its notice centred.</summary>
    private static (QuoteBox Box, MediaLayout Media) QuoteAt(PostQuote q, QuoteState state, PostRules.CardMedia media, int x, int y, int width, bool narrow)
    {
        int ix = x + QuoteEdgeCells, iw = Math.Max(1, width - 2 * QuoteEdgeCells), row = y + QuoteEdgeCells;
        if (state != QuoteState.Shown)
        {
            string notice = CellText.Clip(Notice(q, state), iw, true);
            var at = new CardText(ix + Math.Max(0, (iw - CellText.Width(notice)) / 2), row, notice);
            return (new QuoteBox(new CardRect(x, y, width, 1 + 2 * QuoteEdgeCells), state, null, [], null, at), MediaLayout.None);
        }
        string name = CellText.Clean(q.AuthorName), handle = q.AuthorHandle is { Length: > 0 } h ? "@" + h : "";
        var head = new CardText(ix, row++, CellText.Clip(name.Length == 0 ? handle : handle.Length == 0 ? name : name + " " + handle, iw, true));
        var words = CutLines(CellText.Wrap(CellText.Clean(q.Text), iw), narrow ? QuoteLinesNarrow : QuoteLinesWide, iw);
        var lines = new List<CardText>(words.Count);
        foreach (var w in words) lines.Add(new CardText(ix, row++, w));
        var laid = MediaAt(media, ix, row, iw, narrow);
        row += laid.Rows;
        CardText? note = q.QuotesAnother ? new CardText(ix, row++, QuoteNote) : null;
        return (new QuoteBox(new CardRect(x, y, width, row + QuoteEdgeCells - y), state, head, lines, note, null), laid);
    }

    /// <summary>The words a quote box shows where the quoted post is not: Bluesky's notice for one that is gone, the label of a record that is
    /// not a post (a feed, a list), and the game's own for one the feed's rules hide.</summary>
    internal static string Notice(PostQuote q, QuoteState state) => state switch
    {
        QuoteState.Deleted => DeletedNotice,
        QuoteState.Blocked => BlockedNotice,
        QuoteState.Detached => DetachedNotice,
        QuoteState.Other => q.OtherLabel ?? QuoteNote,
        _ => HiddenNotice,
    };

    /// <summary>The first <paramref name="max"/> of the wrapped lines, the last cut with an ellipsis when more were left out: joined to
    /// the next line it is always wider than the width, for the wrap broke there.</summary>
    private static List<string> CutLines(List<string> wrapped, int max, int width)
    {
        if (wrapped.Count <= max) return wrapped;
        var kept = wrapped.GetRange(0, max);
        kept[^1] = CellText.Clip(kept[^1] + " " + wrapped[max], width, true);
        return kept;
    }

    /// <summary>Where one set of media sits with its top left at (<paramref name="x"/>, <paramref name="y"/>) in a column
    /// <paramref name="width"/> cells wide: the pictures, else the link card, else the video. A quote box calls it for the quoted post's
    /// media at its own inner corner. Every size comes from what the view states, never from a picture, so the rows are the same
    /// before the pictures come as after.</summary>
    public static MediaLayout MediaAt(PostRules.CardMedia media, int x, int y, int width, bool narrow)
    {
        if (media.Pictures is { Count: > 0 } pictures)
        {
            var rects = Pictures(pictures, x, y, width, narrow, out int rows);
            return new MediaLayout(rects, null, null, rows);
        }
        if (media.Link is { } link)
        {
            var box = LinkAt(link, x, y, width, narrow);
            return new MediaLayout([], box, null, box.Box.H);
        }
        if (media.Video is { } video)
        {
            var box = VideoAt(video, x, y, width, narrow);
            return new MediaLayout([], null, box, box.Picture.H);
        }
        return MediaLayout.None;
    }

    /// <summary>A link card's box, the full width of the column. Beside a thumbnail on a wide card: the title (two rows at most), the host
    /// and one row of description, as tall as the thumbnail. On a narrow card the thumbnail fills the top at the Open Graph shape, with the
    /// title and host under it. With no thumbnail it is the title and the host.</summary>
    private static LinkBox LinkAt(LinkCard link, int x, int y, int width, bool narrow)
    {
        int inner = Math.Max(1, width - 2 * LinkInsetCols);
        if (link.ThumbUrl is null)
        {
            var title = new CardText(x + LinkInsetCols, y, CellText.Clip(link.Title, inner, true));
            var host = new CardText(x + LinkInsetCols, y + 1, CellText.Clip(link.Host, inner, true));
            return new LinkBox(new CardRect(x, y, width, 2 + LinkEdgeRows), null, [title], host, null);
        }
        int beside = width - LinkThumbCols - 2 * LinkInsetCols;
        if (!narrow && beside >= LinkWordsColsMin)
        {
            var thumb = new CardRect(x, y, LinkThumbCols, LinkThumbRows);
            int tx = x + LinkThumbCols + LinkInsetCols;
            var title = TitleLines(link.Title, tx, y, beside);
            var host = new CardText(tx, y + title.Count, CellText.Clip(link.Host, beside, true));
            string description = CellText.Clean(link.Description);
            CardText? about = description.Length == 0 || host.Row + 1 >= y + LinkThumbRows ? null
                : new CardText(tx, host.Row + 1, CellText.Clip(description, beside, true));
            return new LinkBox(new CardRect(x, y, width, LinkThumbRows + LinkEdgeRows), thumb, title, host, about);
        }
        var (cols, rows) = Fit(LinkThumbAspect, Math.Min(width, NarrowLinkThumbCols), NarrowLinkThumbRows);
        var top = new CardRect(x, y, cols, rows);
        var under = TitleLines(link.Title, x + LinkInsetCols, y + rows, inner);
        var hostUnder = new CardText(x + LinkInsetCols, y + rows + under.Count, CellText.Clip(link.Host, inner, true));
        return new LinkBox(new CardRect(x, y, width, rows + under.Count + 1 + LinkEdgeRows), top, under, hostUnder, null);
    }

    /// <summary>A link card's title wrapped to <paramref name="width"/>, at most <see cref="LinkTitleRows"/> rows, the last cut with an ellipsis.</summary>
    private static List<CardText> TitleLines(string title, int x, int y, int width)
    {
        var wrapped = CutLines(CellText.Wrap(CellText.Clean(title), width), LinkTitleRows, width);
        if (wrapped.Count == 0) wrapped.Add("");
        var lines = new List<CardText>(wrapped.Count);
        for (int i = 0; i < wrapped.Count; i++) lines.Add(new CardText(x, y + i, wrapped[i]));
        return lines;
    }

    /// <summary>A video's box, in the shape the post states or <see cref="VideoAspectDefault"/>: inside the cap of a lone picture on a wide
    /// card, and of one of two pictures on a narrow one, where a lone picture's cap would leave the words a short field.</summary>
    private static VideoBox VideoAt(VideoRef video, int x, int y, int width, bool narrow)
    {
        int capCols = Math.Min(width, narrow ? TwoPictureCols : OnePictureCols), capRows = narrow ? TwoPictureRows : OnePictureRows;
        var (cols, rows) = Fit(video.AspectRatio is > 0 and var a ? a : VideoAspectDefault, capCols, capRows);
        var picture = new CardRect(x, y, cols, rows);
        int side = Math.Min(VideoMarkCells, Math.Min(cols, rows));
        var mark = new CardRect(x, y + rows - side, side, side);
        return new VideoBox(picture, mark, new CardText(mark.X + side / 2, mark.Y + side / 2, PlayMark.ToString()));
    }

    /// <summary>Where the post's pictures sit, under its words: one alone, two side by side, three or four in one row on a wide card
    /// and two to a row on a narrow one. Each keeps its shape inside its cap, so a wide picture stays wide and a tall one tall, and
    /// the size comes from the shape the post states, never from the picture itself: the card is as tall before the picture arrives
    /// as after. <paramref name="rows"/> is how many rows they take in all.</summary>
    private static IReadOnlyList<CardRect> Pictures(IReadOnlyList<PostPicture> pictures, int x, int y, int width, bool narrow, out int rows)
    {
        rows = 0;
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
    public static (int Cols, int Rows) PictureCells(PostPicture picture, int maxCols, int maxRows) =>
        Fit(picture.Width > 0 && picture.Height > 0 ? picture.Width / (double)picture.Height : 1, maxCols, maxRows);

    /// <summary>The cells a picture of shape <paramref name="aspect"/> (width over height) takes inside a box <paramref name="maxCols"/> by
    /// <paramref name="maxRows"/>: as large as fits with its shape kept.</summary>
    public static (int Cols, int Rows) Fit(double aspect, int maxCols, int maxRows)
    {
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

    /// <summary>The one-line note of what is attached. A post whose pictures or video are <paramref name="drawn"/> on the card keeps the note
    /// only for its alt text: the bare <c>[image]</c> would say what the card already shows. A drawn link card has none, for its box
    /// shows the title and host the note would. A quote keeps its note, since its own media are drawn but the quote is not.</summary>
    internal static string? NoteText(FeedPost p, bool drawn = false)
    {
        string text = CellText.Clean(p.AttachmentText);
        if (drawn && p.Attachment != AttachmentKind.Quote && (text.Length == 0 || p.Attachment == AttachmentKind.Link)) return null;
        string Tagged(string tag) => text.Length == 0 ? $"[{tag}]" : $"[{tag}: {text}]";
        return p.Attachment switch
        {
            AttachmentKind.Picture => Tagged("image"),
            AttachmentKind.Video => Tagged("video"),
            AttachmentKind.Link => Tagged("link"),
            AttachmentKind.Quote => QuoteNote,
            AttachmentKind.Other => "[attachment]",
            _ => null,
        };
    }
}
