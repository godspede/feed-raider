using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// A quoted post on its card: where the quote box stands on a wide and a narrow card, how the quoted words are cut, the notice of a
// quote that cannot be shown, the quoted media inside the box, and that the card's height is set when it is measured.
public class QuoteCardTests
{
    private const int WideCols = 60, NarrowCols = 30;
    private const string QuotedName = "Quoted Name", QuotedHandle = "q.test", QuotedDid = "did:plc:q";
    private const string Ellipsis = "…";
    private const string ShortWords = "a few quoted words";
    /// <summary>Quoted words far longer than any box holds.</summary>
    private static readonly string LongWords = string.Join(" ", Enumerable.Repeat("words that run on", 40));

    private static PostQuote Quote(QuoteState state = QuoteState.Shown, string words = ShortWords, bool optOut = false,
        IReadOnlyList<PostPicture>? pictures = null, bool quotesAnother = false) =>
        new(state, QuotedName, QuotedHandle, words, pictures is not null, state == QuoteState.Other ? "a list" : null, optOut,
            Pictures: pictures, AuthorDid: QuotedDid, QuotesAnother: quotesAnother);

    private static FeedPost Quoting(PostQuote quote, IReadOnlyList<PostPicture>? own = null) => PressRig.Post(1) with
    {
        HasEmbed = true,
        Attachment = AttachmentKind.Quote,
        Quote = quote,
        Pictures = own,
    };

    private static IReadOnlyList<PostPicture> Pictures(string tag) =>
        [new PostPicture("https://cdn.bsky.app/img/feed_thumbnail/plain/did:plc:x/" + tag + "@jpeg")];

    private static bool Inside(CardRect outer, CardRect inner) =>
        inner.X > outer.X && inner.Y > outer.Y && inner.X + inner.W < outer.X + outer.W && inner.Y + inner.H < outer.Y + outer.H;

    [Theory]
    [InlineData(WideCols)]
    [InlineData(NarrowCols)]
    public void A_shown_quote_stands_in_a_box_under_the_words_with_the_quoted_name_and_handle_on_its_first_row_then_its_words(int cols)
    {
        var shape = CardLayout.Measure(Quoting(Quote()), cols, false);
        var q = Assert.IsType<QuoteBox>(shape.Quote);
        var b = q.Box;
        Assert.Equal(CardLayout.TextCol, b.X);
        Assert.Equal(shape.TextWidth, b.W);
        Assert.Equal(shape.Lines[^1].Row + 1, b.Y);
        Assert.Equal(new CardText(b.X + CardLayout.QuoteEdgeCells, b.Y + CardLayout.QuoteEdgeCells, QuotedName + " @" + QuotedHandle), q.Head);
        Assert.Equal([ShortWords], q.Lines.Select(l => l.Text));
        Assert.Equal(q.Head!.Value.Row + 1, q.Lines[0].Row);
        Assert.Equal(q.Lines[^1].Row + 1 + CardLayout.QuoteEdgeCells, b.Y + b.H);
        Assert.True(shape.LikeIcon.Row > b.Y + b.H - 1, "the action row is under the box");
        Assert.Null(shape.Note);
        Assert.Null(q.Notice);
    }

    [Theory]
    [InlineData(WideCols, CardLayout.QuoteLinesWide)]
    [InlineData(NarrowCols, CardLayout.QuoteLinesNarrow)]
    public void Long_quoted_words_are_cut_to_four_lines_on_a_wide_card_and_three_on_a_narrow_one_the_last_ending_in_an_ellipsis(int cols, int lines)
    {
        var q = CardLayout.Measure(Quoting(Quote(words: LongWords)), cols, false).Quote!;
        Assert.Equal(lines, q.Lines.Count);
        Assert.EndsWith(Ellipsis, q.Lines[^1].Text);
        Assert.All(q.Lines, l => Assert.True(CellText.Width(l.Text) <= q.Box.W - 2 * CardLayout.QuoteEdgeCells));
        Assert.All(q.Lines.Take(lines - 1), l => Assert.DoesNotContain(Ellipsis, l.Text));
    }

    [Fact]
    public void A_long_quoted_name_is_cut_on_its_row_with_an_ellipsis()
    {
        var quote = Quote() with { AuthorName = string.Concat(Enumerable.Repeat("Name", 20)) };
        var q = CardLayout.Measure(Quoting(quote), NarrowCols, false).Quote!;
        Assert.EndsWith(Ellipsis, q.Head!.Value.Text);
        Assert.Equal(q.Box.W - 2 * CardLayout.QuoteEdgeCells, CellText.Width(q.Head.Value.Text));
    }

