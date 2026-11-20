using System.Reflection;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>What the hold tests share: a game with one card, a ship that can be parked on a button, and a record of the sounds.</summary>
internal static class HoldRig
{
    public const int ScrCols = EnemyKit.BigCols, ScrRows = EnemyKit.BigRows;
    /// <summary>Frozen frames a hold may step through without counting.</summary>
    private const int StallSlackTicks = 200;
    /// <summary>The most ticks a wait runs before it gives up.</summary>
    private const int MaxWaitTicks = 600;
    /// <summary>One simulation tick, for a wait that looks again after every tick.</summary>
    public const int OneTick = 1;
    /// <summary>How far in from the bottom right corner the ship waits when it is kept off the card.</summary>
    private const double AwayInset = 10.0;

    public readonly record struct Cue(string Name, int Step, int Progress);

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
        g.Sfx = new SfxBus((n, _, step, _) => played.Add(new Cue(n, step, card.Marks.LikeProgress + card.Marks.RepostProgress)));
        Away(g);
        Sweep(g);
        return (g, card, played);
    }

    /// <summary>Takes the enemies and their bolts off the field, so nothing but the hold happens to the ship.</summary>
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

    /// <summary>One call to Step with the field swept first.</summary>
    public static void Step(RaiderGame g)
    {
        Sweep(g);
        g.Step();
    }

    /// <summary>Keeps the ship on the button for this many simulation ticks (frozen frames are stepped through and not counted). The
    /// card is put back where it was after every tick, so a measurement taken before the hold still finds it.</summary>
    public static void Hold(RaiderGame g, Card c, PostAction a, int ticks)
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

// Holding the ship over a button: what counts as on the button, how the three seconds fill and drain, and when the beats fall.
public class HoldTests
{
    /// <summary>A third of a hold: progress shows, and the like is never sent.</summary>
    private const int PartHold = Tuning.HoldFrames / 3;
    /// <summary>A hold one tick short of sending the like: every beat but the send has fallen.</summary>
    private const int OneTickShort = Tuning.HoldFrames - 1;
    /// <summary>A card's top row high on the field, just under the HUD.</summary>
    private const int HighCardTop = 4;
    /// <summary>The narrowest overlap or gap between the ship's box and a button.</summary>
    private const int OnePixel = 1;

    [Fact]
    public void The_ships_box_overlapping_the_button_holds_it_and_one_pixel_clear_does_not()
    {
        var (g, c, _) = HoldRig.Scene();
        var button = g.Cards.ButtonBox(c, PostAction.Like);
        HoldRig.Force(g, nameof(RaiderGame.PY), button.Y + button.H / 2.0);

        HoldRig.Force(g, nameof(RaiderGame.PX), button.X - g.Ship.W / 2.0 + OnePixel);       // the box's last column is the button's first
        Assert.Equal(button.X + OnePixel, HoldTracker.ShipBox(g).X + HoldTracker.ShipBox(g).W);
        Assert.Equal((c, PostAction.Like), HoldTracker.Touching(g));

        HoldRig.Force(g, nameof(RaiderGame.PX), button.X - g.Ship.W / 2.0);           // the box ends where the button begins
        Assert.Equal(button.X, HoldTracker.ShipBox(g).X + HoldTracker.ShipBox(g).W);
        Assert.Null(HoldTracker.Touching(g));

        // the same up and down: the box's last row on the button's first, then one above it
        HoldRig.Force(g, nameof(RaiderGame.PX), button.X + button.W / 2.0);
        HoldRig.Force(g, nameof(RaiderGame.PY), button.Y - g.Ship.H / 2.0 + OnePixel);
        Assert.Equal((c, PostAction.Like), HoldTracker.Touching(g));
        HoldRig.Force(g, nameof(RaiderGame.PY), button.Y - g.Ship.H / 2.0);
        Assert.Null(HoldTracker.Touching(g));
    }

    [Fact]
    public void The_ships_box_is_the_sprite_as_drawn()
    {
        var (g, _, _) = HoldRig.Scene();
        const double px = 60, py = 40;
        HoldRig.Force(g, nameof(RaiderGame.PX), px);
        HoldRig.Force(g, nameof(RaiderGame.PY), py);
        Assert.Equal(new PixelRect((int)Math.Round(px - g.Ship.W / 2.0), (int)Math.Round(py - g.Ship.H / 2.0), g.Ship.W, g.Ship.H), HoldTracker.ShipBox(g));
    }

