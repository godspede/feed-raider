using System.Text.Json;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// What the page's pause menu reads of the feed log: one JSON array, newest first, each entry with the two buttons' states.
public class FeedLogJsonTests
{
    private static FeedPost Post(int n, string text = "plain words", string? name = "Name") =>
        new($"at://did:plc:t{n}/app.bsky.feed.post/{n}", $"cid{n}", $"did:plc:t{n}", $"t{n}.test", text, 1234, 7, 0, null, null, DisplayName: name);

    private static RaiderGame Game(bool signedIn = true, Grants? grants = null)
    {
        var g = new RaiderGame(3) { SignedIn = signedIn };
        if (grants is not null) g.Grants = grants;
        return g;
    }

    private static LogEntry Add(RaiderGame g, FeedPost p)
    {
        var e = new LogEntry(p, g.Book.MarksFor(p));
        g.Log.Add(e);
        return e;
    }

    private static JsonElement[] Read(RaiderGame g) => JsonDocument.Parse(FeedLogJson.Build(g)).RootElement.EnumerateArray().ToArray();

    [Fact]
    public void An_empty_log_is_an_empty_array()
    {
        Assert.Equal("[]", FeedLogJson.Build(Game()));
    }

    [Fact]
    public void Entries_come_newest_first_with_name_handle_text_and_the_counts_as_the_cards_write_them()
    {
        var g = Game();
        Add(g, Post(1)); Add(g, Post(2, "second post", name: null));
        var e = Read(g);
        Assert.Equal(2, e.Length);
        Assert.Equal("at://did:plc:t2/app.bsky.feed.post/2", e[0].GetProperty("uri").GetString());
        Assert.Equal("", e[0].GetProperty("name").GetString());
        Assert.Equal("t2.test", e[0].GetProperty("handle").GetString());
        Assert.Equal("second post", e[0].GetProperty("text").GetString());
        Assert.Equal("Name", e[1].GetProperty("name").GetString());
        Assert.Equal("1.2K", e[1].GetProperty("likeCount").GetString());
        Assert.Equal("7", e[1].GetProperty("repostCount").GetString());
    }

    [Fact]
    public void Each_button_says_its_state_in_a_word()
    {
        var g = Game();
        var a = Add(g, Post(1));
        a.Marks.Like = ButtonState.Lit;
        a.Marks.Repost = ButtonState.Pending;
        var b = Add(g, Post(2));
        b.Marks.Like = ButtonState.Marked;
        var e = Read(g);
        Assert.Equal("marked", e[0].GetProperty("like").GetString());
        Assert.Equal("off", e[0].GetProperty("repost").GetString());
        Assert.Equal("lit", e[1].GetProperty("like").GetString());
        Assert.Equal("pending", e[1].GetProperty("repost").GetString());
    }

    [Fact]
    public void A_button_the_sign_in_may_not_use_is_locked_when_signed_in_and_never_when_signed_out()
    {
        var g = Game(grants: new Grants(true, false, false, false));
        var a = Add(g, Post(1));
        a.Marks.Like = ButtonState.Lit;   // taking it back needs unlike
        var e = Read(g)[0];
        Assert.True(e.GetProperty("likeLocked").GetBoolean());
        Assert.True(e.GetProperty("repostLocked").GetBoolean());
        a.Marks.Like = ButtonState.Off;
        Assert.False(Read(g)[0].GetProperty("likeLocked").GetBoolean());

        var signedOut = Game(signedIn: false, grants: new Grants(false, false, false, false));
        Add(signedOut, Post(1));
        var s = Read(signedOut)[0];
        Assert.False(s.GetProperty("likeLocked").GetBoolean());
        Assert.False(s.GetProperty("repostLocked").GetBoolean());
    }

    [Fact]
    public void Text_the_terminal_cannot_draw_and_quotes_survive_as_valid_json()
    {
        var g = Game();
        Add(g, Post(1, "she said \"hi\"\nand left \\ \u0001"));
        var t = Read(g)[0].GetProperty("text").GetString()!;
        Assert.Contains("she said \"hi\"", t);
        Assert.DoesNotContain('\u0001', t);
    }

    [Fact]
    public void FeedLogJson_Toggle_names_the_action_and_ignores_a_post_that_is_not_there()
    {
        var g = Game(signedIn: false);
        var e = Add(g, Post(1));
        Assert.True(FeedLogJson.Toggle(g, e.Post.Uri, "like"));
        Assert.Equal(ButtonState.Marked, e.Marks.Like);
        Assert.True(FeedLogJson.Toggle(g, e.Post.Uri, "repost"));
        Assert.Equal(ButtonState.Marked, e.Marks.Repost);
        Assert.False(FeedLogJson.Toggle(g, e.Post.Uri, "share"));
        Assert.False(FeedLogJson.Toggle(g, "at://nobody/post/9", "like"));
    }
}
