using System.Text.Json;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class BskyClientTests
{
    [Fact]
    public void Parses_a_follows_page_with_cursor()
    {
        var (follows, cursor) = BskyClient.ParseFollows(JetstreamParserTests.Fixture("follows.json"));
        Assert.Equal("abc123", cursor);
        Assert.Equal([new Follow("did:plc:alice111", "alice.test"), new Follow("did:plc:bob22222", "bob.test")], follows);
    }

    [Fact]
    public void Jetstream_url_asks_for_posts_from_the_given_dids_only()
    {
        string url = JetstreamSource.BuildUrl(JetstreamSource.DefaultEndpoint, ["did:plc:a", "did:plc:b"]);
        Assert.Equal("wss://jetstream2.us-east.bsky.network/subscribe?wantedCollections=app.bsky.feed.post&wantedDids=did%3Aplc%3Aa&wantedDids=did%3Aplc%3Ab", url);
    }

    [Fact]
    public void Options_update_carries_the_same_filter()
    {
        using var doc = JsonDocument.Parse(JetstreamSource.BuildOptionsUpdate(["did:plc:a"]));
        Assert.Equal("options_update", doc.RootElement.GetProperty("type").GetString());
        var p = doc.RootElement.GetProperty("payload");
        Assert.Equal("app.bsky.feed.post", p.GetProperty("wantedCollections")[0].GetString());
        Assert.Equal("did:plc:a", p.GetProperty("wantedDids")[0].GetString());
    }
}
