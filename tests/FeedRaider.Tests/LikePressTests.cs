using System.Runtime.CompilerServices;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// A press on a post's heart likes it at once: what the player sees at the press, what it pays, and what is written.
public class LikePressTests
{
    private const PostAction Like = PostAction.Like;

    /// <summary>What a signed-out player is told the first time they like a post in a run.</summary>
    internal const string SignedOutLikeToast = "Sign in to like for real. Nothing was sent.";

    /// <summary>A card top far below the field, so no part of the card is drawn.</summary>
    internal const int OffFieldTop = 10_000;
    /// <summary>More solid pixels than a stray few, so the ship's sprite was compared and not a sliver of it.</summary>
    private const int MinShipPixels = 20;
    /// <summary>Rows left between two cards stacked by hand.</summary>
    private const int CardGapRows = 2;
    /// <summary>Set as the tick the shield reward is ready from, it means no cooldown is running.</summary>
    internal const long CooldownOver = 0L;
    /// <summary>Ticks after a press by which its frozen frames and the heart's fall back into its button are over.</summary>
    internal const int SettledTicks = 40;
    /// <summary>A rest after a press that must outlast its <see cref="PressLook.HitStop"/> frozen frames, with room to spare.</summary>
    internal const int PastFrozenTicks = 10;
    /// <summary>The blinks a pending button is watched for.</summary>
    internal const int WatchedBlinks = 4;
    /// <summary>A shield spent, set by hand so a press's shield shows.</summary>
    internal const int NoShield = 0;
    /// <summary>The number of a second post offered by hand, and the card count once its card is in.</summary>
    private const int SecondPost = 2;
    /// <summary>The count a test post shows before its like, and after it.</summary>
    internal const string CountBefore = "10", CountAfter = "11";
    /// <summary>The game's private tick before which a like or repost does not refill the shield.</summary>
    internal const string ShieldReadyField = "_shieldRewardReadyTick";

    /// <summary>The posts the queue has offered, which it will not offer again.</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_seen")]
    private static extern ref HashSet<string> Seen(PostQueue queue);

