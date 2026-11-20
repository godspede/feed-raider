using System.Text;
using System.Text.Json;

namespace FeedRaider.Engine.Raider;

/// <summary>The feed log as the page's pause menu reads it, and the pause menu's toggle: a JSON array, newest first, built by hand so the
/// trimmed browser build needs no serializer metadata for it.</summary>
public static class FeedLogJson
{
    /// <summary>Each entry: <c>uri</c>, <c>name</c> (empty when the post has none), <c>handle</c>, <c>text</c>, <c>like</c> and
    /// <c>repost</c> (<c>off</c>, <c>marked</c>, <c>pending</c> or <c>lit</c>), the two counts as a card writes them, and
    /// <c>likeLocked</c> / <c>repostLocked</c>: signed in, and the sign-in may not do what pressing the button would.</summary>
    public static string Build(RaiderGame game)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            var entries = game.Log.Entries;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                w.WriteStartObject();
                w.WriteString("uri", e.Post.Uri);
                w.WriteString("name", Plain(e.Post.DisplayName));
                w.WriteString("handle", Plain(e.Post.AuthorHandle));
                w.WriteString("text", Plain(e.Post.Text));
                w.WriteString("like", Word(e.Marks.Like));
                w.WriteString("repost", Word(e.Marks.Repost));
                w.WriteString("likeCount", CardPainter.CountText(e.Post, e.Marks, PostAction.Like));
                w.WriteString("repostCount", CardPainter.CountText(e.Post, e.Marks, PostAction.Repost));
                w.WriteBoolean("likeLocked", Locked(game, e.Marks.Like, PostAction.Like));
                w.WriteBoolean("repostLocked", Locked(game, e.Marks.Repost, PostAction.Repost));
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>The pause menu's button press: <paramref name="action"/> is <c>like</c> or <c>repost</c>. False for anything else.</summary>
    public static bool Toggle(RaiderGame game, string postUri, string action)
    {
        var a = action switch { "like" => PostAction.Like, "repost" => PostAction.Repost, _ => (PostAction?)null };
        if (a is null || game.Book.Find(postUri) is null) return false;
        game.Toggle(postUri, a.Value);
        return true;
    }

    private static string Word(ButtonState s) => s switch
    {
        ButtonState.Marked => "marked",
        ButtonState.Pending => "pending",
        ButtonState.Lit => "lit",
        _ => "off",
    };

    private static bool Locked(RaiderGame g, ButtonState s, PostAction a)
    {
        if (!g.SignedIn || s == ButtonState.Pending) return false;
        bool like = a == PostAction.Like;
        return s == ButtonState.Lit ? !(like ? g.Grants.Unlike : g.Grants.Unrepost) : !g.MayWrite(a);
    }

    /// <summary>Text with line breaks and tabs made spaces, its other control characters dropped, and nothing else changed.</summary>
    private static string Plain(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (!char.IsControl(c)) sb.Append(c);
            else if (c is '\n' or '\r' or '\t') sb.Append(' ');
        }
        return sb.ToString();
    }
}
