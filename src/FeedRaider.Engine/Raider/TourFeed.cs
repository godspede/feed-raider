namespace FeedRaider.Engine.Raider;

/// <summary>Invented posts for the start page's tour: no network, no account. They are not real posts and name no real
/// accounts. Some carry pictures, which are built in too (<see cref="BuiltinPictures"/>). Each page carries a fresh set of addresses, so the post queue (which skips an address it has seen) never runs dry.
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
            posts.Add(new FeedPost($"tour://{handle}/{page}-{i}", "tour" + page + "-" + i, Did(handle), handle, text, likes, 0, reply is null ? 0 : 1, null, null,
                HasEmbed: pictures.Length > 0, Attachment: pictures.Length > 0 ? AttachmentKind.Picture : AttachmentKind.None,
                Pictures: pictures.Length > 0 ? [.. pictures.Select(BuiltinPictures.Picture)] : null));
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
