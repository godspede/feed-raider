using System.Runtime.CompilerServices;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// Three seconds on a post's heart likes it: what the player sees build up, what happens at the end, and what is written.
public class LikeHoldTests
{
    private const PostAction Like = PostAction.Like;

    private static readonly uint[] Reds = [0x86384f, 0xa03c55, 0xba3f59, 0xd2405b, 0xe63f5a, 0xf23c59, 0xfa3657, 0xff2d55];

    /// <summary>What a signed-out player is told the first time a like finishes in a run.</summary>
    internal const string SignedOutLikeToast = "Sign in to like for real. Nothing was sent.";

    /// <summary>A hold kept on this long has not finished.</summary>
    internal const int LastTickBeforeDone = Tuning.HoldFrames - 1;
    /// <summary>The ticks a hold held to <see cref="LastTickBeforeDone"/> still needs to finish.</summary>
    internal const int FinishingTicks = Tuning.HoldFrames - LastTickBeforeDone;
    /// <summary>Ticks past a beat at which a held icon is measured, so the beat's thump is over and the next beat has not begun.</summary>
    internal const int PastThumpTicks = 5;
    /// <summary>A tick late in the hold, where the heart has grown wide.</summary>
    private const int LateHoldTicks = 80;
    /// <summary>A card top far below the field, so no part of the card is drawn.</summary>
    internal const int OffFieldTop = 10_000;
    /// <summary>More solid pixels than a stray few, so the ship's sprite was compared and not a sliver of it.</summary>
    private const int MinShipPixels = 20;
    /// <summary>The ring's first dot is at the top.</summary>
    private const double RingStartAngle = -Math.PI / 2;
    /// <summary>Room above the finish's shake for the part of the last beat's shake that carries into it.</summary>
    private const double FinishedShakeCarry = 0.6;
    /// <summary>Rows left between two cards stacked by hand.</summary>
    private const int CardGapRows = 2;
    /// <summary>Set as the power's end frame, it means no power is running.</summary>
    internal const long NoPower = -1L;
    /// <summary>Ticks after a finish by which its frozen frames and the heart's fall back into its button are over.</summary>
    internal const int SettledTicks = 40;
    /// <summary>A rest after a finish that must outlast its <see cref="HoldBeats.FinishedHitStop"/> frozen frames, with room to spare.</summary>
    internal const int PastFrozenTicks = 10;
    /// <summary>The blinks a pending button is watched for.</summary>
    internal const int WatchedBlinks = 4;
    /// <summary>A shield spent, set by hand so a finish's shield shows.</summary>
    internal const int NoShield = 0;
    /// <summary>The number of a second post offered by hand, and the card count once its card is in.</summary>
    private const int SecondPost = 2;
    /// <summary>The count a test post shows before its like, and after it.</summary>
    internal const string CountBefore = "10", CountAfter = "11";
    /// <summary>The game's private tick the powered weapon runs out at, which only a pickup sets.</summary>
    internal const string PowerUntilField = "_powerUntil";

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

    /// <summary>How wide the lit ground under an icon and its count is, in pixels: their cells and one cell past each end.</summary>
    internal static int LitGroundPixels(Card c, PostAction a) =>
        (CardPainter.ActionSpan(c, a).Cols + 2 * CardPainter.LitPadCols) * CardField.PixelsPerCell;

