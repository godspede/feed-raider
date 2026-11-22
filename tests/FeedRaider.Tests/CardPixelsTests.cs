using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The posts of the feed drawn as pixels in the background: each card's slab, edge, avatar and two idle buttons, under the
// fight. These draw through the view's FitAndRender and read the field it drew, before the shake and the
// cell reduction, the way RaiderViewPixelTests does.
public class CardPixelsTests
{
    private const int ScrCols = CardRig.ScrCols, ScrRows = CardRig.ScrRows;
    private const uint Slab = CardColours.Slab, Edge = CardColours.Edge;
    private const double LuminanceCap = CardColours.MaxIdleLuminance;
    /// <summary>A card row far past the bottom of any field, so nothing of the card is drawn.</summary>
    private const int OffField = 10_000;
    /// <summary>The avatar's side in pixels and its pixel count.</summary>
    private const int AvatarSide = CardLayout.AvatarCells * CardField.PixelsPerCell, AvatarPixels = AvatarSide * AvatarSide;
    /// <summary>The pixel at the centre of the avatar box, which the tinted disc covers and its corner does not.</summary>
    private const int DiscCentrePixel = (AvatarSide / 2 - 1) * AvatarSide + AvatarSide / 2 - 1;
    /// <summary>A dimmed avatar still has a pixel brighter than this: dimmed, not blacked out.</summary>
    private const double DimmedNotBlackLuminance = 0.2;
    /// <summary>The card row that puts the card under the units the world pass test adds.</summary>
    private const int CardUnderUnitsTop = 8;
    /// <summary>The card row in the shake test, and a plain row in the middle of the card.</summary>
    private const int ShakeCardTop = 6, PlainCardRow = 6;
    /// <summary>The ship's y in the world pass test, among the units it is set beside.</summary>
    private const double ShipAmongUnitsY = 40.0;
    /// <summary>The rows the shake test skips past the card's bottom and above the field's bottom when it looks for cells that moved.</summary>
    private const int BelowCardMarginRows = 3, FieldBottomMarginRows = 4;
    /// <summary>How many steps the stars drift between the two pictures of the sky.</summary>
    private const int StarDriftSteps = 300;

    private static FeedPost Post(int id, bool liked = false, bool reposted = false, string? text = null) =>
        new($"at://did:plc:c{id}/app.bsky.feed.post/{id}", $"cid{id}", $"did:plc:c{id}", $"c{id}.test",
            text ?? $"post number {id} says a few ordinary words and ends with tail{id}word", PressRig.OrdinaryLikes, PressRig.OrdinaryReposts, 0, null, null,
            DisplayName: "Display Name", AlreadyLiked: liked, AlreadyReposted: reposted);

    /// <summary>A game with one card on the field and nothing else on it, the ship parked off the left edge.</summary>
    private static (RaiderGame Game, Card Card) Scene(FeedPost? post = null, int top = CardRig.DefaultCardTop)
    {
        var game = CardRig.Game();
        game.Posts.Offer(post ?? Post(1));
        game.Step();
        CardRig.ClearAndParkShip(game);
        var card = game.Cards.Cards[0];
        card.Top = top;
        return (game, card);
    }

    /// <summary>Renders and returns the field the view drew, before the screen shake and the cell reduction.</summary>
    private static Surface Draw(RaiderGame game, Screen? scr = null)
    {
        var view = new RaiderView();
        view.FitAndRender(game, scr ?? new Screen(ScrCols, ScrRows));
        return view.Field!;
    }

    /// <summary>The same game with the card moved clear of the field: what is behind it.</summary>
    private static Surface Without(RaiderGame game, Card card)
    {
        int top = card.Top;
        card.Top = OffField;
        var field = Draw(game);
        card.Top = top;
        return field;
    }

    private static int X0(RaiderGame game) => game.Cards.CardLeft * CardField.PixelsPerCell;
    private static int Y0(Card card) => card.Top * CardField.PixelsPerCell;
    private static int Width(RaiderGame game) => game.Cards.CardCols * CardField.PixelsPerCell;
    private static int Height(Card card) => card.Shape.Rows * CardField.PixelsPerCell;

