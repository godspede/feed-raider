using FeedRaider.Engine;
using TUImation;

namespace FeedRaider.Tests;

public class DarknessSourcesTests
{
    private const string Owner = "did:plc:owneroowneroowneroowner1";
    private const string Replier = "did:plc:replierreplierreplier22";
    private const string Spammer = "did:plc:spammerspammerspammer222";
    private static string Reply(int n) => $"at://{Replier}/app.bsky.feed.post/3kr{n}";

    private static List<string> Feed(ThreadgateDiffer d, string fixture)
    {
        Assert.True(ThreadgateDiffer.TryParse(JetstreamParserTests.Fixture(fixture), out var did, out var rkey, out var op, out var hidden));
        return d.Apply(did, rkey, op, hidden);
    }

    private static Room RoomOfGuys() =>
        new(Enumerable.Range(0, 6).Select(i => new Follow($"did:plc:guy{i}aaaaaaaaaaaaaaaa", $"guy{i}.test")));

    // ---- threadgate diffing ------------------------------------------------------------------

    [Fact]
    public void A_new_gate_reports_all_its_hidden_replies()
    {
        var d = new ThreadgateDiffer();
        Assert.Equal([Reply(1), Reply(2)], Feed(d, "threadgate-create.json"));
    }

    [Fact]
    public void An_updated_gate_reports_only_what_it_added()
    {
        var d = new ThreadgateDiffer();
        Feed(d, "threadgate-create.json");
        // r1 removed (unhidden), r2 kept, r3 added
        Assert.Equal([Reply(3)], Feed(d, "threadgate-update.json"));
        Assert.Empty(Feed(d, "threadgate-update.json"));   // replaying the same version adds nothing
    }

    [Fact]
    public void A_deleted_gate_is_forgotten_so_a_recreate_is_all_new()
    {
        var d = new ThreadgateDiffer();
        Feed(d, "threadgate-create.json");
        Assert.Empty(Feed(d, "threadgate-delete.json"));
        Assert.Equal(0, d.Tracked);
        Assert.Equal([Reply(1), Reply(2)], Feed(d, "threadgate-create.json"));
    }

    [Fact]
    public void Gates_are_independent_and_other_messages_are_ignored()
    {
        var d = new ThreadgateDiffer();
        Assert.Equal([Reply(1)], d.Apply(Owner, "a", "create", [Reply(1)]));
        Assert.Equal([Reply(1)], d.Apply(Owner, "b", "create", [Reply(1)]));
        Assert.False(ThreadgateDiffer.TryParse(JetstreamParserTests.Fixture("post.json"), out _, out _, out _, out _));
        Assert.False(ThreadgateDiffer.TryParse("not json", out _, out _, out _, out _));
        Assert.False(ThreadgateDiffer.TryParse(JetstreamParserTests.Fixture("identity.json"), out _, out _, out _, out _));
    }

    [Fact]
    public void The_stream_url_is_network_wide_and_only_threadgates()
    {
        Assert.Contains("wantedCollections=app.bsky.feed.threadgate", ThreadgateSource.Endpoint);
        Assert.DoesNotContain("wantedDids", ThreadgateSource.Endpoint);
    }

    private static async IAsyncEnumerable<T> Of<T>(params T[] items)
    {
        foreach (var i in items) { yield return i; await Task.Yield(); }
    }

    [Fact]
    public async Task Hidden_replies_become_anonymous_rude_posts()
    {
        var stats = new DarknessStats();
        var posts = new List<DarkPost>();
        var fetched = new List<string>();
        await DarknessFeed.RunHiddenAsync(Of(Reply(1), Reply(1), "at://did:plc:x/app.bsky.actor.profile/self", Reply(2)),
            (uri, _) => { fetched.Add(uri); return Task.FromResult<string?>("go away @someone.example.social https://t.example/x you muppet"); },
            posts.Add, stats, CancellationToken.None, paceMs: 1);

        Assert.Equal([Reply(1), Reply(2)], fetched);   // duplicate and non-post dropped before any fetch
        Assert.Equal((2, 2), (stats.HiddenNew, stats.HiddenFetched));
        Assert.All(posts, p =>
        {
            Assert.Equal((DarknessKind.Rude, DarkSource.Hidden), (p.Kind, p.Source));
            Assert.Equal("go away you muppet", p.Text);
            Assert.DoesNotContain(Replier, p.ToString());
        });
    }

    // ---- account-level labels ----------------------------------------------------------------

