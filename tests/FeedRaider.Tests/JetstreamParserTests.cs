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
}