    private static bool In(CardRect rect, int cx, int cy) => cx >= rect.X && cx < rect.X + rect.W && cy >= rect.Y && cy < rect.Y + rect.H;

    /// <summary>True for a cell of the card that the avatar or a button takes, which a slab check passes over.</summary>
    private static bool OnAvatarOrButton(Card card, int cx, int cy) =>
        In(card.Shape.Avatar, cx, cy) || In(card.Shape.LikeButton, cx, cy) || In(card.Shape.RepostButton, cx, cy);

    /// <summary>True for a pixel of the field inside the card's rectangle.</summary>
    private static bool OnCard(RaiderGame game, Card card, int x, int y) =>
        x >= X0(game) && x < X0(game) + Width(game) && y >= Y0(card) && y < Y0(card) + Height(card);

    private static string OutsideTheCard(int x, int y) => $"pixel {x},{y} outside the card changed";

    [Fact]
    public void A_cards_slab_covers_exactly_its_rectangle_in_the_slab_colour_with_the_edge_line_on_its_top_pixel_row()
    {
        var (game, card) = Scene();
        var field = Draw(game);
        var behind = Without(game, card);
        int x0 = X0(game), y0 = Y0(card), width = Width(game);
        for (int y = 0; y < field.H; y++)
            for (int x = 0; x < field.W; x++)
            {
                if (!OnCard(game, card, x, y)) { Assert.True(field.Get(x, y) == behind.Get(x, y), OutsideTheCard(x, y)); continue; }
                int cx = (x - x0) / CardField.PixelsPerCell, cy = (y - y0) / CardField.PixelsPerCell;
                if (OnAvatarOrButton(card, cx, cy)) continue;
                Assert.Equal(y == y0 ? Edge : Slab, field.Get(x, y));
            }
        Assert.Equal(Edge, field.Get(x0, y0));
        Assert.Equal(Edge, field.Get(x0 + width - 1, y0));
        Assert.Equal(Slab, field.Get(x0, y0 + 1));
    }

    [Fact]
    public void Each_slab_cell_is_four_pixels_of_one_colour_at_every_row_the_card_passes_through()
    {
        var (game, card) = Scene();
        for (int top = -card.Shape.Rows; top < game.Layout.Rows; top++)
        {
            card.Top = top;
            var field = Draw(game);
            for (int cy = 1; cy < card.Shape.Rows; cy++)
            {
                int row = top + cy;
                if (row < RaiderView.HudRows || row >= game.Layout.Rows) continue;
                for (int cx = 0; cx < card.Shape.Cols; cx++)
                {
                    if (OnAvatarOrButton(card, cx, cy)) continue;
                    Assert.True(Cell(game, card, field, cx, cy).All(pixel => pixel == Slab),
                        $"cell {cx},{cy} of the card at top {top} is not one slab colour");
                }
            }
        }
    }

    private static uint[] Picture(Func<int, uint> at) => Enumerable.Range(0, AvatarPixels).Select(at).ToArray();

    private static Surface Avatar(RaiderGame game, Card card, Surface field)
    {
        var avatar = new Surface(AvatarSide, AvatarSide);
        for (int y = 0; y < AvatarSide; y++)
            for (int x = 0; x < AvatarSide; x++)
                avatar.Set(x, y, field.Get(X0(game) + CardField.PixelsPerCell * card.Shape.Avatar.X + x, Y0(card) + CardField.PixelsPerCell * card.Shape.Avatar.Y + y));
        return avatar;
    }

    [Fact]
    public void The_avatar_is_in_columns_one_to_four_rows_one_to_four_each_pixel_mixed_a_third_of_the_way_toward_the_slab()
    {
        var (game, card) = Scene();
        var pic = Picture(i => Surface.Pack(40 + i, 90 + i, 130 + i / 2));
        game.SetAvatar(card.Post.AuthorDid, pic);
        var avatar = Avatar(game, card, Draw(game));
        Assert.Equal(new CardRect(1, 1, CardLayout.AvatarCells, CardLayout.AvatarCells), card.Shape.Avatar);
        for (int i = 0; i < AvatarPixels; i++) Assert.Equal(Surface.Mix(pic[i], Slab, CardColours.AvatarDim), avatar.P[i]);
    }