    /// <summary>Renders the game to a screen, checks the button's icon is the character there, and returns its colour.</summary>
    internal static uint IconFg(RaiderGame g, Card c, PostAction a)
    {
        var scr = new Screen(HoldRig.ScrCols, HoldRig.ScrRows);
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

    [Fact]
    public void The_beat_table_holds_its_widths_colours_and_halo()
    {
        Assert.Equal([0, 15, 30, 45, 58, 69, 78, 85], HoldBeats.Ticks);
        Assert.Equal([7, 9, 11, 13, 15, 17, 19, 21], HoldBeats.Widths);
        Assert.Equal(25, HoldBeats.FinishedWidth);
        Assert.Equal(Reds, HoldBeats.Colours(Like));
        Assert.Equal(0xffd9e0u, HoldBeats.Finished(Like));
        for (int i = 0; i < HoldBeats.Widths.Length; i++) Assert.Equal(0.10 + 0.04 * i, HoldBeats.Halo(i), 6);
    }

    [Fact]
    public void The_heart_is_a_character_at_rest_and_9_to_21_pixels_wide_through_the_beats_in_the_tables_colours_with_as_many_rows_filled_as_progress_says()
    {
        var (g, c, _) = HoldRig.Scene();
        var box = GrownButtonBox(g, c, Like);
        var rest = HoldRig.Draw(g);
        Assert.Equal(0, Extent(rest, box, CardColours.LikeIdle).Width);
        Assert.Equal(CardColours.LikeIcon, IconFg(g, c, Like));
        Assert.Equal(CardLayout.HeartOff.ToString(), CardPainter.IconAt(c, Like).Text);

        int at = 0;
        for (int i = 0; i < HoldBeats.Ticks.Length - 1; i++)
        {
            HoldRig.Hold(g, c, Like, Math.Min(HoldBeats.Ticks[i + 1] + PastThumpTicks, LastTickBeforeDone) - at);
            at = c.Marks.LikeProgress;
            HoldRig.Away(g);   // the ship, its ring and the button's outline are out of the measured box
            var f = HoldRig.Draw(g);
            uint lit = Reds[i + 1];
            int w = HoldBeats.Widths[i + 1], h = HoldBeats.IconHeight(w);
            var (width, rows) = Extent(f, box, lit, CardColours.LikeIdle);
            Assert.Equal(w, width);
            Assert.Equal(h, rows);
            int filled = Extent(f, box, lit).Rows;
            Assert.Equal((int)Math.Round(h * at / (double)Tuning.HoldFrames), filled);
            // the share rounds up to every row only on the last tick before done
            Assert.True(filled > 0 && (filled < h || at == LastTickBeforeDone), $"part filled at tick {at}");
            for (int k = 0; k < Reds.Length; k++)
                if (k != i + 1) Assert.Equal(0, Extent(f, box, Reds[k]).Width);
        }
    }

    [Fact]
    public void On_a_beat_the_heart_is_drawn_two_pixels_wider_for_three_frames()
    {
        var (g, c, _) = HoldRig.Scene();
        var box = GrownButtonBox(g, c, Like);
        Assert.Equal((2, 3), (HoldBeats.ThumpPixels, HoldBeats.ThumpFrames));
        const int step = 2;
        HoldRig.Hold(g, c, Like, HoldBeats.Ticks[step]);
        for (int i = 0; i < HoldBeats.ThumpFrames; i++)
        {
            HoldRig.Away(g);
            g.Rings.Clear();   // the beat's own ring starts on the heart's edge
            Assert.Equal(HoldBeats.Widths[step] + HoldBeats.ThumpPixels, Extent(HoldRig.Draw(g), box, Reds[step], CardColours.LikeIdle).Width);
            HoldRig.Hold(g, c, Like, HoldRig.OneTick);
        }
        HoldRig.Away(g);
        Assert.Equal(HoldBeats.Widths[step], Extent(HoldRig.Draw(g), box, Reds[step], CardColours.LikeIdle).Width);
    }

    [Fact]
    public void The_glow_is_under_the_ship_every_pixel_of_the_ships_sprite_is_the_same_with_and_without_a_hold()
    {
        var (g, c, _) = HoldRig.Scene();
        HoldRig.Hold(g, c, Like, LateHoldTicks);
        HoldRig.Park(g, c, Like);
        HoldRig.Sweep(g);
        g.Particles.Clear(); g.Rings.Clear();
        var held = HoldRig.Draw(g);
        Assert.True(Extent(held, GrownButtonBox(g, c, Like), Reds[HoldBeats.StepAt(LateHoldTicks)], CardColours.LikeIdle).Width > HoldBeats.Widths[0], "the grown heart is on the field");

        // the same ship in the same place over a field with no card at all
        int top = c.Top;
        c.Top = OffFieldTop;
        c.Marks.LikeProgress = 0;
        var noCard = HoldRig.Draw(g);
        c.Top = top;

        var ship = HoldTracker.ShipBox(g);
        int solid = 0;
        for (int y = 0; y < g.Ship.H; y++)
            for (int x = 0; x < g.Ship.W; x++)
            {
                uint px = g.Ship.Px[y * g.Ship.W + x];
                if ((px & Sprite.Opaque) == 0) continue;
                uint colour = px & EnemyKit.RgbMask;
                solid++;
                Assert.Equal(colour, held.Get(ship.X + x, ship.Y + y));
                Assert.Equal(noCard.Get(ship.X + x, ship.Y + y), held.Get(ship.X + x, ship.Y + y));
            }
        Assert.True(solid > MinShipPixels);
    }

    [Fact]
    public void A_ring_of_dots_round_the_ship_fills_clockwise_from_the_top_as_the_hold_goes_on()
    {
        var (g, c, _) = HoldRig.Scene();
        const int halfway = Tuning.HoldFrames / 2;
        HoldRig.Hold(g, c, Like, halfway);
        HoldRig.Park(g, c, Like);
        g.Particles.Clear(); g.Rings.Clear();
        var f = HoldRig.Draw(g);
        Assert.Equal(48, RaiderView.HoldRingDots);
        Assert.Equal(13, RaiderView.HoldRingRadius);
        const int halfTheDots = RaiderView.HoldRingDots / 2;
        int lit = 0, right = 0;
        for (int i = 0; i < RaiderView.HoldRingDots; i++)
        {
            double a = RingStartAngle + i / (double)RaiderView.HoldRingDots * Math.Tau;
            int x = (int)Math.Round(g.PX + Math.Cos(a) * RaiderView.HoldRingRadius), y = (int)Math.Round(g.PY + Math.Sin(a) * RaiderView.HoldRingRadius);
            if (f.Get(x, y) != Reds[HoldBeats.StepAt(halfway)]) continue;
            lit++;
            if (i <= halfTheDots) right++;
        }
        Assert.Equal(halfTheDots, lit);
        Assert.Equal(halfTheDots, right);
    }

    [Fact]
    public void At_tick_90_two_frames_of_hit_stop_fourteen_particles_two_rings_shake_1_6_the_heart_lit_and_the_count_one_higher()
    {
        var (g, c, played) = HoldRig.Scene();
        g.OnLike = _ => { };
        HoldRig.Hold(g, c, Like, LastTickBeforeDone);
        g.Particles.Clear(); g.Rings.Clear();
        Assert.Equal(0, g.HitStop);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        Assert.Equal(CountBefore, CardPainter.CountText(c, Like));

        HoldRig.Hold(g, c, Like, FinishingTicks);
        Assert.Equal(HoldBeats.FinishedHitStop, g.HitStop);
        Assert.Equal(RepostHoldTests.FinishBurst, g.Particles.Count(p => p.Kind == ParticleKind.Heart));
        Assert.Equal(RepostHoldTests.FinishRings, g.Rings.Select(r => (r.R0, r.R1)).OrderBy(r => r.Item1));
        Assert.True(g.Shake >= HoldBeats.FinishedShake && g.Shake < HoldBeats.FinishedShake + FinishedShakeCarry, $"shake {g.Shake}");
        Assert.NotEqual(ButtonState.Off, c.Marks.Like);
        Assert.Equal(0, c.Marks.LikeProgress);
        Assert.Equal(CountAfter, CardPainter.CountText(c, Like));
        Assert.Equal(SfxNames.Like, played[^1].Name);

        var f = HoldRig.Draw(g);
        Assert.Equal(HoldBeats.FinishedWidth, Extent(f, GrownButtonBox(g, c, Like), HoldBeats.Finished(Like)).Width);
        int particles = g.Particles.Count;
        played.Clear();
        HoldRig.Hold(g, c, Like, Tuning.Fps);
        Assert.Equal(0, c.Marks.LikeProgress);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Like or SfxNames.Hold);
        Assert.True(g.Particles.Count <= particles);
        g.WriteConfirmed(c.Post.Uri, Like, "at://did:plc:me/app.bsky.feed.like/1");
        Assert.Equal(ButtonState.Lit, c.Marks.Like);
        HoldRig.Away(g);
        const int pixelRowsOfACell = CardField.PixelsPerCell;
        Assert.Equal((LitGroundPixels(c, Like), pixelRowsOfACell), Extent(HoldRig.Draw(g), g.Cards.ButtonBox(c, Like), CardColours.LikeButtonLit));
        Assert.Equal(CardColours.LikeCountLit, IconFg(g, c, Like));
        Assert.Equal(CardLayout.HeartOn.ToString(), CardPainter.IconAt(c, Like).Text);
    }

