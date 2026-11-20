using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Three seconds on a post's repost loop reposts it: the same clock as the heart, in green, the loop drawn round instead of filled,
// and what a sign-in that may not repost sees instead.
public class RepostHoldTests
{
    private const PostAction Like = PostAction.Like, Repost = PostAction.Repost;

    private static readonly uint[] Greens = [0x2f6c4f, 0x2e7f58, 0x2b9261, 0x28a56a, 0x25b871, 0x22c876, 0x20d079, 0x1fd67a];
    private const uint Pale = 0xd9ffe9;
    /// <summary>The burst of a finished like or repost: this many particles, and two rings growing between these radii.</summary>
    internal const int FinishBurst = 14;
    internal static readonly (double From, double To)[] FinishRings = [(3, 14), (6, 22)];
    /// <summary>The repost count a test post shows before its repost, and after it.</summary>
    internal const string RepostsBefore = "4", RepostsAfter = "5";
    /// <summary>The reason a failed write gives in the tests here.</summary>
    private const string WriteFailure = "could not reach Bluesky";
    /// <summary>Each beat turns the loop's arrowheads a quarter of the way round.</summary>
    private const int QuartersOfALoop = 4;

    /// <summary>Where an icon <paramref name="width"/> wide is drawn on the button: centred.</summary>
    private static (int X, int Y) IconOrigin(RaiderGame g, Card c, int width)
    {
        var button = g.Cards.ButtonBox(c, Repost);
        return (button.X + Centred(button.W, width), button.Y + Centred(button.H, HoldBeats.IconHeight(width)));

        static int Centred(int room, int size) => (int)Math.Floor((room - size) / 2.0);
    }

    [Fact]
    public void A_repost_hold_runs_on_the_same_clock_and_beats_as_a_like_in_the_green_tables_colours()
    {
        Assert.Equal(Greens, HoldBeats.Colours(Repost));
        Assert.Equal(Pale, HoldBeats.Finished(Repost));

        var (g, c, played) = HoldRig.Scene();
        var box = LikeHoldTests.GrownButtonBox(g, c, Repost);
        Assert.Equal(0, LikeHoldTests.Extent(HoldRig.Draw(g), box, CardColours.RepostIdle).Width);
        Assert.Equal(CardColours.RepostIcon, LikeHoldTests.IconFg(g, c, Repost));

        int[] ticks = HoldBeats.Ticks[1..];
        int at = 0;
        for (int i = 0; i < ticks.Length; i++)
        {
            HoldRig.Hold(g, c, Repost, Math.Min(ticks[i] + LikeHoldTests.PastThumpTicks, LikeHoldTests.LastTickBeforeDone) - at);
            at = c.Marks.RepostProgress;
            Assert.Equal(0, c.Marks.LikeProgress);
            HoldRig.Away(g);
            var f = HoldRig.Draw(g);
            int beatWidth = HoldBeats.Widths[i + 1];
            var (width, rows) = LikeHoldTests.Extent(f, box, Greens[i + 1], CardColours.RepostIdle);
            Assert.Equal(beatWidth, width);
            Assert.Equal(HoldBeats.IconHeight(beatWidth), rows);
            for (int k = 0; k < Greens.Length; k++)
                if (k != i + 1) Assert.Equal(0, LikeHoldTests.Extent(f, box, Greens[k]).Width);
        }
        var beats = played.Where(p => p.Name == SfxNames.Hold && p.Step > 0).ToList();
        Assert.Equal(ticks, beats.Select(b => b.Progress));
        Assert.Equal(Enumerable.Range(1, ticks.Length), beats.Select(b => b.Step));

        // the last tick of the hold finishes it, as it does a like
        Assert.Equal(ButtonState.Off, c.Marks.Repost);
        HoldRig.Hold(g, c, Repost, LikeHoldTests.FinishingTicks);
        Assert.NotEqual(ButtonState.Off, c.Marks.Repost);
        Assert.Equal(0, c.Marks.RepostProgress);

        // and it drains like one: nothing for six ticks, then two a tick
        const int held = 50, drained = 10;
        var (g2, c2, _) = HoldRig.Scene();
        HoldRig.Hold(g2, c2, Repost, held);
        HoldRig.Rest(g2, Tuning.HoldGraceFrames);
        Assert.Equal(held, c2.Marks.RepostProgress);
        HoldRig.Rest(g2, drained);
        Assert.Equal(held - Tuning.HoldDrain * drained, c2.Marks.RepostProgress);
    }

