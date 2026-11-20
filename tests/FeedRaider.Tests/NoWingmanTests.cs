using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// In flight nothing on the field likes a post by itself: there is no wingman and no automatic bomb, posts reach the game's queue and
// nothing on the field reads them, and E acts only on the feed log's marked post, so pressing it in flight writes nothing (resting the
// ship on a card's heart is the one way to like on the field: HoldTests). In the left-pane run the feed log shows its heading and no
// entries, since no post has scrolled off the field into it yet.
public class NoWingmanTests
{
    private const int Cols = 130, Rows = 46;
    private const int UntouchedRunFrames = 200, HalfRunFrames = 100, SettleFrames = 30, LikeKeyWatchFrames = 90;
    /// <summary>Seconds the no-wingman run keeps the fire key down.</summary>
    private const int FedRunSeconds = 60;
    /// <summary>Frames between one post offered to the queue and the next in the no-wingman run.</summary>
    private const int FedPostEveryFrames = 20;
    /// <summary>Frames between one press of the like key and the next.</summary>
    private const int LikeKeyEveryFrames = 10;
    /// <summary>Frames the left-pane test runs before it reads the panel.</summary>
    private const int LeftPaneRunFrames = 400;
    /// <summary>Seconds each music run lasts.</summary>
    private const int MusicRunSeconds = 40;
    /// <summary>Frames between one post offered and the next in the fed music run.</summary>
    private const int MusicPostEveryFrames = 30;
    private const string Text = "this is a perfectly ordinary post with enough words in it";

    private static FeedPost Post(int n, int likes = 10) =>
        new($"at://did:plc:t{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:t{n}", $"t{n}.test", Text, likes, 0, 0, null, null);

    private readonly record struct Ev(string Name, long Frame);

