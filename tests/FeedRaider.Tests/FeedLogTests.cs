using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// On a wide screen the side panel on the left is the feed log: a heading, then the posts that have left the field, the newest at the bottom
// pushing the older ones up and off the top. Each entry shows the display name, handle, likes, whole text and attachment note.
public class FeedLogTests
{
    private const int Cols = 130, Rows = 46;
    internal const string Heading = "FEED LOG";
    private const string FullName = "Ada Lovelace", ShortName = "Ada";
    private const string PlainText = "plain words only";
    internal const string HandleMark = "@";
    /// <summary>What a clipped line ends in.</summary>
    private const string Ellipsis = "…";

    private static string Words(int id) =>
        $"post number {id} opens here and keeps going with plenty of ordinary words so that it wraps over several rows and ends with {Tail(id)}";

    /// <summary>The last word of <see cref="Words"/>, found only at the end of the post.</summary>
    private static string Tail(int id) => $"tail{id}word";

    private static FeedPost Post(int id, string? text = null, int likes = 10, string? name = null, AttachmentKind kind = AttachmentKind.None, string? note = null) =>
        new($"at://did:plc:b{id}/app.bsky.feed.post/{id}", $"cid{id}", $"did:plc:b{id}", Handle(id), text ?? Words(id), likes, 0, 0, null, null,
            DisplayName: name, Attachment: kind, AttachmentText: note);

    private static string Handle(int id) => $"b{id}.test";

    /// <summary>The handle with the log's `@` mark in front (<see cref="HandleMark"/>).</summary>
    private static string ShownHandle(int id) => HandleMark + Handle(id);

    private static void AddToLog(RaiderGame game, FeedPost post) => game.Log.Add(new LogEntry(post, game.Book.For(post)));

