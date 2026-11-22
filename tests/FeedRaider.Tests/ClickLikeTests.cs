using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// A click on a card's heart or repost: it likes or reposts at once, a second one undoes, and a press anywhere else does nothing.
public class ClickLikeTests
{
    private const PostAction Like = PostAction.Like, Repost = PostAction.Repost;
    private const int NoShield = 0;
    private const string ShieldReadyField = "_shieldRewardReadyTick";
    private const long CooldownOver = 0;
    private const string LikeRecord = "at://did:plc:me/app.bsky.feed.like/1";
    private const string LikeRemoved = "Like removed.";
    private const string SpeakerWords = "a few words from the enemy that stand over the heart";
    /// <summary>The most frames a press may wait for a frozen frame to pass before it is read.</summary>
    private const int MaxFrozenFrames = 30;

    private sealed class Writes
    {
        public readonly List<string> Likes = [], Reposts = [], Deletes = [];
        public void Hook(RaiderGame g)
        {
            g.OnLike = p => Likes.Add(p.Uri);
            g.OnRepost = p => Reposts.Add(p.Uri);
            g.OnDelete = (p, a) => Deletes.Add(p.Uri);
        }
    }

    private static (double Col, double Row) CellAt(RaiderGame g, double x, double y) =>
        (g.Layout.Col0 + x / CardField.PixelsPerCell, y / CardField.PixelsPerCell);

    private static (double X, double Y) Centre(RaiderGame g, Card c, PostAction a)
    {
        var box = g.Cards.ButtonBox(c, a);
        return (box.X + box.W / 2.0, box.Y + box.H / 2.0);
    }

    /// <summary>A press at field pixel (x, y), read on the next tick that is not frozen. The ship is kept off every button and the
    /// card is put back where it was, so the ship never stands on a button the press did not choose.</summary>
    private static void PressAt(RaiderGame g, Card c, double x, double y, bool touch = false)
    {
        int top = c.Top;
        var (col, row) = CellAt(g, x, y);
        g.Input.Click(col, row, touch);
        long tick = g.Tick;
        for (int i = 0; g.Tick == tick; i++)
        {
            Assert.True(i < MaxFrozenFrames, "the press was never read");
            PressRig.Away(g);
            PressRig.Step(g);
        }
        c.Top = top;
    }

    private static void Press(RaiderGame g, Card c, PostAction a, bool touch = false)
    {
        var (x, y) = Centre(g, c, a);
        PressAt(g, c, x, y, touch);
    }

    /// <summary>Lets the press debounce run out, with the ship off every button.</summary>
    private static void PastRepeat(RaiderGame g) => PressRig.Rest(g, Tuning.PressRepeatFrames);

    [Fact]
    public void A_click_on_a_heart_likes_it_at_once_writes_it_once_and_gives_the_shield_back()
    {
        var (g, c, played) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        PressRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        Press(g, c, Like);
        Assert.Equal([c.Post.Uri], writes.Likes);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.Contains(played, p => p.Name == SfxNames.Like);
        Assert.Contains(g.Particles, p => p.Kind == ParticleKind.Heart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_click_or_a_tap_on_a_gold_cards_heart_gives_a_bomb_and_shows_the_banger_popup(bool touch)
    {
        var (g, c, played) = PressRig.Scene(post: PressRig.Post(1, likes: Tuning.GoldLikes));
        Press(g, c, Like, touch);
        Assert.Equal(Tuning.ShipBombs + 1, g.Bombs);
        Assert.Contains(g.Popups, p => p.Text == RaiderGame.GoldPopup);
        Assert.Contains(played, cue => cue.Name == SfxNames.PickupBomb);
    }

    [Fact]
    public void A_click_on_a_repost_button_reposts_and_leaves_the_heart_alone()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        Press(g, c, Repost);
        Assert.Equal([c.Post.Uri], writes.Reposts);
        Assert.Empty(writes.Likes);
        Assert.Equal(ButtonState.Pending, c.Marks.Repost);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
    }

    [Fact]
    public void A_click_on_a_lit_heart_undoes_it_through_the_delete_the_feed_log_uses_and_plays_the_undo_sound()
    {
        var (g, c, played) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        var said = new List<string>();
        g.Said = said.Add;
        Press(g, c, Like);
        g.WriteConfirmed(c.Post.Uri, Like, LikeRecord);
        Assert.Equal(ButtonState.Lit, c.Marks.Like);
        PastRepeat(g);
        played.Clear();

        Press(g, c, Like);
        Assert.Equal([c.Post.Uri], writes.Deletes);
        Assert.Single(writes.Likes);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        Assert.Contains(played, p => p.Name == SfxNames.Undo);
        g.WriteConfirmed(c.Post.Uri, Like, null, undo: true);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        Assert.Null(c.Marks.LikeUri);
        Assert.Equal(LikeRemoved, said[^1]);
    }

    [Fact]
    public void Like_undo_like_pays_the_reward_once()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        PressRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        Press(g, c, Like);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        g.WriteConfirmed(c.Post.Uri, Like, LikeRecord);
        PastRepeat(g);
        Press(g, c, Like);
        g.WriteConfirmed(c.Post.Uri, Like, null, undo: true);
        Assert.True(c.Marks.Paid);

        PressRig.Force(g, nameof(RaiderGame.Shield), NoShield);
        PressRig.Force(g, ShieldReadyField, CooldownOver);
        PastRepeat(g);
        Press(g, c, Like);
        Assert.Equal(2, writes.Likes.Count);
        Assert.Equal(NoShield, g.Shield);
    }