    [Fact]
    public void The_lit_part_of_the_loops_outline_is_progress_over_the_hold_length_of_its_length_clockwise_from_the_top_left()
    {
        foreach (int progress in new[] { 20, 36, 50, 63, 74, 83, LikeHoldTests.LastTickBeforeDone })
        {
            var (g, c, _) = HoldRig.Scene();
            HoldRig.Hold(g, c, Repost, progress);
            HoldRig.Away(g);
            g.Rings.Clear(); g.Particles.Clear();
            var f = HoldRig.Draw(g);

            int step = HoldBeats.StepAt(progress), width = HoldBeats.Widths[step];
            var path = CardPainter.LoopPath(width);
            var (x0, y0) = IconOrigin(g, c, width);
            // the path starts in the top left corner and runs to the right along the top: clockwise
            Assert.Equal(0, path[0].Y);
            const int maxStartX = 2;
            Assert.True(path[0].X <= maxStartX, $"starts at x {path[0].X}");
            Assert.Equal((path[0].X + 1, 0), path[1]);
            Assert.Equal(path.Length, path.Distinct().Count());
            Assert.Equal(width - 1, path.Max(p => p.X));
            Assert.Equal(HoldBeats.IconHeight(width) - 1, path.Max(p => p.Y));

            int lit = (int)Math.Round(path.Length * progress / (double)Tuning.HoldFrames), drawn = 0, litSeen = 0;
            for (int i = 0; i < path.Length; i++)
            {
                uint px = f.Get(x0 + path[i].X, y0 + path[i].Y);
                bool isLit = px == Greens[step], isIdle = px == CardColours.RepostIdle;
                if (!isLit && !isIdle) continue;   // a gap between the two arrows
                drawn++;
                if (isLit) litSeen++;
                Assert.True(isLit == (i < lit), $"progress {progress}: pixel {i} of {path.Length} is {(isLit ? "lit" : "idle")}, the lit part ends at {lit}");
            }
            Assert.True(drawn >= path.Length * (QuartersOfALoop - 1) / QuartersOfALoop, $"progress {progress}: {drawn} of {path.Length} drawn");
            Assert.True(litSeen > 0 && litSeen < drawn, $"progress {progress}: {litSeen} lit of {drawn}");
        }
    }

    [Fact]
    public void On_each_beat_the_arrowheads_are_a_quarter_turn_further_round_the_loop()
    {
        // a frame so long after the beat that its thump has settled
        const long settled = long.MaxValue / 2;
        const int measuredStep = 3;
        int width = HoldBeats.Widths[measuredStep], pathLength = CardPainter.LoopPath(width).Length, quarterTurn = pathLength / QuartersOfALoop;
        Assert.Equal(0, CardPainter.LoopPhase(width, 0, settled));
        for (int step = 1; step < HoldBeats.Ticks.Length; step++)
        {
            int before = CardPainter.LoopPhase(width, step - 1, settled), after = CardPainter.LoopPhase(width, step, settled);
            Assert.Equal(quarterTurn, ((after - before) % pathLength + pathLength) % pathLength);
            // and it gets there over the frames of the thump, not in one jump
            int mid = CardPainter.LoopPhase(width, step, 0);
            int gone = ((mid - before) % pathLength + pathLength) % pathLength;
            Assert.True(gone > 0 && gone < quarterTurn, $"step {step}: {gone} of {quarterTurn} on the beat's first frame");
        }

        // the picture changes with the beat: the same pixels are not drawn at step 2 as at step 3 scaled alike
        const int heldStep = 2;
        var (g, c, _) = HoldRig.Scene();
        HoldRig.Hold(g, c, Repost, HoldBeats.Ticks[heldStep] + LikeHoldTests.PastThumpTicks);
        HoldRig.Away(g);
        g.Rings.Clear();
        var held = HoldRig.Draw(g);
        var box = LikeHoldTests.GrownButtonBox(g, c, Repost);
        var gapsHeld = LoopGaps(held);
        c.Marks.RepostProgress = HoldBeats.Ticks[heldStep + 1] - HoldRig.OneTick;
        var gapsBeforeNextBeat = LoopGaps(HoldRig.Draw(g));
        Assert.Equal(gapsHeld, gapsBeforeNextBeat);
        Assert.NotEmpty(gapsHeld);
        Assert.True(LikeHoldTests.Extent(held, box, Greens[heldStep]).Width > 0);

        // the places along the held beat's loop path drawn in neither its lit nor its idle colour
        List<int> LoopGaps(Surface frame)
        {
            int heldWidth = HoldBeats.Widths[heldStep];
            var path = CardPainter.LoopPath(heldWidth);
            var (x0, y0) = IconOrigin(g, c, heldWidth);
            var gaps = new List<int>();
            for (int i = 0; i < path.Length; i++)
            {
                uint px = frame.Get(x0 + path[i].X, y0 + path[i].Y);
                if (px != Greens[heldStep] && px != CardColours.RepostIdle) gaps.Add(i);
            }
            return gaps;
        }
    }

