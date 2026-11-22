using System.Text.Json;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// A post's attached pictures on its card: which posts the rules let through, what the feed mapper reads, where the layout puts
// one to four pictures, and what the painter draws in their boxes.
public class CardPicturesTests
{
    private const string Words = "a plain joke about a very confused pigeon";
    /// <summary>A post of pictures alone.</summary>
    private const string NoWords = "";
    /// <summary>The words a picture's alt text gives.</summary>
    private const string AltText = "what it shows";
    /// <summary>The stated shape of an ordinary picture: a 4:3 photo.</summary>
    private const int PhotoW = 4, PhotoH = 3;
    /// <summary>A picture's long and short side in pixels, for a wide (16:9) picture and a tall one.</summary>
    private const int LongSide = 1600, ShortSide = 900;
    /// <summary>The side a picture with no stated shape reads as.</summary>
    private const int ShapeNotStated = 0;
    /// <summary>The stated side of a square picture.</summary>
    private const int SquareSide = 1;
    /// <summary>A card wide enough to keep its text beside the avatar, and one narrow enough to put it under.</summary>
    private const int WideCardCols = CardActionRowTests.PhoneCard, NarrowCardCols = 30;
    /// <summary>The count of pictures in the tests that need more than one, and of a post with none.</summary>
    private const int TwoPictures = 2, NoPictures = 0;
    /// <summary>The test author's id and handle, and the address of their post.</summary>
    private const string AuthorDid = "did:plc:a", AuthorHandle = "a.test", PostUri = "at://" + AuthorDid + "/app.bsky.feed.post/1";
    /// <summary>Where the numbered thumbnails of the test author's pictures are kept.</summary>
    private const string ThumbPrefix = "https://cdn.bsky.app/img/feed_thumbnail/plain/" + AuthorDid + "/cid";

    private static PostPicture NumberedPicture(int number, int width, int height) => new($"{ThumbPrefix}{number}", width, height);

    private static FeedPost PicturePost(string text = Words, int pictures = 1, int width = PhotoW, int height = PhotoH, string? alt = null, bool adult = false,
        bool optOut = false, PostRef? parent = null, AttachmentKind kind = AttachmentKind.Picture) =>
        new(PostUri, "c", AuthorDid, AuthorHandle, text, PressRig.OrdinaryLikes, PressRig.OrdinaryReposts, 0, null, parent,
            DisplayName: "Some Name", HasEmbed: true, Adult: adult, AuthorOptOut: optOut, Attachment: kind, AttachmentText: alt,
            Pictures: pictures == NoPictures ? null : Enumerable.Range(0, pictures).Select(i => NumberedPicture(i, width, height)).ToArray(),
            Video: kind == AttachmentKind.Video ? new VideoRef(AuthorDid, "cidvideo", null, alt) : null);

    // ---- the rules

    [Fact]
    public void A_post_of_pictures_and_no_words_is_shown() => Assert.True(PostRules.Shown(PicturePost(NoWords), false, false));