    [Fact]
    public void A_click_while_the_write_is_pending_sends_nothing_and_says_still_sending_at_most_once_a_second()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        var said = new List<string>();
        g.Said = said.Add;
        Press(g, c, Like);
        said.Clear();
        PastRepeat(g);
        Press(g, c, Like);
        PastRepeat(g);
        Press(g, c, Like);
        Assert.Equal([RaiderGame.StillSending], said);
        PressRig.Rest(g, Tuning.Fps);
        Press(g, c, Like);
        Assert.Equal([RaiderGame.StillSending, RaiderGame.StillSending], said);
        Assert.Single(writes.Likes);
        Assert.Empty(writes.Deletes);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
    }

    [Fact]
    public void Signed_out_a_click_marks_the_heart_writes_nothing_and_says_nothing_was_sent_and_a_second_click_clears_the_mark()
    {
        var (g, c, played) = PressRig.Scene(signedIn: false);
        var writes = new Writes();
        writes.Hook(g);
        var said = new List<string>();
        g.Said = said.Add;
        Press(g, c, Like);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
        Assert.Equal([LikePressTests.SignedOutLikeToast], said);
        PastRepeat(g);
        played.Clear();
        Press(g, c, Like);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        Assert.Contains(played, p => p.Name == SfxNames.Undo);
        Assert.Empty(writes.Likes);
        Assert.Empty(writes.Deletes);
    }

    [Fact]
    public void Two_presses_on_one_button_inside_the_repeat_span_act_once()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        Press(g, c, Like);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
        Press(g, c, Like);
        Assert.Equal(ButtonState.Marked, c.Marks.Like);
        PastRepeat(g);
        Press(g, c, Like);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
    }

    [Fact]
    public void One_cell_above_the_heart_a_tap_likes_it_and_a_click_does_not()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        var box = g.Cards.ButtonBox(c, Like);
        double x = box.X + box.W / 2.0, above = box.Y - Tuning.TouchPadCells * CardField.PixelsPerCell;
        PressAt(g, c, x, above);
        Assert.Empty(writes.Likes);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        PressAt(g, c, x, above, touch: true);
        Assert.Equal([c.Post.Uri], writes.Likes);
    }

    [Fact]
    public void Past_the_pad_a_tap_likes_nothing()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        var box = g.Cards.ButtonBox(c, Like);
        const double justPast = 1;
        PressAt(g, c, box.X + box.W / 2.0, box.Y - Tuning.TouchPadCells * CardField.PixelsPerCell - justPast, touch: true);
        Assert.Empty(writes.Likes);
    }

    [Fact]
    public void A_press_on_a_heart_under_the_hud_does_nothing()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        c.Top = CardPainter.HudPixelRows / CardField.PixelsPerCell - 1 - c.Shape.LikeButton.Y;
        Assert.True(g.Cards.ButtonBox(c, Like).Y < CardPainter.HudPixelRows);
        Press(g, c, Like, touch: true);
        Assert.Empty(writes.Likes);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
    }

    [Fact]
    public void A_tap_on_the_bomb_button_bombs_and_likes_nothing()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        int bombs = g.Bombs;
        var (col, row, _, _) = g.BombButton;
        g.Input.Click(col, row, touch: true);
        PressRig.Step(g);
        Assert.Equal(bombs - 1, g.Bombs);
        Assert.Empty(writes.Likes);
        Assert.Empty(writes.Reposts);
    }

    [Fact]
    public void A_click_on_a_speech_box_over_a_heart_likes_nothing_and_the_same_click_without_the_box_likes()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        var (x, y) = Centre(g, c, Like);
        var speaker = new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = x, Y = y, Header = "SPAM", Text = SpeakerWords, Live = true };
        EnemySpeechBox.Update(speaker, g.Layout.PCols, g.Layout.Rows);
        speaker.SpeechBoxAge = 1;
        var (col, row) = CellAt(g, x, y);
        int boxTopRow = (int)Math.Floor(row) - 1;
        speaker.Y += (EnemySpeechBox.Box(speaker, g.Layout).R - boxTopRow) * CardField.PixelsPerCell;
        g.Enemies.Add(speaker);
        var box = Assert.Single(EnemySpeechBox.Placed(g, g.Layout, Tuning.MaxSpeechBoxes)).Box;
        Assert.True(col >= box.C && col < box.Right && row >= box.R && row < box.Bottom, $"the box {box} is not over the heart at {col}, {row}");
        g.Input.Click(col, row);
        g.Step();
        Assert.Empty(writes.Likes);

        PressRig.Sweep(g);
        PastRepeat(g);
        Press(g, c, Like);
        Assert.Equal([c.Post.Uri], writes.Likes);
    }

    [Fact]
    public void A_click_on_a_button_and_a_key_with_the_ship_on_it_act_on_the_same_card()
    {
        var (g, c, _) = PressRig.Scene();
        foreach (var a in (PostAction[])[Like, Repost])
        {
            var (x, y) = Centre(g, c, a);
            PressRig.Force(g, nameof(RaiderGame.PX), x);
            PressRig.Force(g, nameof(RaiderGame.PY), y);
            Assert.Equal((c, a), g.Cards.ButtonAt(x, y, 0));
            Assert.Same(c, g.KeyCard);
        }
    }

    [Fact]
    public void A_tap_on_a_card_away_from_its_buttons_does_nothing()
    {
        var (g, c, _) = PressRig.Scene();
        var writes = new Writes();
        writes.Hook(g);
        var box = g.Cards.ButtonBox(c, Like);
        const int wellAboveCells = 3;
        PressAt(g, c, box.X + box.W / 2.0, box.Y - wellAboveCells * CardField.PixelsPerCell, touch: true);
        Assert.Empty(writes.Likes);
        Assert.Empty(writes.Reposts);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
    }
}