    [Fact]
    public void Finishing_plays_repost_not_like_the_sparks_and_rings_are_green_and_the_toast_names_the_author()
    {
        var (g, c, played) = HoldRig.Scene();
        int writes = 0;
        g.OnRepost = _ => writes++;
        g.OnLike = _ => Assert.Fail("a repost is not a like");
        var said = new List<string>();
        g.Said = said.Add;
        HoldRig.Hold(g, c, Repost, LikeHoldTests.LastTickBeforeDone);
        g.Particles.Clear(); g.Rings.Clear();
        Assert.Equal(RepostsBefore, CardPainter.CountText(c, Repost));

        HoldRig.Hold(g, c, Repost, LikeHoldTests.FinishingTicks);
        Assert.Equal(SfxNames.Repost, played[^1].Name);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Like);
        Assert.Equal(HoldBeats.FinishedHitStop, g.HitStop);
        Assert.Equal(FinishBurst, g.Particles.Count);
        Assert.All(g.Particles, p => Assert.Equal(ParticleKind.Spark, p.Kind));
        Assert.All(g.Particles, p => Assert.Contains(p.Colour, new[] { Greens[^1], Pale }));
        Assert.Equal(FinishRings, g.Rings.Select(r => (r.R0, r.R1)).OrderBy(r => r.Item1));
        Assert.All(g.Rings, r => Assert.Equal(Greens[^1], r.Colour));
        Assert.Equal(["Reposted @h1.test. Undo in the feed log."], said);
        Assert.Equal(RepostsAfter, CardPainter.CountText(c, Repost));
        Assert.Equal(LikeHoldTests.CountBefore, CardPainter.CountText(c, Like));
        Assert.Equal(1, writes);