    [Fact]
    public void A_pure_white_avatar_stays_under_the_luminance_cap_and_so_does_any_bright_one()
    {
        var (game, card) = Scene();
        uint[] bright = [0xffffff, 0xffff00, 0x00ffff];
        game.SetAvatar(card.Post.AuthorDid, Picture(i => bright[i % bright.Length]));
        var avatar = Avatar(game, card, Draw(game));
        foreach (uint pixel in avatar.P) Assert.True(Surface.Luminance(pixel) <= LuminanceCap, $"{pixel:x6} is brighter than the cap");
        Assert.Contains(avatar.P, pixel => Surface.Luminance(pixel) > DimmedNotBlackLuminance);
    }

    [Fact]
    public void Before_the_picture_arrives_the_tinted_disc_is_there_and_the_picture_replaces_it_on_the_frame_it_arrives()
    {
        var (game, card) = Scene();
        var disc = Avatar(game, card, Draw(game));
        Assert.True(disc.P.Distinct().Count() >= 2, "a disc and its ground");
        Assert.NotEqual(disc.P[0], disc.P[DiscCentrePixel]);
        Assert.All(disc.P, pixel => Assert.True(Surface.Luminance(pixel) <= LuminanceCap));
        var pic = Picture(i => Surface.Pack(i * 3, 200 - i, 60));
        game.SetAvatar(card.Post.AuthorDid, pic);
        var now = Avatar(game, card, Draw(game));
        for (int i = 0; i < AvatarPixels; i++) Assert.Equal(Surface.Mix(pic[i], Slab, CardColours.AvatarDim), now.P[i]);
    }

    /// <summary>Checks every pixel of a button's box: the lit ground under the icon and count, one cell past each end, when
    /// <paramref name="lit"/>, and the bare slab everywhere else.</summary>
    private static void CheckButton(RaiderGame game, Card card, Surface field, PostAction action, bool lit)
    {
        var button = action == PostAction.Like ? card.Shape.LikeButton : card.Shape.RepostButton;
        uint ground = action == PostAction.Like ? CardColours.LikeButtonLit : CardColours.RepostButtonLit;
        var (col, cols) = CardPainter.ActionSpan(card, action);
        var row = CardPainter.IconAt(card, action).Row;
        for (int cy = button.Y; cy < button.Y + button.H; cy++)
            for (int cx = button.X; cx < button.X + button.W; cx++)
            {
                bool onGround = lit && cy == row && cx >= col - CardPainter.LitPadCols && cx < col + cols + CardPainter.LitPadCols;
                var cell = Cell(game, card, field, cx, cy);
                Assert.True(cell.All(pixel => pixel == (onGround ? ground : Slab)), $"{action} button cell {cx},{cy}");
            }
    }

    private static uint[] Cell(RaiderGame game, Card card, Surface field, int cx, int cy)
    {
        int px = X0(game) + cx * CardField.PixelsPerCell, py = Y0(card) + cy * CardField.PixelsPerCell;
        return [.. from dy in Enumerable.Range(0, CardField.PixelsPerCell) from dx in Enumerable.Range(0, CardField.PixelsPerCell) select field.Get(px + dx, py + dy)];
    }

    [Fact]
    public void At_rest_the_two_buttons_boxes_are_bare_slab_under_an_icon_and_a_count_written_as_text()
    {
        var (game, card) = Scene();
        var field = Draw(game);
        CheckButton(game, card, field, PostAction.Like, lit: false);
        CheckButton(game, card, field, PostAction.Repost, lit: false);
        Assert.Equal(CardLayout.HeartOff.ToString(), CardPainter.IconAt(card, PostAction.Like).Text);
        Assert.Equal(CardLayout.LoopMark.ToString(), CardPainter.IconAt(card, PostAction.Repost).Text);
        Assert.Equal(CardColours.LikeIcon, CardPainter.IconColour(ButtonState.Off, PostAction.Like, game.Frame));
        Assert.Equal(CardColours.RepostIcon, CardPainter.IconColour(ButtonState.Off, PostAction.Repost, game.Frame));
    }

