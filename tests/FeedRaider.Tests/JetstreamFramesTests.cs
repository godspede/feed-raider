using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

/// <summary>ReceiveTextAsync against a real WebSocket server on localhost: a message that arrives in several frames.</summary>
public class JetstreamFramesTests
{
    private static async Task<string?[]> Receive(string[] messages, int bufferBytes, int frameBytes)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var http = new HttpListener();
        http.Prefixes.Add($"http://localhost:{port}/");
        http.Start();
        var serve = Task.Run(async () =>
        {
            var ws = (await (await http.GetContextAsync()).AcceptWebSocketAsync(null)).WebSocket;
            foreach (var m in messages)
            {
                var bytes = Encoding.UTF8.GetBytes(m);
                for (int at = 0; at < bytes.Length; at += frameBytes)
                {
                    int n = Math.Min(frameBytes, bytes.Length - at);
                    await ws.SendAsync(bytes.AsMemory(at, n), WebSocketMessageType.Text, at + n >= bytes.Length, CancellationToken.None);
                }
            }
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var client = new ClientWebSocket();
        await client.ConnectAsync(new Uri($"ws://localhost:{port}/"), cts.Token);
        var got = new List<string?>();
        var buffer = new byte[bufferBytes];
        foreach (var _ in messages) got.Add((await JetstreamSource.ReceiveTextAsync(client, buffer, cts.Token)).Text);
        got.Add((await JetstreamSource.ReceiveTextAsync(client, buffer, cts.Token)).Text);   // the close
        await serve;
        return [.. got];
    }

    [Fact]
    public async Task A_message_sent_in_several_frames_arrives_whole()
    {
        string a = $$"""{"text":"{{new string('x', 400)}}"}""", b = "{\"text\":\"second\"}";
        var got = await Receive([a, b], bufferBytes: 64, frameBytes: 100);
        Assert.Equal(new string?[] { a, b, null }, got);
    }

    [Fact]
    public async Task A_character_split_between_frames_is_not_broken()
    {
        string m = $$"""{"text":"{{string.Concat(Enumerable.Repeat("é€\U0001F600", 40))}}"}""";
        var got = await Receive([m], bufferBytes: 7, frameBytes: 5);
        Assert.Equal(new string?[] { m, null }, got);
    }

    [Fact]
    public async Task A_message_that_fits_one_frame_arrives_whole()
    {
        var got = await Receive(["{\"a\":1}"], bufferBytes: 64, frameBytes: 64);
        Assert.Equal(new string?[] { "{\"a\":1}", null }, got);
    }
}
