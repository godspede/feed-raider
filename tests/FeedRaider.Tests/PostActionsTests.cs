using FeedRaider.Engine;

namespace FeedRaider.Tests;

// The page's write calls as the engine sees them: like, repost and delete through JS functions that answer with JSON.
public class PostActionsTests
{
    private static readonly PostRef Post = new("at://did:plc:a/app.bsky.feed.post/1", "cidp");

    [Fact]
    public async Task A_repost_passes_the_post_uri_and_cid_and_returns_the_new_records_address()
    {
        var sent = new List<(string, string)>();
        IPostActions actions = new JsActions((_, _) => throw new InvalidOperationException(),
            (uri, cid) => { sent.Add((uri, cid)); return Task.FromResult("""{"uri":"at://did:plc:me/app.bsky.feed.repost/9","cid":"cr"}"""); },
            _ => throw new InvalidOperationException());
        var made = await actions.RepostAsync(Post, default);
        Assert.Equal([(Post.Uri, Post.Cid)], sent);
        Assert.Equal(new PostRef("at://did:plc:me/app.bsky.feed.repost/9", "cr"), made);
    }

    [Fact]
    public async Task A_delete_passes_the_records_address_and_nothing_else()
    {
        var sent = new List<string>();
        IPostActions actions = new JsActions((_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(),
            rec => { sent.Add(rec); return Task.CompletedTask; });
        await actions.DeleteAsync("at://did:plc:me/app.bsky.feed.like/3k", default);
        Assert.Equal(["at://did:plc:me/app.bsky.feed.like/3k"], sent);
    }

    [Fact]
    public async Task A_like_still_goes_through_the_like_function()
    {
        IPostActions actions = new JsActions((uri, cid) => Task.FromResult($$"""{"uri":"at://l/{{uri.Length}}","cid":"{{cid}}"}"""),
            (_, _) => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        var made = await actions.LikeAsync(Post, default);
        Assert.Equal("cidp", made.Cid);
    }

    [Theory]
    [InlineData(PostAction.Like, false, "Missing required scope", "your sign-in does not allow likes; sign in again")]
    [InlineData(PostAction.Repost, false, "sign in again: insufficient scope", "your sign-in does not allow reposts; sign in again")]
    [InlineData(PostAction.Like, true, "insufficient scope", "your sign-in does not allow undoing likes; sign in again")]
    [InlineData(PostAction.Repost, true, "sign in again", "your sign-in does not allow undoing reposts; sign in again")]
    public void A_refusal_for_missing_scope_reads_as_sign_in_again(PostAction action, bool undo, string raw, string said) =>
        Assert.Equal(said, Plain.WriteFailureText(new Exception(raw), action, undo));

    [Fact]
    public void Other_failures_get_plain_words_for_each_action()
    {
        Assert.Equal("you are signed out", Plain.WriteFailureText(new Exception("not logged in"), PostAction.Repost, false));
        Assert.Equal("too many reposts; wait a moment", Plain.WriteFailureText(new Exception("429 rate limit"), PostAction.Repost, false));
        Assert.Equal("too many likes; wait a moment", Plain.WriteFailureText(new Exception("ratelimit"), PostAction.Like, true));
        Assert.Equal("could not reach Bluesky", Plain.WriteFailureText(new Exception("Failed to fetch"), PostAction.Like, false));
        Assert.Equal("boom", Plain.WriteFailureText(new Exception("boom"), PostAction.Repost, true));
    }

    [Fact]
    public void Grants_say_what_a_session_may_do()
    {
        Assert.Equal(new Grants(true, true, true, true), Grants.All);
        Assert.Equal(new Grants(false, false, false, false), Grants.None);
    }

    private sealed class RecordingActions : IPostActions
    {
        public readonly List<string> Calls = [];

        public Task<PostRef> LikeAsync(PostRef post, CancellationToken ct)
        {
            Calls.Add("like " + post.Uri + " " + post.Cid);
            return Task.FromResult(new PostRef("at://me/like/1", "cid-like"));
        }

        public Task<PostRef> RepostAsync(PostRef post, CancellationToken ct)
        {
            Calls.Add("repost " + post.Uri + " " + post.Cid);
            return Task.FromResult(new PostRef("at://me/repost/1", "cid-repost"));
        }

        public Task DeleteAsync(string recordUri, CancellationToken ct) => throw new InvalidOperationException();
    }

    private static FeedPost Loaded(int n) => new($"at://did:plc:t/app.bsky.feed.post/{n}", $"cid{n}", "did:plc:t", "t.test", "words", 0, 0, 0, null, null);

    [Fact]
    public async Task A_repost_by_uri_sends_a_repost_of_that_post_and_not_a_like()
    {
        var actions = new RecordingActions();
        var writes = new PostWrites(actions);
        writes.Remember([Loaded(1), Loaded(2), Loaded(3)]);
        await writes.RepostAsync("at://did:plc:t/app.bsky.feed.post/2", default);
        Assert.Equal(["repost at://did:plc:t/app.bsky.feed.post/2 cid2"], actions.Calls);
    }

    [Fact]
    public async Task A_repost_by_uri_answers_with_the_new_records_address_and_cid()
    {
        var writes = new PostWrites(new RecordingActions());
        writes.Remember([Loaded(1)]);
        var made = await writes.RepostAsync(Loaded(1).Uri, default);
        Assert.Equal(new PostRef("at://me/repost/1", "cid-repost"), made);
    }

    [Fact]
    public async Task A_like_by_uri_sends_a_like_of_that_post_and_answers_with_the_new_record()
    {
        var actions = new RecordingActions();
        var writes = new PostWrites(actions);
        writes.Remember([Loaded(1), Loaded(2)]);
        var made = await writes.LikeAsync(Loaded(2).Uri, default);
        Assert.Equal(["like at://did:plc:t/app.bsky.feed.post/2 cid2"], actions.Calls);
        Assert.Equal(new PostRef("at://me/like/1", "cid-like"), made);
    }

    [Fact]
    public async Task A_post_the_page_has_not_loaded_is_refused_and_nothing_is_sent()
    {
        var actions = new RecordingActions();
        var writes = new PostWrites(actions);
        writes.Remember([Loaded(1)]);
        await Assert.ThrowsAsync<ArgumentException>(() => writes.RepostAsync(Loaded(9).Uri, default));
        await Assert.ThrowsAsync<ArgumentException>(() => writes.LikeAsync(Loaded(9).Uri, default));
        Assert.Empty(actions.Calls);
    }
}