    [Fact]
    public void Signed_out_nothing_is_written_the_heart_ends_marked_the_first_finish_of_a_run_shows_the_toast_and_later_ones_do_not_and_the_reward_is_the_same()
    {
        var (g, c, _) = HoldRig.Scene(signedIn: false);
        int writes = 0;
        g.OnLike = _ => writes++;
        var said = new List<string>();
        g.Said = said.Add;
        HoldRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        HoldRig.Hold(g, c, Like, Tuning.HoldFrames);
        Assert.Equal(0, writes);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
        Assert.Equal([SignedOutLikeToast], said);
        Assert.Equal(CountBefore, CardPainter.CountText(c, Like));
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.True(g.Powered);

        g.Posts.Offer(HoldRig.Post(SecondPost));
        HoldRig.Until(g, () => g.Cards.Cards.Count >= SecondPost);
        var c2 = g.Cards.Cards[1];
        c2.Top = c.Top + c.Shape.Rows + CardGapRows;
        HoldRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        HoldRig.Force(g, PowerUntilField, NoPower);
        HoldRig.Hold(g, c2, Like, Tuning.HoldFrames);
        Assert.Equal(ButtonState.Marked, c2.Marks.Like);
        Assert.Single(said);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.True(g.Powered);
        Assert.Equal(0, writes);
    }

