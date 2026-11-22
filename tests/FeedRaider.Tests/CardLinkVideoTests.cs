using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// A post's link card and video on its card: where the layout puts them on a wide and a narrow card, what the game asks the host for,
// and what the painter draws before and after the thumbnail comes.
public class CardLinkVideoTests
{
    /// <summary>A card wide enough to put a link's words beside its thumbnail, and one narrow enough to put them under it.</summary>
    private const int WideCardCols = CardActionRowTests.PhoneCard, NarrowCardCols = 30;
    private const string AuthorDid = "did:plc:a", Words = "a short note on a link";
    private const string ThumbUrl = "https://cdn.bsky.app/img/feed_thumbnail/plain/" + AuthorDid + "/cidthumb@jpeg";
    private const string Title = "A long title for the page this post links to, long enough to wrap", Host = "example.com",
        About = "What the page says it is about";
    private const string VideoCid = "bafkvideo1";
    /// <summary>A phone video's shape, taller than wide.</summary>
    private const double TallVideo = 9 / 16.0;
    /// <summary>A colour above the luminance cap, which the painter dims; any answer the host gives for a picture.</summary>
    private const uint Bright = 0xffffff;

    private static FeedPost Post(LinkCard? link = null, VideoRef? video = null, string? alt = null) =>
        new("at://" + AuthorDid + "/app.bsky.feed.post/1", "c", AuthorDid, "a.test", Words, PressRig.OrdinaryLikes, PressRig.OrdinaryReposts, 0, null, null,
            DisplayName: "Some Name", HasEmbed: true, Attachment: link is not null ? AttachmentKind.Link : AttachmentKind.Video, AttachmentText: alt,
            Link: link, Video: video);

    private static LinkCard Link(string? thumb = ThumbUrl) => new(Title, Host, About, thumb);
    private static VideoRef Video(double? aspect = null, string? alt = null) => new(AuthorDid, VideoCid, aspect, alt);

