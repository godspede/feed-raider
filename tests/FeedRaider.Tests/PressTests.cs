using System.Reflection;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>What the like and repost tests share: a game with one card, a ship that can be parked on a button and press the keys,
/// and a record of the sounds.</summary>
internal static class PressRig
{
    public const int ScrCols = EnemyKit.BigCols, ScrRows = EnemyKit.BigRows;
    /// <summary>Frozen frames a wait may step through without counting.</summary>
    private const int StallSlackTicks = 200;
    /// <summary>The most ticks a wait runs before it gives up.</summary>
    private const int MaxWaitTicks = 600;
    /// <summary>One simulation tick, for a wait that looks again after every tick.</summary>
    public const int OneTick = 1;
    /// <summary>How far in from the bottom right corner the ship waits when it is kept off the card.</summary>
    private const double AwayInset = 10.0;

    public readonly record struct Cue(string Name, int Step);

    /// <summary>A like count far under the gold line, for a post that is nothing special.</summary>
    public const int OrdinaryLikes = 10;

    /// <summary>The repost count every post here carries.</summary>
    public const int OrdinaryReposts = 4;

    public static FeedPost Post(int n, bool liked = false, bool reposted = false, int likes = OrdinaryLikes) =>
        new($"at://did:plc:h{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:h{n}", $"h{n}.test",
            $"post number {n} says a few ordinary words and then a few more", likes, OrdinaryReposts, 0, null, null,
            DisplayName: "Display Name", AlreadyLiked: liked, AlreadyReposted: reposted);

    public static void Force(object o, string member, object value)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var p = o.GetType().GetProperty(member, all);
        if (p is not null) p.GetSetMethod(true)!.Invoke(o, [value]);
        else o.GetType().GetField(member, all)!.SetValue(o, value);
    }

    /// <summary>The row the card is set to unless a test says otherwise: on the field, below the score bar.</summary>
    private const int CardTop = 6;

    /// <summary>A game with one card on the field, nothing else on it, and the ship far from the card.</summary>
    public static (RaiderGame G, Card C, List<Cue> Played) Scene(bool signedIn = true, FeedPost? post = null, int top = CardTop)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = signedIn, BossGates = false };
        g.SetLayout(FieldLayout.Compute(ScrCols, ScrRows));
        g.Posts.Offer(post ?? Post(1));
        g.Step();
        var card = g.Cards.Cards[0];
        card.Top = top;
        var played = new List<Cue>();
        g.Sfx = new SfxBus((n, _, step, _) => played.Add(new Cue(n, step)));
        Away(g);
        Sweep(g);
        return (g, card, played);
    }

    /// <summary>Takes the enemies and their bolts off the field, so nothing but the press happens to the ship.</summary>
    public static void Sweep(RaiderGame g)
    {
        g.Enemies.Clear(); g.Bullets.Clear(); g.Pickups.Clear();
    }

    public static void Away(RaiderGame g)
    {
        Force(g, nameof(RaiderGame.PX), g.PW - AwayInset);
        Force(g, nameof(RaiderGame.PY), g.PH - AwayInset);
    }

    /// <summary>Centres the ship on the button.</summary>
    public static void Park(RaiderGame g, Card c, PostAction a)
    {
        var box = g.Cards.ButtonBox(c, a);
        Force(g, nameof(RaiderGame.PX), box.X + box.W / 2.0);
        Force(g, nameof(RaiderGame.PY), box.Y + box.H / 2.0);
    }

    public static void Step(RaiderGame g)
    {
        Sweep(g);
        g.Step();
    }

    /// <summary>Parks the ship on the button and presses its key (Z for a like, X for a repost) for one simulation tick, frozen
    /// frames stepped through. The card is put back where it was, so a measurement taken before the press still finds it.</summary>
    public static void Press(RaiderGame g, Card c, PostAction a)
    {
        int top = c.Top;
        Park(g, c, a);
        PressKey(g, a);
        long until = g.Tick + OneTick;
        for (int guard = 0; g.Tick < until; guard++)
        {
            Assert.True(guard < OneTick + StallSlackTicks, Stalled(g));
            Step(g);
        }
        c.Top = top;
    }

    /// <summary>The key a player presses for <paramref name="a"/>, wherever the ship is.</summary>
    public static void PressKey(RaiderGame g, PostAction a)
    {
        if (a == PostAction.Like) g.Input.ShipLike();
        else g.Input.ShipRepost();
    }

    /// <summary>Keeps the ship on the button for this many simulation ticks, pressing nothing. The card is put back where it was
    /// after every tick.</summary>
    public static void RestOn(RaiderGame g, Card c, PostAction a, int ticks)
    {
        int top = c.Top;
        long until = g.Tick + ticks;
        for (int guard = 0; g.Tick < until; guard++)
        {
            Assert.True(guard < ticks + StallSlackTicks, Stalled(g));
            Park(g, c, a);
            Step(g);
            c.Top = top;
        }
    }

    private static string Stalled(RaiderGame g) => $"the simulation stopped at tick {g.Tick}: alive {g.Alive}, hit stop {g.HitStop}";

    /// <summary>Keeps the ship off every button for this many simulation ticks.</summary>
    public static void Rest(RaiderGame g, int ticks)
    {
        long until = g.Tick + ticks;
        for (int guard = 0; g.Tick < until; guard++)
        {
            Assert.True(guard < ticks + StallSlackTicks, Stalled(g));
            Away(g);
            Step(g);
        }
    }

    /// <summary>Steps with the ship off every button until the condition holds, and fails if it never does.</summary>
    public static void Until(RaiderGame g, Func<bool> met)
    {
        for (int i = 0; !met(); i++)
        {
            Assert.True(i < MaxWaitTicks, $"still waiting at tick {g.Tick} with {g.Cards.Cards.Count} cards on the field");
            Rest(g, OneTick);
        }
    }

    /// <summary>Renders and returns the field the view drew, before the screen shake and the cell reduction.</summary>
    public static Surface Draw(RaiderGame g)
    {
        var v = new RaiderView();
        v.FitAndRender(g, new Screen(ScrCols, ScrRows));
        return v.Field!;
    }
}

