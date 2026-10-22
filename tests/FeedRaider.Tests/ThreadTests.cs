using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Following an author's self-thread: picking the next post from a getPostThread body, and the game opening it under the liked one.
public class ThreadTests
{
    private const string Ann = "did:plc:ann111";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static FeedPost Root() =>
        new($"at://{Ann}/app.bsky.feed.post/p1", "cidp1", Ann, "ann.example.com", "first post of a small example thread about gardens and tomatoes", 3, 0, 4, null, null);

    // ---------------------------------------------------------------- picking the next post

    [Fact]
    public void The_next_post_is_the_earliest_direct_reply_by_the_same_author()
    {
        var next = ThreadMapper.Next(Fixture("thread-self.json"), Root());
        Assert.NotNull(next);
        Assert.Equal($"at://{Ann}/app.bsky.feed.post/p2", next!.Uri);   // p3 is later, the other account's reply is not the author's
        Assert.Equal(Root().Uri, next.ReplyParent!.Uri);
    }

    [Fact]
    public void The_thread_ends_on_an_embed_a_short_reply_no_reply_or_a_bad_body()
    {
        Assert.Null(ThreadMapper.Next(Fixture("thread-embed.json"), Root()));   // the first reply has a picture; a later plain one does not rescue it
        Assert.Null(ThreadMapper.Next(Fixture("thread-short.json"), Root()));
        Assert.Null(ThreadMapper.Next(Fixture("thread-none.json"), Root()));
        Assert.Null(ThreadMapper.Next("""{"thread":{"$type":"app.bsky.feed.defs#notFoundPost","notFound":true}}""", Root()));
        Assert.Null(ThreadMapper.Next("not json at all", Root()));
    }

    [Fact]
    public async Task A_failed_or_slow_lookup_ends_the_thread_without_a_word()
    {
        var g = Game(signedIn: true);
        g.OfferPost(Root());
        var w = Arrived(g);
        await RaiderFeeds.ThreadAsync(g, new ThrowingSource(), w.Post);
        Assert.True(w.Followed);
        Assert.Null(g.Toast);
        Assert.Empty(w.Earlier);

        var g2 = Game(signedIn: true);
        g2.OfferPost(Root());
        var w2 = Arrived(g2);
        await RaiderFeeds.ThreadAsync(g2, new HangingSource(), w2.Post, timeoutMs: 40);
        Assert.True(w2.Followed);
        Assert.Null(g2.Toast);
    }