    private static (RaiderGame Game, List<Ev> Played, List<string> Writes) New(bool signedIn = true)
    {
        var g = new RaiderGame(EnemyKit.PlainSeed) { SignedIn = signedIn };
        g.SetLayout(FieldLayout.Compute(Cols, Rows));
        var played = new List<Ev>();
        var writes = new List<string>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(new Ev(n, g.Frame)));
        g.OnLike = p => writes.Add(p.Uri);
        g.ThreadWanted = p => writes.Add("thread " + p.Uri);
        return (g, played, writes);
    }

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    private static Enemy AddGrunt(RaiderGame g, double x, double y)
    {
        var e = new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = x, BaseX = x, Y = y, Hp = 3, MaxHp = 3 };
        g.Enemies.Add(e);
        return e;
    }

    [Fact]
    public void A_sixty_second_run_fed_posts_has_no_wingman_and_no_wingman_shot()
    {
        var (g, played, writes) = New();
        Assert.DoesNotContain(typeof(RaiderGame).GetMembers().Concat(typeof(Bullet).GetMembers()), m => m.Name.Contains("Wing", StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < FedRunSeconds * Tuning.Fps; i++)
        {
            if (i % FedPostEveryFrames == 0) g.Posts.Offer(Post(i));
            g.Input.Key(GameKey.Fire, true);
            g.Step();
        }
        Assert.True(g.Posts.Count > 0, "the posts reach the queue");
        Assert.Empty(writes);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Like or SfxNames.NoLike);
    }

    [Fact]
    public void A_post_with_a_thousand_likes_clears_nothing()
    {
        var (a, playedA, _) = New();
        var (b, playedB, _) = New();
        foreach (var g in new[] { a, b }) { AddGrunt(g, 40, 6); AddGrunt(g, 70, 8); AddGrunt(g, 100, 10); }
        Run(a, UntouchedRunFrames);
        Run(b, HalfRunFrames);
        b.Posts.Offer(Post(1, likes: 1000));
        Run(b, HalfRunFrames);
        Assert.DoesNotContain(playedB, p => p.Name == SfxNames.Bomb);
        Assert.Equal(a.Enemies.Count, b.Enemies.Count);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Bombs, b.Bombs);
        Assert.Equal(playedA.Select(p => p.Name), playedB.Select(p => p.Name));
        Assert.Equal(0, b.Posts.Count);   // it became a card like any other post
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void E_pressed_in_flight_writes_nothing_and_says_nothing(bool signedIn)
    {
        var (g, played, writes) = New(signedIn);
        var said = new List<string>();
        g.Said = said.Add;
        g.Posts.Offer(Post(1));
        Run(g, SettleFrames);
        played.Clear();
        for (int i = 0; i < LikeKeyWatchFrames; i++)
        {
            if (i % LikeKeyEveryFrames == 0) g.Input.Like();
            g.Step();
            Assert.Null(g.Toast);
        }
        Assert.Empty(writes);
        Assert.Empty(said);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Like or SfxNames.NoLike);
    }

    [Fact]
    public void A_pointer_resting_on_the_field_never_likes()
    {
        var (g, played, writes) = New();
        var said = new List<string>();
        g.Said = said.Add;
        g.Posts.Offer(Post(1));
        Run(g, SettleFrames);
        played.Clear();
        var (px, py) = (g.Layout.Col0 + g.PX / 2, g.PY / 2);
        g.Input.Pointer(px, py);
        Run(g, 3 * Tuning.Fps);
        g.Input.Tap(px, py);
        Run(g, 10);
        Assert.Empty(writes);
        Assert.Empty(said);
        Assert.Null(g.Toast);
        Assert.DoesNotContain(played, p => p.Name is SfxNames.Like or SfxNames.NoLike);
    }

    private static string Row(Screen scr, int row, int to) => new string(Enumerable.Range(0, to).Select(c => scr.CharAt(c, row)).ToArray());

    private static Screen Render(RaiderGame g)
    {
        var scr = new Screen(Cols, Rows);
        new RaiderView().FitAndRender(g, scr);
        return scr;
    }

    [Fact]
    public void The_left_pane_shows_its_heading_and_no_entries()
    {
        var (g, _, _) = New();
        g.Posts.Offer(Post(1));
        Run(g, LeftPaneRunFrames);
        var lay = g.Layout;
        int right = lay.Col0 - Tuning.SideGapCols;
        Assert.True(lay.PanelCols > 0);
        var scr = Render(g);
        Assert.Contains("FEED LOG", Row(scr, 2, right));
        for (int r = 3; r < lay.Rows; r++) Assert.True(string.IsNullOrWhiteSpace(Row(scr, r, right)), $"row {r} of the panel is empty");
        Assert.Empty(g.Log.Entries);
    }

    [Fact]
    public void A_post_added_to_the_log_shows_under_the_heading()
    {
        var (g, _, _) = New();
        g.Log.Add(new LogEntry(Post(7), g.Book.For(Post(7))));
        var lay = g.Layout;
        int right = lay.Col0 - Tuning.SideGapCols;
        var scr = Render(g);
        Assert.Contains("FEED LOG", Row(scr, 2, right));
        Assert.Contains(Enumerable.Range(3, lay.Rows - 3), r => Row(scr, r, right).Contains("@t7.test"));
    }

    [Fact]
    public void The_music_snapshot_has_no_wingman_count_and_the_music_is_what_it_was()
    {
        Assert.Equal(["Ahead", "Boss"],
            typeof(MusicSnapshot).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
        List<string> Cues(bool fed)
        {
            var cues = new List<string>();
            var g = new RaiderGame(EnemyKit.PlainSeed);
            g.SetLayout(FieldLayout.Compute(HoldRig.ScrCols, HoldRig.ScrRows));
            g.Music = new MusicDirector(cues.Add);
            for (int i = 0; i < MusicRunSeconds * Tuning.Fps; i++)
            {
                if (fed && i % MusicPostEveryFrames == 0) g.Posts.Offer(Post(i, likes: 1000));
                g.Step();
            }
            return cues;
        }
        var plain = Cues(false);
        Assert.Equal(["gameplay"], plain.Where(c => !MusicCues.IsAhead(c)));   // the hint that names the next boss is no change of music
        Assert.Equal(plain, Cues(true));
    }
}
