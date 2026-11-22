using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// Following an author's self-thread: a like asks for the author's next reply, the reply is picked from a
// getPostThread body, and the game brings it in as the next card with the thread note.
public class ThreadTests
{
    private const string Ann = "did:plc:ann111", AnnHandle = "ann.example.com";
    /// <summary>The author's own first reply in the self-thread fixture: the post a lookup brings in next.</summary>
    private const string AnnsReply = $"{AnnsPosts}p2";
    /// <summary>The start of the address of every post of Ann's.</summary>
    private const string AnnsPosts = $"at://{Ann}/app.bsky.feed.post/";
    /// <summary>Fixtures: a thread the author replies to, one with a picture reply, one only another account answers.</summary>
    private const string SelfThread = "thread-self.json", EmbedThread = "thread-embed.json", OtherAuthorsThread = "thread-none.json";
    private const PostAction Like = PostAction.Like, Repost = PostAction.Repost;
    private const int ShortTimeoutMs = 40;
    /// <summary>The numbers of the thread's first post, of the author's reply to it, and of a post already waiting in the queue.</summary>
    private const int FirstPost = 1, ReplyPost = 2, WaitingPost = 7;

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static FeedPost Root() =>
        new($"{AnnsPosts}p1", "cidp1", Ann, AnnHandle, "first post of a small example thread about gardens and tomatoes", 3, 0, 4, null, null);

    // ---------------------------------------------------------------- picking the next post

    [Fact]
    public void The_next_post_is_the_earliest_direct_reply_by_the_same_author()
    {
        var next = ThreadMapper.Next(Fixture(SelfThread), Root());
        Assert.NotNull(next);
        Assert.Equal(AnnsReply, next.Uri);   // p3 is later, the other account's reply is not the author's
        Assert.Equal(Root().Uri, next.ReplyParent!.Uri);
    }

    [Fact]
    public void A_short_reply_still_continues_the_thread()
    {
        var next = ThreadMapper.Next(Fixture("thread-short.json"), Root());
        Assert.Equal("too short", next?.Text);
    }

