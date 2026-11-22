using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// The feed log is where a like or repost is undone, or made late: E and R act on the marked entry, [ and ] move the mark,
// and a click can name a button. A toggle here is only ever the write: it never pays the reward a like on the field pays.
public class LogToggleTests
{
    private const int Cols = 130, Rows = 46;
    private const PostAction Like = PostAction.Like, Repost = PostAction.Repost;
    /// <summary>The likes and reposts every test post carries.</summary>
    private const int Likes = 10, Reposts = 4;
    /// <summary>The records the network hands back: a like as the server names it, a new like, one made before the run, and a new repost.</summary>
    private const string ServerLikeUri = "at://did:plc:me/app.bsky.feed.like/1";
    private const string NewLikeUri = "at://me/like/1", OldLikeUri = "at://me/like/old", NewRepostUri = "at://me/repost/1";
    /// <summary>A write's failure reason, the line said when a like is taken back.</summary>
    private const string NoNetwork = "no network", LikeRemoved = "Like removed.";

    private static FeedPost Post(int id, bool liked = false, string? likeUri = null) =>
        new($"at://did:plc:t{id}/app.bsky.feed.post/{id}", $"cid{id}", $"did:plc:t{id}", $"t{id}.test",
            $"post number {id} says a few ordinary words and then a few more", Likes, Reposts, 0, null, null,
            DisplayName: "Display Name", AlreadyLiked: liked, LikeUri: likeUri);

    /// <summary>The handle as the feed log shows it.</summary>
    private static string ShownHandle(int id) => $"@t{id}.test";

    /// <summary>How the recorder writes down each network write.</summary>
    private static string LikeWrite(FeedPost post) => "like " + post.Uri;
    private static string RepostWrite(FeedPost post) => "repost " + post.Uri;
    private static string DeleteWrite(PostAction action, FeedPost post) => $"delete {action} {post.Uri}";

    /// <summary>What the game sent out: the network writes, the lines it said and the sounds it played.</summary>
    private sealed class Recorder
    {
        public readonly List<string> Writes = [];
        public readonly List<string> Said = [];
        public readonly List<string> Played = [];
    }