    [Fact]
    public void A_marked_heart_is_the_outline_in_the_lit_colour_with_a_line_at_half_strength_under_it_and_its_count()
    {
        var (g, c, _) = HoldRig.Scene(signedIn: false);
        HoldRig.Hold(g, c, Like, Tuning.HoldFrames);
        HoldRig.Rest(g, SettledTicks);
        g.Particles.Clear(); g.Rings.Clear();
        var f = HoldRig.Draw(g);
        var button = g.Cards.ButtonBox(c, Like);
        Assert.Equal(Surface.Mix(CardColours.Slab, CardColours.LikeLit, 0.5), CardColours.LikeMarked);
        const int lineRows = 1;
        Assert.Equal((CardPainter.ActionSpan(c, Like).Cols * CardField.PixelsPerCell, lineRows), Extent(f, button, CardColours.LikeMarked));
        Assert.Equal(0, Extent(f, button, CardColours.LikeButtonLit).Width);
        HoldRig.Away(g);
        Assert.Equal(CardColours.LikeLit, IconFg(g, c, Like));
        Assert.Equal(CardLayout.HeartOff.ToString(), CardPainter.IconAt(c, Like).Text);
    }

    [Fact]
    public void A_pending_heart_blinks_every_six_frames()
    {
        var (g, c, _) = HoldRig.Scene();
        g.OnLike = _ => { };
        HoldRig.Hold(g, c, Like, Tuning.HoldFrames);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        HoldRig.Rest(g, Tuning.Fps);
        g.Particles.Clear(); g.Rings.Clear();
        Assert.Equal(6, CardPainter.BlinkFrames);
        var seen = new List<bool>();
        for (int i = 0; i < WatchedBlinks * CardPainter.BlinkFrames; i++)
        {
            seen.Add(IconFg(g, c, Like) == CardColours.LikeCountLit);
            HoldRig.Rest(g, HoldRig.OneTick);
            g.Particles.Clear(); g.Rings.Clear();
        }
        Assert.Contains(true, seen);
        Assert.Contains(false, seen);
        int changes = Enumerable.Range(1, seen.Count - 1).Count(i => seen[i] != seen[i - 1]);
        Assert.InRange(changes, WatchedBlinks - 1, WatchedBlinks);
    }

    [Fact]
    public void A_second_finish_on_the_same_post_after_it_comes_round_again_pays_nothing()
    {
        var (g, c, _) = HoldRig.Scene(signedIn: false);
        HoldRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        HoldRig.Hold(g, c, Like, Tuning.HoldFrames);
        Assert.True(c.Marks.Paid);
        Assert.Equal(Tuning.ShieldHits, g.Shield);

        HoldRig.Rest(g, PastFrozenTicks);
        // the queue forgets what it has offered once its memory is full; here it is made to forget now
        Seen(g.Posts).Clear();
        Assert.True(g.Posts.Offer(c.Post));
        HoldRig.Until(g, () => g.Cards.Cards.Count >= SecondPost);
        var again = g.Cards.Cards[1];
        Assert.Same(c.Marks, again.Marks);
        again.Top = c.Top + c.Shape.Rows + CardGapRows;
        HoldRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        HoldRig.Force(g, PowerUntilField, NoPower);
        g.Popups.Clear();
        HoldRig.Hold(g, again, Like, Tuning.HoldFrames);
        Assert.Equal(0, again.Marks.LikeProgress);
        Assert.Equal(NoShield, g.Shield);
        Assert.False(g.Powered);
        Assert.DoesNotContain(g.Popups, p => p.Text is PickupTests.ShieldUp or PickupTests.PowerUp);
    }
}
