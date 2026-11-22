namespace FeedRaider.Engine.Raider;

/// <summary>Invented posts for the start page's tour: no network, no account. They are not real posts and name no real
/// accounts. Some carry pictures, a link card, a video or a quote, whose pictures are built in too (<see cref="BuiltinPictures"/>). Each page carries a fresh set of addresses, so the post queue (which skips an address it has seen) never runs dry.
/// The first post of the first page has enough likes to be a gold card and an invented reply by its own author, so the like
/// the tour's pilot makes pays a bomb and brings in a thread card.</summary>
public sealed class TourFeedSource : IFeedSource, IThreadSource
{
    private static readonly (string Handle, string Text, int Likes, string? Reply, string[] Pictures)[] Samples =
    [
        ("pixel.example", "the feed scrolls faster than I can read it and somehow that is the whole appeal", 2400, "and yes, I did scroll past this one twice before I noticed it was mine", []),
        ("moss.example", "tried to take a short break from posting and wrote four drafts about it", 12, null, []),
        ("kettle.example", "hot take: the second cup of tea is always the one you actually taste", 87, null, ["tea"]),
        ("orbit.example", "reminder that every notification is a very small tap on the shoulder", 31, "this one included, sorry", []),
        ("lichen.example", "my plant has more followers than I do and it has never once posted", 640, null, ["plant", "hill"]),
        ("static.example", "somebody please explain why the quiet hours are when the best ideas arrive", 5, null, []),
        ("ferry.example", "caught the early boat and the whole harbour smelled like toast and diesel", 1100, null, ["harbour", "toast", "tea", "hill"]),
        ("quill.example", "rewrote the same sentence nine times and the first one was right", 48, null, []),
    ];

    /// <summary>The samples that carry a link card or a video, by handle: the link with a built-in thumbnail, and the name of the built-in
    /// picture that is the video's thumbnail.</summary>
    private static readonly Dictionary<string, (LinkCard? Link, string? Video)> Attached = new(StringComparer.Ordinal)
    {
        ["moss.example"] = (new LinkCard("How to take a break from posting, and keep it", "notes.example",
            "Seven small habits for a quieter feed", RaiderGame.BuiltinPrefix + "desk"), null),
        ["static.example"] = (null, "street"),
    };

    /// <summary>The samples that quote a post, by handle: one quotes an invented post with a built-in picture, drawn in its quote box,
    /// and one quotes a post that is gone, whose notice turns to mines.</summary>
    private static readonly Dictionary<string, PostQuote> Quoted = new(StringComparer.Ordinal)
    {
        ["quill.example"] = new PostQuote(QuoteState.Shown, "Wren", "wren.example", "first drafts are just the second draft being shy",
            true, null, Pictures: [BuiltinPictures.Picture("desk")], AuthorDid: Did("wren.example")),
        ["orbit.example"] = new PostQuote(QuoteState.Deleted, null, null, "", false, null),
    };

    /// <summary>What a reply's address ends in, after the address of the post it answers.</summary>
    private const string ReplySuffix = "/reply";

    public const int PerPage = 8;
    /// <summary>How far each page moves along the samples: less than a page, so neighbouring pages overlap but start on different posts.</summary>
    private const int SampleStridePerPage = 3;

    public Task<FeedPage> GetPageAsync(string feedUri, string? cursor, int limit, CancellationToken ct)
    {
        int page = int.TryParse(cursor, out var c) ? c : 0;
        var posts = new List<FeedPost>();
        for (int i = 0; i < Math.Min(limit, PerPage); i++)
        {
            var (handle, text, likes, reply, pictures) = Samples[(page * SampleStridePerPage + i) % Samples.Length];
            var (link, video) = Attached.GetValueOrDefault(handle);
            var quote = Quoted.GetValueOrDefault(handle);
            var kind = quote is not null ? AttachmentKind.Quote : pictures.Length > 0 ? AttachmentKind.Picture : link is not null ? AttachmentKind.Link
                : video is not null ? AttachmentKind.Video : AttachmentKind.None;
            posts.Add(new FeedPost($"tour://{handle}/{page}-{i}", "tour" + page + "-" + i, Did(handle), handle, text, likes, 0, reply is null ? 0 : 1, null, null,
                HasEmbed: kind != AttachmentKind.None, Attachment: kind,
                Pictures: pictures.Length > 0 ? [.. pictures.Select(BuiltinPictures.Picture)] : null,
                Link: link, Video: video is null ? null : BuiltinPictures.Video(Did(handle), video), Quote: quote));
        }
        return Task.FromResult(new FeedPage(posts, (page + 1).ToString()));
    }

    /// <summary>The invented reply to a built-in post by its own author, or null when the sample has none. A reply has no reply
    /// of its own.</summary>
    public Task<FeedPost?> NextAsync(FeedPost post, CancellationToken ct)
    {
        string? reply = post.ReplyParent is null ? Array.Find(Samples, s => s.Handle == post.AuthorHandle).Reply : null;
        return Task.FromResult(reply is null ? null : new FeedPost(post.Uri + ReplySuffix, post.Cid + ReplySuffix, Did(post.AuthorHandle), post.AuthorHandle, reply, 0, 0, 0, post.Ref, post.Ref));
    }

    private static string Did(string handle) => "did:tour:" + handle;
}
