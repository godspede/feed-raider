using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class JetstreamParserTests
{
    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parses_plain_post_commit()
    {
        Assert.True(JetstreamParser.TryParse(Fixture("post.json"), out var e));
        Assert.Equal("did:plc:alice111", e!.Did);
        Assert.Equal("hello from the room", e.Text);
        Assert.Null(e.ReplyParentDid);
        Assert.Equal(1725911162329308, e.TimeUs);
    }

    [Fact]
    public void Reply_takes_parent_did_not_root_did()
    {
        Assert.True(JetstreamParser.TryParse(Fixture("reply.json"), out var e));
        Assert.Equal("did:plc:bob22222", e!.ReplyParentDid);
    }

    [Theory]
    [InlineData("delete.json")]
    [InlineData("like.json")]
    [InlineData("identity.json")]
    public void Ignores_everything_that_is_not_a_new_post(string fixture)
    {
        Assert.False(JetstreamParser.TryParse(Fixture(fixture), out var e));
        Assert.Null(e);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Garbage_is_not_a_post(string json) => Assert.False(JetstreamParser.TryParse(json, out _));

    [Theory]
    [InlineData("at://did:plc:abc/app.bsky.feed.post/3k", "did:plc:abc")]
    [InlineData("at://did:web:example.com", "did:web:example.com")]
    [InlineData("at://alice.example.com/app.bsky.feed.post/3k", null)]
    [InlineData("https://x", null)]
    [InlineData(null, null)]
    public void DidFromAtUri_cases(string? uri, string? expected) => Assert.Equal(expected, JetstreamParser.DidFromAtUri(uri));

    /// <summary>A create-post commit with the given raw JSON for the did and the text fields.</summary>
    private static string Commit(string did, string text) =>
        "{\"did\":" + did + ",\"time_us\":1,\"kind\":\"commit\",\"commit\":{\"operation\":\"create\",\"collection\":\"app.bsky.feed.post\",\"record\":{\"text\":" + text + "}}}";

    [Theory]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[\"did:plc:abc\"]")]
    public void A_did_that_is_not_a_string_is_not_a_post(string did) => Assert.False(JetstreamParser.TryParse(Commit(did, "\"hi\""), out _));

    [Theory]
    [InlineData("5")]
    [InlineData("false")]
    [InlineData("{\"a\":1}")]
    [InlineData("[\"hi\"]")]
    public void A_text_that_is_not_a_string_reads_as_empty(string text)
    {
        Assert.True(JetstreamParser.TryParse(Commit("\"did:plc:abc\"", text), out var e));
        Assert.Equal("", e!.Text);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"create\"")]
    [InlineData("[]")]
    public void A_commit_that_is_not_an_object_is_not_a_post(string commit) =>
        Assert.False(JetstreamParser.TryParse("{\"did\":\"did:plc:abc\",\"kind\":\"commit\",\"commit\":" + commit + "}", out _));

    [Theory]
    [InlineData("\"operation\":5")]
    [InlineData("\"operation\":true")]
    [InlineData("\"collection\":5")]
    [InlineData("\"collection\":{}")]
    public void An_operation_or_collection_that_is_not_a_string_is_not_a_post(string bad)
    {
        string json = Fixture("post.json");
        string field = bad.StartsWith("\"operation\"") ? "\"operation\":\"create\"" : "\"collection\":\"app.bsky.feed.post\"";
        Assert.True(JetstreamParser.TryParse(json, out _));
        Assert.False(JetstreamParser.TryParse(json.Replace(field, bad), out _));
    }

    [Fact]
    public void The_text_is_kept_verbatim_with_its_spaces()
    {
        Assert.True(JetstreamParser.TryParse(Commit("\"did:plc:abc\"", "\"  padded \""), out var e));
        Assert.Equal("  padded ", e!.Text);
    }

    [Fact]
    public void A_reply_uri_that_is_not_a_string_has_no_parent()
    {
        string json = Fixture("reply.json");
        Assert.True(JetstreamParser.TryParse(json, out var plain));
        Assert.NotNull(plain!.ReplyParentDid);
        string bad = json.Replace("\"at://did:plc:bob22222/app.bsky.feed.post/3l3parent\"", "5");
        Assert.NotEqual(json, bad);
        Assert.True(JetstreamParser.TryParse(bad, out var e));
        Assert.Null(e!.ReplyParentDid);
    }
}