    [Fact]
    public void With_both_buttons_touched_the_nearer_one_is_held_and_only_one()
    {
        // a narrow card, and a box wider than the ship: wide enough to reach both buttons across the gap a ship fits in
        var posts = new PostQueue();
        const int narrowCols = 28, fieldRows = 40;
        var field = new CardField(posts, new PostBook(), narrowCols, fieldRows, FeedSpeed.Default);
        posts.Offer(HoldRig.Post(1));
        field.Step();
        var c = field.Cards[0];
        c.Top = HighCardTop;
        var like = field.ButtonBox(c, PostAction.Like);
        var repost = field.ButtonBox(c, PostAction.Repost);
        int gap = repost.X - (like.X + like.W);
        const int boxW = 19, boxH = 12, reach = 4, above = 3;
        Assert.InRange(gap, OnePixel, boxW - 2 * OnePixel);   // the box can lie across the gap onto a pixel of each button
        int y = like.Y - above;
        // a box across the gap, one pixel further onto the like button than onto the repost button
        var nearLike = new PixelRect(like.X + like.W - reach, y, boxW, boxH);
        Assert.Equal((c, PostAction.Like), HoldTracker.Pick(field, nearLike, (_, _) => true));
        var nearRepost = new PixelRect(repost.X + reach - boxW, y, boxW, boxH);
        Assert.Equal((c, PostAction.Repost), HoldTracker.Pick(field, nearRepost, (_, _) => true));
        // a button that cannot be held is passed over for the one that can
        Assert.Equal((c, PostAction.Repost), HoldTracker.Pick(field, nearLike, (_, a) => a == PostAction.Repost));
        Assert.Null(HoldTracker.Pick(field, nearLike, (_, _) => false));
    }

    [Fact]
    public void Progress_rises_one_a_tick_while_held_waits_six_ticks_off_the_button_then_falls_two_a_tick_and_carries_on_when_the_ship_comes_back()
    {
        Assert.Equal(90, Tuning.HoldFrames);
        Assert.Equal(6, Tuning.HoldGraceFrames);
        Assert.Equal(2, Tuning.HoldDrain);
        const int drained = 5;
        var (g, c, _) = HoldRig.Scene();
        for (int i = 1; i <= PartHold; i++)
        {
            HoldRig.Hold(g, c, PostAction.Like, HoldRig.OneTick);
            Assert.Equal(i, c.Marks.LikeProgress);
        }
        for (int i = 1; i <= Tuning.HoldGraceFrames; i++)
        {
            HoldRig.Rest(g, HoldRig.OneTick);
            Assert.Equal(PartHold, c.Marks.LikeProgress);
        }
        for (int i = 1; i <= drained; i++)
        {
            HoldRig.Rest(g, HoldRig.OneTick);
            Assert.Equal(PartHold - Tuning.HoldDrain * i, c.Marks.LikeProgress);
        }
        HoldRig.Hold(g, c, PostAction.Like, HoldRig.OneTick);
        Assert.Equal(PartHold - Tuning.HoldDrain * drained + HoldRig.OneTick, c.Marks.LikeProgress);
        HoldRig.Rest(g, Tuning.HoldGraceFrames + PartHold);
        Assert.Equal(0, c.Marks.LikeProgress);
    }

    [Fact]
    public void A_row_step_moves_the_box_so_a_still_ship_at_the_default_speed_loses_contact_after_four_and_a_half_seconds()
    {
        var (g, c, _) = HoldRig.Scene(top: HighCardTop);
        Assert.Equal(FeedSpeed.Default, g.FeedSpeed);
        var button = g.Cards.ButtonBox(c, PostAction.Like);
        // the ship waits below the button, its top row one pixel under the button's last row; it never moves
        double px = button.X + button.W / 2.0, py = button.Y + button.H + OnePixel + g.Ship.H / 2.0;
        const int watchTicks = 400;   // long enough for the whole button to pass the ship
        int? first = null, last = null;
        for (int t = 0; t < watchTicks; t++)
        {
            HoldRig.Force(g, nameof(RaiderGame.PX), px);
            HoldRig.Force(g, nameof(RaiderGame.PY), py);
            long tick = g.Tick;
            HoldRig.Step(g);
            if (g.Tick == tick) { t--; continue; }             // a frozen frame is not time
            if (HoldTracker.Touching(g) is not null) { first ??= t; last = t; }
            c.Marks.Like = ButtonState.Off;   // let it be held the whole way past
        }
        Assert.True(first is not null, "the button reached the ship");
        int touchedTicks = last!.Value - first.Value + 1;
        // a 6 pixel button passing a 12 pixel box is in touch for 18 pixels, 9 rows, 135 ticks
        Assert.Equal((button.H + g.Ship.H) / CardField.PixelsPerCell * g.Cards.FramesPerRow, touchedTicks);
        Assert.Equal(135, touchedTicks);
    }