    [Fact]
    public void A_picture_reply_continues_the_thread_a_video_or_missing_reply_ends_it_and_a_bad_body_throws()
    {
        Assert.Single(ThreadMapper.Next(Fixture(EmbedThread), Root())!.Pictures!);   // a reply with a picture follows: the picture is drawn on its card
        string video = Fixture(EmbedThread).Replace("app.bsky.embed.images#view", "app.bsky.embed.video#view");
        Assert.Null(ThreadMapper.Next(video, Root()));   // the first reply has a video; a later plain one does not rescue it
        Assert.Null(ThreadMapper.Next(Fixture(OtherAuthorsThread), Root()));
        Assert.Null(ThreadMapper.Next("""{"thread":{"$type":"app.bsky.feed.defs#notFoundPost","notFound":true}}""", Root()));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ThreadMapper.Next("not json at all", Root()));   // a failure, not a thread's end
    }

    [Fact]
    public async Task A_lookup_that_could_not_be_read_says_so_and_queues_nothing()
    {
        var failed = Game(signedIn: true);
        await RaiderFeeds.ThreadAsync(failed, new ThrowingSource(), Root());
        Assert.Equal("Reply lookup failed: the thread could not be read.", failed.Toast);
        Assert.Equal(0, failed.Posts.Count);
    }

    [Fact]
    public async Task A_slow_lookup_ends_the_thread_without_a_word()
    {
        var slow = Game(signedIn: true);
        await RaiderFeeds.ThreadAsync(slow, new HangingSource(), Root(), timeoutMs: ShortTimeoutMs);
        Assert.Null(slow.Toast);
        Assert.Equal(0, slow.Posts.Count);
    }

    [Fact]
    public async Task A_lookup_through_the_source_queues_the_post_from_the_fixture_at_the_front()
    {
        var g = Game(signedIn: true);
        g.Posts.Offer(ThreadPost(WaitingPost));
        var src = new JsThreadSource((uri, depth, parents) =>
        {
            Assert.Equal((Root().Uri, 1, 0), (uri, depth, parents));
            return Task.FromResult(Fixture(SelfThread));
        });
        await RaiderFeeds.ThreadAsync(g, src, Root());
        Assert.True(g.Posts.TryDequeue(out var first, out _));
        Assert.Equal(AnnsReply, first.Uri);
        Assert.Equal(1, g.Posts.Count);                         // the one already waiting stays behind it
    }

    // ---------------------------------------------------------------- the game

    private sealed class ThrowingSource : IThreadSource
    {
        public Task<FeedPost?> NextAsync(FeedPost post, CancellationToken ct) => throw new HttpRequestException("offline");
    }

    private sealed class HangingSource : IThreadSource
    {
        public async Task<FeedPost?> NextAsync(FeedPost post, CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); return null; }
    }

    private static RaiderGame Game(bool signedIn)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = signedIn };
        g.SetLayout(FieldLayout.Compute(EnemyKit.BigCols, EnemyKit.BigRows));
        return g;
    }

    /// <summary>Post <paramref name="number"/> of a long thread of Ann's, a reply to the one numbered before it.</summary>
    private static FeedPost ThreadPost(int number, string? text = null) =>
        new($"{AnnsPosts}c{number}", $"cidc{number}", Ann, AnnHandle, text ?? $"post number {number} of a long thread, with plenty of words in it to read", 2, 0, 1,
            null, number == FirstPost ? null : new PostRef($"{AnnsPosts}c{number - 1}", $"cidc{number - 1}"));

    [Fact]
    public void A_reply_the_host_found_goes_to_the_front_of_the_queue()
    {
        var g = Game(signedIn: true);
        g.Posts.Offer(ThreadPost(WaitingPost));
        g.ThreadOpened(ThreadPost(ReplyPost));
        Assert.True(g.Posts.TryDequeue(out var first, out _));
        Assert.Equal(ThreadPost(ReplyPost).Uri, first.Uri);
    }

    [Fact]
    public void A_reply_found_twice_is_queued_once()
    {
        var g = Game(signedIn: true);
        g.ThreadOpened(ThreadPost(ReplyPost));
        g.ThreadOpened(ThreadPost(ReplyPost));
        Assert.Equal(1, g.Posts.Count);
    }

    [Fact]
    public void A_reply_that_is_only_a_link_or_has_no_text_is_not_queued()
    {
        var g = Game(signedIn: true);
        g.ThreadOpened(ThreadPost(ReplyPost) with { LinkText = true });
        g.ThreadOpened(ThreadPost(ReplyPost + 1, text: "   "));
        Assert.Equal(0, g.Posts.Count);
    }

    [Fact]
    public void In_Random_a_labelled_self_reply_is_not_queued()
    {
        var g = Game(signedIn: false);
        g.PlainOnly = true;
        g.ThreadOpened(ThreadPost(ReplyPost) with { Labelled = true });   // e.g. a !warn label
        Assert.Equal(0, g.Posts.Count);
    }

    [Fact]
    public void In_Random_an_unlabelled_self_reply_is_queued()
    {
        var g = Game(signedIn: false);
        g.PlainOnly = true;
        g.ThreadOpened(ThreadPost(ReplyPost));
        Assert.Equal(1, g.Posts.Count);
    }

    [Fact]
    public void Outside_Random_a_labelled_self_reply_is_queued()
    {
        var g = Game(signedIn: true);
        g.ThreadOpened(ThreadPost(ReplyPost) with { Labelled = true });
        Assert.Equal(1, g.Posts.Count);
    }

    [Fact]
    public void An_adult_reply_or_one_with_a_video_is_dropped()
    {
        var g = Game(signedIn: true);
        g.ThreadOpened(ThreadPost(ReplyPost) with { Adult = true });
        g.ThreadOpened(ThreadPost(ReplyPost + 1) with { HasEmbed = true, Attachment = AttachmentKind.Video });
        Assert.Equal(0, g.Posts.Count);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void A_reply_by_an_author_who_opted_out_of_signed_out_viewers_is_queued_only_for_a_signed_in_player(bool signedIn, int queued)
    {
        var g = Game(signedIn);
        g.ThreadOpened(ThreadPost(ReplyPost) with { AuthorOptOut = true });
        Assert.Equal(queued, g.Posts.Count);
    }

    // ---------------------------------------------------------------- the like that asks

    /// <summary>A game with the first post of a thread as its one card, and the addresses of the posts whose next reply it asked for.</summary>
    private static (RaiderGame G, Card C, List<string> Asked) LikeScene(bool signedIn, FeedPost? first = null)
    {
        var (g, c, _) = PressRig.Scene(signedIn, first ?? ThreadPost(FirstPost));
        var asked = new List<string>();
        g.ThreadWanted = p => asked.Add(p.Uri);
        return (g, c, asked);
    }

    /// <summary>Lets the feed scroll until a second card has entered, and returns it.</summary>
    private static Card ScrollToNextCard(RaiderGame g, Card first)
    {
        PressRig.Until(g, () => g.Cards.Cards.Count > 1);
        return g.Cards.Cards.Single(k => k != first);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_like_asks_once_for_the_authors_next_reply_signed_in_or_out(bool signedIn)
    {
        var (g, c, asked) = LikeScene(signedIn);
        PressRig.RestOn(g, c, Like, Tuning.Fps);
        Assert.Empty(asked);
        PressRig.Press(g, c, Like);
        PressRig.Rest(g, Tuning.Fps);
        Assert.Equal([ThreadPost(FirstPost).Uri], asked);
    }

    [Fact]
    public void A_like_made_again_after_its_write_failed_does_not_ask_again()
    {
        var (g, c, asked) = LikeScene(signedIn: true);
        g.OnLike = _ => { };
        PressRig.Press(g, c, Like);
        g.WriteFailed(c.Post.Uri, Like);
        Assert.Equal(ButtonState.Off, c.Marks.Like);
        PressRig.Rest(g, Tuning.PressRepeatFrames + 1);
        PressRig.Press(g, c, Like);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        Assert.Single(asked);
    }

    [Fact]
    public void A_repost_asks_for_no_reply()
    {
        var (g, c, asked) = LikeScene(signedIn: true);
        PressRig.Press(g, c, Repost);
        Assert.Equal(ButtonState.Marked, c.Marks.Repost);
        Assert.Empty(asked);
    }

    [Fact]
    public void A_like_toggled_in_the_feed_log_asks_for_no_reply()
    {
        var (g, c, asked) = LikeScene(signedIn: true);
        g.OnLike = _ => { };
        g.Log.Add(new LogEntry(c.Post, c.Marks));
        g.Toggle(c.Post.Uri, Like);
        Assert.Equal(ButtonState.Pending, c.Marks.Like);
        Assert.Empty(asked);
    }

    [Fact]
    public void The_reply_a_like_brings_is_the_next_card_to_enter_ahead_of_every_waiting_post_and_carries_the_thread_note()
    {
        var (g, c, _) = LikeScene(signedIn: false);
        g.ThreadWanted = _ =>
        {
            g.Posts.Offer(PressRig.Post(WaitingPost));
            g.Posts.Offer(PressRig.Post(WaitingPost + 1));
            g.ThreadOpened(ThreadPost(ReplyPost));
        };
        PressRig.Press(g, c, Like);
        var next = ScrollToNextCard(g, c);
        Assert.Equal(ThreadPost(ReplyPost).Uri, next.Post.Uri);
        Assert.True(next.Thread);
        Assert.Equal(CardLayout.ThreadNote, next.Shape.Note!.Value.Text);
        Assert.False(c.Thread);

        string waitingUri = PressRig.Post(WaitingPost).Uri;
        PressRig.Until(g, () => g.Cards.Cards.Any(k => k.Post.Uri == waitingUri));
        var waiting = g.Cards.Cards.Single(k => k.Post.Uri == waitingUri);
        Assert.False(waiting.Thread);
        Assert.Null(waiting.Shape.Note);
    }

    [Fact]
    public void The_thread_note_is_drawn_in_its_own_colour_under_the_words_and_above_a_picture()
    {
        var withPicture = ThreadPost(ReplyPost) with { HasEmbed = true, Attachment = AttachmentKind.Picture, AttachmentText = "a described picture", Pictures = [new PostPicture("https://cdn.example/p.jpg", 400, 300)] };
        var (g, c, _) = LikeScene(signedIn: false);
        int firstTop = c.Top;
        g.ThreadWanted = _ => g.ThreadOpened(withPicture);
        PressRig.Press(g, c, Like);
        var next = ScrollToNextCard(g, c);
        var s = next.Shape;
        var note = s.Note!.Value;
        Assert.Equal(CardLayout.ThreadNote, note.Text);
        Assert.Equal(s.Lines[^1].Row + 1, note.Row);
        Assert.Equal(note.Row + 1, Assert.Single(s.Pictures).Y);

        c.Top = PressRig.ScrRows;
        next.Top = firstTop;
        PressRig.Away(g);
        PressRig.Sweep(g);
        g.Particles.Clear(); g.Rings.Clear();
        var scr = new Screen(PressRig.ScrCols, PressRig.ScrRows);
        new RaiderView().FitAndRender(g, scr);
        int col = g.Layout.Col0 + g.Cards.CardLeft + note.Col, row = next.Top + note.Row;
        Assert.Equal(CardLayout.ThreadNote[0], scr.CharAt(col, row));
        Assert.Equal(CardColours.ThreadText, scr.Fg[row * scr.Cols + col]);
        Assert.NotEqual(CardColours.DimText, CardColours.ThreadText);
    }

    [Fact]
    public void Liking_the_thread_card_asks_for_the_reply_after_it()
    {
        var (g, c, asked) = LikeScene(signedIn: false);
        int firstTop = c.Top;
        g.ThreadWanted = p =>
        {
            asked.Add(p.Uri);
            if (p.Uri == ThreadPost(FirstPost).Uri) g.ThreadOpened(ThreadPost(ReplyPost));
        };
        PressRig.Press(g, c, Like);
        var next = ScrollToNextCard(g, c);
        c.Top = PressRig.ScrRows;
        next.Top = firstTop;
        PressRig.Press(g, next, Like);
        Assert.Equal([ThreadPost(FirstPost).Uri, ThreadPost(ReplyPost).Uri], asked);
    }

    /// <summary>Likes the thread's first post in a game whose lookups go to <paramref name="src"/>, waits for the lookup the like
    /// started, and returns the game and how many lookups there were.</summary>
    private static async Task<(RaiderGame G, int Lookups)> LikeAndLookUp(IThreadSource src)
    {
        var (g, c, _) = LikeScene(signedIn: false, Root());
        var lookups = new List<Task>();
        g.ThreadWanted = p => lookups.Add(RaiderFeeds.ThreadAsync(g, src, p, timeoutMs: ShortTimeoutMs));
        PressRig.Press(g, c, Like);
        await Task.WhenAll(lookups);
        return (g, lookups.Count);
    }

    [Fact]
    public async Task A_like_whose_only_reply_is_by_another_author_brings_no_card()
    {
        var (g, lookups) = await LikeAndLookUp(new JsThreadSource((_, _, _) => Task.FromResult(Fixture(OtherAuthorsThread))));
        Assert.Equal(1, lookups);
        Assert.Equal(0, g.Posts.Count);
    }

    [Fact]
    public async Task A_like_whose_lookup_runs_out_of_time_changes_nothing_the_player_sees()
    {
        var (g, lookups) = await LikeAndLookUp(new HangingSource());
        Assert.Equal(1, lookups);
        Assert.Equal(0, g.Posts.Count);
        Assert.Equal(LikePressTests.SignedOutLikeToast, g.Toast);
        PressRig.Rest(g, Tuning.Fps);
        Assert.Single(g.Cards.Cards);
    }

    [Fact]
    public async Task A_like_whose_lookup_fails_says_why_and_brings_no_card()
    {
        var (g, lookups) = await LikeAndLookUp(new ThrowingSource());
        Assert.Equal(1, lookups);
        Assert.Equal(0, g.Posts.Count);
        Assert.StartsWith("Reply lookup failed: ", g.Toast);
        PressRig.Rest(g, Tuning.Fps);
        Assert.Single(g.Cards.Cards);
    }

    [Fact]
    public async Task A_like_whose_lookup_finds_the_authors_reply_brings_it_in_as_the_thread_card()
    {
        var (g, lookups) = await LikeAndLookUp(new JsThreadSource((_, _, _) => Task.FromResult(Fixture(SelfThread))));
        Assert.Equal(1, lookups);
        var next = ScrollToNextCard(g, g.Cards.Cards[0]);
        Assert.Equal(AnnsReply, next.Post.Uri);
        Assert.True(next.Thread);
    }
}