    private static RaiderGame GameWithPosts(int posts)
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        for (int i = 1; i <= posts; i++) AddToLog(game, Post(i));
        return game;
    }

    private static int LogCol0(RaiderGame game) => game.Layout.Col0 - Tuning.SideGapCols - game.Layout.PanelCols;

    /// <summary>The characters of the feed log's side panel on <paramref name="row"/>.</summary>
    private static string PanelRow(Screen screen, RaiderGame game, int row) => new(Enumerable.Range(LogCol0(game), game.Layout.PanelCols).Select(c => screen.CharAt(c, row)).ToArray());

    /// <summary>The row of the feed log's side panel showing <paramref name="text"/>, or -1.</summary>
    private static int RowOf(Screen screen, RaiderGame game, string text) => Enumerable.Range(0, screen.Rows).FirstOrDefault(r => PanelRow(screen, game, r).Contains(text), -1);

    private static Screen Render(RaiderView view, RaiderGame game)
    {
        var scr = new Screen(Cols, Rows);
        view.FitAndRender(game, scr);
        return scr;
    }

    [Fact]
    public void The_log_keeps_the_newest_posts_oldest_first_and_drops_the_oldest_past_the_cap()
    {
        const int PastTheCap = 5;
        var log = new FeedLog();
        for (int i = 1; i <= FeedLog.MaxEntries + PastTheCap; i++) log.Add(new LogEntry(Post(i), new PostMarks(Post(i))));
        Assert.Equal(FeedLog.MaxEntries, log.Entries.Count);
        Assert.Equal(Handle(PastTheCap + 1), log.Entries[0].Post.AuthorHandle);
        Assert.Equal(Handle(FeedLog.MaxEntries + PastTheCap), log.Entries[^1].Post.AuthorHandle);
    }

    [Fact]
    public void A_fresh_game_shows_the_heading_and_no_entries_until_a_card_has_left()
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        const int PostId = 1;
        game.Posts.Offer(Post(PostId));
        var scr = Render(new RaiderView(), game);
        Assert.True(RowOf(scr, game, Heading) >= 0);
        Assert.Equal(-1, RowOf(scr, game, HandleMark));
        Assert.Equal(-1, RowOf(scr, game, CardLayout.HeartOn.ToString()));
        const int MaxFramesToLeave = 60 * Tuning.Fps;   // a minute, so a post that never leaves fails below
        game.FeedSpeed = FeedSpeed.Max;
        for (int i = 0; i < MaxFramesToLeave && game.Log.Entries.Count == 0; i++) game.Step();
        scr = Render(new RaiderView(), game);
        Assert.True(RowOf(scr, game, ShownHandle(PostId)) >= 0, "the post that left is in the side panel");
        Assert.True(RowOf(scr, game, Tail(PostId)) >= 0);
    }

    [Fact]
    public void An_entry_shows_the_display_name_above_the_handle_and_none_when_there_is_none()
    {
        var game = GameWithPosts(0);
        AddToLog(game, Post(1, name: FullName));
        AddToLog(game, Post(2));
        var scr = Render(new RaiderView(), game);
        int name = RowOf(scr, game, FullName), handle = RowOf(scr, game, ShownHandle(1));
        Assert.True(name >= 0 && handle == name + 1, $"name {name}, handle {handle}");
        Assert.True(RowOf(scr, game, ShownHandle(2)) > handle);
        Assert.Equal(-1, RowOf(scr, game, "Display Name"));
    }

    [Fact]
    public void An_entry_with_an_attachment_ends_with_its_one_line_note()
    {
        const string PostText = "a cat sat down", Note = "a cat on a keyboard";
        var game = GameWithPosts(0);
        AddToLog(game, Post(1, PostText, name: ShortName, kind: AttachmentKind.Picture, note: Note));
        AddToLog(game, Post(2, PlainText));
        var scr = Render(new RaiderView(), game);
        int text = RowOf(scr, game, PostText), note = RowOf(scr, game, "[image: " + Note + "]");
        Assert.True(text >= 0 && note == text + 1, $"text {text}, note {note}");
        Assert.True(RowOf(scr, game, ShownHandle(2)) > note);
    }

    [Fact]
    public void New_posts_enter_at_the_bottom_and_push_the_old_ones_up_and_off_the_top()
    {
        var game = GameWithPosts(3);
        var view = new RaiderView();
        var scr = Render(view, game);
        int firstRow = RowOf(scr, game, ShownHandle(1)), secondRow = RowOf(scr, game, ShownHandle(2)), thirdRow = RowOf(scr, game, ShownHandle(3));
        Assert.True(firstRow >= 0 && firstRow < secondRow && secondRow < thirdRow, $"oldest on top: {firstRow} {secondRow} {thirdRow}");
        Assert.True(RowOf(scr, game, Tail(3)) > thirdRow, "the newest post ends at the bottom");

        AddToLog(game, Post(4));
        scr = Render(view, game);
        Assert.True(RowOf(scr, game, ShownHandle(2)) < secondRow, "an arrival pushes the earlier posts up");
        Assert.True(RowOf(scr, game, ShownHandle(4)) > RowOf(scr, game, ShownHandle(3)));

        for (int i = 5; i <= 8; i++) AddToLog(game, Post(i));
        scr = Render(view, game);
        Assert.Equal(-1, RowOf(scr, game, ShownHandle(1)));
        Assert.True(RowOf(scr, game, ShownHandle(8)) > RowOf(scr, game, ShownHandle(7)));
        Assert.True(RowOf(scr, game, ShownHandle(7)) > RowOf(scr, game, ShownHandle(6)));
        Assert.True(RowOf(scr, game, Heading) >= 0, "the heading stays");
    }

    [Fact]
    public void Nothing_moves_while_no_new_post_comes_in()
    {
        var game = GameWithPosts(2);
        var view = new RaiderView();
        var before = Render(view, game);
        var rows = Enumerable.Range(0, Rows).Select(r => PanelRow(before, game, r)).ToArray();
        const int FramesWatched = 300;
        for (int i = 0; i < FramesWatched; i++) { game.Step(); Render(view, game); }
        var after = Render(view, game);
        Assert.Equal(rows, Enumerable.Range(0, Rows).Select(r => PanelRow(after, game, r)).ToArray());
    }

    [Fact]
    public void Each_entry_shows_its_handle_likes_and_whole_post()
    {
        const int Sentences = 5;
        const int SideCols = 2, AnyLines = 200;   // a column clear on each side of the text, and no cap on its lines
        string text = string.Join(' ', Enumerable.Repeat("tomatoes ripen slowly in cool spring weather", Sentences)) + " and the very last word zebra";
        const int PostId = 1;
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        AddToLog(game, Post(PostId, text, likes: 1234));
        var scr = Render(new RaiderView(), game);
        Assert.True(RowOf(scr, game, ShownHandle(PostId)) >= 0);
        Assert.True(RowOf(scr, game, $"{CardLayout.HeartOff} 1.2K") >= 0);
        var lines = ShortLines.Format(text, game.Layout.PanelCols - SideCols, AnyLines);
        Assert.True(lines.Length > Sentences);
        foreach (var line in lines) Assert.True(RowOf(scr, game, line) >= 0, $"\"{line}\" is shown");
        Assert.DoesNotContain(Enumerable.Range(0, Rows), r => PanelRow(scr, game, r).Contains(Ellipsis));
    }

    [Fact]
    public void An_entry_has_no_bar_down_its_left_edge()
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(Cols, Rows));
        const int PostId = 1;
        AddToLog(game, Post(PostId, "a post with a few words in it", likes: 3));
        var scr = Render(new RaiderView(), game);
        Assert.True(RowOf(scr, game, ShownHandle(PostId)) >= 0);
        Assert.DoesNotContain(Enumerable.Range(0, Rows), r => "▌█│┃".Contains(scr.CharAt(LogCol0(game), r)));
    }

    [Fact]
    public void A_narrow_screen_has_no_side_panel()
    {
        const int PostId = 1;
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(CardsInGameTests.NarrowCols, CardsInGameTests.NarrowRows));
        AddToLog(game, Post(PostId));
        var scr = new Screen(CardsInGameTests.NarrowCols, CardsInGameTests.NarrowRows);
        new RaiderView().FitAndRender(game, scr);
        var all = string.Join("\n", Enumerable.Range(0, CardsInGameTests.NarrowRows).Select(r => new string(Enumerable.Range(0, CardsInGameTests.NarrowCols).Select(c => scr.CharAt(c, r)).ToArray())));
        Assert.DoesNotContain(Heading, all);
        Assert.DoesNotContain(ShownHandle(PostId), all.Replace($"{CardLayout.HeartOff} {CardLayout.LoopMark} {ShownHandle(PostId)}", ""));   // the phone's one-line feed strip is the only place the handle shows
    }

    private const string Smile = "\U0001F600";
    private const string Cjk = "\u65E5\u672C\u8A9E\u306E\u6295\u7A3F";

    [Fact]
    public void An_emoji_in_the_name_or_text_is_dropped_and_never_a_question_mark()
    {
        var game = GameWithPosts(0);
        AddToLog(game, Post(1, "good " + Smile + " morning " + Smile + Smile + " all", name: $"{Smile} {ShortName} {Smile}"));
        var scr = Render(new RaiderView(), game);
        Assert.True(RowOf(scr, game, ShortName) >= 0);
        Assert.True(RowOf(scr, game, "good morning all") >= 0);
        Assert.DoesNotContain(Enumerable.Range(0, Rows), r => PanelRow(scr, game, r).Contains('?'));
    }

    [Fact]
    public void A_wide_char_takes_two_cells_and_never_straddles_the_panel_edge()
    {
        const int CjkRuns = 5;
        var game = GameWithPosts(0);
        AddToLog(game, Post(1, string.Concat(Enumerable.Repeat(Cjk, CjkRuns)), name: Cjk + Cjk + Cjk + Cjk));
        var scr = Render(new RaiderView(), game);
        int left = LogCol0(game), right = left + game.Layout.PanelCols, wide = 0;
        for (int row = 0; row < Rows; row++)
            for (int col = left; col < right; col++)
            {
                if (CellWidth.Of(scr.CharAt(col, row)) != CellText.WideCells) continue;
                wide++;
                Assert.True(col + 1 < right, "the second cell is inside the panel");
                Assert.Equal(Screen.Continue, scr.CharAt(col + 1, row));
            }
        Assert.True(wide >= Cjk.Length * CjkRuns, $"every char of the text is drawn: {wide}");
    }

    [Fact]
    public void An_entry_with_no_drawable_text_shows_no_rule_and_no_handle()
    {
        var game = GameWithPosts(0);
        AddToLog(game, Post(1, Smile + Smile));
        AddToLog(game, Post(2, PlainText));
        var scr = Render(new RaiderView(), game);
        Assert.Equal(-1, RowOf(scr, game, ShownHandle(1)));
        Assert.True(RowOf(scr, game, ShownHandle(2)) >= 0);
    }
}
