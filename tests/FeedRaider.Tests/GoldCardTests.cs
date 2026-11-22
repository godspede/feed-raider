using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// A post with a thousand likes is a gold card: a gold top edge, a star with the like count on its name row, and one bomb
// for the first like or repost on it.
public class GoldCardTests
{
    private const PostAction Like = PostAction.Like, Repost = PostAction.Repost;
    private const int CardCols = CardActionRowTests.PhoneCard;
    /// <summary>The number of the one post each scene here shows.</summary>
    private const int PostNumber = 1;

    private static FeedPost GoldPost() => PressRig.Post(PostNumber, likes: Tuning.GoldLikes);

    private static uint EdgePixel(RaiderGame g, Card c) => PressRig.Draw(g).Get(g.Cards.CardLeft * CardField.PixelsPerCell, c.Top * CardField.PixelsPerCell);

    [Fact]
    public void A_post_with_a_thousand_likes_has_the_gold_edge_and_the_star_with_its_count_at_the_right_of_the_name_row()
    {
        var (g, c, _) = PressRig.Scene(post: GoldPost());
        Assert.True(c.Gold);
        Assert.Equal(0x8a7020u, CardColours.GoldEdge);
        Assert.Equal(CardColours.GoldEdge, EdgePixel(g, c));

        var s = CardLayout.Measure(GoldPost(), CardCols, false);
        var star = s.Star!.Value;
        Assert.Equal(CardLayout.StarMark + "1K", star.Text);
        Assert.Equal(s.Name.Row, star.Row);
        Assert.Equal(s.TextX + s.TextWidth, star.Col + star.Text.Length);
        Assert.True(s.Name.Col + s.Name.Text.Length < star.Col, "the name runs into the star");
    }

    [Fact]
    public void A_post_one_like_short_of_a_thousand_has_the_plain_edge_and_no_star()
    {
        var plain = PressRig.Post(PostNumber, likes: Tuning.GoldLikes - 1);
        var (g, c, _) = PressRig.Scene(post: plain);
        Assert.False(c.Gold);
        Assert.Equal(CardColours.Edge, EdgePixel(g, c));
        Assert.Null(CardLayout.Measure(plain, CardCols, false).Star);
    }

    [Fact]
    public void A_long_name_on_a_gold_card_is_cut_short_of_the_star()
    {
        var s = CardLayout.Measure(GoldPost() with { DisplayName = new string('n', CardCols) }, CardCols, false);
        Assert.StartsWith("nnn", s.Name.Text);
        Assert.True(s.Name.Col + s.Name.Text.Length < s.Star!.Value.Col);
    }

    [Fact]
    public void The_star_is_drawn_on_the_screen_in_the_gold_text_colour()
    {
        var (g, c, _) = PressRig.Scene(post: GoldPost());
        var scr = new Screen(PressRig.ScrCols, PressRig.ScrRows);
        new RaiderView().FitAndRender(g, scr);
        var star = c.Shape.Star!.Value;
        int col = g.Layout.Col0 + g.Cards.CardLeft + star.Col, row = c.Top + star.Row;
        Assert.Equal(CardLayout.StarMark[0], scr.CharAt(col, row));
        Assert.Equal(CardColours.GoldText, scr.Fg[row * scr.Cols + col]);
    }

    [Fact]
    public void A_key_press_on_a_gold_card_with_two_bombs_makes_three_and_shows_the_banger_popup()
    {
        var (g, c, played) = PressRig.Scene(post: GoldPost());
        Assert.Equal(Tuning.ShipBombs, g.Bombs);
        PressRig.Press(g, c, Like);
        Assert.Equal(Tuning.ShipBombs + 1, g.Bombs);
        Assert.Contains(g.Popups, p => p.Text == RaiderGame.GoldPopup);
        Assert.Contains(played, cue => cue.Name == SfxNames.PickupBomb);
    }

    [Fact]
    public void A_key_press_on_a_gold_card_with_three_bombs_leaves_three_and_promises_no_bomb()
    {
        var (g, c, _) = PressRig.Scene(post: GoldPost());
        PressRig.Force(g, nameof(RaiderGame.Bombs), Tuning.BombCap);
        PressRig.Press(g, c, Repost);
        Assert.Equal(Tuning.BombCap, g.Bombs);
        Assert.Contains(g.Popups, p => p.Text == RaiderGame.GoldPopupFull);
        Assert.DoesNotContain(g.Popups, p => p.Text.Contains("+BOMB"));
    }