    [Fact]
    public void A_lit_button_cannot_be_held_and_no_button_can_while_the_ship_waits_to_respawn()
    {
        var (g, c, _) = HoldRig.Scene(post: HoldRig.Post(1, liked: true));
        Assert.Equal(ButtonState.Lit, c.Marks.Like);
        HoldRig.Hold(g, c, PostAction.Like, PartHold);
        Assert.Equal(0, c.Marks.LikeProgress);
        Assert.Null(HoldTracker.Touching(g));

        var (g2, c2, _) = HoldRig.Scene();
        HoldRig.Hold(g2, c2, PostAction.Like, PartHold);
        Assert.Equal(PartHold, c2.Marks.LikeProgress);
        HoldRig.Force(g2, nameof(RaiderGame.RespawnIn), Tuning.RespawnFrames);
        Assert.False(g2.Alive);
        HoldRig.Hold(g2, c2, PostAction.Like, PartHold);
        Assert.True(c2.Marks.LikeProgress < PartHold, "it drains while the ship is away");
        Assert.Null(HoldTracker.Touching(g2));
    }

    [Fact]
    public void Hit_stop_frames_add_no_progress()
    {
        const int frozen = 5;
        var (g, c, _) = HoldRig.Scene();
        HoldRig.Hold(g, c, PostAction.Like, PartHold);
        const double shake = 0.5;
        g.AddShake(shake, frozen);
        Assert.Equal(frozen, g.HitStop);
        for (int i = 0; i < frozen; i++)
        {
            HoldRig.Park(g, c, PostAction.Like);
            g.Step();
            Assert.Equal(PartHold, c.Marks.LikeProgress);
        }
        HoldRig.Park(g, c, PostAction.Like);
        g.Step();
        Assert.Equal(PartHold + HoldRig.OneTick, c.Marks.LikeProgress);
    }

    [Fact]
    public void Beats_fire_at_ticks_15_30_45_58_69_78_and_85_once_each_on_the_way_up_and_never_on_the_way_down()
    {
        Assert.Equal([0, 15, 30, 45, 58, 69, 78, 85], HoldBeats.Ticks);
        var (g, c, played) = HoldRig.Scene();
        HoldRig.Hold(g, c, PostAction.Like, OneTickShort);
        var beats = played.Where(p => p.Name == SfxNames.Hold && p.Step > 0).ToList();
        Assert.Equal(HoldBeats.Ticks[1..], beats.Select(b => b.Progress));
        Assert.Equal(Enumerable.Range(1, HoldBeats.Ticks.Length - 1), beats.Select(b => b.Step));

        // all the way down again: no beat, though every line is crossed
        played.Clear();
        HoldRig.Rest(g, Tuning.HoldFrames);
        Assert.Equal(0, c.Marks.LikeProgress);
        Assert.Empty(played);

        // and the step the view reads is the last beat passed
        for (int step = 1; step < HoldBeats.Ticks.Length; step++)
        {
            Assert.Equal(step - 1, HoldBeats.StepAt(HoldBeats.Ticks[step] - 1));
            Assert.Equal(step, HoldBeats.StepAt(HoldBeats.Ticks[step]));
        }
        Assert.Equal(HoldBeats.Ticks.Length - 1, HoldBeats.StepAt(OneTickShort));
    }

    [Fact]
    public void A_card_that_leaves_the_field_takes_no_half_filled_button_into_the_log()
    {
        var (g, c, _) = HoldRig.Scene();
        HoldRig.Hold(g, c, PostAction.Like, PartHold);
        c.Top = g.Cards.FieldRows - 1;
        HoldRig.Rest(g, g.Cards.FramesPerRow + HoldRig.OneTick);
        Assert.DoesNotContain(c, g.Cards.Cards);
        Assert.Equal(0, c.Marks.LikeProgress);
    }
}