    [Fact]
    public void Random_takes_a_post_with_pictures_with_or_without_words()
    {
        Assert.True(PostRules.Shown(PicturePost(), false, true));
        Assert.True(PostRules.Shown(PicturePost("short"), false, true));
        Assert.True(PostRules.Shown(PicturePost(NoWords), false, true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void An_adult_post_with_pictures_is_refused_in_every_mode(bool signedOut, bool textOnly)
    {
        Assert.False(PostRules.Shown(PicturePost(adult: true), signedOut, textOnly));
        Assert.False(PostRules.Shown(PicturePost(NoWords, adult: true), signedOut, textOnly));
        Assert.False(PostRules.Followable(PicturePost(adult: true)));
    }

    [Fact]
    public void A_signed_out_viewer_is_not_shown_the_pictures_of_an_author_who_opted_out()
    {
        Assert.False(PostRules.Shown(PicturePost(optOut: true), true, false));
        Assert.True(PostRules.Shown(PicturePost(optOut: true), false, false));
    }

    [Fact]
    public void A_reply_with_pictures_is_still_not_shown_but_a_thread_may_follow_it()
    {
        var reply = PicturePost(parent: new PostRef("at://x/p/0", "c0"));
        Assert.False(PostRules.Shown(reply, false, false));
        Assert.True(PostRules.Followable(reply));
        Assert.True(PostRules.Followable(PicturePost(NoWords)));
    }

    [Theory]
    [InlineData(AttachmentKind.Link)]
    [InlineData(AttachmentKind.Quote)]
    [InlineData(AttachmentKind.Other)]
    public void A_post_with_an_attachment_it_cannot_draw_keeps_its_note_and_its_rules(AttachmentKind kind)
    {
        var p = PicturePost(pictures: NoPictures, kind: kind);
        Assert.True(PostRules.Shown(p, false, false));
        Assert.False(PostRules.Shown(p, false, true));
        Assert.False(PostRules.Shown(PicturePost(NoWords, pictures: NoPictures, kind: kind), false, false));
        Assert.False(PostRules.Followable(p));
        Assert.Empty(CardLayout.Measure(p, WideCardCols, false).Pictures);
        Assert.NotNull(CardLayout.Measure(p, WideCardCols, false).Note);
    }

    // ---- the feed mapper

    private static FeedPost Parse(string embed, string text = "hello there")
    {
        string json = $$"""{"uri":"{{PostUri}}","cid":"c1","author":{"did":"{{AuthorDid}}","handle":"{{AuthorHandle}}"},"record":{"text":"{{text}}"},"embed":{{embed}}}""";
        using var doc = JsonDocument.Parse(json);
        return FeedMapper.ParsePost(doc.RootElement)!;
    }

    /// <summary>The stated shape of the first picture of a <see cref="ImagesEmbedJson"/>; each one after it is a pixel wider, so the test can tell them apart.</summary>
    private const int ViewWidth = 100, ViewHeight = 50;
    /// <summary>What the feed puts after a thumbnail's address.</summary>
    private const string JpegSuffix = "@jpeg";

    private static string ImagesEmbedJson(int n) => "{\"$type\":\"app.bsky.embed.images#view\",\"images\":[" + string.Join(",", Enumerable.Range(0, n).Select(i =>
        $"{{\"thumb\":\"{ThumbPrefix}{i}{JpegSuffix}\",\"alt\":\"\",\"aspectRatio\":{{\"width\":{ViewWidth + i},\"height\":{ViewHeight}}}}}")) + "]}";

    [Fact]
    public void The_mapper_reads_each_pictures_thumbnail_and_shape_and_no_more_than_four()
    {
        var p = Parse(ImagesEmbedJson(TwoPictures));
        Assert.Equal(TwoPictures, p.Pictures!.Count);
        const int second = 1;
        Assert.Equal(new PostPicture(ThumbPrefix + second + JpegSuffix, ViewWidth + second, ViewHeight), p.Pictures[second]);
        Assert.Equal(FeedMapper.MaxPictures, Parse(ImagesEmbedJson(FeedMapper.MaxPictures + TwoPictures)).Pictures!.Count);
        Assert.True(PostRules.Shown(Parse(ImagesEmbedJson(1), text: NoWords), false, true));
    }

    [Fact]
    public void A_picture_with_no_stated_shape_is_still_read_and_one_with_no_thumbnail_is_not()
    {
        var p = Parse("{\"$type\":\"app.bsky.embed.images#view\",\"images\":[{\"thumb\":\"https://cdn.bsky.app/x\"},{\"alt\":\"no thumb\"}]}");
        Assert.Equal([new PostPicture("https://cdn.bsky.app/x", ShapeNotStated, ShapeNotStated)], p.Pictures!);
    }

    [Fact]
    public void A_video_and_a_link_card_carry_no_pictures_and_a_quote_with_media_draws_the_posts_own_pictures_in_its_body()
    {
        var withMedia = Parse("{\"$type\":\"app.bsky.embed.recordWithMedia#view\",\"media\":" + ImagesEmbedJson(TwoPictures) + "}");
        Assert.Equal(AttachmentKind.Quote, withMedia.Attachment);
        Assert.Equal(TwoPictures, CardLayout.Measure(withMedia, WideCardCols, false).Pictures.Count);
        Assert.Null(Parse("{\"$type\":\"app.bsky.embed.video#view\",\"cid\":\"x\",\"thumbnail\":\"https://video.bsky.app/t.jpg\"}").Pictures);
        Assert.Null(Parse("{\"$type\":\"app.bsky.embed.external#view\",\"external\":{\"uri\":\"https://e.example/a\",\"title\":\"t\",\"thumb\":\"https://cdn.bsky.app/t\"}}").Pictures);
    }

    // ---- the layout

    public static TheoryData<int, int, int, int> Shapes()
    {
        var d = new TheoryData<int, int, int, int>();
        foreach (int cols in new[] { WideCardCols, 80, NarrowCardCols, 24 })
            for (int n = 1; n <= FeedMapper.MaxPictures; n++)
            {
                d.Add(cols, n, LongSide, ShortSide);
                d.Add(cols, n, ShortSide, LongSide);
                d.Add(cols, n, ShapeNotStated, ShapeNotStated);
            }
        return d;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_picture_has_a_box_inside_the_text_column_under_the_words_above_the_buttons_and_clear_of_the_others(int cols, int n, int w, int h)
    {
        var s = CardLayout.Measure(PicturePost(pictures: n, width: w, height: h, alt: AltText), cols, false);
        Assert.Equal(n, s.Pictures.Count);
        int under = s.Lines[^1].Row;
        for (int i = 0; i < n; i++)
        {
            var b = s.Pictures[i];
            Assert.True(b.W >= 1 && b.H >= 1);
            Assert.True(b.X >= s.TextX && b.X + b.W <= s.TextX + s.TextWidth, $"picture {i} leaves the text column: {b}");
            Assert.True(b.Y > under, $"picture {i} is on the words");
            Assert.True(b.Y + b.H <= s.Note!.Value.Row, $"picture {i} reaches the note");
            Assert.True(s.Note.Value.Row < s.LikeButton.Y, "the note is on the buttons");
            for (int j = 0; j < i; j++)
            {
                var o = s.Pictures[j];
                bool apart = b.X >= o.X + o.W + CardLayout.PictureGap || o.X >= b.X + b.W + CardLayout.PictureGap
                    || b.Y >= o.Y + o.H + CardLayout.PictureGap || o.Y >= b.Y + b.H + CardLayout.PictureGap;
                Assert.True(apart, $"pictures {j} and {i} touch: {o} {b}");
            }
        }
        Assert.Equal(s.Rows - CardLayout.FooterRows, s.LikeButton.Y);
    }

    [Theory]
    [InlineData(WideCardCols)]
    [InlineData(NarrowCardCols)]
    public void A_wide_picture_is_wider_than_a_tall_one_and_a_tall_one_takes_more_rows_for_its_width(int cols)
    {
        var wide = CardLayout.Measure(PicturePost(width: LongSide, height: ShortSide), cols, false).Pictures[0];
        var tall = CardLayout.Measure(PicturePost(width: ShortSide, height: LongSide), cols, false).Pictures[0];
        Assert.True(wide.W > tall.W);
        Assert.True(wide.W > CardLayout.CellTallness * wide.H, "a wide picture is wider than a square");
        Assert.True(tall.W < CardLayout.CellTallness * tall.H, "a tall picture is narrower than a square");
        Assert.True(wide.W <= CardLayout.OnePictureCols && tall.H <= CardLayout.OnePictureRows);
    }

    [Fact]
    public void Four_pictures_fit_in_one_row_on_the_card_of_a_412_pixel_page_and_two_to_a_row_on_a_narrow_card()
    {
        int phone = FieldLayout.Compute(AtlasKeyTests.PhoneCols, AtlasKeyTests.PhoneRows).PCols - 2 * CardField.Margin;
        var s = CardLayout.Measure(PicturePost(pictures: FeedMapper.MaxPictures, width: SquareSide, height: SquareSide), phone, false);
        Assert.Single(s.Pictures.Select(b => b.Y).Distinct());
        Assert.True(s.Pictures[^1].X + s.Pictures[^1].W <= s.TextX + s.TextWidth);
        var narrow = CardLayout.Measure(PicturePost(pictures: FeedMapper.MaxPictures, width: SquareSide, height: SquareSide), NarrowCardCols, false);
        const int narrowRows = 2;
        Assert.Equal(narrowRows, narrow.Pictures.Select(b => b.Y).Distinct().Count());
    }

    [Fact]
    public void The_bare_image_note_goes_when_the_pictures_are_drawn_and_the_alt_text_stays()
    {
        Assert.Null(CardLayout.Measure(PicturePost(), WideCardCols, false).Note);
        Assert.Equal("[image: a red fox]", CardLayout.Measure(PicturePost(alt: "a red fox"), WideCardCols, false).Note!.Value.Text);
        Assert.Equal("[image]", CardLayout.Measure(PicturePost(pictures: NoPictures), WideCardCols, false).Note!.Value.Text);
    }

    [Fact]
    public void A_card_of_pictures_and_no_words_has_its_pictures_where_the_words_would_start()
    {
        var s = CardLayout.Measure(PicturePost(NoWords), WideCardCols, false);
        Assert.Empty(s.Lines);
        Assert.Equal(CardLayout.Measure(PicturePost(), WideCardCols, false).Lines[0].Row, s.Pictures[0].Y);
        Assert.Equal(s.LikeButton.Y, s.Pictures[0].Y + s.Pictures[0].H);
    }

    // ---- the game and the painter

    /// <summary>The row the card under test is set to: on the field, below the score bar.</summary>
    private const int CardTop = 8;
    /// <summary>Two bright colours above the luminance cap, which the painter must dim.</summary>
    private const uint BrightWhite = 0xffffff, BrightOrange = 0xff4010;
    /// <summary>A colour dark enough to need only the first mix toward the card's slab.</summary>
    private const uint DarkBlue = 0x204060;
    /// <summary>Room for the rounding of the luminance sum.</summary>
    private const double LuminanceSlack = 1e-9;

    /// <summary>A game stepped once, with the ship off the field and nothing on it but the card of <paramref name="post"/>, if one is given.</summary>
    private static RaiderGame QuietGame(FeedPost? post = null, Action<PictureAsk>? pictureWanted = null)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false, PictureWanted = pictureWanted };
        g.SetLayout(FieldLayout.Compute(PressRig.ScrCols, PressRig.ScrRows));
        if (post is not null) g.Posts.Offer(post);
        g.Step();
        g.Enemies.Clear(); g.Bullets.Clear(); g.Particles.Clear(); g.Rings.Clear(); g.Pickups.Clear();
        PressRig.Force(g, nameof(RaiderGame.PX), CardRig.ParkedShipX);
        return g;
    }

    internal static (RaiderGame G, Card C, List<PictureAsk> Asked) Scene(FeedPost post)
    {
        var asked = new List<PictureAsk>();
        var g = QuietGame(post, asked.Add);
        var card = g.Cards.Cards[0];
        card.Top = CardTop;
        return (g, card, asked);
    }

    /// <summary>The field pixels of the card's picture numbered <paramref name="index"/>.</summary>
    private static PixelRect PictureBox(RaiderGame g, Card c, int index)
    {
        var cells = c.Shape.Pictures[index];
        return new PixelRect((g.Cards.CardLeft + cells.X) * CardField.PixelsPerCell, (c.Top + cells.Y) * CardField.PixelsPerCell, cells.W * CardField.PixelsPerCell, cells.H * CardField.PixelsPerCell);
    }

    /// <summary>A picture's pixels, white and orange in turn: all of them above the luminance cap.</summary>
    private static uint[] BrightPixels(int width, int height) => Enumerable.Range(0, width * height).Select(i => i % 2 == 0 ? BrightWhite : BrightOrange).ToArray();

    /// <summary>Answers the card's first picture with bright pixels at the size it asked for.</summary>
    private static void AnswerFirstPicture(RaiderGame g, List<PictureAsk> asked) =>
        g.SetPicture(asked[0].Key, asked[0].W, asked[0].H, BrightPixels(asked[0].W, asked[0].H));

    [Fact]
    public void A_card_asks_for_each_picture_once_as_it_is_made_at_two_pixels_to_a_cell_of_its_box()
    {
        const int pictures = 3;
        var (g, c, asked) = Scene(PicturePost(pictures: pictures));
        Assert.Equal(pictures, asked.Count);
        for (int i = 0; i < pictures; i++)
        {
            string thumb = c.Post.Pictures![i].Thumb;
            Assert.Equal(new PictureAsk(thumb, PictureSource.Pds, AuthorDid, thumb, c.Shape.Pictures[i].W * CardField.PixelsPerCell, c.Shape.Pictures[i].H * CardField.PixelsPerCell), asked[i]);
        }
        for (int i = 0; i < Tuning.Fps; i++) g.Step();
        Assert.Equal(pictures, asked.Count);
    }

    [Fact]
    public void Until_a_picture_arrives_its_box_is_one_quiet_colour_and_the_card_is_the_same_height_before_and_after()
    {
        var (g, c, asked) = Scene(PicturePost(pictures: TwoPictures));
        int rows = c.Shape.Rows;
        var f = PressRig.Draw(g);
        for (int i = 0; i < TwoPictures; i++)
        {
            var b = PictureBox(g, c, i);
            for (int y = 0; y < b.H; y++)
                for (int x = 0; x < b.W; x++)
                    Assert.Equal(CardColours.PictureWaiting, f.Get(b.X + x, b.Y + y));
        }
        Assert.True(Surface.Luminance(CardColours.PictureWaiting) <= CardColours.MaxIdleLuminance);
        AnswerFirstPicture(g, asked);
        f = PressRig.Draw(g);
        var first = PictureBox(g, c, 0);
        Assert.NotEqual(CardColours.PictureWaiting, f.Get(first.X, first.Y));
        var second = PictureBox(g, c, 1);
        Assert.Equal(CardColours.PictureWaiting, f.Get(second.X, second.Y));   // the one that never came
        Assert.Equal(rows, c.Shape.Rows);
    }

    [Fact]
    public void A_picture_is_drawn_pixel_for_pixel_in_its_box_dimmed_like_the_avatar_and_under_the_luminance_cap()
    {
        var (g, c, asked) = Scene(PicturePost());
        var (w, h) = (asked[0].W, asked[0].H);
        var px = BrightPixels(w, h);
        px[0] = DarkBlue;
        g.SetPicture(c.Post.Pictures![0].Thumb, w, h, px);
        var f = PressRig.Draw(g);
        var b = PictureBox(g, c, 0);
        Assert.Equal((w, h), (b.W, b.H));
        Assert.Equal(Surface.Mix(DarkBlue, CardColours.Slab, CardColours.AvatarDim), f.Get(b.X, b.Y));
        var seen = new HashSet<uint>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint got = f.Get(b.X + x, b.Y + y);
                seen.Add(got);
                Assert.True(Surface.Luminance(got) <= CardColours.MaxIdleLuminance + LuminanceSlack, $"pixel {x},{y} is too bright");
                Assert.Equal(g.PictureOf(c.Post.Pictures[0].Thumb)!.Px[y * w + x], got);
            }
        Assert.Equal(px.Distinct().Count(), seen.Count);
        Assert.Equal(CardColours.Slab, f.Get(b.X - 1, b.Y));
        Assert.Equal(CardColours.Slab, f.Get(b.X + b.W, b.Y));
        Assert.Equal(CardColours.Slab, f.Get(b.X, b.Y + b.H));
    }

    [Fact]
    public void A_picture_half_above_the_fields_top_is_clipped_there()
    {
        var (g, c, asked) = Scene(PicturePost());
        AnswerFirstPicture(g, asked);
        var pic = c.Shape.Pictures[0];
        int halfAbove = -(pic.Y + pic.H / 2);
        c.Top = halfAbove;
        var f = PressRig.Draw(g);
        c.Top = LikePressTests.OffFieldTop;
        var behind = PressRig.Draw(g);
        c.Top = halfAbove;
        var b = PictureBox(g, c, 0);
        for (int y = 0; y < CardPainter.HudPixelRows; y++)
            for (int x = 0; x < f.W; x++)
                Assert.True(f.Get(x, y) == behind.Get(x, y), $"HUD pixel {x},{y} changed");
        Assert.NotEqual(behind.Get(b.X, b.Y + b.H - 1), f.Get(b.X, b.Y + b.H - 1));
        Assert.Equal(g.PictureOf(c.Post.Pictures[0].Thumb)!.Px[(b.H - 1) * b.W], f.Get(b.X, b.Y + b.H - 1));
    }

    [Fact]
    public void An_enemy_and_a_shot_over_a_picture_are_drawn_as_they_are_over_the_sky()
    {
        var (g, c, asked) = Scene(PicturePost());
        AnswerFirstPicture(g, asked);
        var b = PictureBox(g, c, 0);
        var alone = PressRig.Draw(g);
        double middleX = CardActionRowTests.Middle(b.X, b.W), middleY = CardActionRowTests.Middle(b.Y, b.H);
        void AddGunshipAndShot(RaiderGame game)
        {
            var e = game.SpawnKind(EnemyKind.Gunship, EnemyKit.MidWidth);
            e.X = middleX; e.Y = middleY; e.Grace = 0; e.Age = 0; e.Flash = 0;
            game.Bullets.Add(new Bullet { X = b.X + ShotInset, Y = b.Y + ShotInset, Friendly = true });
        }
        AddGunshipAndShot(g);
        var over = PressRig.Draw(g);
        var sky = QuietGame();
        var empty = PressRig.Draw(sky);
        AddGunshipAndShot(sky);
        var units = PressRig.Draw(sky);
        int covered = 0;
        for (int y = b.Y; y < b.Y + b.H; y++)
            for (int x = b.X; x < b.X + b.W; x++)
            {
                if (units.Get(x, y) == empty.Get(x, y)) continue;
                covered++;
                Assert.Equal(units.Get(x, y), over.Get(x, y));
                Assert.NotEqual(alone.Get(x, y), over.Get(x, y));
            }
        Assert.True(covered > MinCoveredPixels, "the enemy and the shot were not over the picture");
    }

    /// <summary>How far in from the picture's top left corner the shot is put, so it lies on the picture.</summary>
    private const int ShotInset = 3;
    /// <summary>More changed pixels than a stray edge of the enemy or the shot could make.</summary>
    private const int MinCoveredPixels = 8;

    [Fact]
    public void The_words_are_never_written_into_a_pictures_cells()
    {
        var (g, c, asked) = Scene(PicturePost(alt: AltText));
        AnswerFirstPicture(g, asked);
        var scr = new Screen(PressRig.ScrCols, PressRig.ScrRows);
        new RaiderView().FitAndRender(g, scr);
        var pic = c.Shape.Pictures[0];
        foreach (var t in c.Shape.Lines.Append(c.Shape.Note!.Value).Append(c.Shape.Name).Append(c.Shape.Handle))
            Assert.False(t.Row >= pic.Y && t.Row < pic.Y + pic.H && t.Col < pic.X + pic.W && t.Col + t.Text.Length > pic.X, $"'{t.Text}' is on the picture");
    }

    [Fact]
    public void An_answer_of_the_wrong_size_is_dropped_and_the_game_keeps_a_bounded_number_of_pictures()
    {
        var g = new RaiderGame(EnemyKit.PlainSeed);
        const int side = 4, tooWide = RaiderGame.PictureMaxSide + 1;
        const string shortAnswer = "a", noWidthAnswer = "b", tooWideAnswer = "c", kept = "p";
        g.SetPicture(shortAnswer, side, side, new uint[side * side - 1]);
        g.SetPicture(noWidthAnswer, 0, side, []);
        g.SetPicture(tooWideAnswer, tooWide, 1, new uint[tooWide]);
        Assert.Null(g.PictureOf(shortAnswer));
        Assert.Null(g.PictureOf(noWidthAnswer));
        Assert.Null(g.PictureOf(tooWideAnswer));
        const int overflow = 5;
        for (int i = 0; i < RaiderGame.PictureCacheMax + overflow; i++) g.SetPicture(kept + i, side, side, new uint[side * side]);
        Assert.Null(g.PictureOf(kept + 0));
        Assert.Null(g.PictureOf(kept + (overflow - 1)));
        Assert.NotNull(g.PictureOf(kept + overflow));
        Assert.NotNull(g.PictureOf(kept + (RaiderGame.PictureCacheMax + overflow - 1)));
    }

    /// <summary>The fewest colours a built-in picture shows before it reads as blank.</summary>
    private const int MinPictureColours = 3;
    /// <summary>The side of a small render that proves a built-in picture's name is known.</summary>
    private const int ProbeSide = 8;

    [Fact]
    public void A_built_in_picture_is_drawn_from_the_games_own_data_and_asks_the_host_for_nothing()
    {
        foreach (string name in BuiltinPictures.Names)
        {
            var picture = BuiltinPictures.Picture(name);
            var post = PicturePost() with { Pictures = [picture] };
            var (g, c, asked) = Scene(post);
            Assert.Empty(asked);
            var pic = g.PictureOf(picture.Thumb);
            Assert.NotNull(pic);
            Assert.Equal((c.Shape.Pictures[0].W * CardField.PixelsPerCell, c.Shape.Pictures[0].H * CardField.PixelsPerCell), (pic.W, pic.H));
            Assert.True(pic.Px.Distinct().Count() >= MinPictureColours, $"{name} is nearly blank");
        }
        Assert.Null(BuiltinPictures.Render("no such picture", ProbeSide, ProbeSide));
    }

    [Fact]
    public void The_buttons_of_a_card_made_taller_by_four_pictures_are_under_them_and_still_take_a_press()
    {
        var post = PicturePost(pictures: FeedMapper.MaxPictures, alt: "four of them") with { Uri = "at://did:plc:h9/app.bsky.feed.post/9" };
        var (g, c, _) = PressRig.Scene(post: post);
        var like = g.Cards.ButtonBox(c, PostAction.Like);
        foreach (var b in c.Shape.Pictures) Assert.True((c.Top + b.Y + b.H) * CardField.PixelsPerCell <= like.Y, "a picture reaches the like button");
        Assert.Equal((c.Top + c.Shape.Rows - CardLayout.FooterRows) * CardField.PixelsPerCell, like.Y);
        PressRig.Press(g, c, PostAction.Like);
        Assert.NotEqual(ButtonState.Off, c.Marks.Like);
    }

    [Fact]
    public async Task The_built_in_posts_carry_built_in_pictures_that_pass_the_rules_and_need_no_host()
    {
        var page = await new TourFeedSource().GetPageAsync(string.Empty, null, TourFeedSource.PerPage, CancellationToken.None);
        var pictured = page.Posts.Where(p => p.Pictures is { Count: > 0 }).ToList();
        Assert.NotEmpty(pictured);
        Assert.Contains(pictured, p => p.Pictures!.Count == FeedMapper.MaxPictures);
        foreach (var post in pictured)
        {
            Assert.True(PostRules.Shown(post, false, textOnly: true));
            foreach (var picture in post.Pictures!)
            {
                Assert.StartsWith(RaiderGame.BuiltinPrefix, picture.Thumb);
                Assert.NotNull(BuiltinPictures.Render(picture.Thumb[RaiderGame.BuiltinPrefix.Length..], ProbeSide, ProbeSide));
            }
        }
    }
}
