using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// Real posts flow down the field as cards in the simulation and land in the feed log when they leave. These tests read the card field and the log directly.
public class CardsInGameTests
{
    private const int Cols = 130, Rows = 46;
    /// <summary>A phone held upright: too narrow for the side panels.</summary>
    internal const int NarrowCols = 48, NarrowRows = 60;

    /// <summary>The handle of the test post with this id.</summary>
    private const string HandleLead = "c", HandleTail = ".test";
    private static string Handle(int id) => HandleLead + id + HandleTail;

    /// <summary>The post id a handle made by <see cref="Handle"/> carries.</summary>
    private static int IdOf(string handle) => int.Parse(handle[HandleLead.Length..^HandleTail.Length]);

    private static FeedPost Post(int id, string? text = null, string? name = "Display Name", string? did = null) =>
        new($"at://did:plc:c{id}/app.bsky.feed.post/{id}", $"cid{id}", did ?? $"did:plc:c{id}", Handle(id), text ?? $"post number {id} says a few ordinary words and ends with tail{id}word",
            10, 0, 0, null, null, DisplayName: name);

    private static RaiderGame NewGame(int speed = FeedSpeed.Default, int cols = Cols, int rows = Rows)
    {
        var game = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = true, BossGates = false };
        game.SetLayout(FieldLayout.Compute(cols, rows));
        game.FeedSpeed = speed;
        return game;
    }

    [Fact]
    public void A_post_offered_to_the_game_becomes_a_card_above_the_field_and_leaves_the_bottom_on_schedule()
    {
        var game = NewGame();
        var post = Post(1);
        game.Posts.Offer(post);
        game.Step();
        var card = Assert.Single(game.Cards.Cards);
        Assert.Same(post, card.Post);
        Assert.True(card.Top < 0 && card.Top + card.Shape.Rows <= 0, "the whole card is above the field");
        Assert.Equal(0, game.Posts.Count);

        int rows = card.Shape.Rows, framesPerRow = game.Cards.FramesPerRow;
        long due = (long)(Rows + rows) * framesPerRow;
        while (game.Tick < due - 1) game.Step();
        Assert.Empty(game.Log.Entries);
        Assert.Single(game.Cards.Cards);
        while (game.Tick < due + 1) game.Step();
        Assert.Empty(game.Cards.Cards);
        Assert.Same(post, Assert.Single(game.Log.Entries).Post);
    }

    [Fact]
    public void The_card_field_is_as_big_as_the_field_and_follows_a_new_layout()
    {
        var game = NewGame();
        Assert.Equal(game.Layout.PCols, game.Cards.FieldCols);
        Assert.Equal(game.Layout.Rows, game.Cards.FieldRows);
        game.Posts.Offer(Post(1));
        const int settledFrames = 100;
        for (int i = 0; i < settledFrames; i++) game.Step();
        int top = game.Cards.Cards[0].Top;
        const int newCols = 100, newRows = 40;
        game.SetLayout(FieldLayout.Compute(newCols, newRows));
        Assert.Equal(game.Layout.PCols, game.Cards.FieldCols);
        Assert.Equal(newRows, game.Cards.FieldRows);
        Assert.Equal(top, game.Cards.Cards[0].Top);
    }

    [Fact]
    public void Hit_stop_frames_do_not_move_the_cards()
    {
        var game = NewGame();
        game.Posts.Offer(Post(1));
        game.Step();
        var card = game.Cards.Cards[0];
        int rows = card.Shape.Rows, framesPerRow = game.Cards.FramesPerRow;
        int frozen = 0;
        int[] bombTicks = [framesPerRow * 2 - 4, framesPerRow * 5 + 2];
        for (int i = 0; i < framesPerRow * 27; i++)
        {
            // Rows are due at ticks 16, 31, 46 ...: each bomb freezes the game across one of them.
            if (Array.IndexOf(bombTicks, i) >= 0) game.Input.Bomb();
            long tick = game.Tick; int top = card.Top;
            game.Step();
            if (game.Tick == tick) { frozen++; Assert.Equal(top, card.Top); }
            Assert.Equal(-rows + (int)Math.Max(0, (game.Tick - 1) / framesPerRow), card.Top);
        }
        Assert.True(frozen >= Tuning.HitStopBomb, "the bombs froze the game");
    }

    [Fact]
    public void A_card_that_left_is_the_newest_log_entry_with_its_name_handle_and_whole_text()
    {
        var game = NewGame(FeedSpeed.Max);
        string text = string.Join(' ', Enumerable.Repeat("tomatoes ripen slowly in cool spring weather", 6)) + " zebra";
        const string name = "Ada Lovelace";
        var post = Post(1, text, name);
        game.Posts.Offer(post);
        const int logWaitFrames = 1000;
        for (int i = 0; i < logWaitFrames && game.Log.Entries.Count == 0; i++) game.Step();
        var entry = Assert.Single(game.Log.Entries);
        Assert.Equal(name, entry.Post.DisplayName);
        Assert.Equal(Handle(1), entry.Post.AuthorHandle);
        Assert.Equal(text, entry.Post.Text);
        Assert.Same(game.Book.MarksFor(post), entry.Marks);
    }

    [Fact]
    public void The_log_keeps_thirty_two_entries_and_new_ones_enter_at_the_bottom()
    {
        var game = NewGame(FeedSpeed.Max);
        const int lastId = 40, offeredUpTo = 60, guardFrames = 20_000, queued = 4;   // the queue is kept this full
        int next = 1;
        long guard = 0;
        while (!game.Log.Entries.Any(entry => entry.Post.AuthorHandle == Handle(lastId)) && guard++ < guardFrames)
        {
            while (game.Posts.Count < queued && next <= offeredUpTo) game.Posts.Offer(Post(next++, "short"));
            game.Step();
        }
        Assert.True(guard < guardFrames, "forty posts passed");
        Assert.Equal(FeedLog.MaxEntries, game.Log.Entries.Count);
        var ids = game.Log.Entries.Select(entry => IdOf(entry.Post.AuthorHandle)).ToArray();
        Assert.Equal(lastId, ids[^1]);
        Assert.Equal(ids.OrderBy(id => id), ids);
        Assert.Equal(ids.Distinct().Count(), ids.Length);
        Assert.Equal(ids[0] + ids.Length - 1, ids[^1]);
    }

    [Fact]
    public void The_avatar_is_asked_for_once_per_author_when_the_card_is_made()
    {
        var game = NewGame(FeedSpeed.Max);
        var asked = new List<string>();
        const string first = "did:plc:a", second = "did:plc:b", known = "did:plc:known";
        game.AvatarWanted = post => asked.Add(post.AuthorDid);
        game.SetAvatar(known, new uint[64]);
        string[] dids = [first, second, first, known];
        for (int i = 0; i < dids.Length; i++) game.Posts.Offer(Post(i + 1, did: dids[i]));
        Assert.Empty(asked);                      // waiting in the queue is not a card
        game.Step();
        Assert.Equal([first], asked);
        const int allPassedFrames = 1500;
        for (int i = 0; i < allPassedFrames; i++) game.Step();
        Assert.Equal(dids.Length, game.Log.Entries.Count);
        Assert.Equal([first, second], asked);
    }

    [Fact]
    public void Two_runs_with_the_same_seed_inputs_and_speed_have_their_cards_on_the_same_rows_at_every_tick()
    {
        static List<string> Run(int speed)
        {
            var game = NewGame(speed);
            var seen = new List<string>();
            const int runFrames = 900, postEvery = 40, fireEvery = 3;
            const int lengthSteps = 7, lengthStep = 30, shortestLength = 5;   // the post texts run from short to long
            for (int i = 0; i < runFrames; i++)
            {
                if (i % postEvery == 0) game.Posts.Offer(Post(i, new string('x', i % lengthSteps * lengthStep + shortestLength)));
                game.Input.Key(GameKey.Fire, i % fireEvery == 0);
                game.Step();
                seen.Add(string.Join(',', game.Cards.Cards.Select(card => $"{card.Post.Uri}@{card.Top}")) + "|" + game.Log.Entries.Count);
            }
            return seen;
        }
        foreach (int speed in new[] { 1, 3, 5 })
        {
            var firstRun = Run(speed);
            Assert.Equal(firstRun, Run(speed));
            Assert.Contains(firstRun, tick => tick.Contains('@'));
        }
    }

    [Fact]
    public void A_change_of_speed_reaches_the_card_field()
    {
        var game = NewGame();
        Assert.Equal(FeedSpeed.FramesPerRow(FeedSpeed.Default), game.Cards.FramesPerRow);
        const int speed = 5;
        game.FeedSpeed = speed;
        Assert.Equal(speed, game.FeedSpeed);
        game.Posts.Offer(Post(1));
        const int settleFrames = 40;
        for (int i = 0; i < settleFrames; i++) game.Step();
        Assert.Equal(FeedSpeed.FramesPerRow(speed), game.Cards.FramesPerRow);
        game.FeedSpeed = FeedSpeed.Max + 1;
        Assert.Equal(FeedSpeed.Max, game.FeedSpeed);
    }

    [Fact]
    public void A_narrow_screen_has_no_side_panel_while_cards_pass()
    {
        const int postsOffered = 3, framesForPostsToLeave = 1200;
        var game = NewGame(FeedSpeed.Max, NarrowCols, NarrowRows);
        for (int i = 1; i <= postsOffered; i++) game.Posts.Offer(Post(i));
        for (int i = 0; i < framesForPostsToLeave; i++) game.Step();
        Assert.NotEmpty(game.Log.Entries);
        var scr = new Screen(NarrowCols, NarrowRows);
        new RaiderView().FitAndRender(game, scr);
        var all = string.Join("\n", Enumerable.Range(0, NarrowRows).Select(row => new string(Enumerable.Range(0, NarrowCols).Select(col => scr.CharAt(col, row)).ToArray())));
        Assert.DoesNotContain(FeedLogTests.Heading, all);
        Assert.DoesNotContain(FeedLogTests.HandleMark + Handle(1), all);
    }

    [Fact]
    public void A_rude_post_held_for_a_speaker_is_never_also_a_card()
    {
        var game = NewGame();
        var rude = Post(1);
        var plain = Post(2);
        game.OfferDarkness(new DarknessPost(DarknessKind.Rude, Hashing.MixedFnv64(rude.Uri), rude.Text));
        Assert.True(game.Rude.Holds(Hashing.MixedFnv64(rude.Uri)));
        game.Posts.Offer(rude);
        game.Posts.Offer(plain);
        bool plainSeen = false;
        const int rudeWatchFrames = 600;
        for (int i = 0; i < rudeWatchFrames; i++)
        {
            game.Step();
            Assert.DoesNotContain(game.Cards.Cards, card => card.Post.Uri == rude.Uri);
            plainSeen |= game.Cards.Cards.Any(card => card.Post.Uri == plain.Uri);
        }
        Assert.True(plainSeen, "the plain post never became a card");
        Assert.Equal(0, game.Posts.Count);
    }
}