    /// <summary>The widest row of pixels in any of <paramref name="colours"/> inside the box, and the rows that have one.</summary>
    internal static (int Width, int Rows) Extent(Surface f, PixelRect box, params uint[] colours)
    {
        int width = 0, rows = 0;
        for (int y = box.Y; y < box.Y + box.H; y++)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int x = box.X; x < box.X + box.W; x++)
                if (colours.Contains(f.Get(x, y))) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); }
            if (hi >= lo) { rows++; width = Math.Max(width, hi - lo + 1); }
        }
        return (width, rows);
    }

    /// <summary>How wide the pixels in <paramref name="colours"/> are across every row of the box together: a small loop's two
    /// sides fall on different rows, so no one row spans it.</summary>
    internal static int SpanWidth(Surface f, PixelRect box, params uint[] colours)
    {
        int lo = int.MaxValue, hi = int.MinValue;
        for (int y = box.Y; y < box.Y + box.H; y++)
            for (int x = box.X; x < box.X + box.W; x++)
                if (colours.Contains(f.Get(x, y))) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); }
        return hi >= lo ? hi - lo + 1 : 0;
    }

    /// <summary>How wide the lit ground under an icon and its count is, in pixels: their cells and one cell past each end.</summary>
    internal static int LitGroundPixels(Card c, PostAction a) =>
        (CardPainter.ActionSpan(c, a).Cols + 2 * CardPainter.LitPadCols) * CardField.PixelsPerCell;

    /// <summary>Renders the game to a screen, checks the button's icon is the character there, and returns its colour.</summary>
    internal static uint IconFg(RaiderGame g, Card c, PostAction a)
    {
        var scr = new Screen(PressRig.ScrCols, PressRig.ScrRows);
        new RaiderView().FitAndRender(g, scr);
        var icon = CardPainter.IconAt(c, a);
        int col = g.Layout.Col0 + g.Cards.CardLeft + icon.Col, row = c.Top + icon.Row;
        Assert.Equal(icon.Text[0], scr.CharAt(col, row));
        return scr.Fg[row * scr.Cols + col];
    }

    internal const int GrowthPixels = 14;

    /// <summary>The button's box grown by <see cref="GrowthPixels"/> pixels each way: where a grown icon can be.</summary>
    internal static PixelRect GrownButtonBox(RaiderGame g, Card c, PostAction a)
    {
        var p = g.Cards.ButtonBox(c, a);
        return new PixelRect(p.X - GrowthPixels, p.Y - GrowthPixels, p.W + 2 * GrowthPixels, p.H + 2 * GrowthPixels);
    }

    [Theory]
    [InlineData(PostAction.Like)]
    [InlineData(PostAction.Repost)]
    public void The_pressed_icons_drawn_height_stays_within_the_footer_on_every_frame_of_its_press_and_its_settle(PostAction a)
    {
        var (g, c, _) = PressRig.Scene();
        var grown = GrownButtonBox(g, c, a);
        uint[] icon = [PressLook.FlashColour(a), PressLook.Lit(a)];
        int measured = 0;
        void Measure(string when)
        {
            PressRig.Away(g);
            g.Particles.Clear(); g.Rings.Clear();
            var f = PressRig.Draw(g);
            // a press shakes the screen, and the glow is drawn offset so that it shakes with the card's cells
            var (_, shakeY) = g.ShakeOffset();
            var drawn = g.Cards.ButtonBox(c, a);
            var button = drawn with { Y = drawn.Y + CardPainter.EvenShake(shakeY) - shakeY };
            int top = int.MaxValue, bottom = int.MinValue;
            for (int y = grown.Y; y < grown.Y + grown.H; y++)
                for (int x = grown.X; x < grown.X + grown.W; x++)
                    if (icon.Contains(f.Get(x, y))) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            if (top > bottom) return;
            measured++;
            Assert.True(bottom - top + 1 <= PressLook.MaxIconHeight, $"{when}: the icon is {bottom - top + 1} pixels tall");
            Assert.InRange(top, button.Y, button.Y + button.H - 1);
            Assert.InRange(bottom, button.Y, button.Y + button.H - 1);
        }
        g.OnLike = _ => { };
        g.OnRepost = _ => { };
        PressRig.Press(g, c, a);
        for (int frame = 0; frame < SettledTicks; frame++)
        {
            Measure($"frame {frame} of the press");
            int top = c.Top;
            PressRig.Step(g);
            c.Top = top;
        }
        Assert.True(measured > 0, "the pressed icon was drawn on some frame of the press");
    }

    [Fact]
    public void The_target_outline_is_under_the_ship_every_pixel_of_the_ships_sprite_is_the_same_with_and_without_a_card()
    {
        var (g, c, _) = PressRig.Scene();
        PressRig.Park(g, c, Like);
        PressRig.PressKey(g, Like);   // a key marks the ship as key steered, so the outline is drawn; no step, so nothing is pressed
        PressRig.Sweep(g);
        Assert.Same(c, g.KeyCard);
        g.Particles.Clear(); g.Rings.Clear();
        var over = PressRig.Draw(g);
        Assert.True(Extent(over, GrownButtonBox(g, c, Like), PressLook.Dim(Like)).Width > 0, "the outline is on the field");

        int top = c.Top;
        c.Top = OffFieldTop;
        var noCard = PressRig.Draw(g);
        c.Top = top;

        var ship = ShipBox(g);
        int solid = 0;
        for (int y = 0; y < g.Ship.H; y++)
            for (int x = 0; x < g.Ship.W; x++)
            {
                uint px = g.Ship.Px[y * g.Ship.W + x];
                if ((px & Sprite.Opaque) == 0) continue;
                uint colour = px & EnemyKit.RgbMask;
                solid++;
                Assert.Equal(colour, over.Get(ship.X + x, ship.Y + y));
                Assert.Equal(noCard.Get(ship.X + x, ship.Y + y), over.Get(ship.X + x, ship.Y + y));
            }
        Assert.True(solid > MinShipPixels);
    }

    /// <summary>The ship's sprite as drawn, centred on the ship.</summary>
    private static PixelRect ShipBox(RaiderGame g) =>
        new((int)Math.Round(g.PX - g.Ship.W / 2.0), (int)Math.Round(g.PY - g.Ship.H / 2.0), g.Ship.W, g.Ship.H);

    [Fact]
    public void A_press_on_the_heart_freezes_the_game_for_the_hit_stop_bursts_particles_and_rings_shakes_and_lights_the_heart_with_the_count_one_higher()
    {
        var (g, c, played) = PressRig.Scene();
        g.OnLike = _ => { };
        g.Particles.Clear(); g.Rings.Clear();
        Assert.Equal(0, g.HitStop);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        Assert.Equal(CountBefore, CardPainter.CountText(c, Like));

        PressRig.Press(g, c, Like);
        Assert.Equal(PressLook.HitStop, g.HitStop);
        Assert.Equal(RepostPressTests.PressBurst, g.Particles.Count(p => p.Kind == ParticleKind.Heart));
        Assert.Equal(RepostPressTests.PressRings, g.Rings.Select(r => (r.R0, r.R1)).OrderBy(r => r.Item1));
        Assert.True(g.Shake >= PressLook.Shake, $"shake {g.Shake}");
        Assert.NotEqual(ButtonState.Off, c.Marks.Like);
        Assert.Equal(CountAfter, CardPainter.CountText(c, Like));
        Assert.Equal(SfxNames.Like, played[^1].Name);

        PressRig.Away(g);
        g.Particles.Clear(); g.Rings.Clear();
        var f = PressRig.Draw(g);
        Assert.Equal(PressLook.MaxIconWidth, Extent(f, GrownButtonBox(g, c, Like), PressLook.FlashColour(Like)).Width);
        int particles = g.Particles.Count;
        played.Clear();
        PressRig.RestOn(g, c, Like, Tuning.Fps);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Like);
        Assert.True(g.Particles.Count <= particles);
        g.WriteConfirmed(c.Post.Uri, Like, "at://did:plc:me/app.bsky.feed.like/1");
        Assert.Equal(ButtonState.Lit, c.Marks.Like);
        PressRig.Away(g);
        const int pixelRowsOfACell = CardField.PixelsPerCell;
        Assert.Equal((LitGroundPixels(c, Like), pixelRowsOfACell), Extent(PressRig.Draw(g), g.Cards.ButtonBox(c, Like), CardColours.LikeButtonLit));
        Assert.Equal(CardColours.LikeCountLit, IconFg(g, c, Like));
        Assert.Equal(CardLayout.HeartOn.ToString(), CardPainter.IconAt(c, Like).Text);
    }

    [Fact]
    public void Signed_out_nothing_is_written_the_heart_ends_marked_the_first_press_of_a_run_shows_the_toast_and_later_ones_do_not_and_the_reward_is_the_same()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        int writes = 0;
        g.OnLike = _ => writes++;
        var said = new List<string>();
        g.Said = said.Add;
        PressRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        PressRig.Press(g, c, Like);
        Assert.Equal(0, writes);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
        Assert.Equal([SignedOutLikeToast], said);
        Assert.Equal(CountBefore, CardPainter.CountText(c, Like));
        Assert.Equal(Tuning.ShieldHits, g.Shield);

        g.Posts.Offer(PressRig.Post(SecondPost));
        PressRig.Until(g, () => g.Cards.Cards.Count >= SecondPost);
        var c2 = g.Cards.Cards[1];
        c2.Top = c.Top + c.Shape.Rows + CardGapRows;
        PressRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        PressRig.Force(g, ShieldReadyField, CooldownOver);
        PressRig.Press(g, c2, Like);
        Assert.Equal(ButtonState.Marked, c2.Marks.Like);
        Assert.Single(said);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void A_marked_heart_is_the_outline_in_the_lit_colour_with_a_line_at_half_strength_under_it_and_its_count()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        PressRig.Press(g, c, Like);
        PressRig.Rest(g, SettledTicks);
        g.Particles.Clear(); g.Rings.Clear();
        var f = PressRig.Draw(g);
        var button = g.Cards.ButtonBox(c, Like);
        Assert.Equal(Surface.Mix(CardColours.Slab, CardColours.LikeLit, 0.5), CardColours.LikeMarked);
        const int lineRows = 1;
        Assert.Equal((CardPainter.ActionSpan(c, Like).Cols * CardField.PixelsPerCell, lineRows), Extent(f, button, CardColours.LikeMarked));
        Assert.Equal(0, Extent(f, button, CardColours.LikeButtonLit).Width);
        PressRig.Away(g);
        Assert.Equal(CardColours.LikeLit, IconFg(g, c, Like));
        Assert.Equal(CardLayout.HeartOff.ToString(), CardPainter.IconAt(c, Like).Text);
    }

    [Fact]
    public void A_pending_heart_blinks_every_six_frames()
    {
        var (g, c, _) = PressRig.Scene();
        g.OnLike = _ => { };
        PressRig.Press(g, c, Like);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        PressRig.Rest(g, Tuning.Fps);
        g.Particles.Clear(); g.Rings.Clear();
        Assert.Equal(6, CardPainter.BlinkFrames);
        var seen = new List<bool>();
        for (int i = 0; i < WatchedBlinks * CardPainter.BlinkFrames; i++)
        {
            seen.Add(IconFg(g, c, Like) == CardColours.LikeCountLit);
            PressRig.Rest(g, PressRig.OneTick);
            g.Particles.Clear(); g.Rings.Clear();
        }
        Assert.Contains(true, seen);
        Assert.Contains(false, seen);
        int changes = Enumerable.Range(1, seen.Count - 1).Count(i => seen[i] != seen[i - 1]);
        Assert.InRange(changes, WatchedBlinks - 1, WatchedBlinks);
    }

    [Fact]
    public void A_post_that_comes_round_again_undone_and_liked_a_second_time_pays_nothing_more()
    {
        const int pastRepeatTicks = Tuning.PressRepeatFrames + 1;
        var (g, c, _) = PressRig.Scene(signedIn: false);
        PressRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        PressRig.Press(g, c, Like);
        Assert.True(c.Marks.Paid);
        Assert.Equal(Tuning.ShieldHits, g.Shield);

        PressRig.Rest(g, PastFrozenTicks);
        // the queue forgets what it has offered once its memory is full; here it is made to forget now
        Seen(g.Posts).Clear();
        Assert.True(g.Posts.Offer(c.Post));
        PressRig.Until(g, () => g.Cards.Cards.Count >= SecondPost);
        var again = g.Cards.Cards[1];
        Assert.Same(c.Marks, again.Marks);
        again.Top = c.Top + c.Shape.Rows + CardGapRows;
        PressRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        PressRig.Force(g, ShieldReadyField, CooldownOver);
        g.Popups.Clear();
        PressRig.Press(g, again, Like);
        Assert.Equal(ButtonState.Off, again.Marks.Like);   // the press on a marked heart clears the mark
        PressRig.RestOn(g, again, Like, pastRepeatTicks);
        PressRig.Press(g, again, Like);
        Assert.Equal(ButtonState.Marked, again.Marks.Like);
        Assert.Equal(NoShield, g.Shield);
        Assert.DoesNotContain(g.Popups, p => p.Text.StartsWith("SHIELD"));
    }
}
