using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// A press on a post's repost loop reposts it at once: the same burst as the heart, in green, and what a sign-in that may not
// repost sees instead.
public class RepostPressTests
{
    private const PostAction Like = PostAction.Like, Repost = PostAction.Repost;

    /// <summary>The burst of a like or repost: this many particles, and two rings growing between these radii.</summary>
    internal const int PressBurst = 14;
    internal static readonly (double From, double To)[] PressRings = [(3, 14), (6, 22)];
    /// <summary>The repost count a test post shows before its repost, and after it.</summary>
    internal const string RepostsBefore = "4", RepostsAfter = "5";
    /// <summary>The reason a failed write gives in the tests here.</summary>
    private const string WriteFailure = "could not reach Bluesky";

    [Fact]
    public void Pressing_plays_repost_not_like_the_sparks_and_rings_are_green_and_the_toast_names_the_author()
    {
        var (g, c, played) = PressRig.Scene();
        int writes = 0;
        g.OnRepost = _ => writes++;
        g.OnLike = _ => Assert.Fail("a repost is not a like");
        var said = new List<string>();
        g.Said = said.Add;
        PressRig.Park(g, c, Repost);
        g.Particles.Clear(); g.Rings.Clear();
        Assert.Equal(RepostsBefore, CardPainter.CountText(c, Repost));

        PressRig.PressKey(g, Repost);
        PressRig.Step(g);
        Assert.Equal(SfxNames.Repost, played[^1].Name);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Like);
        Assert.Equal(PressLook.HitStop, g.HitStop);
        Assert.Equal(PressBurst, g.Particles.Count);
        Assert.All(g.Particles, p => Assert.Equal(ParticleKind.Spark, p.Kind));
        Assert.All(g.Particles, p => Assert.Contains(p.Colour, new[] { PressLook.Lit(Repost), PressLook.FlashColour(Repost) }));
        Assert.Equal(PressRings, g.Rings.Select(r => (r.R0, r.R1)).OrderBy(r => r.Item1));
        Assert.All(g.Rings, r => Assert.Equal(PressLook.Lit(Repost), r.Colour));
        Assert.Equal(["Reposted @h1.test. Undo in the feed log."], said);
        Assert.Equal(RepostsAfter, CardPainter.CountText(c, Repost));
        Assert.Equal(LikePressTests.CountBefore, CardPainter.CountText(c, Like));
        Assert.Equal(1, writes);