    [Fact]
    public void A_key_press_on_a_plain_card_gives_no_bomb_and_no_banger_popup()
    {
        var (g, c, played) = PressRig.Scene();
        PressRig.Press(g, c, Like);
        Assert.Equal(Tuning.ShipBombs, g.Bombs);
        Assert.DoesNotContain(g.Popups, p => p.Text.StartsWith(RaiderGame.GoldPopupFull));
        Assert.DoesNotContain(played, cue => cue.Name == SfxNames.PickupBomb);
    }

    [Fact]
    public void The_second_button_on_the_same_gold_card_gives_no_second_bomb()
    {
        var (g, c, _) = PressRig.Scene(post: GoldPost());
        const int startBombs = 1;
        PressRig.Force(g, nameof(RaiderGame.Bombs), startBombs);
        PressRig.Press(g, c, Like);
        Assert.Equal(startBombs + 1, g.Bombs);
        PressRig.Press(g, c, Repost);
        Assert.Equal(ButtonState.Marked, c.Marks.Repost);
        Assert.Equal(startBombs + 1, g.Bombs);
    }

    [Fact]
    public void A_toggle_in_the_log_on_a_gold_post_gives_no_bomb()
    {
        var (g, c, _) = PressRig.Scene(post: GoldPost());
        g.OnLike = _ => { };
        g.Log.Add(new LogEntry(c.Post, c.Marks));
        g.Toggle(c.Post.Uri, Like);
        const int toggleTicks = 3;
        PressRig.Rest(g, toggleTicks);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        Assert.Equal(Tuning.ShipBombs, g.Bombs);
        Assert.DoesNotContain(g.Popups, p => p.Text.StartsWith(RaiderGame.GoldPopupFull));
    }

    [Fact]
    public void A_gold_card_arriving_clears_nothing_and_gives_nothing_by_itself()
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        g.SetLayout(FieldLayout.Compute(PressRig.ScrCols, PressRig.ScrRows));
        const double enemyX = 10;
        const int enemyHp = 3;
        var enemy = new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = enemyX, BaseX = enemyX, Y = g.PH / 2.0, Hp = enemyHp, MaxHp = enemyHp };
        g.Enemies.Add(enemy);
        g.Posts.Offer(GoldPost());
        g.Step();
        Assert.True(Assert.Single(g.Cards.Cards).Gold);
        Assert.Contains(enemy, g.Enemies);
        Assert.Equal(enemyHp, enemy.Hp);
        Assert.Equal(Tuning.ShipBombs, g.Bombs);
        Assert.Equal(0, g.ScreenFlash);
    }

    [Theory]
    [InlineData(PressRig.ScrCols, PressRig.ScrRows)]
    [InlineData(34, 60)]
    public async Task The_start_pages_tour_likes_a_gold_post_which_pays_a_bomb_and_brings_in_its_authors_reply(int cols, int rows)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = false, Charmed = true };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        var tourFeed = new TourFeedSource();
        var lookups = new List<Task>();
        g.ThreadWanted = p => lookups.Add(RaiderFeeds.ThreadAsync(g, tourFeed, p));
        var page = (await tourFeed.GetPageAsync("tour", null, TourFeedSource.PerPage, default)).Posts;
        foreach (var p in page) g.Posts.Offer(p);
        var pilot = new Autopilot(EnemyKit.PlainSeed);
        bool paid = false;
        for (int t = 0; t < Autopilot.LikeUntil && !g.Cards.Cards.Any(c => c.Thread); t++)
        {
            pilot.Drive(g);
            g.Step();
            paid |= g.Popups.Any(p => p.Text == RaiderGame.GoldPopup);
            await Task.WhenAll(lookups);
        }
        Assert.True(paid, "no gold card paid");
        var thread = Assert.Single(g.Cards.Cards, c => c.Thread);
        Assert.True(PostRules.Gold(Assert.Single(page, p => p.Ref == thread.Post.ReplyParent)));
    }
}
