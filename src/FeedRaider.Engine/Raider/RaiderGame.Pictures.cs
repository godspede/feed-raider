namespace FeedRaider.Engine.Raider;

/// <summary>A post's picture as the cards draw it: <see cref="W"/> by <see cref="H"/> colours, row by row, already dimmed.</summary>
public sealed record CardPicture(int W, int H, uint[] Px);

/// <summary>Where the host reads a picture from: the owner's PDS by the CID its CDN address names (a post's picture, a link card's
/// thumbnail), or the video service by the owner's DID and the video's CID.</summary>
public enum PictureSource { Pds, VideoService }

/// <summary>One picture the game wants: the <see cref="Key"/> to answer it under (<see cref="RaiderGame.SetPicture"/>), where to read it,
/// the DID of the post's author it belongs to, its <see cref="Address"/> (the CDN address for <see cref="PictureSource.Pds"/>, the video's CID
/// for <see cref="PictureSource.VideoService"/>), and the size in pixels to downsample it to.</summary>
public readonly record struct PictureAsk(string Key, PictureSource Source, string Did, string Address, int W, int H);

/// <summary>
/// The pictures attached to the posts on the field. A card asks for each of its pictures once, as it is made above the top of the
/// field, at the size its layout drew a box for; the host answers with <see cref="SetPicture"/> when it has one, and until then,
/// or if it never does, the card shows the empty box. The game keeps the most recent <see cref="PictureCacheMax"/> of them.
/// </summary>
public sealed partial class RaiderGame
{
    /// <summary>How many pictures the game holds at once: several times what the field can show.</summary>
    public const int PictureCacheMax = 48;

    /// <summary>The longest side of a picture the game takes, in pixels.</summary>
    public const int PictureMaxSide = 64;

    /// <summary>The start of the address of a picture that is drawn from the game's own data (see <see cref="BuiltinPictures"/>).</summary>
    public const string BuiltinPrefix = "builtin:";

    /// <summary>The start of the key a video's thumbnail is held under: the video names no address the game reads.</summary>
    public const string VideoKeyPrefix = "video:";

    /// <summary>Asks the host for a picture of a card (a post's picture, a link card's thumbnail or a video's); answer with
    /// <see cref="SetPicture"/> under the ask's key.</summary>
    public Action<PictureAsk>? PictureWanted { get; set; }

    /// <summary>The key a video's thumbnail is held under: its author's DID and its CID, which is all the host builds the address from.</summary>
    public static string VideoKey(VideoRef v) => $"{VideoKeyPrefix}{v.Did}/{v.Cid}";

    private readonly Dictionary<string, CardPicture> _pictures = new(StringComparer.Ordinal);
    private readonly Queue<string> _pictureOrder = new();
    private readonly HashSet<string> _pictureAsked = new(StringComparer.Ordinal);

    /// <summary>The host's answer: the picture at this thumbnail address, as <paramref name="w"/> by <paramref name="h"/> colours.
    /// It is dimmed here, once, the way an avatar is. An answer of the wrong size is dropped.</summary>
    public void SetPicture(string thumb, int w, int h, uint[] px)
    {
        if (w < 1 || h < 1 || w > PictureMaxSide || h > PictureMaxSide || px.Length != w * h || _pictures.ContainsKey(thumb)) return;
        _pictures[thumb] = new CardPicture(w, h, CardPainter.Dim(px));
        _pictureOrder.Enqueue(thumb);
        while (_pictureOrder.Count > PictureCacheMax)
        {
            string old = _pictureOrder.Dequeue();
            _pictures.Remove(old);
            _pictureAsked.Remove(old);   // a later card of that post asks again
        }
    }

    /// <summary>The picture at this thumbnail address, or null while it has not arrived.</summary>
    public CardPicture? PictureOf(string thumb) => _pictures.GetValueOrDefault(thumb);

    /// <summary>Asks for each picture of a new card, once, at the size of its box: its pictures, its link card's thumbnail and its
    /// video's. Media the card shows for its quote belong to the quoted author, so the ask names that author.</summary>
    private void WantPictures(Card card)
    {
        var shape = card.Shape;
        var media = shape.Media;
        string? owner = media.Quoted ? card.Post.Quote?.AuthorDid : card.Post.AuthorDid;
        if (owner is not null && media.Pictures is { } pictures)
            for (int i = 0; i < shape.Pictures.Count && i < pictures.Count; i++) WantPicture(pictures[i].Thumb, PictureSource.Pds, owner, pictures[i].Thumb, shape.Pictures[i]);
        if (owner is not null && shape.Link is { Thumb: { } thumb } && media.Link?.ThumbUrl is { } url) WantPicture(url, PictureSource.Pds, owner, url, thumb);
        if (shape.Video is { } box && media.Video is { } video) WantPicture(VideoKey(video), PictureSource.VideoService, video.Did, video.Cid, box.Picture);
    }

    /// <summary>Asks for one picture unless it is held or already asked for. A built-in one (its address starts with
    /// <see cref="BuiltinPrefix"/>) is drawn here and now.</summary>
    private void WantPicture(string key, PictureSource source, string did, string address, CardRect box)
    {
        if (_pictures.ContainsKey(key) || !_pictureAsked.Add(key)) return;
        int w = Math.Min(PictureMaxSide, box.W * CardField.PixelsPerCell), h = Math.Min(PictureMaxSide, box.H * CardField.PixelsPerCell);
        if (address.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            if (BuiltinPictures.Render(address[BuiltinPrefix.Length..], w, h) is { } px) SetPicture(key, w, h, px);
        }
        else PictureWanted?.Invoke(new PictureAsk(key, source, did, address, w, h));
    }
}
