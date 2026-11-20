using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class CardFieldTests
{
    private static FeedPost P(int n, string text = "hello") =>
        new($"at://did:plc:a/app.bsky.feed.post/{n}", "c", "did:plc:a", "a.test", text, 1, 0, 0, null, null);

    // field of 56 columns: cards are 52 wide, two columns in from each side
    private static CardField Field(int step = 3, int cols = 56, int rows = 40, int posts = 0)
    {
        var q = new PostQueue();
        for (int i = 0; i < posts; i++) q.Offer(P(i));
        return new CardField(q, new PostBook(), cols, rows, step);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 20)]
    [InlineData(3, 15)]
    [InlineData(4, 10)]
    [InlineData(5, 6)]
    public void Each_speed_step_has_its_frames_per_row(int step, int frames) => Assert.Equal(frames, FeedSpeed.FramesPerRow(step));

    [Fact]
    public void A_speed_out_of_range_is_clamped()
    {
        Assert.Equal(30, FeedSpeed.FramesPerRow(-4));
        Assert.Equal(6, FeedSpeed.FramesPerRow(99));
        Assert.Equal(3, FeedSpeed.Default);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void At_each_speed_a_card_moves_one_row_every_frames_per_row_steps_and_not_between(int step)
    {
        var f = Field(step, posts: 1);
        f.Step();   // the first card enters
        var c = f.Cards[0];
        int fpr = FeedSpeed.FramesPerRow(step);
        Assert.Equal(fpr, f.FramesPerRow);
        for (int row = 0; row < 3; row++)
        {
            int top = c.Top;
            for (int i = 0; i < fpr - 1; i++) { f.Step(); Assert.Equal(top, c.Top); }
            f.Step();
            Assert.Equal(top + 1, c.Top);
        }
    }

    [Fact]
    public void A_card_enters_just_above_the_top()
    {
        var f = Field(posts: 2);
        f.Step();
        Assert.Single(f.Cards);
        Assert.Equal(-f.Cards[0].Shape.Rows, f.Cards[0].Top);
    }

    [Fact]
    public void The_next_card_enters_once_the_one_before_has_cleared_two_rows_and_a_post_is_waiting()
    {
        var f = Field(step: 5, posts: 2);
        f.Step();
        var first = f.Cards[0];
        int guard = 0;
        while (f.Cards.Count == 1 && guard++ < 10000)
        {
            Assert.True(first.Top < 2);
            f.Step();
        }
        Assert.Equal(2, f.Cards.Count);
        Assert.Equal(2, first.Top);
        Assert.Equal(-f.Cards[1].Shape.Rows, f.Cards[1].Top);
    }

    [Fact]
    public void With_no_post_waiting_no_card_enters()
    {
        var f = Field();
        for (int i = 0; i < 200; i++) f.Step();
        Assert.Empty(f.Cards);
    }

    [Fact]
    public void A_post_offered_later_enters_when_it_arrives()
    {
        var q = new PostQueue();
        var f = new CardField(q, new PostBook(), 56, 40, 5);
        for (int i = 0; i < 20; i++) f.Step();
        q.Offer(P(1));
        f.Step();
        Assert.Single(f.Cards);
    }

    [Fact]
    public void A_card_that_has_passed_the_bottom_is_removed_and_reported_exactly_once()
    {
        var left = new List<Card>();
        var f = Field(step: 5, rows: 20, posts: 1);
        f.Left += left.Add;
        f.Step();
        var c = f.Cards[0];
        while (c.Top < 19) f.Step();   // its top row is still on the field
        Assert.Contains(c, f.Cards);
        Assert.Empty(left);
        for (int i = 0; i < 6; i++) f.Step();   // one more row step: its top row is below the bottom row
        Assert.Empty(f.Cards);
        for (int i = 0; i < 100; i++) f.Step();
        Assert.Single(left);
        Assert.Same(c, left[0]);
    }

    [Fact]
    public void A_change_of_speed_takes_effect_at_the_next_row_step()
    {
        var f = Field(step: 1, posts: 1);
        f.Step();
        var c = f.Cards[0];
        for (int i = 0; i < 5; i++) f.Step();
        int top = c.Top;
        f.SetSpeed(5);
        Assert.Equal(30, f.FramesPerRow);   // the old speed holds until the row completes
        int guard = 0;
        while (c.Top == top && guard++ < 100) f.Step();
        Assert.Equal(top + 1, c.Top);
        Assert.Equal(6, f.FramesPerRow);
        int t2 = c.Top;
        for (int i = 0; i < 5; i++) { f.Step(); Assert.Equal(t2, c.Top); }
        f.Step();
        Assert.Equal(t2 + 1, c.Top);
    }

    [Fact]
    public void A_resize_measures_the_cards_again_and_keeps_their_top_rows()
    {
        var q = new PostQueue();
        q.Offer(P(1, string.Join(" ", Enumerable.Repeat("word", 60))));
        var f = new CardField(q, new PostBook(), 64, 40, 5);
        for (int i = 0; i < 6 * 8; i++) f.Step();
        var c = f.Cards[0];
        int top = c.Top, rows = c.Shape.Rows;
        Assert.Equal(60, c.Shape.Cols);
        f.Resize(30, 58);
        Assert.Same(c, f.Cards[0]);
        Assert.Equal(top, c.Top);
        Assert.Equal(26, c.Shape.Cols);
        Assert.NotEqual(rows, c.Shape.Rows);
    }

    [Fact]
    public void A_buttons_pixel_box_moves_one_cell_of_pixels_with_each_row_step()
    {
        var f = Field(step: 5, cols: 64, posts: 1);
        for (int i = 0; i < 6 * 3; i++) f.Step();
        var c = f.Cards[0];
        var a = f.ButtonBox(c, PostAction.Like);
        for (int i = 0; i < 6; i++) f.Step();
        var b = f.ButtonBox(c, PostAction.Like);
        Assert.Equal(a.X, b.X);
        Assert.Equal(a.Y + CardField.PixelsPerCell, b.Y);
        Assert.Equal(22, a.W);
        Assert.Equal(6, a.H);
        var r = f.ButtonBox(c, PostAction.Repost);
        Assert.True(r.X >= a.X + a.W);
        Assert.Equal(a.Y + CardField.PixelsPerCell, r.Y);
    }

    [Fact]
    public void A_buttons_pixel_box_is_offset_by_the_cards_left_edge_in_the_field()
    {
        var f = Field(step: 5, cols: 64, posts: 1);
        f.Step();
        var c = f.Cards[0];
        Assert.Equal(2, f.CardLeft);
        Assert.Equal((f.CardLeft + c.Shape.LikeButton.X) * CardField.PixelsPerCell, f.ButtonBox(c, PostAction.Like).X);
        Assert.Equal((c.Top + c.Shape.LikeButton.Y) * CardField.PixelsPerCell, f.ButtonBox(c, PostAction.Like).Y);
    }

    [Fact]
    public void The_same_post_offered_twice_gets_the_same_marks_object()
    {
        var book = new PostBook();
        var a = book.For(P(1));
        var b = book.For(P(1));
        Assert.Same(a, b);
        Assert.NotSame(a, book.For(P(2)));
    }

    [Fact]
    public void Marks_start_from_what_the_post_already_carries()
    {
        var liked = P(1) with { AlreadyLiked = true, LikeUri = "at://x/like/1" };
        var m = new PostBook().For(liked);
        Assert.Equal(ButtonState.Lit, m.Like);
        Assert.Equal("at://x/like/1", m.LikeUri);
        Assert.Equal(ButtonState.Off, m.Repost);
        Assert.Equal(ButtonState.Lit, new PostBook().For(P(2) with { AlreadyReposted = true }).Repost);
        Assert.False(m.Paid);
        Assert.Equal(0, m.LikeProgress);
        Assert.Equal(0, m.RepostProgress);
    }

    [Fact]
    public void A_card_shares_its_marks_with_the_book()
    {
        var book = new PostBook();
        var q = new PostQueue();
        q.Offer(P(1));
        var f = new CardField(q, book, 56, 40, 5);
        f.Step();
        Assert.Same(book.For(P(1)), f.Cards[0].Marks);
    }
}