    [Fact]
    public async Task A_lookup_through_the_source_opens_the_post_from_the_fixture()
    {
        var g = Game(signedIn: true);
        g.OfferPost(Root());
        var w = Arrived(g);
        var src = new JsThreadSource((uri, depth, parents) =>
        {
            Assert.Equal((Root().Uri, 1, 0), (uri, depth, parents));
            return Task.FromResult(Fixture("thread-self.json"));
        });
        await RaiderFeeds.ThreadAsync(g, src, w.Post);
        Assert.EndsWith("/p2", w.Post.Uri);
        Assert.Single(w.Earlier);
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

    private static RaiderGame Game(bool signedIn, int cols = 120, int rows = 46)
    {
        var g = new RaiderGame(5) { LoggedIn = signedIn };
        g.SetLayout(FieldLayout.Compute(cols, rows));
        return g;
    }

    private static FeedPost Chain(int n, bool liked = false, int replies = 1, string? text = null) =>
        new($"at://{Ann}/app.bsky.feed.post/c{n}", $"cidc{n}", Ann, "ann.example.com", text ?? $"post number {n} of a long thread, with plenty of words in it to read", 2, 0, replies,
            null, n == 1 ? null : new PostRef($"at://{Ann}/app.bsky.feed.post/c{n - 1}", $"cidc{n - 1}"), AlreadyLiked: liked);

    private static int Number(FeedPost p) => int.Parse(p.Uri[(p.Uri.LastIndexOf('c') + 1)..]);

    private static Wingman Arrived(RaiderGame g)
    {
        for (int i = 0; i < 400 && !g.Wings.Any(w => w.Age > Tuning.WingEnterFrames); i++) g.Step();
        return g.Wings.First(x => x.Age > Tuning.WingEnterFrames);
    }

    private static void Press(RaiderGame g, Wingman w)
    {
        for (int i = 0; i < 200 && (Math.Abs(g.PX - w.X) > 3 || Math.Abs(g.PY - w.Y) > 3); i++)
        {
            g.Input.Key(GameKey.Left, g.PX > w.X + 1); g.Input.Key(GameKey.Right, g.PX < w.X - 1);
            g.Input.Key(GameKey.Up, g.PY > w.Y + 1); g.Input.Key(GameKey.Down, g.PY < w.Y - 1);
            g.Step();
        }
        foreach (var k in Enum.GetValues<GameKey>()) g.Input.Key(k, false);
        g.Input.Like(); g.Step();
    }

    /// <summary>A game whose host answers each lookup at once with the next post of the chain, up to <paramref name="length"/> posts.</summary>
    private static (RaiderGame G, List<string> Liked, List<string> Asked) Chained(bool signedIn, int length, bool firstLiked = false)
    {
        var g = Game(signedIn);
        var liked = new List<string>();
        var asked = new List<string>();
        g.OnLike = p => liked.Add(p.Uri);
        g.ThreadWanted = p =>
        {
            asked.Add(p.Uri);
            int n = Number(p);
            if (n < length) g.ThreadOpened(p.Uri, Chain(n + 1, replies: n + 1 < length ? 1 : 0)); else g.ThreadEnded(p.Uri);
        };
        g.OfferPost(Chain(1, liked: firstLiked));
        return (g, liked, asked);
    }

    [Fact]
    public void Signed_out_the_first_press_says_sign_in_and_later_ones_stay_quiet()
    {
        var (g, liked, asked) = Chained(signedIn: false, length: 3);
        var w = Arrived(g);
        var sounds = new List<string>();
        g.Sfx = new SfxBus((name, _, _, _) => sounds.Add(name));
        Press(g, w);
        Assert.Equal(RaiderGame.SignInHint, g.Toast);
        Assert.Contains("like", sounds);
        Assert.Single(w.Earlier);                                 // the reply opened all the same
        for (int i = 0; i < 100; i++) g.Step();
        Assert.Null(g.Toast);
        g.Input.Like(); g.Step();                                 // the second press, on the reply
        Assert.Null(g.Toast);
        Assert.Equal(2, w.Earlier.Count);
        Assert.Empty(liked);                                      // nothing was ever written
        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public void Like_targets_the_newest_open_post_and_each_like_opens_the_next()
    {
        var (g, liked, asked) = Chained(signedIn: true, length: 3);
        var w = Arrived(g);
        Press(g, w);
        Assert.Equal([Chain(1).Uri], liked);
        Assert.Equal(Chain(2).Uri, w.Post.Uri);
        Assert.False(w.Liked);                                    // the reply itself is not liked yet
        g.Input.Like(); g.Step();
        Assert.Equal([Chain(1).Uri, Chain(2).Uri], liked);
        Assert.Equal(Chain(3).Uri, w.Post.Uri);
        g.Input.Like(); g.Step();
        Assert.Equal([Chain(1).Uri, Chain(2).Uri, Chain(3).Uri], liked);
        Assert.Equal(Chain(3).Uri, w.Post.Uri);                   // the thread ended there
        Assert.Equal(3, asked.Count);
        g.Input.Like(); g.Step();
        Assert.Equal(3, liked.Count);                             // and the last post is not liked twice
        Assert.Equal(2, w.Earlier.Count);
    }

    [Fact]
    public void A_post_already_liked_still_opens_the_next_without_a_second_like()
    {
        var (g, liked, asked) = Chained(signedIn: true, length: 3, firstLiked: true);
        var w = Arrived(g);
        Assert.True(w.Liked);
        Press(g, w);
        Assert.Empty(liked);
        Assert.Single(asked);
        Assert.Equal(Chain(2).Uri, w.Post.Uri);
        g.Input.Like(); g.Step();
        Assert.Equal([Chain(2).Uri], liked);                      // the reply was not liked before: that one is written
    }

    [Fact]
    public void An_already_liked_post_with_no_replies_is_not_offered()
    {
        var g = Game(signedIn: true);
        g.OfferPost(Chain(1, liked: true, replies: 0));
        var w = Arrived(g);
        Assert.True(w.Followed);
        Assert.Null(g.Prompt);
    }

    [Fact]
    public void A_reply_that_comes_back_after_the_wingman_moved_on_or_left_is_ignored()
    {
        var g = Game(signedIn: true);
        g.OfferPost(Chain(1));
        var w = Arrived(g);
        g.ThreadOpened("at://someone/else", Chain(2));
        Assert.Empty(w.Earlier);
        for (int i = 0; i < w.TotalFrames; i++) g.Step();
        Assert.Empty(g.Wings);
        g.ThreadOpened(Chain(1).Uri, Chain(2));                   // nobody is there to show it
        Assert.Empty(g.Wings);
    }

    [Fact]
    public void An_ineligible_reply_handed_to_the_game_ends_the_thread()
    {
        var g = Game(signedIn: true);
        g.OfferPost(Chain(1));
        var w = Arrived(g);
        g.ThreadOpened(w.Post.Uri, Chain(2, text: "short"));
        Assert.Empty(w.Earlier);
        Assert.True(w.Followed);
    }

    [Fact]
    public void In_Random_a_labelled_self_reply_does_not_open()
    {
        var g = Game(signedIn: false);
        g.PlainOnly = true;
        g.OfferPost(Chain(1));
        var w = Arrived(g);
        g.ThreadOpened(w.Post.Uri, Chain(2) with { Labelled = true });   // e.g. a !warn label
        Assert.Empty(w.Earlier);
        Assert.True(w.Followed);
    }

    [Fact]
    public void In_Random_an_unlabelled_self_reply_still_opens()
    {
        var g = Game(signedIn: false);
        g.PlainOnly = true;
        g.OfferPost(Chain(1));
        var w = Arrived(g);
        g.ThreadOpened(w.Post.Uri, Chain(2));
        Assert.Single(w.Earlier);
    }

    [Fact]
    public void Outside_Random_a_labelled_self_reply_still_opens()
    {
        var g = Game(signedIn: true);
        g.OfferPost(Chain(1));
        var w = Arrived(g);
        g.ThreadOpened(w.Post.Uri, Chain(2) with { Labelled = true });
        Assert.Single(w.Earlier);
    }

    [Fact]
    public void A_wingman_stays_while_its_thread_is_read_but_only_up_to_a_bound()
    {
        var (g, liked, _) = Chained(signedIn: true, length: 60);
        var w = Arrived(g);
        int usual = w.TotalFrames;
        Press(g, w);
        Assert.True(w.TotalFrames - w.Age >= Tuning.ThreadReadFrames);
        for (int i = 0; i < 40; i++) { g.Input.Like(); g.Step(); }
        Assert.True(w.Age < w.TotalFrames);
        Assert.True(w.FightExtra <= Tuning.ThreadMaxExtraFrames);
        Assert.Equal(usual + w.FightExtra, w.TotalFrames);
        for (int i = 0; i < w.TotalFrames + 10; i++) g.Step();
        Assert.DoesNotContain(w, g.Wings);
        Assert.True(w.Age <= usual + Tuning.ThreadMaxExtraFrames);
        Assert.NotEmpty(liked);
    }

    [Fact]
    public void An_open_thread_holds_its_comm_open_until_the_newest_post_is_liked()
    {
        var (g, _, _) = Chained(signedIn: true, length: 3);
        var w = Arrived(g);
        Press(g, w);
        Assert.Same(w, g.Comms.Speaker);
        for (int i = 0; i < WingComms.TotalFrames(w) + 10; i++) g.Step();
        Assert.NotEqual(WingPhase.Leave, w.Phase);
        Assert.Same(w, g.Comms.Speaker);                          // past the post's own time, still open
        Assert.True(w.CommAge < WingComms.TotalFrames(w) - Tuning.CommCollapseFrames);   // and short of handing the mic over
    }

    [Fact]
    public void The_bubble_stacks_at_most_a_few_rows_and_stubs_the_oldest_posts()
    {
        var (g, _, _) = Chained(signedIn: true, length: 8);
        var w = Arrived(g);
        Press(g, w);
        for (int i = 0; i < 4; i++) { g.Input.Like(); g.Step(); }
        Assert.Equal(5, w.Earlier.Count);
        Assert.Equal(WingComms.MaxStubs, WingComms.StubRows(w));
        Assert.True(w.PrevLines.Length <= WingComms.PrevLinesMax);
        Assert.StartsWith("+", WingComms.Stubs(w, w.CommTextW)[0]);
        Assert.True(WingComms.CommH(w) <= g.Layout.Rows / 2, $"{WingComms.CommH(w)} rows of {g.Layout.Rows}");

        string wordy = string.Concat(Enumerable.Repeat("word ", 40)) + "end";
        var phone = Game(signedIn: true, 48, 60);
        phone.ThreadWanted = p => phone.ThreadOpened(p.Uri, Chain(Number(p) + 1, text: wordy));
        phone.OfferPost(Chain(1));
        var pw = Arrived(phone);
        Press(phone, pw);
        for (int i = 0; i < 5; i++) { phone.Input.Like(); phone.Step(); }
        Assert.True(WingComms.CommH(pw) <= phone.Layout.Rows / 2, $"{WingComms.CommH(pw)} rows of {phone.Layout.Rows}");
    }

    [Fact]
    public void The_wide_panel_shows_the_whole_post_and_the_whole_open_thread()
    {
        string tail = "and this is the very last word zebra";
        string longText = string.Join(' ', Enumerable.Repeat("tomatoes ripen slowly in cool spring weather", 6)) + " " + tail;
        var g = Game(signedIn: true, 130, 46);
        g.OfferPost(Chain(1, text: longText));
        var w = Arrived(g);
        for (int i = 0; i < 240; i++) g.Step();                    // typed out in full
        var view = new RaiderView();
        var scr = new Screen(130, 46);
        view.Render(g, scr);
        Assert.Contains("zebra", Panel(scr, g));
        // a reply opens: both posts are in the panel
        g.ThreadOpened(w.Post.Uri, Chain(2, text: "second post says the harvest was big this year, ending in giraffe"));
        for (int i = 0; i < 120; i++) g.Step();
        view.Render(g, scr);
        string panel = Panel(scr, g);
        Assert.Contains("zebra", panel);
        Assert.Contains("giraffe", panel);
    }

    /// <summary>The panel column's text, rows joined with the spaces dropped (a wrapped line ends where a word does).</summary>
    private static string Panel(Screen s, RaiderGame g)
    {
        int c0 = Math.Max(1, g.Layout.Col0 - 28);
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < s.Rows; r++) for (int c = c0; c < c0 + 26; c++) if (s.CharAt(c, r) != ' ') sb.Append(s.CharAt(c, r));
        return sb.ToString();
    }
}
