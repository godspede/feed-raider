using System.Formats.Cbor;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class DarknessTests
{
    private const string AuthorDid = "did:plc:authorauthorauthor1234";
    private const string AuthorHandle = "grumpy-author.example.social";
    private const string AuthorName = "Grumpy Person Display";

    private static string PostUri(string rkey = "3kabc") => $"at://{AuthorDid}/app.bsky.feed.post/{rkey}";

    /// <summary>A subscribeLabels frame as the labeler sends it: header map, then body map.</summary>
    internal static byte[] LabelsFrame(long seq, params (string Uri, string Val, bool? Neg)[] labels)
    {
        var w = new CborWriter(CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
        w.WriteStartMap(2);
        w.WriteTextString("op"); w.WriteInt32(1);
        w.WriteTextString("t"); w.WriteTextString("#labels");
        w.WriteEndMap();
        w.WriteStartMap(2);
        w.WriteTextString("seq"); w.WriteInt64(seq);
        w.WriteTextString("labels");
        w.WriteStartArray(labels.Length);
        foreach (var (uri, val, neg) in labels)
        {
            w.WriteStartMap(neg is null ? 5 : 6);
            w.WriteTextString("ver"); w.WriteInt32(1);
            w.WriteTextString("src"); w.WriteTextString("did:plc:ar7c4by46qjdydhdevvrndac");
            w.WriteTextString("uri"); w.WriteTextString(uri);
            w.WriteTextString("val"); w.WriteTextString(val);
            w.WriteTextString("cts"); w.WriteTextString("2026-01-01T00:00:00.000Z");
            if (neg is { } n) { w.WriteTextString("neg"); w.WriteBoolean(n); }
            w.WriteEndMap();
        }
        w.WriteEndArray();
        w.WriteEndMap();
        return w.Encode();
    }

    private static byte[] OtherFrame(string type)
    {
        var w = new CborWriter(CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
        w.WriteStartMap(2);
        w.WriteTextString("op"); w.WriteInt32(1);
        w.WriteTextString("t"); w.WriteTextString(type);
        w.WriteEndMap();
        w.WriteStartMap(1);
        w.WriteTextString("seq"); w.WriteInt64(7);
        w.WriteEndMap();
        return w.Encode();
    }

    // ---- frame decode ------------------------------------------------------------------------

    [Fact]
    public void Decodes_a_labels_frame()
    {
        var bytes = LabelsFrame(42, (PostUri(), "spam", null), (PostUri("x"), "rude", false));
        var labels = LabelFrame.Decode(bytes, out long seq);
        Assert.Equal(42, seq);
        Assert.Equal(2, labels.Count);
        Assert.Equal(("spam", false, 42), (labels[0].Val, labels[0].Neg, (int)labels[0].Seq));
        Assert.Equal(PostUri(), labels[0].Uri);
        Assert.False(labels[1].Neg);
    }

    [Fact]
    public void Reads_neg_true()
    {
        var labels = LabelFrame.Decode(LabelsFrame(1, (PostUri(), "spam", true)), out _);
        Assert.True(Assert.Single(labels).Neg);
    }

    [Fact]
    public void Other_frames_and_garbage_decode_to_nothing()
    {
        Assert.Empty(LabelFrame.Decode(OtherFrame("#info"), out _));
        Assert.Empty(LabelFrame.Decode(new byte[] { 0xff, 0x00, 0x13 }, out _));
        Assert.Empty(LabelFrame.Decode(Array.Empty<byte>(), out _));
        var truncated = LabelsFrame(5, (PostUri(), "spam", null));
        Assert.Empty(LabelFrame.Decode(truncated.AsMemory(0, truncated.Length - 10), out _));
    }

    // ---- filter ------------------------------------------------------------------------------

    [Theory]
    [InlineData("spam", DarknessKind.Spam)]
    [InlineData("rude", DarknessKind.Rude)]
    [InlineData("engagement-farming", DarknessKind.Spam)]
    public void Allowlisted_post_labels_pass(string val, DarknessKind kind) =>
        Assert.Equal(kind, DarknessFilter.Accept(new LabelEvent(PostUri(), val, false, 1)));

    [Theory]
    [InlineData("porn")]
    [InlineData("sexual")]
    [InlineData("nudity")]
    [InlineData("graphic-media")]
    [InlineData("gore")]
    [InlineData("self-harm")]
    [InlineData("threat")]
    [InlineData("intolerant")]
    [InlineData("!takedown")]
    [InlineData("needs-review")]
    [InlineData("Spam")]
    [InlineData("")]
    public void Everything_else_is_dropped(string val) =>
        Assert.Null(DarknessFilter.Accept(new LabelEvent(PostUri(), val, false, 1)));

    [Fact]
    public void Negations_are_dropped() =>
        Assert.Null(DarknessFilter.Accept(new LabelEvent(PostUri(), "spam", true, 1)));

    [Theory]
    [InlineData("did:plc:authorauthorauthor1234")]
    [InlineData("at://did:plc:authorauthorauthor1234")]
    [InlineData("at://did:plc:authorauthorauthor1234/app.bsky.actor.profile/self")]
    [InlineData("at://did:plc:authorauthorauthor1234/app.bsky.feed.like/3k")]
    [InlineData("at://did:plc:authorauthorauthor1234/app.bsky.feed.post/")]
    public void Only_post_uris_pass(string uri) =>
        Assert.Null(DarknessFilter.Accept(new LabelEvent(uri, "spam", false, 1)));

    [Fact]
    public void Whole_pipeline_keeps_only_the_allowed_ones()
    {
        var frame = LabelFrame.Decode(LabelsFrame(9,
            (PostUri("a"), "spam", null), (PostUri("b"), "porn", null), (PostUri("c"), "rude", true), (PostUri("d"), "rude", null),
            (AuthorDid, "spam", null)), out _);
        var kept = frame.Where(l => DarknessFilter.Accept(l) is not null).Select(l => l.Uri).ToList();
        Assert.Equal([PostUri("a"), PostUri("d")], kept);
    }

    // ---- text and anonymity ------------------------------------------------------------------

    [Fact]
    public void Clean_strips_mentions_urls_hashtags_handles_and_dids()
    {
        string raw = "hey @someone.bsky.social   look\n\tat https://spam.example/buy?x=1 #cheap #deals and www.evil.example, "
                     + "also grumpy-author.example.social said did:plc:abc123 hi";
        string s = DarknessText.Clean(raw);
        Assert.Equal("hey look at and also said hi", s);
        foreach (var bad in new[] { "@", "http", "www", "#", "example", "did:", ".social" })
            Assert.DoesNotContain(bad, s);
    }

    [Fact]
    public void Clean_truncates_to_about_sixty_characters_on_a_word_boundary()
    {
        string s = DarknessText.Clean(string.Join(' ', Enumerable.Repeat("blah", 40)));
        Assert.InRange(s.Length, 20, 61);
        Assert.EndsWith("…", s);
        Assert.Equal("", DarknessText.Clean("   @only.mention.test https://x.test  "));
        Assert.Equal("", DarknessText.Clean(null));
    }

    [Fact]
    public void Fragments_are_short()
    {
        var f = DarknessText.Fragments(DarknessText.Clean("buy followers now cheap fast guaranteed results today only"));
        Assert.True(f.Count >= 3);
        Assert.All(f, x => Assert.True(x.Length <= 14));
    }

    private const string GetPostsJson = """
        {"posts":[{"uri":"at://did:plc:authorauthorauthor1234/app.bsky.feed.post/3kabc","cid":"bafy",
          "author":{"did":"did:plc:authorauthorauthor1234","handle":"grumpy-author.example.social","displayName":"Grumpy Person Display","avatar":"https://cdn.bsky.app/img/avatar/plain/did:plc:authorauthorauthor1234/xyz@jpeg"},
          "record":{"$type":"app.bsky.feed.post","text":"you absolute muppet @friend.example.social https://t.example/abc #angry","createdAt":"2026-01-01T00:00:00Z"}}]}
        """;

    [Fact]
    public void Fetch_parsers_take_text_only()
    {
        Assert.StartsWith("you absolute muppet", PostTextFetcher.ParseGetPosts(GetPostsJson));
        Assert.Null(PostTextFetcher.ParseGetPosts("""{"posts":[]}"""));
        Assert.Null(PostTextFetcher.ParseGetPosts("not json"));
        Assert.Equal("hello", PostTextFetcher.ParseGetRecord("""{"uri":"x","value":{"text":"hello"}}"""));
        Assert.Equal("https://pds.example.com", PostTextFetcher.ParsePds(
            """{"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}]}"""));
        Assert.Null(PostTextFetcher.ParsePds("""{"service":[{"type":"AtprotoPersonalDataServer","serviceEndpoint":"http://insecure.example"}]}"""));
    }

    private static async IAsyncEnumerable<LabelEvent> Labels(params LabelEvent[] ls)
    {
        foreach (var l in ls) { yield return l; await Task.Yield(); }
    }

    [Fact]
    public async Task Nothing_identifying_is_ever_drawn_or_logged()
    {
        var stats = new DarknessStats();
        var posts = new List<DarkPost>();
        var fetched = new List<string>();
        await DarknessFeed.RunAsync(
            Labels(new LabelEvent(PostUri("a"), "rude", false, 1), new LabelEvent(PostUri("b"), "porn", false, 2), new LabelEvent(PostUri("a"), "rude", false, 3)),
            (uri, _) => { fetched.Add(uri); return Task.FromResult(PostTextFetcher.ParseGetPosts(GetPostsJson)); },
            posts.Add, stats, CancellationToken.None, paceMs: 1);

        Assert.Equal((3, 1, 1), (stats.LabelsSeen, stats.LabelsPassed, stats.PostsFetched));
        Assert.Equal([PostUri("a")], fetched);   // porn dropped before any fetch, duplicate fetched once
        var post = Assert.Single(posts);
        Assert.Equal("you absolute muppet", post.Text);

        var room = new Room(Enumerable.Range(0, 6).Select(i => new Follow($"did:plc:guy{i}aaaaaaaaaaaaaaaa", $"guy{i}.test")));
        room.Haunt(post);
        string everything = "";
        for (long ms = 0; ms < 20000; ms += 250) everything += room.Render(100, 40, ms);
        Assert.True(room.Dark.Spawned > 0);
        Assert.Contains("muppet", everything);   // the text is drawn...
        foreach (var leak in new[] { AuthorDid, "authorauthor", AuthorHandle, "grumpy", AuthorName, "Grumpy", "avatar", "cdn.bsky", "friend", "t.example", "angry" })
            Assert.DoesNotContain(leak, everything, StringComparison.OrdinalIgnoreCase);
        // ...and the monster itself holds nothing but a hash, a kind and the cleaned words
        Assert.All(room.Dark.Monsters, m => Assert.Equal("you absolute muppet", m.Text));
        Assert.DoesNotContain(post.ToString(), AuthorDid);
    }

    // ---- monsters ----------------------------------------------------------------------------

    private static Monster Brute(ulong seed = 12345, long spawn = 1000, DarknessKind kind = DarknessKind.Rude) =>
        new() { Kind = kind, Seed = seed, GroupSeed = seed, SpawnMs = spawn, Text = "x", TargetDid = "t" };

    [Theory]
    [InlineData(DarknessKind.Rude)]
    [InlineData(DarknessKind.Spam)]
    public void Same_seed_and_time_give_the_same_position(DarknessKind kind)
    {
        foreach (long ms in new long[] { 1000, 1700, 3333, 5000, 9000, 15000 })
        {
            var a = MonsterMotion.Position(Brute(77, kind: kind), ms, (40, 20), 100, 60);
            var b = MonsterMotion.Position(Brute(77, kind: kind), ms, (40, 20), 100, 60);
            Assert.Equal(a, b);
        }
        Assert.NotEqual(MonsterMotion.Start(Brute(1), 100, 60), MonsterMotion.Start(Brute(2), 100, 60));
    }

    [Fact]
    public void A_charging_brute_ends_up_beside_its_target()
    {
        var m = Brute();
        var start = MonsterMotion.Start(m, 100, 60);
        var end = MonsterMotion.Position(m, m.SpawnMs + 10_000, (40, 20), 100, 60);
        Assert.InRange(Math.Abs(end.X - 40), 9, 13);
        Assert.Equal(start, MonsterMotion.Position(m, m.SpawnMs, (40, 20), 100, 60));
    }

    private static List<(string, (double, double))> Guys(params (double X, double Y)[] at) =>
        at.Select((p, i) => ($"g{i}", p)).ToList();

    [Fact]
    public void Spawning_is_throttled_and_capped()
    {
        var d = new Darkness();
        var guys = Guys((10, 10), (60, 30));
        for (int i = 0; i < 20; i++) d.Offer(new DarkPost(DarknessKind.Rude, (ulong)i + 1, "grr"));
        Assert.Equal(Darkness.MaxPending, d.Pending);

        d.Update(0, guys, 100, 60);
        Assert.Single(d.Monsters);
        d.Update(500, guys, 100, 60);
        Assert.Single(d.Monsters);                       // too soon after the last one
        d.Update(Darkness.SpawnGapMs, guys, 100, 60);
        Assert.Equal(2, d.Monsters.Count);
        for (long ms = 4000; ms < 14000; ms += 2000) d.Update(ms, guys, 100, 60);
        Assert.True(d.Monsters.Count <= Darkness.MaxMonsters);
    }

    [Fact]
    public void Spam_comes_as_a_small_swarm_carrying_fragments()
    {
        var d = new Darkness();
        d.Offer(new DarkPost(DarknessKind.Spam, 5, "buy followers now cheap fast guaranteed results"));
        d.Update(0, Guys((10, 10)), 100, 60);
        Assert.Equal(3, d.Monsters.Count);
        Assert.All(d.Monsters, m => Assert.Equal(DarknessKind.Spam, m.Kind));
        Assert.Equal(d.Monsters[0].GroupSeed, d.Monsters[2].GroupSeed);
        Assert.All(d.Monsters, m => Assert.True(m.Text.Length is > 0 and <= 14));
    }

    [Fact]
    public void Three_guys_near_a_monster_dispel_it()
    {
        var d = new Darkness();
        d.Offer(new DarkPost(DarknessKind.Rude, 9, "grr"));
        var crowd = Guys((40, 20), (46, 22), (36, 24), (42, 18));
        d.Update(0, crowd, 100, 60);
        var m = Assert.Single(d.Monsters);
        long t = 1000;
        for (; t < 12_000 && !m.Dissolving; t += 100) d.Update(t, crowd, 100, 60);
        Assert.True(m.Dissolving);
        Assert.Equal(1, d.Dispelled);
        d.Update(t + Darkness.DissolveMs + 1, crowd, 100, 60);
        Assert.Empty(d.Monsters);
    }

    [Fact]
    public void Two_guys_do_not_dispel_it()
    {
        var d = new Darkness();
        d.Offer(new DarkPost(DarknessKind.Rude, 9, "grr"));
        var pair = Guys((40, 20), (46, 22));
        for (long t = 0; t < 12_000; t += 100) d.Update(t, pair, 100, 60);
        Assert.Equal(0, d.Dispelled);
        Assert.False(d.Monsters[0].Dissolving);
    }

    [Fact]
    public void A_charged_guy_flinches()
    {
        var d = new Darkness();
        d.Offer(new DarkPost(DarknessKind.Rude, 9, "grr"));
        var g = Guys((40, 20));
        for (long t = 0; t < 8000; t += 100) d.Update(t, g, 100, 60);
        var f = d.Flinch(40, 20, 8000);
        Assert.NotEqual(0, f.X);
        Assert.Equal((0, 0), d.Flinch(0, 0, 8000));
    }

    [Fact]
    public void Dark_sprites_are_dark_and_flicker()
    {
        var brute = MonsterSprites.Build(Brute(), 0)!;
        Assert.Equal((MonsterSprites.BruteW, MonsterSprites.BruteH), (brute.Width, brute.Height));
        var mote = Brute(3, kind: DarknessKind.Spam);
        int off = Enumerable.Range(0, 200).Count(i => MonsterSprites.Build(mote, i * 90L) is null);
        Assert.InRange(off, 1, 100);
    }
}
