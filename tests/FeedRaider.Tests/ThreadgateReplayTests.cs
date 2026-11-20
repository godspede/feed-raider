using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

/// <summary>ThreadgateSource against a real WebSocket server on localhost: the replay of the recent past at the start of a run.</summary>
public class ThreadgateReplayTests
{
    private const string Owner = "did:plc:owneroowneroowneroowner1";
    private const long FirstEventUs = 1760000001000000;
    private const int TimeoutSeconds = 20;
    private static string Reply(int number) => $"at://did:plc:replierreplierreplier222/app.bsky.feed.post/3kr{number}";

    private static string GateUpdate(long timeUs, params int[] hidden) =>
        $$$"""{"did":"{{{Owner}}}","time_us":{{{timeUs}}},"kind":"commit","commit":{"rev":"3m","operation":"update","collection":"{{{ThreadgateDiffer.GateCollection}}}","rkey":"3kgate1","record":{"$type":"{{{ThreadgateDiffer.GateCollection}}}","post":"at://{{{Owner}}}/app.bsky.feed.post/3kgate1","allow":[],"hiddenReplies":[{{{string.Join(",", hidden.Select(number => $"\"{Reply(number)}\""))}}}]},"cid":"bafz"}}""";

    /// <summary>Serves one connection per script, in order, and notes the address each was opened at. The first is left for the client
    /// to drop once it has read the first event, the last stays open, and each one between is closed once its script is sent.</summary>
    private static async Task Serve(HttpListener http, string[][] scripts, List<string> urls)
    {
        for (int i = 0; i < scripts.Length; i++)
        {
            var context = await http.GetContextAsync();
            lock (urls) urls.Add(context.Request.RawUrl!);
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            foreach (var message in scripts[i])
                await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
            if (i > 0 && i < scripts.Length - 1) await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_first_event_names_now_then_the_stream_opens_again_its_replay_before_it_once_and_reports_each_hidden_reply_once()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var http = new HttpListener();
        http.Prefixes.Add($"http://localhost:{port}/");
        http.Start();
        string query = "/subscribe?wantedCollections=" + ThreadgateDiffer.GateCollection;
        var urls = new List<string>();
        string[][] scripts =
        [
            [GateUpdate(FirstEventUs, 1)],                                    // "now": not reported, the replay brings it again
            [GateUpdate(FirstEventUs - 1, 1, 2), GateUpdate(FirstEventUs, 1)], // the replay, which then drops
            [GateUpdate(FirstEventUs + 1, 1, 3)],                             // live again: only what the gate newly hides
        ];
        var serve = Serve(http, scripts, urls);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        string[] expected = [Reply(1), Reply(2), Reply(3)];
        var got = new List<string>();
        await foreach (var uri in new ThreadgateSource($"ws://localhost:{port}{query}").ReadAsync(cts.Token))
        {
            got.Add(uri);
            if (got.Count == expected.Length) break;
        }
        await serve.WaitAsync(cts.Token);

        Assert.Equal(expected, got);
        long replayFrom = FirstEventUs - (long)ThreadgateSource.Replay.TotalMicroseconds;
        Assert.Equal([query, $"{query}&cursor={replayFrom}", query], urls);
    }

    [Theory]
    [InlineData("""{"did":"did:plc:alice111","kind":"identity"}""")]
    [InlineData("""{"time_us":"soon"}""")]
    [InlineData("""[1725911166000000]""")]
    [InlineData("not json")]
    public void A_message_without_a_number_for_time_us_has_no_event_time(string json) => Assert.Null(ThreadgateDiffer.EventTime(json));
}
