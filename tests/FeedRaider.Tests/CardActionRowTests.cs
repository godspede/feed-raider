using System.Text;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The action row as a player sees it, on a phone 412 pixels wide and on the desktop: every state of a button reads differently
// from the button at rest, the count follows its icon, and the box the ship rests on is larger than the icon it surrounds.
public class CardActionRowTests
{
    private const PostAction Like = PostAction.Like, Repost = PostAction.Repost;

    // the screen of a 412 pixel phone and the card it gives, then the desktop's
    private const int PhoneCols = AtlasKeyTests.PhoneCols, PhoneRows = AtlasKeyTests.PhoneRows;
    internal const int PhoneCard = 52;
    private const int DesktopCols = PressRig.ScrCols, DesktopRows = PressRig.ScrRows, DesktopCard = 60;

    /// <summary>The number of the one post each scene here shows.</summary>
    private const int PostNumber = 1;
    /// <summary>The name the button's look with nothing done to it is kept under.</summary>
    private const string AtRest = "at rest";

    private static (RaiderGame G, Card C) Scene(int cols, int rows, bool signedIn = true, FeedPost? post = null)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = signedIn, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.Posts.Offer(post ?? PressRig.Post(PostNumber));
        g.Step();
        var card = g.Cards.Cards[0];
        card.Top = CardRig.DefaultCardTop;
        PressRig.Away(g);
        PressRig.Sweep(g);
        return (g, card);
    }

    /// <summary>Renders the card alone at its place: the ship is moved off it and everything else is taken off the field.</summary>
    private static Screen Render(RaiderGame g, Card c, int cols, int rows)
    {
        PressRig.Away(g);
        PressRig.Sweep(g);
        g.Particles.Clear(); g.Rings.Clear();
        c.Top = CardRig.DefaultCardTop;
        var scr = new Screen(cols, rows);
        new RaiderView().FitAndRender(g, scr);
        return scr;
    }

    /// <summary>What the screen shows in every cell of a button's box: the character and its two colours.</summary>
    private static string ButtonLook(RaiderGame g, Card c, PostAction a, int cols, int rows)
    {
        var scr = Render(g, c, cols, rows);
        var box = a == Like ? c.Shape.LikeButton : c.Shape.RepostButton;
        var look = new StringBuilder();
        for (int cy = box.Y; cy < box.Y + box.H; cy++)
            for (int cx = box.X; cx < box.X + box.W; cx++)
            {
                int col = g.Layout.Col0 + g.Cards.CardLeft + cx, row = c.Top + cy;
                look.Append(scr.CharAt(col, row)).Append($"{scr.Fg[row * scr.Cols + col]:x6}{scr.BgAt(col, row):x6} ");
            }
        return look.ToString();
    }

    /// <summary>The characters the screen shows where the card places <paramref name="text"/>.</summary>
    private static string ShownText(RaiderGame g, Card c, Screen scr, CardText text) =>
        new(Enumerable.Range(0, text.Text.Length).Select(i => scr.CharAt(g.Layout.Col0 + g.Cards.CardLeft + text.Col + i, c.Top + text.Row)).ToArray());

    private static void AnswerNothing(RaiderGame g, PostAction a)
    {
        if (a == Like) g.OnLike = _ => { };
        else g.OnRepost = _ => { };
    }

    private static Grants GrantsWithout(PostAction a) =>
        a == Like ? Grants.All with { Like = false, Unlike = false } : Grants.All with { Repost = false, Unrepost = false };

    [Theory]
    [InlineData(PhoneCols, PhoneRows, Like)]
    [InlineData(PhoneCols, PhoneRows, Repost)]
    [InlineData(DesktopCols, DesktopRows, Like)]
    [InlineData(DesktopCols, DesktopRows, Repost)]
    public void Lit_pending_marked_and_locked_each_look_different_from_the_button_at_rest_and_from_each_other(int cols, int rows, PostAction a)
    {
        var looks = new Dictionary<string, string>();

        var (rest, restCard) = Scene(cols, rows);
        looks[AtRest] = ButtonLook(rest, restCard, a, cols, rows);

        var (lit, litCard) = Scene(cols, rows, post: PressRig.Post(PostNumber, liked: a == Like, reposted: a == Repost));
        Assert.Equal(ButtonState.Lit, litCard.Marks.Of(a));
        looks["lit"] = ButtonLook(lit, litCard, a, cols, rows);

        var (pending, pendingCard) = Scene(cols, rows);
        AnswerNothing(pending, a);
        PressRig.Press(pending, pendingCard, a);
        Assert.Equal(ButtonState.Pending, pendingCard.Marks.Of(a));
        PressRig.Rest(pending, LikePressTests.SettledTicks);
        looks["pending"] = ButtonLook(pending, pendingCard, a, cols, rows);

        var (marked, markedCard) = Scene(cols, rows, signedIn: false);
        PressRig.Press(marked, markedCard, a);
        Assert.Equal(ButtonState.Marked, markedCard.Marks.Of(a));
        PressRig.Rest(marked, LikePressTests.SettledTicks);
        looks["marked"] = ButtonLook(marked, markedCard, a, cols, rows);

        var (locked, lockedCard) = Scene(cols, rows);
        locked.Grants = GrantsWithout(a);
        Assert.True(CardPainter.Locked(locked, lockedCard, a));
        looks["locked"] = ButtonLook(locked, lockedCard, a, cols, rows);

        foreach (var (state, look) in looks)
            if (state != AtRest) Assert.True(look != looks[AtRest], $"{a} {state} looks like the button {AtRest}");
        Assert.Equal(looks.Count, looks.Values.Distinct().Count());
    }

    [Theory]
    [InlineData(PhoneCols, PhoneRows, Like)]
    [InlineData(PhoneCols, PhoneRows, Repost)]
    [InlineData(DesktopCols, DesktopRows, Like)]
    [InlineData(DesktopCols, DesktopRows, Repost)]
    public void A_pending_button_has_two_looks_and_changes_from_one_to_the_other_every_blink(int cols, int rows, PostAction a)
    {
        var (g, c) = Scene(cols, rows);
        AnswerNothing(g, a);
        PressRig.Press(g, c, a);
        PressRig.Rest(g, LikePressTests.SettledTicks);
        const int pendingLooks = 2, watchedBlinks = 2;
        var seen = new List<string>();
        for (int i = 0; i < watchedBlinks * CardPainter.BlinkFrames; i++)
        {
            seen.Add(ButtonLook(g, c, a, cols, rows));
            PressRig.Rest(g, PressRig.OneTick);
        }
        Assert.Equal(pendingLooks, seen.Distinct().Count());
        Assert.InRange(Enumerable.Range(1, seen.Count - 1).Count(i => seen[i] != seen[i - 1]), watchedBlinks - 1, watchedBlinks);
    }

    [Theory]
    [InlineData(PhoneCols, PhoneRows, Like, LikePressTests.CountBefore, LikePressTests.CountAfter)]
    [InlineData(PhoneCols, PhoneRows, Repost, RepostPressTests.RepostsBefore, RepostPressTests.RepostsAfter)]
    [InlineData(DesktopCols, DesktopRows, Like, LikePressTests.CountBefore, LikePressTests.CountAfter)]
    [InlineData(DesktopCols, DesktopRows, Repost, RepostPressTests.RepostsBefore, RepostPressTests.RepostsAfter)]
    public void A_press_signed_in_writes_the_count_one_higher_one_space_after_the_icon(int cols, int rows, PostAction a, string before, string after)
    {
        var (g, c) = Scene(cols, rows);
        AnswerNothing(g, a);
        var icon = CardPainter.IconAt(c, a);
        var was = CardPainter.CountAt(c, a);
        Assert.Equal(before, was.Text);
        Assert.Equal(before, ShownText(g, c, Render(g, c, cols, rows), was));

        PressRig.Press(g, c, a);
        PressRig.Rest(g, LikePressTests.SettledTicks);
        var count = CardPainter.CountAt(c, a);
        Assert.Equal(after, count.Text);
        Assert.Equal(icon.Row, count.Row);
        const int oneSpace = 1;
        Assert.Equal(icon.Col + icon.Text.Length + oneSpace, count.Col);
        Assert.Equal(after, ShownText(g, c, Render(g, c, cols, rows), count));
    }

    // each row: the screen, the card it gives, and the width of a button's box in field pixels
    [Theory]
    [InlineData(PhoneCols, PhoneRows, PhoneCard, 16)]
    [InlineData(DesktopCols, DesktopRows, DesktopCard, 22)]
    public void A_buttons_box_is_wider_than_the_ship_and_three_rows_tall_and_the_repost_box_sits_right_of_the_hearts_on_its_row(int cols, int rows, int card, int boxWidth)
    {
        var (g, c) = Scene(cols, rows);
        Assert.Equal(card, g.Cards.CardCols);
        var like = g.Cards.ButtonBox(c, Like);
        var repost = g.Cards.ButtonBox(c, Repost);
        foreach (var box in (PixelRect[])[like, repost])
        {
            Assert.Equal(boxWidth, box.W);
            Assert.Equal(CardLayout.FooterRows * CardField.PixelsPerCell, box.H);
            Assert.True(box.W > g.Ship.W);
            Assert.True(box.W > CardLayout.IconWidthPx && box.H >= CardLayout.IconHeightPx);
        }
        Assert.Equal(like.Y, repost.Y);
        Assert.True(repost.X >= like.X + like.W, "the repost box starts right of the heart's");
    }

    /// <summary>The middle of a span of field pixels <paramref name="length"/> long from <paramref name="start"/>.</summary>
    internal static double Middle(int start, int length) => start + length / 2.0;
}