    [Fact]
    public void Account_spam_passes_and_nothing_else_on_an_account_does()
    {
        Assert.Equal(DarknessKind.Spam, DarknessFilter.AcceptAccount(new LabelEvent(Spammer, "spam", false, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent(Spammer, "spam", true, 1)));   // neg
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent(Spammer, "rude", false, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent(Spammer, "porn", false, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent($"at://{Spammer}/app.bsky.feed.post/3k", "spam", false, 1)));
        Assert.Null(DarknessFilter.AcceptAccount(new LabelEvent("did:plc:short", "spam", false, 1)));
    }

    [Fact]
    public void Latest_post_parsers_skip_reposts_and_take_text_only()
    {
        Assert.StartsWith("win free crypto", PostTextFetcher.ParseAuthorFeed(JetstreamParserTests.Fixture("author-feed.json")));
        Assert.Equal("fallback words from the pds", PostTextFetcher.ParseListRecords(JetstreamParserTests.Fixture("list-records.json")));
        Assert.Null(PostTextFetcher.ParseAuthorFeed("{\"feed\":[]}"));
        Assert.Null(PostTextFetcher.ParseListRecords("garbage"));
    }

    [Fact]
    public async Task An_account_spam_label_becomes_a_spam_post_with_the_latest_text_only()
    {
        var stats = new DarknessStats();
        var posts = new List<DarkPost>();
        var fetched = new List<string>();
        await DarknessFeed.RunAsync(
            Of(new LabelEvent(Spammer, "spam", false, 1), new LabelEvent(Spammer, "spam", true, 2), new LabelEvent(Spammer, "porn", false, 3)),
            (subject, _) => { fetched.Add(subject); return Task.FromResult(PostTextFetcher.ParseAuthorFeed(JetstreamParserTests.Fixture("author-feed.json"))); },
            posts.Add, stats, CancellationToken.None, paceMs: 1);

        Assert.Equal([Spammer], fetched);
        Assert.Equal((3, 1, 1), (stats.LabelsSeen, stats.AccountLabels, stats.PostsFetched));
        var post = Assert.Single(posts);
        Assert.Equal((DarknessKind.Spam, DarkSource.Label, "win free crypto now"), (post.Kind, post.Source, post.Text));

        var room = RoomOfGuys();
        room.Haunt(post);
        string everything = "";
        for (long ms = 0; ms < 12000; ms += 250) everything += room.Render(100, 40, ms);
        Assert.True(room.Dark.SpawnedFromLabels > 0);
        Assert.Equal(0, room.Dark.SpawnedFromHidden);
        Assert.Contains("crypto", everything);
        foreach (var leak in new[] { "spammerspammer", "spammy", "McSpam", "cdn.bsky", "links.test", "Repost of someone" })
            Assert.DoesNotContain(leak, everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Spawns_are_counted_by_source()
    {
        var room = RoomOfGuys();
        room.Haunt(new DarkPost(DarknessKind.Rude, 1, "grr", DarkSource.Hidden));
        for (long ms = 0; ms < 3000; ms += 250) room.Render(100, 40, ms);
        Assert.Equal(1, room.Dark.SpawnedFromHidden);
        Assert.Equal(0, room.Dark.SpawnedFromLabels);
        Assert.Equal(DarkSource.Hidden, room.Dark.Monsters[0].Source);
    }

    // ---- layers ------------------------------------------------------------------------------

    private static Canvas Block(int w, int h, Rgb c)
    {
        var cv = new Canvas(w, h);
        cv.FillRect(0, 0, w, h, c);
        return cv;
    }

    [Fact]
    public void Monsters_draw_above_guys_whatever_the_call_order()
    {
        var guy = Block(6, 6, new Rgb(200, 10, 10));
        var monster = Block(6, 6, new Rgb(10, 200, 10));
        var guyFirst = new Frame(20, 6);
        guyFirst.Blit(guy, 4, 2); guyFirst.BlitTop(monster, 6, 4);
        var monsterFirst = new Frame(20, 6);
        monsterFirst.BlitTop(monster, 6, 4); monsterFirst.Blit(guy, 4, 2);
        string a = guyFirst.ToAnsi();
        Assert.Equal(a, monsterFirst.ToAnsi());
        Assert.Contains("38;2;10;200;10", a);   // the monster shows where they overlap
    }

    [Fact]
    public void Monster_text_is_never_hidden_by_a_guy_or_his_label()
    {
        var f = new Frame(40, 8);
        f.Whisper("buy followers", 20, 4);
        f.Blit(Block(12, 12, new Rgb(200, 10, 10)), 10, 0);   // a guy standing right over the text
        f.Label("someone", 20, 3);
        string plain = System.Text.RegularExpressions.Regex.Replace(f.ToAnsi(), "\x1b\\[[0-9;]*[A-Za-z]", "");
        Assert.Contains("buy followers", plain);
    }

    [Fact]
    public void Spam_motes_are_big_enough_to_read()
    {
        Assert.True(MonsterSprites.MoteW >= 9 && MonsterSprites.MoteH >= 6);
    }
}
