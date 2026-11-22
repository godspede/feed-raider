using System.Formats.Cbor;
using FeedRaider.Engine;
using static FeedRaider.Tests.DarknessSourcesTests;

namespace FeedRaider.Tests;

public class DarknessTests
{
    private const string AuthorDid = "did:plc:authorauthorauthorauthor";
    private const string AuthorRepo = "at://" + AuthorDid;
    private const string AuthorHandle = "grumpy-author.example.social";
    private const string AuthorName = "Grumpy Person Display";
    private const string LabelerDid = "did:plc:ar7c4by46qjdydhdevvrndac";
    /// <summary>The fields every label carries: ver, src, uri, val and cts; a negation adds one more.</summary>
    private const int LabelFields = 5;
    /// <summary>The header's op for a message.</summary>
    private const int MessageOp = 1;
    private const string CleanedText = "you absolute muppet", RudeWords = "grr", FetchedText = "some text", Ellipsis = "…";

    private static string PostUri(string rkey = "3kabc") => $"{AuthorRepo}/app.bsky.feed.post/{rkey}";

    /// <summary>A frame's header map of the given type, then the start of its body map with its sequence number.</summary>
    private static CborWriter FrameStart(string type, long seq, int bodyFields)
    {
        var writer = new CborWriter(CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
        writer.WriteStartMap(2);
        writer.WriteTextString("op"); writer.WriteInt32(MessageOp);
        writer.WriteTextString("t"); writer.WriteTextString(type);
        writer.WriteEndMap();
        writer.WriteStartMap(bodyFields);
        writer.WriteTextString("seq"); writer.WriteInt64(seq);
        return writer;
    }

    /// <summary>A subscribeLabels frame as the labeler sends it: header map, then body map.</summary>
    internal static byte[] LabelsFrame(long seq, params (string Uri, string Val, bool? Neg)[] labels)
    {
        var writer = FrameStart("#labels", seq, 2);
        writer.WriteTextString("labels");
        writer.WriteStartArray(labels.Length);
        foreach (var (uri, val, neg) in labels)
        {
            writer.WriteStartMap(neg is null ? LabelFields : LabelFields + 1);
            writer.WriteTextString("ver"); writer.WriteInt32(1);
            writer.WriteTextString("src"); writer.WriteTextString(LabelerDid);
            writer.WriteTextString("uri"); writer.WriteTextString(uri);
            writer.WriteTextString("val"); writer.WriteTextString(val);
            writer.WriteTextString("cts"); writer.WriteTextString("2026-01-01T00:00:00.000Z");
            if (neg is { } negated) { writer.WriteTextString("neg"); writer.WriteBoolean(negated); }
            writer.WriteEndMap();
        }
        writer.WriteEndArray();
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] OtherFrame(string type)
    {
        var writer = FrameStart(type, 7, 1);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static LabelEvent PostLabel(string val, bool neg = false) => new(PostUri(), val, neg, 1);

    // ---- frame decode ------------------------------------------------------------------------

    [Fact]
    public void Decodes_a_labels_frame()
    {
        const long Seq = 42;
        var bytes = LabelsFrame(Seq, (PostUri(), Spam, null), (PostUri("x"), Rude, false));
        var labels = LabelFrame.Decode(bytes, out long seq);
        Assert.Equal(Seq, seq);
        Assert.Equal(2, labels.Count);
        Assert.Equal((Spam, false, Seq), (labels[0].Val, labels[0].Neg, labels[0].Seq));
        Assert.Equal(PostUri(), labels[0].Uri);
        Assert.False(labels[1].Neg);
    }

    [Fact]
    public void Reads_neg_true()
    {
        var labels = LabelFrame.Decode(LabelsFrame(1, (PostUri(), Spam, true)), out _);
        Assert.True(Assert.Single(labels).Neg);
    }

    [Fact]
    public void Other_frames_and_garbage_decode_to_nothing()
    {
        Assert.Empty(LabelFrame.Decode(OtherFrame("#info"), out _));
        Assert.Empty(LabelFrame.Decode(new byte[] { 0xff, 0x00, 0x13 }, out _));
        Assert.Empty(LabelFrame.Decode(Array.Empty<byte>(), out _));
        var truncated = LabelsFrame(5, (PostUri(), Spam, null));
        Assert.Empty(LabelFrame.Decode(truncated.AsMemory(0, truncated.Length - 10), out _));
    }

    // ---- filter ------------------------------------------------------------------------------

    [Theory]
    [InlineData(Spam, DarknessKind.Spam)]
    [InlineData(Rude, DarknessKind.Rude)]
    [InlineData("engagement-farming", DarknessKind.Spam)]
    public void Allowlisted_post_labels_pass(string val, DarknessKind kind) =>
        Assert.Equal(kind, DarknessFilter.Accept(PostLabel(val)));

    [Theory]
    [InlineData(Porn)]
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
        Assert.Null(DarknessFilter.Accept(PostLabel(val)));

    [Fact]
    public void Negations_are_dropped() =>
        Assert.Null(DarknessFilter.Accept(PostLabel(Spam, neg: true)));

    [Theory]
    [InlineData(AuthorDid)]
    [InlineData(AuthorRepo)]
    [InlineData(AuthorRepo + "/app.bsky.actor.profile/self")]
    [InlineData(AuthorRepo + "/app.bsky.feed.like/3k")]
    [InlineData(AuthorRepo + "/app.bsky.feed.post/")]
    public void Only_post_uris_pass(string uri) =>
        Assert.Null(DarknessFilter.Accept(new LabelEvent(uri, Spam, false, 1)));

    [Fact]
    public void Whole_pipeline_keeps_only_the_allowed_ones()
    {
        string spamPost = PostUri("a"), rudePost = PostUri("d");
        var frame = LabelFrame.Decode(LabelsFrame(9,
            (spamPost, Spam, null), (PostUri("b"), Porn, null), (PostUri("c"), Rude, true), (rudePost, Rude, null),
            (AuthorDid, Spam, null)), out _);
        var kept = frame.Where(label => DarknessFilter.Accept(label) is not null).Select(label => label.Uri).ToList();
        Assert.Equal([spamPost, rudePost], kept);
    }

    // ---- text and anonymity ------------------------------------------------------------------

    [Fact]
    public void Clean_strips_mentions_urls_hashtags_handles_and_dids()
    {
        string raw = "hey @someone.bsky.social   look\n\tat https://spam.example/buy?x=1 #cheap #deals and www.evil.example, "
                     + $"also {AuthorHandle} said did:plc:abc123 hi";
        string cleaned = DarknessText.Clean(raw);
        Assert.Equal("hey look at and also said hi", cleaned);
        foreach (var bad in new[] { "@", "http", "www", "#", "example", "did:", ".social" })
            Assert.DoesNotContain(bad, cleaned);
    }

    [Fact]
    public void Clean_truncates_to_about_sixty_characters_on_a_word_boundary()
    {
        string post = string.Join(' ', Enumerable.Repeat("blah", 40));   // 199 chars: whole at the post limit
        string cleaned = DarknessText.Clean(post);
        Assert.InRange(cleaned.Length, 20, DarknessText.MaxChars + Ellipsis.Length);
        Assert.EndsWith(Ellipsis, cleaned);
        string feathers = string.Join(' ', Enumerable.Repeat("feather", 20));   // 8-char steps: a plain cut at the limit lands inside a word
        string kept = DarknessText.Clean(feathers)[..^Ellipsis.Length];
        Assert.StartsWith(kept, feathers);
        Assert.Equal(' ', feathers[kept.Length]);
        Assert.Equal(post, DarknessText.Clean(post, DarknessText.MaxPostChars));
        Assert.Empty(DarknessText.Clean("   @only.mention.test https://x.test  "));
        Assert.Empty(DarknessText.Clean(null));
    }

    [Fact]
    public void Fragments_are_short()
    {
        var fragments = DarknessText.Fragments(DarknessText.Clean("buy followers now cheap fast guaranteed results today only"));
        Assert.True(fragments.Count >= 3);
        Assert.All(fragments, fragment => Assert.True(fragment.Length <= FragmentWidth));
    }

    /// <summary>The widest fragment a spam mote carries.</summary>
    private const int FragmentWidth = 14;

    private const string GetPostsJson = $$$"""
        {"posts":[{"uri":"at://{{{AuthorDid}}}/app.bsky.feed.post/3kabc","cid":"bafy",
          "author":{"did":"{{{AuthorDid}}}","handle":"{{{AuthorHandle}}}","displayName":"{{{AuthorName}}}","avatar":"https://cdn.bsky.app/img/avatar/plain/{{{AuthorDid}}}/xyz@jpeg"},
          "record":{"$type":"app.bsky.feed.post","text":"{{{CleanedText}}} @friend.example.social https://t.example/abc #angry","createdAt":"2026-01-01T00:00:00Z"}}]}
        """;

    [Fact]
    public void Fetch_parser_takes_text_only()
    {
        Assert.StartsWith(CleanedText, PostTextFetcher.ParseGetPosts(GetPostsJson, signedOut: false));
        Assert.Null(PostTextFetcher.ParseGetPosts("""{"posts":[]}""", signedOut: false));
    }

    [Fact]
    public void A_body_that_is_not_json_throws_from_either_parser()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => PostTextFetcher.ParseGetPosts(NotJson, signedOut: false));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => PostTextFetcher.ParseAuthorFeed(NotJson, signedOut: false));
    }

    private const string NotJson = "<html>bad gateway</html>";

    [Fact]
    public async Task A_fetch_whose_body_is_not_json_counts_as_a_failure_and_the_stream_goes_on()
    {
        var stats = new DarknessStats();
        var posts = new List<DarknessPost>();
        string first = PostUri("first"), second = PostUri("second");
        await DarknessFeed.RunAsync(
            Labels(new LabelEvent(first, Rude, false, 1), new LabelEvent(second, Rude, false, 2)),
            (uri, _) => Task.FromResult(PostTextFetcher.ParseGetPosts(uri == first ? NotJson : GetPostsJson, signedOut: false)),
            posts.Add, stats, CancellationToken.None, paceMs: 1);
        Assert.Equal((1, 1), (stats.FetchFailures, stats.PostsFetched));
        Assert.Equal(CleanedText, Assert.Single(posts).Text);
    }

    private static async IAsyncEnumerable<LabelEvent> Labels(params LabelEvent[] labels)
    {
        foreach (var label in labels) { yield return label; await Task.Yield(); }
    }

    [Fact]
    public async Task A_burst_of_spam_does_not_push_a_rude_subject_out_and_the_two_take_turns()
    {
        const int Burst = 100;
        string firstRude = $"{Rude}-1", secondRude = $"{Rude}-2";
        static string SpamSubject(int number) => $"{Spam}-{number}";
        var backlog = new FetchBacklog();
        backlog.Add(firstRude, DarknessKind.Rude, Rude);
        backlog.Add(secondRude, DarknessKind.Rude, Rude);
        for (int i = 1; i <= Burst; i++) backlog.Add(SpamSubject(i), DarknessKind.Spam, Spam);
        backlog.Complete();

        var taken = new List<string>();
        while (await backlog.TakeAsync(CancellationToken.None) is var (subject, _, _)) taken.Add(subject);

        int oldestSpamKept = Burst - FetchBacklog.RestCapacity + 1;
        Assert.Equal([firstRude, SpamSubject(oldestSpamKept), secondRude, SpamSubject(oldestSpamKept + 1)], taken.Take(4).ToArray());
        Assert.Equal(2 + FetchBacklog.RestCapacity, taken.Count);
        Assert.Equal(SpamSubject(Burst), taken[^1]);
        Assert.Null(await backlog.TakeAsync(CancellationToken.None));   // complete and empty stays so
    }

    [Fact]
    public async Task A_rude_label_buried_in_a_replayed_flood_of_spam_is_fetched_among_the_first()
    {
        const int Before = 50, After = 150;
        // the fetch already under way when the flood lands, then one turn each
        const int FirstFew = 3;
        var labels = new List<LabelEvent>();
        for (int i = 0; i < Before; i++) labels.Add(new LabelEvent(PostUri("early" + i), Spam, false, labels.Count + 1));
        labels.Add(new LabelEvent(PostUri("rude0"), Rude, false, labels.Count + 1));
        for (int i = 0; i < After; i++) labels.Add(new LabelEvent(PostUri("late" + i), Spam, false, labels.Count + 1));
        var held = new TaskCompletionSource<string?>();
        var posts = new List<DarknessPost>();
        int asked = 0;
        await DarknessFeed.RunAsync(Burst(labels, held), (_, _) => Interlocked.Increment(ref asked) == 1 ? held.Task : Task.FromResult<string?>(FetchedText),
            posts.Add, new DarknessStats(), CancellationToken.None, paceMs: 1);
        Assert.InRange(posts.FindIndex(post => post.Kind == DarknessKind.Rude), 0, FirstFew - 1);
        Assert.InRange(posts.Count, FetchBacklog.RestCapacity, FetchBacklog.RestCapacity + FirstFew);
    }

    /// <summary>Every label at once, as a replay sends them; the first fetch is held until the last label is out.</summary>
    private static async IAsyncEnumerable<LabelEvent> Burst(List<LabelEvent> labels, TaskCompletionSource<string?> firstFetch)
    {
        foreach (var label in labels) yield return label;
        await Task.Yield();
        firstFetch.SetResult(FetchedText);
    }

    [Fact]
    public async Task Nothing_identifying_is_ever_drawn_or_kept_on_the_enemy()
    {
        const long WatchMs = 20_000;
        var stats = new DarknessStats();
        var posts = new List<DarknessPost>();
        var fetched = new List<string>();
        string rudePost = PostUri(Rude);
        await DarknessFeed.RunAsync(
            Labels(new LabelEvent(rudePost, Rude, false, 1), new LabelEvent(PostUri(Porn), Porn, false, 2), new LabelEvent(rudePost, Rude, false, 3)),
            (uri, _) => { fetched.Add(uri); return Task.FromResult(PostTextFetcher.ParseGetPosts(GetPostsJson, signedOut: false)); },
            posts.Add, stats, CancellationToken.None, paceMs: 1);

        Assert.Equal((3, 1, 1), (stats.LabelsSeen, stats.LabelsPassed, stats.PostsFetched));
        Assert.Equal([rudePost], fetched);   // porn dropped before any fetch, duplicate fetched once
        var post = Assert.Single(posts);
        Assert.Equal(CleanedText, post.Text);

        var room = RoomOfFigures();
        room.Darkness.Offer(post);
        string everything = Watch(room, WatchMs);
        Assert.True(room.Darkness.Spawned > 0);
        Assert.Contains("muppet", everything);   // the text is drawn...
        foreach (var leak in new[] { AuthorDid, "authorauthor", AuthorHandle, "grumpy", AuthorName, "Grumpy", "img/avatar", "cdn.bsky", "friend", "t.example", "angry" })
            Assert.DoesNotContain(leak, everything, StringComparison.OrdinalIgnoreCase);
        // ...and the enemy itself holds nothing but a hash, a kind and the cleaned words
        Assert.All(room.Darkness.Enemies, enemy => Assert.Equal(CleanedText, enemy.Text));
        Assert.DoesNotContain(AuthorDid, post.ToString());
    }

    // ---- enemies ----------------------------------------------------------------------------

    /// <summary>The room the enemies roam: columns, and height in pixels.</summary>
    private const int FieldW = 100, FieldH = 60;
    private static readonly (double X, double Y) Target = (40, 20), BesideTarget = (46, 22);
    /// <summary>A charging brute stops this many pixels to the side of its target, give or take its shake.</summary>
    private const int BruteSideOffset = 11, ShakeSpread = 2;
    /// <summary>A mote's flicker is sampled once a step, one step being this long, over this many steps.</summary>
    private const long FlickerMs = 90;
    private const int FlickerSamples = 200;
    /// <summary>How far apart the tests sample the room, in ms.</summary>
    private const long StepMs = 100;
    /// <summary>Long enough for any enemy to reach its target and stand there a while, in ms.</summary>
    private const long LongAfterMs = 12_000;
    /// <summary>How many motes one spam post sends.</summary>
    private const int MotesPerSpam = 3;

    private static DarknessEnemy DarknessEnemyOf(ulong seed = 12345, long spawn = 1000, DarknessKind kind = DarknessKind.Rude) =>
        new() { Kind = kind, Seed = seed, GroupSeed = seed, SpawnMs = spawn, Text = RudeWords, TargetDid = "target" };

    private static Darkness WithOneRude()
    {
        var darkness = new Darkness();
        darkness.Offer(new DarknessPost(DarknessKind.Rude, 9, RudeWords));
        return darkness;
    }

    [Theory]
    [InlineData(DarknessKind.Rude)]
    [InlineData(DarknessKind.Spam)]
    public void Same_seed_and_time_give_the_same_position(DarknessKind kind)
    {
        foreach (long ms in new long[] { 1000, 1700, 3333, 5000, 9000, 15000 })
        {
            var first = DarknessEnemyMotion.Position(DarknessEnemyOf(77, kind: kind), ms, Target, FieldW, FieldH);
            var second = DarknessEnemyMotion.Position(DarknessEnemyOf(77, kind: kind), ms, Target, FieldW, FieldH);
            Assert.Equal(first, second);
        }
        Assert.NotEqual(DarknessEnemyMotion.Start(DarknessEnemyOf(1), FieldW, FieldH), DarknessEnemyMotion.Start(DarknessEnemyOf(2), FieldW, FieldH));
    }

    [Fact]
    public void A_charging_brute_ends_up_beside_its_target()
    {
        var brute = DarknessEnemyOf();
        var start = DarknessEnemyMotion.Start(brute, FieldW, FieldH);
        var end = DarknessEnemyMotion.Position(brute, brute.SpawnMs + LongAfterMs, Target, FieldW, FieldH);
        Assert.InRange(Math.Abs(end.X - Target.X), BruteSideOffset - ShakeSpread, BruteSideOffset + ShakeSpread);
        Assert.Equal(start, DarknessEnemyMotion.Position(brute, brute.SpawnMs, Target, FieldW, FieldH));
    }

    private static List<(string, (double, double))> Figures(params (double X, double Y)[] at) =>
        at.Select((place, index) => ($"g{index}", place)).ToList();

    [Fact]
    public void Spawning_is_throttled_and_capped()
    {
        var darkness = new Darkness();
        var figures = Figures((10, 10), (60, 30));
        for (int i = 0; i < 2 * Darkness.MaxPending; i++) darkness.Offer(new DarknessPost(DarknessKind.Rude, (ulong)i + 1, RudeWords));
        Assert.Equal(Darkness.MaxPending, darkness.Pending);

        darkness.Update(0, figures, FieldW, FieldH);
        Assert.Single(darkness.Enemies);
        darkness.Update(Darkness.SpawnGapMs / 2, figures, FieldW, FieldH);
        Assert.Single(darkness.Enemies);                       // too soon after the last one
        darkness.Update(Darkness.SpawnGapMs, figures, FieldW, FieldH);
        Assert.Equal(2, darkness.Enemies.Count);
        for (long ms = 2 * Darkness.SpawnGapMs; ms < (Darkness.MaxEnemies + 1) * Darkness.SpawnGapMs; ms += Darkness.SpawnGapMs) darkness.Update(ms, figures, FieldW, FieldH);
        Assert.True(darkness.Enemies.Count <= Darkness.MaxEnemies);
    }

    [Fact]
    public void Spam_comes_as_a_small_swarm_carrying_fragments()
    {
        var darkness = new Darkness();
        darkness.Offer(new DarknessPost(DarknessKind.Spam, 5, "buy followers now cheap fast guaranteed results"));
        darkness.Update(0, Figures((10, 10)), FieldW, FieldH);
        Assert.Equal(MotesPerSpam, darkness.Enemies.Count);
        Assert.All(darkness.Enemies, mote => Assert.Equal(DarknessKind.Spam, mote.Kind));
        Assert.Equal(darkness.Enemies[0].GroupSeed, darkness.Enemies[MotesPerSpam - 1].GroupSeed);
        Assert.All(darkness.Enemies, mote => Assert.True(mote.Text.Length is > 0 and <= FragmentWidth));
    }

    [Fact]
    public void Three_figures_near_an_enemy_dispel_it()
    {
        var darkness = WithOneRude();
        var crowd = Figures(Target, BesideTarget, (36, 24), (42, 18));
        darkness.Update(0, crowd, FieldW, FieldH);
        var brute = Assert.Single(darkness.Enemies);
        long ms = 1000;
        for (; ms < LongAfterMs && !brute.Dissolving; ms += StepMs) darkness.Update(ms, crowd, FieldW, FieldH);
        Assert.True(brute.Dissolving);
        Assert.Equal(1, darkness.Dispelled);
        darkness.Update(ms + Darkness.DissolveMs + 1, crowd, FieldW, FieldH);
        Assert.Empty(darkness.Enemies);
    }

    [Fact]
    public void Two_figures_do_not_dispel_it()
    {
        var darkness = WithOneRude();
        var pair = Figures(Target, BesideTarget);
        for (long ms = 0; ms < LongAfterMs; ms += StepMs) darkness.Update(ms, pair, FieldW, FieldH);
        Assert.Equal(0, darkness.Dispelled);
        Assert.False(darkness.Enemies[0].Dissolving);
    }

    [Fact]
    public void A_charged_figure_flinches()
    {
        var darkness = WithOneRude();
        const long ChargedMs = 8000;   // long after the brute has reached the figure
        var figure = Figures(Target);
        for (long ms = 0; ms < ChargedMs; ms += StepMs) darkness.Update(ms, figure, FieldW, FieldH);
        var flinch = darkness.Flinch(Target.X, Target.Y, ChargedMs);
        Assert.NotEqual(0, flinch.X);
        Assert.Equal((0, 0), darkness.Flinch(0, 0, ChargedMs));
    }

    [Fact]
    public void Brute_has_its_size_and_a_mote_flickers()
    {
        var brute = DarknessEnemySprites.Build(DarknessEnemyOf(), 0)!;
        Assert.Equal((DarknessEnemySprites.BruteW, DarknessEnemySprites.BruteH), (brute.Width, brute.Height));
        var mote = DarknessEnemyOf(3, kind: DarknessKind.Spam);
        int unseen = Enumerable.Range(0, FlickerSamples).Count(sample => DarknessEnemySprites.Build(mote, sample * FlickerMs) is null);
        Assert.InRange(unseen, 1, FlickerSamples / 2);
    }
}