        // the frozen frames show the loop near white at its pressed width, and it turns as they pass
        PressRig.Away(g);
        Assert.Equal(PressLook.MaxIconWidth, LikePressTests.SpanWidth(PressRig.Draw(g), LikePressTests.GrownButtonBox(g, c, Repost), PressLook.FlashColour(Repost)));
        int n = CardPainter.LoopPath(PressLook.MaxIconWidth).Length;
        var turns = Enumerable.Range(0, CardPainter.SpinFrames + 1).Select(i => CardPainter.SpinPhase(PressLook.MaxIconWidth, i)).ToList();
        Assert.Equal(0, turns[0]);
        Assert.Equal(n, turns[CardPainter.SpinFrames]);
        for (int i = 1; i <= CardPainter.SpinFrames; i++) Assert.True(turns[i] > turns[i - 1], "one full turn over the spin frames, always forward");
    }

    [Fact]
    public void Signed_in_with_the_repost_grant_pressing_asks_for_one_repost_write_and_keeps_the_records_address()
    {
        var (g, c, _) = PressRig.Scene();
        Assert.Equal(Grants.All, g.Grants);
        var asked = new List<FeedPost>();
        g.OnRepost = asked.Add;
        PressRig.Press(g, c, Repost);
        Assert.Equal([c.Post], asked);
        Assert.Equal(ButtonState.Pending, c.Marks.Repost);
        Assert.Equal(ButtonState.Off, c.Marks.Like);

        PressRig.RestOn(g, c, Repost, Tuning.Fps);
        PressRig.Press(g, c, Repost);   // pressed again while the write is out
        Assert.Single(asked);
        Assert.Equal(ButtonState.Pending, c.Marks.Repost);
        const string record = "at://did:plc:me/app.bsky.feed.repost/7";
        g.WriteConfirmed(c.Post.Uri, Repost, record);
        Assert.Equal(ButtonState.Lit, c.Marks.Repost);
        Assert.Equal(record, c.Marks.RepostUri);
        Assert.Null(c.Marks.LikeUri);
        PressRig.Away(g);
        PressRig.Rest(g, LikePressTests.PastFrozenTicks);
        g.Particles.Clear(); g.Rings.Clear();
        Assert.Equal(LikePressTests.LitGroundPixels(c, Repost), LikePressTests.Extent(PressRig.Draw(g), g.Cards.ButtonBox(c, Repost), CardColours.RepostButtonLit).Width);
        Assert.Equal(CardColours.RepostCountLit, LikePressTests.IconFg(g, c, Repost));

        // a write that fails puts the loop back and says so
        var (g2, c2, _) = PressRig.Scene();
        var said = new List<string>();
        g2.OnRepost = _ => { };
        PressRig.Press(g2, c2, Repost);
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
        var (g, c, _) = PressRig.Scene();
        g.OnLike = _ => { };
        g.OnRepost = _ => { };
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Press(g, c, first);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.True(c.Marks.Paid);

        PressRig.Rest(g, LikePressTests.PastFrozenTicks);
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Force(g, LikePressTests.ShieldReadyField, LikePressTests.CooldownOver);
        g.Popups.Clear();
        PressRig.Press(g, c, second);
        Assert.NotEqual(ButtonState.Off, c.Marks.Of(second));
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        Assert.DoesNotContain(g.Popups, p => p.Text.StartsWith("SHIELD"));
    }

    [Fact]
    public void Without_the_repost_grant_the_button_is_drawn_locked_and_each_press_on_it_writes_nothing_and_shows_the_sign_in_toast()
    {
        var (g, c, played) = PressRig.Scene();
        var button = g.Cards.ButtonBox(c, Repost);
        Assert.False(CardPainter.Locked(g, c, Repost));
        Assert.Equal(0, LikePressTests.Extent(PressRig.Draw(g), button, CardColours.DimText).Width);

        g.Grants = Grants.All with { Repost = false, Unlike = false, Unrepost = false };
        int writes = 0;
        g.OnRepost = _ => writes++;
        var said = new List<string>();
        g.Said = said.Add;
        Assert.True(CardPainter.Locked(g, c, Repost));
        Assert.False(CardPainter.Locked(g, c, Like));
        var f = PressRig.Draw(g);
        // idle, with a small lock where the count was
        Assert.Equal(CardColours.RepostIcon, LikePressTests.IconFg(g, c, Repost));
        var mark = LikePressTests.Extent(f, button, CardColours.DimText);
        const int lockMinW = 3, lockMaxW = 5, lockMinRows = 4, lockMaxRows = 6;
        Assert.InRange(mark.Width, lockMinW, lockMaxW);
        Assert.InRange(mark.Rows, lockMinRows, lockMaxRows);
        Assert.Equal(LikePressTests.CountBefore, CardPainter.CountText(c, Like));

        const string toast = "Repost needs a fresh sign-in. See the pause menu.";
        PressRig.Press(g, c, Repost);
        Assert.Equal(ButtonState.Off, c.Marks.Repost);
        Assert.Equal(0, writes);
        Assert.Equal([toast], said);
        Assert.Equal(SfxNames.NoLike, played[^1].Name);
        Assert.DoesNotContain(played, p => p.Name == SfxNames.Repost);

        // away and pressed again: the same refusal, said again
        const int awayTicks = 20;
        PressRig.Rest(g, awayTicks);
        PressRig.Press(g, c, Repost);
        Assert.Equal([toast, toast], said);
        Assert.Equal(0, writes);

        // the heart on the same card still works
        g.OnLike = _ => { };
        PressRig.Press(g, c, Like);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);

        // a fresh sign-in mid-run unlocks the button
        g.Grants = Grants.All;
        Assert.False(CardPainter.Locked(g, c, Repost));
        PressRig.Press(g, c, Repost);
        Assert.Equal(1, writes);
        Assert.Equal(ButtonState.Pending, c.Marks.Repost);
    }

    [Fact]
    public void Signed_out_the_loop_ends_marked_and_nothing_is_written()
    {
        var (g, c, played) = PressRig.Scene(signedIn: false);
        g.Grants = Grants.None;   // what a sign-in may do means nothing to a player with none
        int writes = 0;
        g.OnRepost = _ => writes++;
        var said = new List<string>();
        g.Said = said.Add;
        Assert.False(CardPainter.Locked(g, c, Repost));
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Press(g, c, Repost);
        Assert.Equal(0, writes);
        Assert.Equal(ButtonState.Marked, c.Marks.Repost);
        Assert.Equal(["Sign in to repost for real. Nothing was sent."], said);
        Assert.Equal(RepostsBefore, CardPainter.CountText(c, Repost));
        Assert.Equal(SfxNames.Repost, played[^1].Name);
        Assert.Equal(Tuning.ShieldHits, g.Shield);

        // a line at half strength runs under the marked loop and its count
        PressRig.Rest(g, LikePressTests.SettledTicks);
        g.Particles.Clear(); g.Rings.Clear();
        var f = PressRig.Draw(g);
        var button = g.Cards.ButtonBox(c, Repost);
        Assert.Equal(CardPainter.ActionSpan(c, Repost).Cols * CardField.PixelsPerCell, LikePressTests.Extent(f, button, CardColours.RepostMarked).Width);
        PressRig.Away(g);
        Assert.Equal(CardColours.RepostLit, LikePressTests.IconFg(g, c, Repost));
        Assert.Equal(0, LikePressTests.Extent(f, button, CardColours.RepostLit, CardColours.RepostIdle).Width);

        // the heart of the same post, later in the run: marked too, and no second toast
        PressRig.Press(g, c, Like);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
        Assert.Single(said);
    }
}