    private static (RaiderGame Game, Recorder Recorder) RecordedGame(bool signedIn = true, Grants? grants = null)
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = signedIn, BossGates = false };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        var recorder = new Recorder();
        game.OnLike = post => recorder.Writes.Add(LikeWrite(post));
        game.OnRepost = post => recorder.Writes.Add(RepostWrite(post));
        game.OnDelete = (post, action) => recorder.Writes.Add(DeleteWrite(action, post));
        game.Said = recorder.Said.Add;
        game.Sfx = new SfxBus((sound, _, _, _) => recorder.Played.Add(sound));
        if (grants is not null) game.Grants = grants;
        return (game, recorder);
    }

    private static LogEntry AddToLog(RaiderGame game, FeedPost post)
    {
        var entry = new LogEntry(post, game.Book.MarksFor(post));
        game.Log.Add(entry);
        return entry;
    }

    private static void StepIdle(RaiderGame game, int frames = 1) { for (int i = 0; i < frames; i++) { PressRig.Away(game); PressRig.Step(game); } }

    // --- the mark ---

    [Fact]
    public void The_mark_starts_on_the_newest_entry_and_moves_to_each_new_arrival()
    {
        var (game, _) = RecordedGame();
        Assert.Null(game.Log.Marked);
        var older = AddToLog(game, Post(1));
        Assert.Same(older, game.Log.Marked);
        var newer = AddToLog(game, Post(2));
        Assert.Same(newer, game.Log.Marked);
    }

    [Fact]
    public void Bracket_left_moves_the_mark_to_the_older_entry_and_it_then_stays_with_that_post()
    {
        var (game, _) = RecordedGame();
        var first = AddToLog(game, Post(1));
        var second = AddToLog(game, Post(2));
        AddToLog(game, Post(3));
        game.Log.MoveMark(-1);
        Assert.Same(second, game.Log.Marked);
        var fourth = AddToLog(game, Post(4));
        Assert.Same(second, game.Log.Marked);
        game.Log.MoveMark(-1);
        Assert.Same(first, game.Log.Marked);
        game.Log.MoveMark(-1);
        Assert.Same(first, game.Log.Marked);   // nothing older
        const int downPastTheNewest = 4;   // three moves reach the fourth, and one more finds nothing newer
        for (int i = 0; i < downPastTheNewest; i++) game.Log.MoveMark(1);
        Assert.Same(fourth, game.Log.Marked);
        var entry = AddToLog(game, Post(5));
        Assert.Same(entry, game.Log.Marked);   // back on the newest, it follows again
    }

    [Fact]
    public void When_the_marked_post_leaves_the_log_the_mark_returns_to_the_newest()
    {
        var log = new FeedLog();
        var first = new LogEntry(Post(1), new PostMarks(Post(1)));
        log.Add(first);
        for (int i = 2; i <= 5; i++) log.Add(new LogEntry(Post(i), new PostMarks(Post(i))));
        while (!ReferenceEquals(log.Marked, first)) log.MoveMark(-1);
        for (int i = 6; i <= FeedLog.MaxEntries + 1; i++) log.Add(new LogEntry(Post(i), new PostMarks(Post(i))));
        Assert.DoesNotContain(first, log.Entries);
        Assert.Same(log.Entries[^1], log.Marked);
    }

    [Fact]
    public void Bracket_right_on_the_newest_does_nothing()
    {
        var (game, _) = RecordedGame();
        AddToLog(game, Post(1));
        var newer = AddToLog(game, Post(2));
        game.Log.MoveMark(1);
        Assert.Same(newer, game.Log.Marked);
    }

    [Fact]
    public void The_keys_move_the_mark_through_the_input()
    {
        var (game, _) = RecordedGame();
        var older = AddToLog(game, Post(1));
        var newer = AddToLog(game, Post(2));
        game.Input.MarkUp(); StepIdle(game);
        Assert.Same(older, game.Log.Marked);
        game.Input.MarkDown(); StepIdle(game);
        Assert.Same(newer, game.Log.Marked);
    }

    // --- like and unlike ---

    [Fact]
    public void E_on_an_unlit_entry_signed_in_asks_for_one_like_pending_then_lit_with_the_count_one_higher()
    {
        var (game, recorder) = RecordedGame();
        var post = Post(1);
        var entry = AddToLog(game, post);
        game.Input.LogLike(); StepIdle(game);
        Assert.Equal([LikeWrite(post)], recorder.Writes);
        Assert.Equal(ButtonState.Pending, entry.Marks.Like);
        StepIdle(game, 30);
        Assert.Single(recorder.Writes);
        game.WriteConfirmed(post.Uri, Like, ServerLikeUri);
        Assert.Equal(ButtonState.Lit, entry.Marks.Like);
        Assert.Equal((Likes + 1).ToString(), CardPainter.CountText(post, entry.Marks, Like));
        Assert.Equal(ServerLikeUri, entry.Marks.LikeUri);
    }

    [Fact]
    public void E_on_a_lit_entry_deletes_the_kept_record_pending_then_off_with_the_count_back_and_says_so()
    {
        var (game, recorder) = RecordedGame();
        var post = Post(1);
        var entry = AddToLog(game, post);
        game.Toggle(post.Uri, Like);
        game.WriteConfirmed(post.Uri, Like, NewLikeUri);
        recorder.Writes.Clear(); recorder.Said.Clear();
        game.Input.LogLike(); StepIdle(game);
        Assert.Equal([DeleteWrite(Like, post)], recorder.Writes);
        Assert.Equal(ButtonState.Pending, entry.Marks.Like);
        game.WriteConfirmed(post.Uri, Like, null, undo: true);
        Assert.Equal(ButtonState.Off, entry.Marks.Like);
        Assert.Null(entry.Marks.LikeUri);
        Assert.Equal(Likes.ToString(), CardPainter.CountText(post, entry.Marks, Like));
        Assert.Equal(LikeRemoved, game.Toast);
        Assert.Contains(LikeRemoved, recorder.Said);
    }

    [Fact]
    public void A_post_liked_before_the_run_can_be_unliked_because_the_address_came_with_the_feed()
    {
        var (game, recorder) = RecordedGame();
        var post = Post(1, liked: true, likeUri: OldLikeUri);
        var entry = AddToLog(game, post);
        Assert.Equal(ButtonState.Lit, entry.Marks.Like);
        Assert.Equal(Likes.ToString(), CardPainter.CountText(post, entry.Marks, Like));
        game.Toggle(post.Uri, Like);
        Assert.Equal([DeleteWrite(Like, post)], recorder.Writes);
        Assert.Equal(OldLikeUri, entry.Marks.LikeUri);   // the runner reads it from here
        game.WriteConfirmed(post.Uri, Like, null, undo: true);
        Assert.Equal(ButtonState.Off, entry.Marks.Like);
        Assert.Equal((Likes - 1).ToString(), CardPainter.CountText(post, entry.Marks, Like));
    }

    [Fact]
    public void R_does_the_same_for_reposts_with_its_own_toast()
    {
        var (game, recorder) = RecordedGame();
        var post = Post(1);
        var entry = AddToLog(game, post);
        game.Input.LogRepost(); StepIdle(game);
        Assert.Equal([RepostWrite(post)], recorder.Writes);
        Assert.Equal(ButtonState.Pending, entry.Marks.Repost);
        game.WriteConfirmed(post.Uri, Repost, NewRepostUri);
        Assert.Equal(ButtonState.Lit, entry.Marks.Repost);
        Assert.Equal((Reposts + 1).ToString(), CardPainter.CountText(post, entry.Marks, Repost));
        recorder.Writes.Clear();
        game.Input.LogRepost(); StepIdle(game);
        Assert.Equal([DeleteWrite(Repost, post)], recorder.Writes);
        game.WriteConfirmed(post.Uri, Repost, null, undo: true);
        Assert.Equal(ButtonState.Off, entry.Marks.Repost);
        Assert.Equal(Reposts.ToString(), CardPainter.CountText(post, entry.Marks, Repost));
        Assert.Equal("Repost removed.", game.Toast);
        Assert.Equal(ButtonState.Off, entry.Marks.Like);
    }

    [Fact]
    public void A_second_press_while_pending_is_ignored()
    {
        var (game, recorder) = RecordedGame();
        var post = Post(1);
        AddToLog(game, post);
        game.Toggle(post.Uri, Like);
        game.Toggle(post.Uri, Like);
        game.Input.LogLike(); StepIdle(game);
        Assert.Single(recorder.Writes);
        game.WriteConfirmed(post.Uri, Like, NewLikeUri);
        game.Toggle(post.Uri, Like);
        game.Toggle(post.Uri, Like);
        Assert.Equal(2, recorder.Writes.Count);
    }

    [Fact]
    public void A_failed_write_puts_the_icon_back_plays_nolike_and_shows_the_toast()
    {
        var (game, recorder) = RecordedGame();
        var post = Post(1);
        var entry = AddToLog(game, post);
        game.Toggle(post.Uri, Like);
        game.WriteFailed(post.Uri, Like, NoNetwork);
        Assert.Equal(ButtonState.Off, entry.Marks.Like);
        Assert.Equal(Likes.ToString(), CardPainter.CountText(post, entry.Marks, Like));
        Assert.Contains(SfxNames.NoLike, recorder.Played);
        Assert.Contains(NoNetwork, game.Toast);
    }

    [Fact]
    public void A_failed_delete_puts_the_icon_back_lit_with_its_address_and_count()
    {
        var (game, recorder) = RecordedGame();
        var post = Post(1, liked: true, likeUri: OldLikeUri);
        var entry = AddToLog(game, post);
        game.Toggle(post.Uri, Like);
        game.WriteFailed(post.Uri, Like, NoNetwork, undo: true);
        Assert.Equal(ButtonState.Lit, entry.Marks.Like);
        Assert.Equal(OldLikeUri, entry.Marks.LikeUri);
        Assert.Equal(Likes.ToString(), CardPainter.CountText(post, entry.Marks, Like));
        Assert.Contains(SfxNames.NoLike, recorder.Played);
        Assert.Contains(NoNetwork, game.Toast);
    }

    [Fact]
    public void Without_the_grant_a_toggle_changes_nothing_and_shows_the_fresh_sign_in_toast()
    {
        var (game, recorder) = RecordedGame(grants: new Grants(Like: true, Unlike: false, Repost: false, Unrepost: false));
        var lit = Post(1, liked: true, likeUri: OldLikeUri);
        var entry = AddToLog(game, lit);
        var plain = Post(2);
        var plainEntry = AddToLog(game, plain);

        game.Toggle(lit.Uri, Like);
        Assert.Equal(ButtonState.Lit, entry.Marks.Like);
        Assert.Equal("Undo needs a fresh sign-in. See the pause menu.", game.Toast);
        game.Say("");
        game.Toggle(plain.Uri, Repost);
        Assert.Equal(ButtonState.Off, plainEntry.Marks.Repost);
        Assert.Equal("Repost needs a fresh sign-in. See the pause menu.", game.Toast);
        Assert.Empty(recorder.Writes);
    }

    [Fact]
    public void Signed_out_E_and_R_flip_the_mark_on_and_off_and_write_nothing()
    {
        var (game, recorder) = RecordedGame(signedIn: false);
        var post = Post(1);
        var entry = AddToLog(game, post);
        game.Input.LogLike(); StepIdle(game);
        Assert.Equal(ButtonState.Marked, entry.Marks.Like);
        Assert.Equal(Likes.ToString(), CardPainter.CountText(post, entry.Marks, Like));
        game.Input.LogLike(); StepIdle(game);
        Assert.Equal(ButtonState.Off, entry.Marks.Like);
        game.Input.LogRepost(); StepIdle(game);
        Assert.Equal(ButtonState.Marked, entry.Marks.Repost);
        game.Input.LogRepost(); StepIdle(game);
        Assert.Equal(ButtonState.Off, entry.Marks.Repost);
        Assert.Empty(recorder.Writes);
    }

    [Fact]
    public void A_toggle_in_the_log_never_raises_the_shield_or_adds_a_bomb_and_an_undo_never_takes_one_away()
    {
        var (game, _) = RecordedGame();
        var post = Post(1);
        AddToLog(game, post);
        PressRig.Force(game, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        int bombs = game.Bombs;
        game.Toggle(post.Uri, Like);
        game.WriteConfirmed(post.Uri, Like, NewLikeUri);
        game.Toggle(post.Uri, Repost);
        game.WriteConfirmed(post.Uri, Repost, NewRepostUri);
        const int settleFrames = 3;
        StepIdle(game, settleFrames);
        Assert.Equal(LikePressTests.NoShield, game.Shield);
        Assert.Equal(bombs, game.Bombs);
        Assert.False(game.Book.Find(post.Uri)!.Paid);

        // a reward already paid on the field stays when the like is undone
        PressRig.Force(game, nameof(RaiderGame.Shield), Tuning.ShieldHits);
        game.Book.Find(post.Uri)!.Paid = true;
        game.Toggle(post.Uri, Like);
        game.WriteConfirmed(post.Uri, Like, null, undo: true);
        Assert.Equal(Tuning.ShieldHits, game.Shield);
        Assert.Equal(bombs, game.Bombs);
    }

    [Fact]
    public void A_post_unliked_in_the_log_can_be_liked_again_when_it_comes_round_and_pays_nothing_the_second_time()
    {
        var (game, _) = RecordedGame();
        var post = PressRig.Post(1);
        game.Posts.Offer(post);
        PressRig.Sweep(game);
        game.Step();
        var card = game.Cards.Cards[0];
        const int cardTop = 6;
        card.Top = cardTop;
        PressRig.Away(game);
        PressRig.Force(game, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Press(game, card, Like);
        Assert.Equal(Tuning.ShieldHits, game.Shield);
        game.WriteConfirmed(post.Uri, Like, NewLikeUri);
        Assert.True(card.Marks.Paid);

        AddToLog(game, post);
        game.Toggle(post.Uri, Like);
        game.WriteConfirmed(post.Uri, Like, null, undo: true);
        Assert.Equal(ButtonState.Off, card.Marks.Like);

        PressRig.Rest(game, Tuning.PressRepeatFrames + 1);
        PressRig.Force(game, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Away(game);
        card.Top = cardTop;
        PressRig.Press(game, card, Like);
        Assert.Equal(ButtonState.Pending, card.Marks.Like);
        Assert.Equal(LikePressTests.NoShield, game.Shield);
    }

    [Fact]
    public void A_card_on_the_field_and_the_log_entry_of_the_same_post_always_show_the_same_state()
    {
        var (game, _) = RecordedGame();
        var post = PressRig.Post(1);
        game.Posts.Offer(post);
        game.Step();
        var card = game.Cards.Cards[0];
        var entry = AddToLog(game, post);
        Assert.Same(card.Marks, entry.Marks);
        game.Toggle(post.Uri, Like);
        Assert.Equal(ButtonState.Pending, card.Marks.Like);
        game.WriteConfirmed(post.Uri, Like, NewLikeUri);
        Assert.Equal(ButtonState.Lit, entry.Marks.Like);
        Assert.Equal(CardPainter.CountText(card, Like), CardPainter.CountText(entry.Post, entry.Marks, Like));
    }

    // --- the view ---

    private static int LogCol0(RaiderGame game) => game.Layout.Col0 - Tuning.SideGapCols - game.Layout.PanelCols;

    private static (Screen Scr, RaiderView View) Render(RaiderGame game)
    {
        var view = new RaiderView();
        var scr = new Screen(Cols, Rows);
        view.FitAndRender(game, scr);
        return (scr, view);
    }

    private static string LogLine(Screen screen, RaiderGame game, int row) => new(Enumerable.Range(LogCol0(game), game.Layout.PanelCols).Select(c => screen.CharAt(c, row)).ToArray());

    private static int RowOf(Screen screen, RaiderGame game, string text) => Enumerable.Range(0, screen.Rows).FirstOrDefault(row => LogLine(screen, game, row).Contains(text), -1);

    [Fact]
    public void An_entry_draws_its_button_row_in_each_of_the_five_states()
    {
        var (game, _) = RecordedGame(grants: new Grants(true, true, false, false));
        var off = Post(1);
        var lit = Post(2, liked: true, likeUri: "at://me/like/2");
        var pending = Post(3);
        var marked = Post(4);
        var locked = Post(5);
        foreach (var post in new[] { off, lit, pending, marked, locked }) AddToLog(game, post);
        game.Book.Find(pending.Uri)!.Like = ButtonState.Pending;
        game.Book.Find(marked.Uri)!.Like = ButtonState.Marked;
        var scr = Render(game).Scr;
        int c0 = LogCol0(game), half = game.Layout.PanelCols / 2;
        int ButtonRowOf(FeedPost post) => RowOf(scr, game, "@" + post.AuthorHandle) + 1;

        int row = ButtonRowOf(off);
        Assert.Equal(CardLayout.HeartOff, scr.CharAt(c0 + RaiderView.LikeIconCol, row));
        Assert.Equal(Palette.Dim, scr.Fg[row * Cols + c0 + RaiderView.LikeIconCol]);
        Assert.Contains(Likes.ToString(), LogLine(scr, game, row));
        Assert.Equal(CardLayout.LoopMark, scr.CharAt(c0 + half + RaiderView.RepostIconCol, row));

        row = ButtonRowOf(lit);
        Assert.Equal(CardLayout.HeartOn, scr.CharAt(c0 + RaiderView.LikeIconCol, row));
        Assert.Equal(CardColours.LikeLit, scr.Fg[row * Cols + c0 + RaiderView.LikeIconCol]);
        Assert.Equal(CardLayout.LoopMark, scr.CharAt(c0 + half + RaiderView.RepostIconCol, row));

        row = ButtonRowOf(marked);
        Assert.Equal(CardLayout.HeartOff, scr.CharAt(c0 + RaiderView.LikeIconCol, row));
        Assert.Equal(Surface.Mix(RaiderView.PanelBg, CardColours.LikeLit, RaiderView.MarkedIconStrength), scr.Fg[row * Cols + c0 + RaiderView.LikeIconCol]);

        row = ButtonRowOf(locked);
        Assert.Contains("lock", LogLine(scr, game, row)[half..]);

        // pending blinks every few frames between the lit colour and the dim one
        row = ButtonRowOf(pending);
        var seen = new HashSet<uint>();
        const int watchedPhases = 24;   // a dozen blinks of a lit phase and an idle one: enough for a third colour to show if there were one
        for (int i = 0; i < watchedPhases; i++)
        {
            for (int frame = 0; frame < CardPainter.BlinkFrames; frame++) game.Step();
            var screen = Render(game).Scr;
            Assert.Equal(CardLayout.HeartOn, screen.CharAt(c0 + RaiderView.LikeIconCol, row));
            seen.Add(screen.Fg[row * Cols + c0 + RaiderView.LikeIconCol]);
        }
        Assert.Equal(2, seen.Count);
        Assert.Contains(CardColours.LikeLit, seen);
    }

    [Fact]
    public void A_lit_repost_shows_green_and_a_marked_one_half_strength()
    {
        var (game, _) = RecordedGame();
        var reposted = Post(1);
        var marked = Post(2);
        AddToLog(game, reposted); AddToLog(game, marked);
        game.Book.Find(reposted.Uri)!.Repost = ButtonState.Lit;
        game.Book.Find(marked.Uri)!.Repost = ButtonState.Marked;
        var scr = Render(game).Scr;
        int repostCol = LogCol0(game) + game.Layout.PanelCols / 2 + RaiderView.RepostIconCol;
        int repostedRow = RowOf(scr, game, ShownHandle(1)) + 1, markedRow = RowOf(scr, game, ShownHandle(2)) + 1;
        Assert.Equal(CardColours.RepostLit, scr.Fg[repostedRow * Cols + repostCol]);
        Assert.Equal(Surface.Mix(RaiderView.PanelBg, CardColours.RepostLit, RaiderView.MarkedIconStrength), scr.Fg[markedRow * Cols + repostCol]);
    }

    [Fact]
    public void The_marked_entry_has_an_arrow_before_its_name_and_a_brighter_rule()
    {
        var (game, _) = RecordedGame();
        AddToLog(game, Post(1)); AddToLog(game, Post(2));
        game.Log.MoveMark(-1);
        var scr = Render(game).Scr;
        int c0 = LogCol0(game);
        int markedName = RowOf(scr, game, ShownHandle(1)), otherName = RowOf(scr, game, ShownHandle(2));
        Assert.Equal(RaiderView.LogMarkGlyph, scr.CharAt(c0 + RaiderView.LogMarkCol, markedName - 1));   // the name row
        Assert.NotEqual(RaiderView.LogMarkGlyph, scr.CharAt(c0 + RaiderView.LogMarkCol, otherName - 1));
        const int ruleCol = 5;   // any cell along the rule
        Assert.NotEqual(scr.Fg[(markedName - 2) * Cols + c0 + ruleCol], scr.Fg[(otherName - 2) * Cols + c0 + ruleCol]);
    }

    [Fact]
    public void LogHit_names_the_entry_and_action_for_a_cell_in_either_half_of_a_button_row_and_nothing_else()
    {
        var (game, _) = RecordedGame();
        var post = Post(1);
        var entry = AddToLog(game, post);
        AddToLog(game, Post(2));
        var (scr, view) = Render(game);
        int c0 = LogCol0(game), width = game.Layout.PanelCols, half = width / 2, row = RowOf(scr, game, ShownHandle(1)) + 1;
        Assert.Equal((entry, Like), view.LogHit(c0, row));
        Assert.Equal((entry, Like), view.LogHit(c0 + half - 1, row));
        Assert.Equal((entry, Repost), view.LogHit(c0 + half, row));
        Assert.Equal((entry, Repost), view.LogHit(c0 + width - 1, row));
        Assert.Null(view.LogHit(c0 + width, row));
        Assert.Null(view.LogHit(c0 - 1, row));
        int likeCol = c0 + RaiderView.LikeIconCol;
        Assert.Null(view.LogHit(likeCol, row - 1));
        Assert.Null(view.LogHit(likeCol, row + 1));
        Assert.Null(view.LogHit(likeCol, 0));
    }

    [Fact]
    public void The_phones_feed_strip_shows_the_marked_entry_with_both_icons_in_their_states()
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(CardsInGameTests.NarrowCols, CardsInGameTests.NarrowRows));
        const int postId = 1;
        var post = Post(postId, liked: true, likeUri: NewLikeUri);
        var entry = AddToLog(game, post);
        game.Book.Find(post.Uri)!.Repost = ButtonState.Marked;
        var scr = new Screen(CardsInGameTests.NarrowCols, CardsInGameTests.NarrowRows);
        new RaiderView().FitAndRender(game, scr);
        int row = Enumerable.Range(0, CardsInGameTests.NarrowRows).First(y => new string(Enumerable.Range(0, CardsInGameTests.NarrowCols).Select(x => scr.CharAt(x, y)).ToArray()).Contains($"{ShownHandle(postId)}: post number {postId}"));
        int x0 = Enumerable.Range(0, CardsInGameTests.NarrowCols).First(x => scr.CharAt(x, row) == CardLayout.HeartOn);
        Assert.Equal(CardColours.LikeLit, scr.Fg[row * CardsInGameTests.NarrowCols + x0]);
        Assert.Equal(CardLayout.LoopMark, scr.CharAt(x0 + RaiderView.NarrowRepostCol, row));
        Assert.Equal(Surface.Mix(RaiderView.PanelBg, CardColours.RepostLit, RaiderView.MarkedIconStrength), scr.Fg[row * CardsInGameTests.NarrowCols + x0 + RaiderView.NarrowRepostCol]);
        Assert.Same(entry, game.Log.Marked);
    }

    [Fact]
    public void A_click_on_the_left_half_of_a_posts_button_row_likes_that_post_and_no_other()
    {
        var (game, recorder) = RecordedGame();
        var first = Post(1);
        AddToLog(game, first);
        AddToLog(game, Post(2));
        var (scr, view) = Render(game);
        view.ToggleLogButtonAt(game, LogCol0(game), RowOf(scr, game, ShownHandle(1)) + 1);
        Assert.Equal([LikeWrite(first)], recorder.Writes);
    }

    [Fact]
    public void A_click_on_the_right_half_of_a_posts_button_row_reposts_that_post()
    {
        var (game, recorder) = RecordedGame();
        var first = Post(1);
        AddToLog(game, first);
        AddToLog(game, Post(2));
        var (scr, view) = Render(game);
        view.ToggleLogButtonAt(game, LogCol0(game) + game.Layout.PanelCols - 1, RowOf(scr, game, ShownHandle(1)) + 1);
        Assert.Equal([RepostWrite(first)], recorder.Writes);
    }

    [Fact]
    public void A_click_away_from_every_button_writes_nothing()
    {
        var (game, recorder) = RecordedGame();
        AddToLog(game, Post(1));
        var (scr, view) = Render(game);
        view.ToggleLogButtonAt(game, LogCol0(game), RowOf(scr, game, ShownHandle(1)));
        view.ToggleLogButtonAt(game, 0, 0);
        Assert.Empty(recorder.Writes);
    }
}
