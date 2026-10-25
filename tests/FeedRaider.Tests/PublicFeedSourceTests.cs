using System.Net;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class PublicFeedSourceTests
{
    private sealed class Handler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Requests.Add(req);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    private const string Feed = "at://did:plc:gen/app.bsky.feed.generator/hot";

    private static string Url(Handler h) => Assert.Single(h.Requests).RequestUri!.OriginalString;

    [Fact]
    public async Task It_asks_the_public_app_view_for_the_feed_with_the_limit()
    {
        var h = new Handler(HttpStatusCode.OK, "{\"feed\":[]}");
        await new PublicFeedSource(new HttpClient(h)).GetPageAsync(Feed, null, 30, CancellationToken.None);
        Assert.Equal(HttpMethod.Get, h.Requests[0].Method);
        Assert.Equal($"{BskyClient.AppView}/xrpc/app.bsky.feed.getFeed?feed={Uri.EscapeDataString(Feed)}&limit=30", Url(h));
        Assert.Null(h.Requests[0].Headers.Authorization);     // no login
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(5000, 100)]
    public async Task The_limit_is_clamped_to_what_the_app_view_allows(int asked, int sent)
    {
        var h = new Handler(HttpStatusCode.OK, "{\"feed\":[]}");
        await new PublicFeedSource(new HttpClient(h)).GetPageAsync(Feed, null, asked, CancellationToken.None);
        Assert.EndsWith("&limit=" + sent, Url(h));
    }

    [Fact]
    public async Task A_cursor_is_passed_on_escaped_and_no_cursor_adds_nothing()
    {
        var h = new Handler(HttpStatusCode.OK, "{\"feed\":[]}");
        await new PublicFeedSource(new HttpClient(h)).GetPageAsync(Feed, "2026-01-01T00:00:00Z/a b&c", 10, CancellationToken.None);
        Assert.EndsWith("&limit=10&cursor=" + Uri.EscapeDataString("2026-01-01T00:00:00Z/a b&c"), Url(h));
        var none = new Handler(HttpStatusCode.OK, "{\"feed\":[]}");
        await new PublicFeedSource(new HttpClient(none)).GetPageAsync(Feed, null, 10, CancellationToken.None);
        Assert.DoesNotContain("cursor", Url(none));
    }

    [Fact]
    public async Task It_reads_signed_out_so_authors_who_opted_out_of_logged_out_viewers_are_dropped()
    {
        var h = new Handler(HttpStatusCode.OK, JetstreamParserTests.Fixture("feed-labels.json"));
        var page = await new PublicFeedSource(new HttpClient(h)).GetPageAsync(Feed, null, 50, CancellationToken.None);
        Assert.Contains(page.Posts, p => p.Uri == "at://did:plc:plain/app.bsky.feed.post/plain");
        Assert.DoesNotContain(page.Posts, p => p.AuthorOptOut);
        Assert.DoesNotContain(page.Posts, p => p.Uri.Contains("optout"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    public async Task An_error_response_throws_with_its_status_code(HttpStatusCode code, int number)
    {
        var h = new Handler(code, "{\"feed\":[],\"error\":\"nope\"}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new PublicFeedSource(new HttpClient(h)).GetPageAsync(Feed, null, 10, CancellationToken.None));
        Assert.Equal($"getFeed failed: {number}", ex.Message);
    }
}