    [Fact]
    public void A_post_the_viewer_already_liked_arrives_with_its_heart_lit_and_one_already_reposted_with_its_loop_lit()
    {
        var (game, card) = Scene(Post(2, liked: true));
        var field = Draw(game);
        CheckButton(game, card, field, PostAction.Like, lit: true);
        CheckButton(game, card, field, PostAction.Repost, lit: false);
        Assert.Equal(CardLayout.HeartOn.ToString(), CardPainter.IconAt(card, PostAction.Like).Text);
        Assert.NotEqual(Slab, CardColours.LikeButtonLit);

        var (repostedGame, repostedCard) = Scene(Post(3, reposted: true));
        var repostedField = Draw(repostedGame);
        CheckButton(repostedGame, repostedCard, repostedField, PostAction.Like, lit: false);
        CheckButton(repostedGame, repostedCard, repostedField, PostAction.Repost, lit: true);
        Assert.NotEqual(Slab, CardColours.RepostButtonLit);
    }

    [Fact]
    public void A_card_half_off_the_top_or_the_bottom_is_clipped_and_the_two_hud_rows_pixels_are_what_they_were()
    {
        var (game, card) = Scene();
        int rows = card.Shape.Rows;
        foreach (int top in new[] { -rows / 2, -2, 1, game.Layout.Rows - rows / 2, game.Layout.Rows - 1 })
        {
            card.Top = top;
            var field = Draw(game);
            var behind = Without(game, card);
            for (int y = 0; y < CardPainter.HudPixelRows; y++)
                for (int x = 0; x < field.W; x++)
                    Assert.True(field.Get(x, y) == behind.Get(x, y), $"HUD pixel {x},{y} changed with the card at {top}");
            int changed = 0;
            for (int y = 0; y < field.H; y++)
                for (int x = 0; x < field.W; x++)
                    if (field.Get(x, y) != behind.Get(x, y))
                    {
                        changed++;
                        Assert.True(OnCard(game, card, x, y), OutsideTheCard(x, y));
                    }
            Assert.True(changed > 0, $"nothing of the card at {top} was drawn");
        }
    }

    /// <summary>Positions where the picture with something drawn differs from the one without.</summary>
    private static HashSet<int> Changed(Surface with, Surface without)
    {
        var changed = new HashSet<int>();
        for (int i = 0; i < with.P.Length; i++) if (with.P[i] != without.P[i]) changed.Add(i);
        return changed;
    }

    [Fact]
    public void Every_unit_bullet_pickup_ring_and_particle_shows_over_a_slab_the_same_pixels_of_the_world_pass_as_over_the_sky()
    {
        var adds = new (string Name, Action<RaiderGame> Add, bool Opaque)[]
        {
            ("ship shot", game => game.Bullets.Add(new Bullet { X = 40, Y = 40, Friendly = true }), true),
            ("enemy bolt", game => game.Bullets.Add(new Bullet { X = 60, Y = 50, Friendly = false }), true),
            ("enemy", game => { var enemy = game.SpawnKind(EnemyKind.Gunship, EnemyKit.MidWidth); enemy.X = 64; enemy.Y = 36; enemy.Grace = 0; enemy.Age = 0; enemy.Flash = 0; }, true),
            ("spark", game => game.Particles.Add(new Particle { X = 50, Y = 44, Life = CardRig.ParticleLife, MaxLife = CardRig.ParticleLife, Colour = CardRig.ParticleColour, Size = CardRig.ParticleSize }), true),
            ("ring", game => game.Rings.Add(new Ring { X = 70, Y = 40, R0 = CardRig.RingStartRadius, R1 = CardRig.RingEndRadius, Life = CardRig.RingLife, Max = CardRig.RingMaxLife, Colour = Palette.Crimson }), false),
            ("pickup", game => game.Pickups.Add(new Pickup { Kind = PickupKind.Shield, X = 80, Y = 44 }), false),
            ("ship", game => { PressRig.Force(game, nameof(RaiderGame.PX), CardRig.ShipOverCardX); PressRig.Force(game, nameof(RaiderGame.PY), ShipAmongUnitsY); }, false),
        };
        foreach (var (name, add, opaque) in adds)
        {
            var (game, card) = Scene(top: CardUnderUnitsTop);
            var plain = CardRig.Game();
            plain.Step();
            CardRig.ClearAndParkShip(plain);
            add(game); add(plain);
            var over = Draw(game);
            var onlyUnits = Draw(plain);
            var cardless = Without(game, card);
            Assert.True(onlyUnits.P.SequenceEqual(cardless.P), $"{name}: the two cardless fields differ");
            CardRig.ClearAndParkShip(game);
            var slabOnly = Draw(game);
            var drawn = Changed(onlyUnits, Draw(CardRig.ClearAndParkShip(plain)));
            Assert.NotEmpty(drawn);
            foreach (int index in drawn.Where(pixel => Inside(game, card, pixel, over.W)))
            {
                Assert.NotEqual(slabOnly.P[index], over.P[index]);
                if (opaque) Assert.Equal(onlyUnits.P[index], over.P[index]);
            }
        }
    }

