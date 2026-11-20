using System.Net;
using System.Text.Json;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

/// <summary>An author who carries Bluesky's <c>!no-unauthenticated</c> label is never shown to a signed-out player: not in a speech
/// box and not as a followed figure in the spectator view.</summary>
public class OptOutTests
{
    private const string Shy = "did:plc:shyshyshyshyshyshyshyshy";
    private const string Open = "did:plc:openopenopenopenopenopen";
    private const string PostPath = "/app.bsky.feed.post/3kabc";

    private static object Labels(string did) => did == Shy ? new[] { new { src = did, val = FeedMapper.OptOutLabel } } : Array.Empty<object>();

    private static object PostView(string did) => new { uri = $"at://{did}{PostPath}", author = new { did, labels = Labels(did) }, record = new { text = TextOf(did) } };

    private static string TextOf(string did) => did == Shy ? "shy words" : "open words";

    /// <summary>Answers as the AppView would for <see cref="Shy"/> and <see cref="Open"/>. With <paramref name="appView"/> false it
    /// has neither: getPosts finds no post and getAuthorFeed refuses the actor, as for an account that is deactivated, suspended or
    /// taken down. Any other path is not found. Every request is kept.</summary>
    private sealed class Net(bool appView = true) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var uri = req.RequestUri!;
            Asked.Add(uri);
            string did = Uri.UnescapeDataString(uri.Query).Contains(Shy, StringComparison.Ordinal) ? Shy : Open;
            var (status, body) = uri.AbsolutePath switch
            {
                "/xrpc/app.bsky.feed.getPosts" when appView => (HttpStatusCode.OK, JsonSerializer.Serialize(new { posts = new[] { PostView(did) } })),
                "/xrpc/app.bsky.feed.getAuthorFeed" when appView => (HttpStatusCode.OK, JsonSerializer.Serialize(new { feed = new[] { new { post = PostView(did) } } })),
                "/xrpc/app.bsky.feed.getPosts" => (HttpStatusCode.OK, """{"posts":[]}"""),
                "/xrpc/app.bsky.feed.getAuthorFeed" => (HttpStatusCode.BadRequest, """{"error":"AccountTakedown"}"""),
                _ => (HttpStatusCode.NotFound, ""),
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    /// <summary>A post by <paramref name="did"/>, or with <paramref name="aPost"/> false the bare DID an account-level label names.</summary>
    private static string Subject(string did, bool aPost) => aPost ? $"at://{did}{PostPath}" : did;

    private static async Task<(string? Text, List<Uri> Asked)> Fetch(string subject, bool signedOut, bool appView = true)
    {
        var net = new Net(appView);
        using var http = new HttpClient(net);
        string? text = await PostTextFetcher.FetchAsync(http, subject, signedOut, CancellationToken.None);
        return (text, net.Asked);
    }

    // ---- speech boxes from the AppView ----------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Signed_out_an_opted_out_author_gives_nothing_from_one_request(bool aPost)
    {
        var (shy, asked) = await Fetch(Subject(Shy, aPost), signedOut: true);
        Assert.Equal("", shy);
        Assert.Single(asked);
        var (open, _) = await Fetch(Subject(Open, aPost), signedOut: true);
        Assert.Equal(TextOf(Open), open);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Signed_in_both_authors_are_shown(bool aPost)
    {
        foreach (var did in new[] { Shy, Open })
        {
            var (text, asked) = await Fetch(Subject(did, aPost), signedOut: false);
            Assert.Equal(TextOf(did), text);
            Assert.Single(asked);
        }
    }

    [Fact]
    public void Signed_out_a_post_view_with_no_author_counts_as_opted_out()
    {
        const string json = """{"posts":[{"record":{"text":"hello"}}]}""";
        Assert.Equal("", PostTextFetcher.ParseGetPosts(json, signedOut: true));
        Assert.Equal("hello", PostTextFetcher.ParseGetPosts(json, signedOut: false));
    }

    // ---- a post the AppView does not return --------------------------------------------------

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task A_post_the_appview_does_not_return_gives_nothing_from_one_request_to_it(bool aPost, bool signedOut)
    {
        foreach (var did in new[] { Shy, Open })
        {
            var (text, asked) = await Fetch(Subject(did, aPost), signedOut, appView: false);
            Assert.Null(text);
            Assert.StartsWith($"{BskyClient.AppView}/xrpc/app.bsky.feed.", Assert.Single(asked).AbsoluteUri, StringComparison.Ordinal);
        }
    }

    private static async IAsyncEnumerable<string> Of(params string[] uris)
    {
        foreach (var u in uris) { yield return u; await Task.Yield(); }
    }

    [Theory]
    [InlineData(true, new[] { "open words" })]
    [InlineData(false, new[] { "shy words", "open words" })]
    public async Task A_hidden_reply_by_an_opted_out_author_becomes_no_speech_box_when_signed_out(bool signedOut, string[] boxes)
    {
        using var http = new HttpClient(new Net());
        var said = new List<string>();
        await DarknessFeed.RunHiddenAsync(Of($"at://{Shy}{PostPath}", $"at://{Open}{PostPath}"),
            (uri, c) => PostTextFetcher.FetchAsync(http, uri, signedOut, c), p => said.Add(p.Text), new DarknessStats(), CancellationToken.None, paceMs: 1);
        Assert.Equal(boxes, said);
    }

    // ---- the spectator view's figures ----------------------------------------------------------

    [Fact]
    public void A_follow_who_opts_out_is_left_out_of_the_follows()
    {
        string json = JsonSerializer.Serialize(new
        {
            follows = new[]
            {
                new { did = Shy, handle = "shy.test", labels = Labels(Shy) },
                new { did = Open, handle = "open.test", labels = Labels(Open) },
            },
            cursor = "next",
        });
        var (follows, cursor) = BskyClient.ParseFollows(json);
        Assert.Equal([new Follow(Open, "open.test")], follows);
        Assert.Equal("next", cursor);
    }
}