        // the frozen frames show the loop near white at its finished width, and it turns as they pass
        Assert.Equal(HoldBeats.FinishedWidth, LikeHoldTests.Extent(HoldRig.Draw(g), LikeHoldTests.GrownButtonBox(g, c, Repost), Pale).Width);
        int n = CardPainter.LoopPath(HoldBeats.FinishedWidth).Length;
        var turns = Enumerable.Range(0, CardPainter.SpinFrames + 1).Select(i => CardPainter.SpinPhase(HoldBeats.FinishedWidth, i)).ToList();
        Assert.Equal(0, turns[0]);
        Assert.Equal(n, turns[CardPainter.SpinFrames]);
        for (int i = 1; i <= CardPainter.SpinFrames; i++) Assert.True(turns[i] > turns[i - 1], "one full turn over the spin frames, always forward");
    }

    [Fact]
    public void Signed_in_with_the_repost_grant_finishing_asks_for_one_repost_write_and_keeps_the_records_address()
    {
        var (g, c, _) = HoldRig.Scene();
        Assert.Equal(Grants.All, g.Grants);
        var asked = new List<FeedPost>();
        g.OnRepost = asked.Add;
        HoldRig.Hold(g, c, Repost, Tuning.HoldFrames);
        Assert.Equal([c.Post], asked);
        Assert.Equal(ButtonState.Pending, c.Marks.Repost);
        Assert.Equal(ButtonState.Off, c.Marks.Like);

        HoldRig.Hold(g, c, Repost, Tuning.Fps);   // staying on it asks for nothing more
        Assert.Single(asked);
        const string record = "at://did:plc:me/app.bsky.feed.repost/7";
        g.WriteConfirmed(c.Post.Uri, Repost, record);
        Assert.Equal(ButtonState.Lit, c.Marks.Repost);
        Assert.Equal(record, c.Marks.RepostUri);
        Assert.Null(c.Marks.LikeUri);
        HoldRig.Away(g);
        HoldRig.Rest(g, LikeHoldTests.PastFrozenTicks);
        g.Particles.Clear(); g.Rings.Clear();
        Assert.Equal(LikeHoldTests.LitGroundPixels(c, Repost), LikeHoldTests.Extent(HoldRig.Draw(g), g.Cards.ButtonBox(c, Repost), CardColours.RepostButtonLit).Width);
        Assert.Equal(CardColours.RepostCountLit, LikeHoldTests.IconFg(g, c, Repost));

        // a write that fails puts the loop back and says so
        var (g2, c2, _) = HoldRig.Scene();
        var said = new List<string>();
        g2.OnRepost = _ => { };
        HoldRig.Hold(g2, c2, Repost, Tuning.HoldFrames);
        g2.Said = said.Add;
        g2.WriteFailed(c2.Post.Uri, Repost, WriteFailure);
        Assert.Equal(ButtonState.Off, c2.Marks.Repost);
        Assert.Equal(RepostsBefore, CardPainter.CountText(c2, Repost));
        Assert.Equal([$"Repost failed: {WriteFailure}."], said);
    }

    [Theory]
    [InlineData(Like, Repost)]
    [InlineData(Repost, Like)]
    public void The_second_of_a_like_and_a_repost_of_one_post_pays_no_second_reward(PostAction first, PostAction second)
    {
        var (g, c, _) = HoldRig.Scene();
        g.OnLike = _ => { };
        g.OnRepost = _ => { };
        HoldRig.Force(g, nameof(RaiderGame.Shield), LikeHoldTests.NoShield);
        HoldRig.Hold(g, c, first, Tuning.HoldFrames);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.True(g.Powered);
        Assert.True(c.Marks.Paid);

        HoldRig.Rest(g, LikeHoldTests.PastFrozenTicks);
        HoldRig.Force(g, nameof(RaiderGame.Shield), LikeHoldTests.NoShield);
        HoldRig.Force(g, LikeHoldTests.PowerUntilField, LikeHoldTests.NoPower);
        g.Popups.Clear();
        HoldRig.Hold(g, c, second, Tuning.HoldFrames);
        Assert.NotEqual(ButtonState.Off, c.Marks.Of(second));
        Assert.Equal(LikeHoldTests.NoShield, g.Shield);
        Assert.False(g.Powered);
        Assert.DoesNotContain(g.Popups, p => p.Text is PickupTests.ShieldUp or PickupTests.PowerUp);
    }

    [Fact]
    public void Without_the_repost_grant_the_button_is_drawn_locked_never_gains_progress_and_the_first_touch_of_a_run_shows_the_toast_once()
    {
        var (g, c, played) = HoldRig.Scene();
        var button = g.Cards.ButtonBox(c, Repost);
        Assert.False(CardPainter.Locked(g, c, Repost));
        Assert.Equal(0, LikeHoldTests.Extent(HoldRig.Draw(g), button, CardColours.DimText).Width);

        g.Grants = Grants.All with { Repost = false, Unlike = false, Unrepost = false };
        int writes = 0;
        g.OnRepost = _ => writes++;
        var said = new List<string>();
        g.Said = said.Add;
        Assert.True(CardPainter.Locked(g, c, Repost));
        Assert.False(CardPainter.Locked(g, c, Like));
        var f = HoldRig.Draw(g);
        // idle, with a small lock where the count was
        Assert.Equal(CardColours.RepostIcon, LikeHoldTests.IconFg(g, c, Repost));
        var mark = LikeHoldTests.Extent(f, button, CardColours.DimText);
        const int lockMinW = 3, lockMaxW = 5, lockMinRows = 4, lockMaxRows = 6;
        Assert.InRange(mark.Width, lockMinW, lockMaxW);
        Assert.InRange(mark.Rows, lockMinRows, lockMaxRows);
        Assert.Equal(LikeHoldTests.CountBefore, CardPainter.CountText(c, Like));

        const int wellPastAHold = Tuning.HoldFrames + Tuning.Fps;
        HoldRig.Hold(g, c, Repost, wellPastAHold);
        Assert.Equal(0, c.Marks.RepostProgress);
        Assert.Equal(ButtonState.Off, c.Marks.Repost);
        Assert.Null(HoldTracker.Touching(g));
        Assert.Equal(0, writes);
        Assert.Equal(["Repost needs a fresh sign-in. See the pause menu."], said);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Hold or SfxNames.Repost);

        // away and back: no second toast in this run
        const int awayTicks = 20, backTicks = Tuning.Fps;
        HoldRig.Rest(g, awayTicks);
        HoldRig.Hold(g, c, Repost, backTicks);
        Assert.Single(said);

        // the heart on the same card still works
        g.OnLike = _ => { };
        HoldRig.Hold(g, c, Like, Tuning.HoldFrames);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);

        // a fresh sign-in mid-run unlocks the button
        g.Grants = Grants.All;
        Assert.False(CardPainter.Locked(g, c, Repost));
        HoldRig.Hold(g, c, Repost, Tuning.HoldFrames);
        Assert.Equal(1, writes);
        Assert.Equal(ButtonState.Pending, c.Marks.Repost);
    }

    [Fact]
    public void Signed_out_the_loop_ends_marked_and_nothing_is_written()
    {
        var (g, c, played) = HoldRig.Scene(signedIn: false);
        g.Grants = Grants.None;   // what a sign-in may do means nothing to a player with none
        int writes = 0;
        g.OnRepost = _ => writes++;
        var said = new List<string>();
        g.Said = said.Add;
        Assert.False(CardPainter.Locked(g, c, Repost));
        HoldRig.Force(g, nameof(RaiderGame.Shield), LikeHoldTests.NoShield);
        HoldRig.Hold(g, c, Repost, Tuning.HoldFrames);
        Assert.Equal(0, writes);
        Assert.Equal(ButtonState.Marked, c.Marks.Repost);
        Assert.Equal(["Sign in to repost for real. Nothing was sent."], said);
        Assert.Equal(RepostsBefore, CardPainter.CountText(c, Repost));
        Assert.Equal(SfxNames.Repost, played[^1].Name);
        Assert.Equal(Tuning.ShieldHits, g.Shield);

        // a line at half strength runs under the marked loop and its count
        HoldRig.Rest(g, LikeHoldTests.SettledTicks);
        g.Particles.Clear(); g.Rings.Clear();
        var f = HoldRig.Draw(g);
        var button = g.Cards.ButtonBox(c, Repost);
        Assert.Equal(CardPainter.ActionSpan(c, Repost).Cols * CardField.PixelsPerCell, LikeHoldTests.Extent(f, button, CardColours.RepostMarked).Width);
        HoldRig.Away(g);
        Assert.Equal(CardColours.RepostLit, LikeHoldTests.IconFg(g, c, Repost));
        Assert.Equal(0, LikeHoldTests.Extent(f, button, CardColours.RepostLit, CardColours.RepostIdle).Width);

        // the heart of the same post, later in the run: marked too, and no second toast
        HoldRig.Hold(g, c, Like, Tuning.HoldFrames);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
        Assert.Single(said);
    }
}
