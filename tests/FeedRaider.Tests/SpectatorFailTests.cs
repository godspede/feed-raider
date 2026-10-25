using System.Net;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class SpectatorFailTests
{
    private sealed class Fail : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("down") });
    }

    private sealed class Sink : IFrameSink
    {
        public List<string> Frames { get; } = [];
        public void Write(string frame) => Frames.Add(frame);
    }

    [Fact]
    public async Task FailedFollowsFetchReplacesTheFetchingLineAndSaysToReload()
    {
        var sink = new Sink();
        var spectator = new Spectator(new HttpClient(new Fail()), () => (100, 30), sink);
        await Assert.ThrowsAsync<InvalidOperationException>(() => spectator.RunAsync("bsky.app", CancellationToken.None));
        Assert.Contains("fetching who", sink.Frames[0]);
        var last = sink.Frames[^1];
        Assert.DoesNotContain("fetching who", last);
        Assert.Contains("could not load who @bsky.app follows, reload to try again", last);
    }
}