    private static bool Inside(CardRect outer, CardRect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.W <= outer.X + outer.W && inner.Y + inner.H <= outer.Y + outer.H;

    private static bool Inside(CardRect box, CardText text) =>
        text.Row >= box.Y && text.Row < box.Y + box.H && text.Col >= box.X && text.Col + CellText.Width(text.Text) <= box.X + box.W;

    private static int Bottom(CardRect r) => r.Y + r.H;

    // ---- the layout

    [Fact]
    public void On_a_wide_card_a_link_puts_its_title_host_and_description_beside_its_thumbnail_and_the_buttons_under_it()
    {
        var shape = CardLayout.Measure(Post(Link()), WideCardCols, false);
        var link = Assert.IsType<LinkBox>(shape.Link);
        var thumb = Assert.IsType<CardRect>(link.Thumb);
        Assert.Equal(CardLayout.TextCol, link.Box.X);
        Assert.Equal(shape.TextWidth, link.Box.W);
        Assert.True(link.Box.Y > shape.Lines[^1].Row, "the link card is not under the words");
        Assert.True(Inside(link.Box, thumb));
        Assert.All(link.Title, t => Assert.True(t.Col >= thumb.X + thumb.W && Inside(link.Box, t), t.Text));
        Assert.True(link.Title.Count <= CardLayout.LinkTitleRows);
        Assert.Equal(link.Title[^1].Row + 1, link.Host.Row);
        Assert.Equal(Host, link.Host.Text);
        var about = Assert.IsType<CardText>(link.Description);
        Assert.Equal(link.Host.Row + 1, about.Row);
        Assert.True(Inside(link.Box, about) && about.Row < Bottom(thumb));
        Assert.True(Bottom(link.Box) <= shape.LikeButton.Y, "the link card reaches the buttons");
        Assert.Null(shape.Note);
        Assert.Empty(shape.Pictures);
    }

    [Fact]
    public void On_a_narrow_card_a_link_puts_its_thumbnail_on_top_wider_than_tall_and_its_title_and_host_under_it()
    {
        var shape = CardLayout.Measure(Post(Link()), NarrowCardCols, false);
        var link = Assert.IsType<LinkBox>(shape.Link);
        var thumb = Assert.IsType<CardRect>(link.Thumb);
        Assert.Equal(link.Box.Y, thumb.Y);
        Assert.True(thumb.W > thumb.H * CardLayout.CellTallness, "the thumbnail lost its wide shape");
        Assert.Equal(Bottom(thumb), link.Title[0].Row);
        Assert.Equal(link.Title[^1].Row + 1, link.Host.Row);
        Assert.Null(link.Description);
        Assert.True(Inside(link.Box, link.Host));
        Assert.True(Bottom(link.Box) <= shape.LikeButton.Y);
    }

    [Theory]
    [InlineData(WideCardCols)]
    [InlineData(NarrowCardCols)]
    public void A_link_with_no_thumbnail_is_its_title_on_one_row_and_its_host_in_a_shorter_box(int cols)
    {
        var bare = CardLayout.Measure(Post(Link(thumb: null)), cols, false);
        var link = Assert.IsType<LinkBox>(bare.Link);
        Assert.Null(link.Thumb);
        var title = Assert.Single(link.Title);
        Assert.EndsWith("…", title.Text);
        Assert.Equal(title.Row + 1, link.Host.Row);
        Assert.True(Inside(link.Box, title) && Inside(link.Box, link.Host));
        Assert.True(link.Box.H < CardLayout.Measure(Post(Link()), cols, false).Link!.Box.H);
        Assert.True(Bottom(link.Box) <= bare.LikeButton.Y);
    }

    [Theory]
    [InlineData(WideCardCols, null)]
    [InlineData(WideCardCols, TallVideo)]
    [InlineData(NarrowCardCols, null)]
    [InlineData(NarrowCardCols, TallVideo)]
    public void A_video_keeps_its_shape_within_a_lone_pictures_cap_or_on_a_narrow_card_a_pairs_with_the_play_mark_in_its_bottom_left_corner(int cols, double? aspect)
    {
        var shape = CardLayout.Measure(Post(video: Video(aspect)), cols, false);
        var video = Assert.IsType<VideoBox>(shape.Video);
        bool narrow = cols < CardLayout.NarrowBelow;
        Assert.True(video.Picture.W <= (narrow ? CardLayout.TwoPictureCols : CardLayout.OnePictureCols), $"{video.Picture.W} columns");
        Assert.True(video.Picture.H <= (narrow ? CardLayout.TwoPictureRows : CardLayout.OnePictureRows), $"{video.Picture.H} rows");
        Assert.Equal(aspect is null, video.Picture.W > video.Picture.H * CardLayout.CellTallness);
        Assert.True(Inside(video.Picture, video.Mark));
        Assert.Equal((video.Picture.X, Bottom(video.Picture)), (video.Mark.X, Bottom(video.Mark)));
        Assert.Equal(CardLayout.PlayMark.ToString(), video.Play.Text);
        Assert.True(Inside(video.Mark, video.Play));
        Assert.True(Bottom(video.Picture) <= shape.LikeButton.Y);
    }

    [Fact]
    public void The_play_mark_takes_one_cell_so_it_sits_inside_its_mark() => Assert.Equal(1, CellWidth.Of(CardLayout.PlayMark));

    [Fact]
    public void A_video_with_alt_text_keeps_its_note_and_one_without_has_none()
    {
        Assert.Null(CardLayout.Measure(Post(video: Video()), WideCardCols, false).Note);
        var note = CardLayout.Measure(Post(video: Video(alt: "a cat"), alt: "a cat"), WideCardCols, false).Note;
        Assert.Equal("[video: a cat]", note?.Text);
    }

    [Fact]
    public void A_quotes_link_card_is_laid_inside_its_quote_box_under_the_quoted_words_and_MediaAt_lays_it_out_where_it_is_asked()
    {
        var quote = new PostQuote(QuoteState.Shown, "Quoted", "q.test", "quoted words", true, null, Link: Link(), AuthorDid: "did:plc:q");
        var post = Post(Link()) with { Link = null, Attachment = AttachmentKind.Quote, Quote = quote };
        var shape = CardLayout.Measure(post, WideCardCols, false);
        var box = Assert.IsType<QuoteBox>(shape.Quote).Box;
        var inShape = Assert.IsType<LinkBox>(shape.Link).Box;
        Assert.True(inShape.X > box.X && inShape.X + inShape.W < box.X + box.W, "inside the box's sides");
        Assert.True(inShape.Y > shape.Quote!.Lines[^1].Row && inShape.Y + inShape.H < box.Y + box.H, "under the quoted words, above the box's foot");
        Assert.True(shape.Media.Quoted);
        Assert.Same(quote.Link, shape.Media.Link);
        var media = PostRules.MediaShown(post);
        Assert.True(media.Quoted);
        const int x = 9, y = 6, width = 40;
        var laid = CardLayout.MediaAt(media, x, y, width, narrow: false);
        var link = Assert.IsType<LinkBox>(laid.Link);
        Assert.Equal(new CardRect(x, y, width, laid.Rows), link.Box);
    }

    // ---- the game and the painter

    [Fact]
    public void A_link_cards_thumbnail_is_asked_of_the_authors_PDS_once_and_the_card_is_as_tall_before_and_after_it_comes()
    {
        var (g, c, asked) = CardPicturesTests.Scene(Post(Link()));
        var thumb = c.Shape.Link!.Thumb!.Value;
        var ask = Assert.Single(asked);
        Assert.Equal(new PictureAsk(ThumbUrl, PictureSource.Pds, AuthorDid, ThumbUrl, thumb.W * CardField.PixelsPerCell, thumb.H * CardField.PixelsPerCell), ask);
        int rows = c.Shape.Rows;
        var box = PixelsOf(g, c, thumb);
        var before = PressRig.Draw(g);
        Assert.Equal(CardColours.LinkThumbWaiting, before.Get(box.X, box.Y));
        Assert.NotEqual(CardColours.LinkSlab, CardColours.LinkThumbWaiting);
        g.SetPicture(ask.Key, ask.W, ask.H, Enumerable.Repeat(Bright, ask.W * ask.H).ToArray());
        var f = PressRig.Draw(g);
        Assert.Equal(g.PictureOf(ThumbUrl)!.Px[0], f.Get(box.X, box.Y));
        var slab = PixelsOf(g, c, c.Shape.Link.Box);
        Assert.Equal(CardColours.LinkSlab, f.Get(slab.X + slab.W - 1, slab.Y + slab.H - 1));
        Assert.Equal(rows, c.Shape.Rows);
        for (int i = 0; i < Tuning.Fps; i++) g.Step();
        Assert.Single(asked);
    }

    [Fact]
    public void A_link_with_no_thumbnail_asks_for_no_picture() => Assert.Empty(CardPicturesTests.Scene(Post(Link(thumb: null))).Asked);

    [Fact]
    public void A_videos_thumbnail_is_asked_of_the_video_service_by_DID_and_CID_and_the_play_mark_shows_before_and_after_it_comes()
    {
        var (g, c, asked) = CardPicturesTests.Scene(Post(video: Video()));
        var video = c.Shape.Video!;
        var ask = Assert.Single(asked);
        Assert.Equal(new PictureAsk(RaiderGame.VideoKey(Video()), PictureSource.VideoService, AuthorDid, VideoCid,
            video.Picture.W * CardField.PixelsPerCell, video.Picture.H * CardField.PixelsPerCell), ask);
        int rows = c.Shape.Rows;
        var picture = PixelsOf(g, c, video.Picture);
        var mark = PixelsOf(g, c, video.Mark);
        foreach (bool answered in new[] { false, true })
        {
            if (answered) g.SetPicture(ask.Key, ask.W, ask.H, Enumerable.Repeat(Bright, ask.W * ask.H).ToArray());
            var scr = new Screen(PressRig.ScrCols, PressRig.ScrRows);
            var view = new RaiderView();
            view.FitAndRender(g, scr);
            var f = view.Field!;
            uint inPicture = f.Get(picture.X + picture.W - 1, picture.Y);
            Assert.Equal(answered ? g.PictureOf(ask.Key)!.Px[ask.W - 1] : CardColours.PictureWaiting, inPicture);
            Assert.Equal(CardColours.VideoMark, f.Get(mark.X, mark.Y));
            Assert.Equal(CardLayout.PlayMark, scr.CharAt(g.Layout.Col0 + g.Cards.CardLeft + video.Play.Col, c.Top + video.Play.Row));
            Assert.Equal(rows, c.Shape.Rows);
        }
    }

    [Fact]
    public async Task The_tour_carries_a_link_card_and_a_video_whose_pictures_are_built_in_and_ask_the_host_for_nothing()
    {
        var page = await new TourFeedSource().GetPageAsync(string.Empty, null, TourFeedSource.PerPage, CancellationToken.None);
        var link = Assert.Single(page.Posts, p => p.Link is not null);
        var video = Assert.Single(page.Posts, p => p.Video is not null);
        foreach (var post in new[] { link, video })
        {
            Assert.True(PostRules.Shown(post, signedOut: true, textOnly: false));
            var (g, c, asked) = CardPicturesTests.Scene(post);
            Assert.Empty(asked);
            string key = post.Video is { } v ? RaiderGame.VideoKey(v) : post.Link!.ThumbUrl!;
            Assert.NotNull(g.PictureOf(key));
            Assert.True(c.Shape.Link is not null || c.Shape.Video is not null);
        }
    }

    private static PixelRect PixelsOf(RaiderGame g, Card c, CardRect cells) =>
        new((g.Cards.CardLeft + cells.X) * CardField.PixelsPerCell, (c.Top + cells.Y) * CardField.PixelsPerCell, cells.W * CardField.PixelsPerCell, cells.H * CardField.PixelsPerCell);
}
