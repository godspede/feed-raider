using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

/// <summary>ReadAsync against a real WebSocket server on localhost: the URL limit and the receive loop.</summary>
public class JetstreamReadTests
{
    /// <summary>Serves one connection: sends <paramref name="messages"/> (each split into two frames), then notes the first message
    /// the client sends, if any. Ends when the client goes away.</summary>
    private sealed class Server : IDisposable
    {
        private readonly HttpListener _http = new();
        public string Endpoint { get; }
        public Task<(string Url, string? FirstMessage)> Served { get; }

        public Server(string[] messages)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _http.Prefixes.Add($"http://localhost:{port}/");
            _http.Start();
            Endpoint = $"ws://localhost:{port}/subscribe";
            Served = Serve(messages);
        }

        private async Task<(string, string?)> Serve(string[] messages)
        {
            var ctx = await _http.GetContextAsync();
            string url = ctx.Request.RawUrl!;
            var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
            foreach (var m in messages)
            {
                var bytes = Encoding.UTF8.GetBytes(m);
                int half = bytes.Length / 2;
                await ws.SendAsync(bytes.AsMemory(0, half), WebSocketMessageType.Text, false, CancellationToken.None);
                await ws.SendAsync(bytes.AsMemory(half), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var buffer = new byte[16 * 1024];
            try
            {
                var r = await ws.ReceiveAsync(buffer, timeout.Token);
                return (url, r.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(buffer, 0, r.Count) : null);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                return (url, null);
            }
        }

        public void Dispose() => _http.Close();
    }

    private static async Task<PostEvent> FirstEvent(JetstreamSource source)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await foreach (var e in source.ReadAsync(cts.Token)) return e;
        throw new InvalidOperationException("the stream ended without an event");
    }

    /// <summary>Dids whose Jetstream URL is exactly <paramref name="length"/> characters long.</summary>
    private static string[] DidsForUrlLength(string endpoint, int length)
    {
        int baseLength = JetstreamSource.BuildUrl(endpoint, []).Length;
        int filler = length - baseLength - "&wantedDids=".Length;
        return ["z" + new string('a', filler - 1)];
    }

    [Fact]
    public async Task Messages_split_across_frames_are_joined_and_non_posts_skipped()
    {
        using var server = new Server([JetstreamParserTests.Fixture("like.json"), JetstreamParserTests.Fixture("post.json")]);
        var e = await FirstEvent(new JetstreamSource(["did:plc:alice111"], server.Endpoint));
        Assert.Equal("hello from the room", e.Text);
        var (url, first) = await server.Served;
        Assert.Contains("wantedDids=did%3Aplc%3Aalice111", url);
        Assert.Null(first);
    }

    [Fact]
    public async Task A_url_exactly_at_the_limit_still_carries_the_dids()
    {
        using var server = new Server([JetstreamParserTests.Fixture("post.json")]);
        var dids = DidsForUrlLength(server.Endpoint, JetstreamSource.MaxUrlLength);
        Assert.Equal(JetstreamSource.MaxUrlLength, JetstreamSource.BuildUrl(server.Endpoint, dids).Length);
        await FirstEvent(new JetstreamSource(dids, server.Endpoint));
        var (url, first) = await server.Served;
        Assert.Contains("wantedDids=", url);
        Assert.Null(first);
    }

    [Fact]
    public async Task A_url_one_over_the_limit_sends_the_dids_in_an_options_update()
    {
        using var server = new Server([JetstreamParserTests.Fixture("post.json")]);
        var dids = DidsForUrlLength(server.Endpoint, JetstreamSource.MaxUrlLength + 1);
        await FirstEvent(new JetstreamSource(dids, server.Endpoint));
        var (url, first) = await server.Served;
        Assert.DoesNotContain("wantedDids", url);
        Assert.Equal(JetstreamSource.BuildOptionsUpdate(dids), first);
    }
}