    [Theory]
    [InlineData(QuoteState.Deleted, false, "⊘  Deleted")]
    [InlineData(QuoteState.Blocked, false, "⊘  Blocked")]
    [InlineData(QuoteState.Detached, false, "Removed by author")]
    [InlineData(QuoteState.Shown, true, "Hidden")]
    [InlineData(QuoteState.Other, false, "a list")]
    public void A_quote_that_cannot_be_shown_is_one_centred_notice_row_in_its_box(QuoteState state, bool optOut, string notice)
    {
        var shape = CardLayout.Measure(Quoting(Quote(state, optOut: optOut, pictures: Pictures("quoted"))), WideCols, false, signedOut: true);
        var q = shape.Quote!;
        var at = Assert.IsType<CardText>(q.Notice);
        Assert.Equal(notice, at.Text);
        Assert.Equal(1 + 2 * CardLayout.QuoteEdgeCells, q.Box.H);
        Assert.Equal(q.Box.Y + CardLayout.QuoteEdgeCells, at.Row);
        int left = at.Col - q.Box.X, right = q.Box.X + q.Box.W - (at.Col + CellText.Width(at.Text));
        Assert.InRange(left - right, -1, 1);
        Assert.Null(q.Head);
        Assert.Empty(q.Lines);
        Assert.Empty(shape.Pictures);
        Assert.Equal(default, shape.Media);
    }

    [Fact]
    public void A_signed_in_viewer_sees_a_quote_whose_author_opted_out_of_signed_out_viewers()
    {
        var post = Quoting(Quote(optOut: true));
        Assert.Equal("Hidden", CardLayout.Measure(post, WideCols, false, signedOut: true).Quote!.Notice?.Text);
        Assert.Equal(QuoteState.Shown, CardLayout.Measure(post, WideCols, false, signedOut: false).Quote!.State);
    }

    [Fact]
    public void A_quotes_pictures_are_drawn_inside_its_box_under_the_quoted_words_when_the_post_has_no_media_of_its_own()
    {
        var quote = Quote(pictures: Pictures("quoted"));
        var shape = CardLayout.Measure(Quoting(quote), WideCols, false);
        var q = shape.Quote!;
        var picture = Assert.Single(shape.Pictures);
        Assert.True(Inside(q.Box, picture), "inside the box");
        Assert.True(picture.Y > q.Lines[^1].Row, "under the quoted words");
        Assert.True(shape.Media.Quoted);
        Assert.Same(quote.Pictures, shape.Media.Pictures);
    }

    [Fact]
    public void A_quote_with_media_of_its_own_post_shows_the_posts_media_above_the_box_and_only_the_quoted_words_inside_it()
    {
        var own = Pictures("own");
        var shape = CardLayout.Measure(Quoting(Quote(pictures: Pictures("quoted")), own), WideCols, false);
        var q = shape.Quote!;
        var picture = Assert.Single(shape.Pictures);
        Assert.True(picture.Y + picture.H <= q.Box.Y, "the post's own picture is above the box");
        Assert.False(shape.Media.Quoted);
        Assert.Same(own, shape.Media.Pictures);
        Assert.Equal(q.Lines[^1].Row + 1 + CardLayout.QuoteEdgeCells, q.Box.Y + q.Box.H);
    }

    [Fact]
    public void A_quote_inside_the_quoted_post_is_a_quote_note_on_the_boxs_last_row()
    {
        var q = CardLayout.Measure(Quoting(Quote(quotesAnother: true)), WideCols, false).Quote!;
        var note = Assert.IsType<CardText>(q.Note);
        Assert.Equal(CardLayout.QuoteNote, note.Text);
        Assert.Equal(q.Box.Y + q.Box.H - 1 - CardLayout.QuoteEdgeCells, note.Row);
        Assert.Equal(q.Lines[^1].Row + 1, note.Row);
    }

    [Fact]
    public void A_quote_card_keeps_the_height_it_was_measured_at_while_its_quoted_picture_comes_and_the_card_scrolls()
    {
        var (g, c, asked) = CardPicturesTests.Scene(Quoting(Quote(pictures: Pictures("quoted"))));
        int rows = c.Shape.Rows;
        var ask = Assert.Single(asked);
        Assert.Equal(QuotedDid, ask.Did);
        g.SetPicture(ask.Key, ask.W, ask.H, new uint[ask.W * ask.H]);
        for (int i = 0; i < Tuning.Fps; i++)
        {
            g.Step();
            Assert.Equal(rows, c.Shape.Rows);
        }
    }

    [Fact]
    public void The_quote_boxs_edge_is_drawn_round_it_in_the_quote_edge_colour()
    {
        var (g, c, _) = PressRig.Scene(post: Quoting(Quote()));
        var b = c.Shape.Quote!.Box;
        int x = (g.Cards.CardLeft + b.X) * CardField.PixelsPerCell, y = (c.Top + b.Y) * CardField.PixelsPerCell;
        int w = b.W * CardField.PixelsPerCell, h = b.H * CardField.PixelsPerCell;
        var f = PressRig.Draw(g);
        Assert.Equal(CardColours.QuoteEdge, f.Get(x, y));
        Assert.Equal(CardColours.QuoteEdge, f.Get(x + w - 1, y + h - 1));
        Assert.Equal(CardColours.Slab, f.Get(x + 1, y + 1));
    }
}
