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
    private const int DesktopCols = HoldRig.ScrCols, DesktopRows = HoldRig.ScrRows, DesktopCard = 60;

    // a third of a hold: the glow is up and nothing is finished
    private const int PartHeldTicks = Tuning.HoldFrames / 3;
    /// <summary>The number of the one post each scene here shows.</summary>
    private const int PostNumber = 1;
    /// <summary>The name the button's look with nothing done to it is kept under.</summary>
    private const string AtRest = "at rest";

    private static (RaiderGame G, Card C) Scene(int cols, int rows, bool signedIn = true, FeedPost? post = null)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = signedIn, BossGates = false };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.Posts.Offer(post ?? HoldRig.Post(PostNumber));
        g.Step();
        var card = g.Cards.Cards[0];
        card.Top = CardRig.DefaultCardTop;
        HoldRig.Away(g);
        HoldRig.Sweep(g);
        return (g, card);
    }

    /// <summary>Renders the card alone at its place: the ship is moved off it and everything else is taken off the field.</summary>
    private static Screen Render(RaiderGame g, Card c, int cols, int rows)
    {
        HoldRig.Away(g);
        HoldRig.Sweep(g);
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
    public void Held_lit_pending_marked_and_locked_each_look_different_from_the_button_at_rest_and_from_each_other(int cols, int rows, PostAction a)
    {
        var looks = new Dictionary<string, string>();

        var (rest, restCard) = Scene(cols, rows);
        looks[AtRest] = ButtonLook(rest, restCard, a, cols, rows);

        var (held, heldCard) = Scene(cols, rows);
        HoldRig.Hold(held, heldCard, a, PartHeldTicks);
        Assert.Equal(PartHeldTicks, heldCard.Marks.Progress(a));
        looks["held"] = ButtonLook(held, heldCard, a, cols, rows);

        var (lit, litCard) = Scene(cols, rows, post: HoldRig.Post(PostNumber, liked: a == Like, reposted: a == Repost));
        Assert.Equal(ButtonState.Lit, litCard.Marks.Of(a));
        looks["lit"] = ButtonLook(lit, litCard, a, cols, rows);

        var (pending, pendingCard) = Scene(cols, rows);
        AnswerNothing(pending, a);
        HoldRig.Hold(pending, pendingCard, a, Tuning.HoldFrames);
        Assert.Equal(ButtonState.Pending, pendingCard.Marks.Of(a));
        HoldRig.Rest(pending, LikeHoldTests.SettledTicks);
        looks["pending"] = ButtonLook(pending, pendingCard, a, cols, rows);

        var (marked, markedCard) = Scene(cols, rows, signedIn: false);
        HoldRig.Hold(marked, markedCard, a, Tuning.HoldFrames);
        Assert.Equal(ButtonState.Marked, markedCard.Marks.Of(a));
        HoldRig.Rest(marked, LikeHoldTests.SettledTicks);
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
        HoldRig.Hold(g, c, a, Tuning.HoldFrames);
        HoldRig.Rest(g, LikeHoldTests.SettledTicks);
        const int pendingLooks = 2, watchedBlinks = 2;
        var seen = new List<string>();
        for (int i = 0; i < watchedBlinks * CardPainter.BlinkFrames; i++)
        {
            seen.Add(ButtonLook(g, c, a, cols, rows));
            HoldRig.Rest(g, HoldRig.OneTick);
        }
        Assert.Equal(pendingLooks, seen.Distinct().Count());
        Assert.InRange(Enumerable.Range(1, seen.Count - 1).Count(i => seen[i] != seen[i - 1]), watchedBlinks - 1, watchedBlinks);
    }

    [Theory]
    [InlineData(PhoneCols, PhoneRows, Like, LikeHoldTests.CountBefore, LikeHoldTests.CountAfter)]
    [InlineData(PhoneCols, PhoneRows, Repost, RepostHoldTests.RepostsBefore, RepostHoldTests.RepostsAfter)]
    [InlineData(DesktopCols, DesktopRows, Like, LikeHoldTests.CountBefore, LikeHoldTests.CountAfter)]
    [InlineData(DesktopCols, DesktopRows, Repost, RepostHoldTests.RepostsBefore, RepostHoldTests.RepostsAfter)]
    public void A_hold_finished_signed_in_writes_the_count_one_higher_one_space_after_the_icon(int cols, int rows, PostAction a, string before, string after)
    {
        var (g, c) = Scene(cols, rows);
        AnswerNothing(g, a);
        var icon = CardPainter.IconAt(c, a);
        var was = CardPainter.CountAt(c, a);
        Assert.Equal(before, was.Text);
        Assert.Equal(before, ShownText(g, c, Render(g, c, cols, rows), was));

        HoldRig.Hold(g, c, a, Tuning.HoldFrames);
        HoldRig.Rest(g, LikeHoldTests.SettledTicks);
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
    public void A_buttons_box_is_wider_than_the_ship_and_three_rows_tall_and_a_ship_between_the_two_holds_neither(int cols, int rows, int card, int boxWidth)
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
        int gap = repost.X - (like.X + like.W);
        Assert.True(gap > g.Ship.W, $"{gap} pixels between the boxes and a ship {g.Ship.W} wide");

        HoldRig.Force(g, nameof(RaiderGame.PX), Middle(like.X + like.W, gap));
        HoldRig.Force(g, nameof(RaiderGame.PY), Middle(like.Y, like.H));
        Assert.Null(HoldTracker.Touching(g));
        HoldRig.Park(g, c, Like);
        Assert.Equal((c, Like), HoldTracker.Touching(g));
        HoldRig.Park(g, c, Repost);
        Assert.Equal((c, Repost), HoldTracker.Touching(g));
    }

    /// <summary>The middle of a span of field pixels <paramref name="length"/> long from <paramref name="start"/>.</summary>
    internal static double Middle(int start, int length) => start + length / 2.0;
}