    private static bool Inside(RaiderGame game, Card card, int index, int width) => OnCard(game, card, index % width, index / width);

    private static bool Uniform(Screen screen, int col, int row, uint colour) =>
        screen.BgAt(col, row) == colour && (screen.Fg[row * screen.Cols + col] == colour || screen.CharAt(col, row) == ' ');

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void A_shake_of_one_pixel_leaves_the_slabs_on_their_cells_and_of_two_pixels_moves_them_one_cell(int pixels, int cells)
    {
        var (game, card) = Scene(top: ShakeCardTop);
        PressRig.Force(game, nameof(RaiderGame.PX), CardRig.ShipOverCardX); PressRig.Force(game, nameof(RaiderGame.PY), CardRig.ShipBelowCardY);
        var still = new Screen(ScrCols, ScrRows);
        Draw(game, still);
        CardRig.ShakeBy(game, pixels);
        var (shakeX, shakeY) = game.ShakeOffset();
        var shaken = new Screen(ScrCols, ScrRows);
        Draw(game, shaken);
        int col0 = game.Layout.Col0, left = col0 + game.Cards.CardLeft, dx = cells * Math.Sign(shakeX), dy = cells * Math.Sign(shakeY);
        int row = card.Top + PlainCardRow + dy;
        Assert.True(Uniform(still, left, card.Top + PlainCardRow, Slab));
        Assert.True(Uniform(shaken, left + dx, row, Slab), "the card's first column is not a whole slab cell where it should land");
        Assert.False(Uniform(shaken, left + dx - 1, row, Slab), "slab spills past the card's edge");
        // the ship, the stars and the sky shake by the pixel, so cells below the card change
        bool moved = false;
        for (int belowRow = card.Top + card.Shape.Rows + BelowCardMarginRows; belowRow < game.Layout.Rows - FieldBottomMarginRows; belowRow++)
            for (int col = col0; col < col0 + game.Layout.PCols; col++)
            {
                int at = belowRow * still.Cols + col;
                moved |= still.Ch[at] != shaken.Ch[at] || still.Fg[at] != shaken.Fg[at] || still.Bg[at] != shaken.Bg[at];
            }
        Assert.True(moved, "nothing but the cards moved with the shake");
    }

    [Fact]
    public void With_no_posts_the_background_is_the_sky_and_only_the_stars_move()
    {
        Surface At(int steps)
        {
            var game = CardRig.Game();
            for (int step = 0; step < steps; step++) game.Step();
            return Draw(CardRig.ClearAndParkShip(game));
        }
        var early = At(1);
        var late = At(StarDriftSteps);
        int stars = new Background(early.W, early.H, RaiderView.DefaultBackgroundSeed).StarTotal;
        int differing = Changed(early, late).Count;
        Assert.InRange(differing, 1, 2 * stars);   // each star lost from one pixel and found in another
    }
}
