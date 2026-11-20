namespace FeedRaider.Engine.Raider;

/// <summary>A post's picture as the cards draw it: <see cref="W"/> by <see cref="H"/> colours, row by row, already dimmed.</summary>
public sealed record CardPicture(int W, int H, uint[] Px);

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

    /// <summary>Asks the host for picture <c>index</c> of the post, downsampled to <c>width</c> by <c>height</c> pixels; answer
    /// with <see cref="SetPicture"/>. Arguments: the post, the index, the width, the height.</summary>
    public Action<FeedPost, int, int, int>? PictureWanted { get; set; }

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

    /// <summary>Asks for each picture of a new card, once, at the size of its box. A built-in picture is drawn here and now.</summary>
    private void WantPictures(Card card)
    {
        var boxes = card.Shape.Pictures;
        if (boxes.Count == 0 || card.Post.Pictures is not { } pictures) return;
        for (int i = 0; i < boxes.Count && i < pictures.Count; i++)
        {
            string thumb = pictures[i].Thumb;
            if (_pictures.ContainsKey(thumb) || !_pictureAsked.Add(thumb)) continue;
            int w = Math.Min(PictureMaxSide, boxes[i].W * CardField.PixelsPerCell), h = Math.Min(PictureMaxSide, boxes[i].H * CardField.PixelsPerCell);
            if (thumb.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
            {
                if (BuiltinPictures.Render(thumb[BuiltinPrefix.Length..], w, h) is { } px) SetPicture(thumb, w, h, px);
            }
            else PictureWanted?.Invoke(card.Post, i, w, h);
        }
    }
}