// The like and repost keys: Z and X press the heart or the repost button of the post under the ship, the way a click does, and
// the ship resting on a button presses nothing.
public class PressTests
{
    /// <summary>Four seconds on a button: long enough that a ship left there would have liked the post if resting liked.</summary>
    private const int LongRestTicks = 4 * Tuning.Fps;
    /// <summary>Past the window in which a second press on the same button is taken for a bounce.</summary>
    private const int PastRepeatTicks = Tuning.PressRepeatFrames + 1;
    /// <summary>A card's top row high on the field, just under the HUD.</summary>
    private const int HighCardTop = 4;
    /// <summary>Rows left between two cards stacked by hand: wider than both cards' key reaches together, so the middle of the gap
    /// is out of reach of either.</summary>
    private const int CardGapRows = 2 * Tuning.ShipKeyReachCells + 1;
    /// <summary>The number of a second post offered by hand, and the card count once its card is in.</summary>
    private const int SecondPost = 2;

    [Theory]
    [InlineData(PostAction.Like)]
    [InlineData(PostAction.Repost)]
    public void The_ship_resting_on_a_button_for_four_seconds_presses_nothing(PostAction a)
    {
        var (g, c, played) = PressRig.Scene();
        int writes = 0;
        g.OnLike = _ => writes++;
        g.OnRepost = _ => writes++;
        PressRig.RestOn(g, c, a, LongRestTicks);
        Assert.Equal(ButtonState.Off, c.Marks.Of(a));
        Assert.False(c.Marks.Paid);
        Assert.Equal(0, writes);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Like or SfxNames.Repost);
    }

    [Theory]
    [InlineData(PostAction.Like)]
    [InlineData(PostAction.Repost)]
    public void The_key_likes_or_reposts_the_post_under_the_ship_and_the_same_key_again_undoes_it(PostAction a)
    {
        var (g, c, played) = PressRig.Scene(signedIn: false);
        PressRig.Press(g, c, a);
        Assert.Equal(ButtonState.Marked, c.Marks.Of(a));
        Assert.Contains(played, p => p.Name == (a == PostAction.Like ? SfxNames.Like : SfxNames.Repost));

        PressRig.RestOn(g, c, a, PastRepeatTicks);
        PressRig.Press(g, c, a);
        Assert.Equal(ButtonState.Off, c.Marks.Of(a));
        Assert.Equal(SfxNames.Undo, played[^1].Name);
    }

    [Fact]
    public void Each_key_presses_only_its_own_button()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        PressRig.Press(g, c, PostAction.Repost);
        Assert.Equal(ButtonState.Marked, c.Marks.Repost);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        PressRig.Press(g, c, PostAction.Like);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
        Assert.Equal(ButtonState.Marked, c.Marks.Repost);
    }

    [Fact]
    public void A_key_held_down_and_read_again_within_the_repeat_window_does_not_undo_its_own_like()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        PressRig.Press(g, c, PostAction.Like);
        PressRig.Press(g, c, PostAction.Like);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
    }

    [Fact]
    public void Anywhere_over_the_card_the_keys_act_on_that_card_not_only_on_its_buttons()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        var box = g.Cards.CardBox(c);
        PressRig.Force(g, nameof(RaiderGame.PX), box.X + box.W / 2.0);
        PressRig.Force(g, nameof(RaiderGame.PY), box.Y + 1.0);
        Assert.Same(c, g.KeyCard);
        PressRig.PressKey(g, PostAction.Like);
        PressRig.Step(g);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
    }

    [Fact]
    public void Between_two_cards_the_keys_pick_the_nearer_one_and_past_the_reach_none()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false, top: HighCardTop);
        g.Posts.Offer(PressRig.Post(SecondPost));
        PressRig.Until(g, () => g.Cards.Cards.Count >= SecondPost);
        var below = g.Cards.Cards[1];
        c.Top = HighCardTop;
        below.Top = c.Top + c.Shape.Rows + CardGapRows;
        var upper = g.Cards.CardBox(c);
        var lower = g.Cards.CardBox(below);
        int gapTop = upper.Y + upper.H, gapBottom = lower.Y;
        double x = upper.X + upper.W / 2.0;
        int reach = Tuning.ShipKeyReachCells * CardField.PixelsPerCell;
        Assert.True(gapBottom - gapTop > 2 * reach, "the gap is wider than both reaches, so its middle is out of reach of either");

        PressRig.Force(g, nameof(RaiderGame.PX), x);
        PressRig.Force(g, nameof(RaiderGame.PY), gapTop + 1.0);
        Assert.Same(c, g.KeyCard);
        PressRig.Force(g, nameof(RaiderGame.PY), gapBottom - 1.0);
        Assert.Same(below, g.KeyCard);
        PressRig.Force(g, nameof(RaiderGame.PY), (gapTop + gapBottom) / 2.0);
        Assert.Null(g.KeyCard);
    }

    [Fact]
    public void A_key_with_no_card_in_reach_plays_the_no_like_sound_and_presses_nothing()
    {
        var (g, c, played) = PressRig.Scene();
        Assert.Null(g.KeyCard);
        PressRig.PressKey(g, PostAction.Like);
        PressRig.Step(g);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        Assert.Contains(played, p => p.Name == SfxNames.NoLike);
    }

    [Fact]
    public void A_key_pressed_while_the_ship_waits_to_respawn_presses_nothing_and_has_gone_stale_when_it_returns()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        PressRig.Force(g, nameof(RaiderGame.RespawnIn), Tuning.RespawnFrames);
        Assert.False(g.Alive);
        Assert.Null(g.KeyCard);
        PressRig.Park(g, c, PostAction.Like);
        PressRig.PressKey(g, PostAction.Like);
        PressRig.Rest(g, Tuning.RespawnFrames + PressRig.OneTick);
        Assert.True(g.Alive);
        PressRig.RestOn(g, c, PostAction.Like, PressRig.OneTick);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
    }

    [Fact]
    public void Signed_out_a_key_on_a_lit_button_clears_the_like_the_post_already_had_and_pays_nothing()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false, post: PressRig.Post(1, liked: true));
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        Assert.Equal(ButtonState.Lit, c.Marks.Like);
        PressRig.Press(g, c, PostAction.Like);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
    }

    [Fact]
    public void A_key_press_after_a_drag_marks_the_ship_as_steered_by_keys()
    {
        var (g, c, _) = PressRig.Scene();
        g.Input.Drag(0, 0);
        Assert.True(g.Input.PointerLast);
        PressRig.Park(g, c, PostAction.Like);
        PressRig.PressKey(g, PostAction.Like);
        Assert.False(g.Input.PointerLast);
    }

    [Theory]
    [InlineData(40, 70)]
    [InlineData(56, 58)]
    [InlineData(80, 30)]
    [InlineData(130, 46)]
    public void The_keys_hint_drawn_at_each_width_names_z_and_x_and_fits_on_its_row(int cols, int rows)
    {
        var g = new RaiderGame(1);
        g.SetLayout(FieldLayout.Compute(cols, rows));
        g.Step();
        var scr = new Screen(cols, rows);
        new RaiderView().FitAndRender(g, scr);
        string hint = RaiderView.KeysHint(cols).Trim();
        Assert.True(hint.Length < cols, $"a {hint.Length}-character hint on a {cols}-column screen");
        Assert.Matches(@"\bZ\b", hint);
        Assert.Matches(@"\bX\b", hint);
        var lines = Enumerable.Range(0, rows).Select(r => new string(scr.Ch, r * cols, cols));
        Assert.Contains(lines, l => l.Contains(hint));
    }
}
