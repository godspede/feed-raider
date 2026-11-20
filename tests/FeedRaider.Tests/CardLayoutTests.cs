using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class CardLayoutTests
{
    private const string SomeName = "Some Name", SomeHandle = "a.test", HandleMark = "@", Ellipsis = "…";
    // a handle and a name too long for any card
    private static readonly string LongHandle = new string('h', 80) + ".test", LongName = new('N', 90);
    // counts in the thousands, the widest a count gets on a card
    private const int ManyLikes = 1200, ManyReposts = 1500;
    private const string ManyLikesShort = "1.2K", ManyRepostsShort = "1.5K";

    private static FeedPost Post(string text = "hello", string? name = SomeName, AttachmentKind kind = AttachmentKind.None,
        string? note = null, int likes = 0, int reposts = 0, string handle = SomeHandle) =>
        new("at://did:plc:a/app.bsky.feed.post/1", "c", "did:plc:a", handle, text, likes, reposts, 0, null, null,
            DisplayName: name, Attachment: kind, AttachmentText: note, HasEmbed: kind != AttachmentKind.None);

    /// <summary>A text that wraps to exactly <paramref name="lines"/> lines of the given width (one 'word' per line).</summary>
    private static string Lines(int lines, int width) => string.Join(" ", Enumerable.Repeat(new string('x', width), lines));

    /// <summary>The cells of words on a card <paramref name="cols"/> wide: from the text column to one short of the right edge.</summary>
    private static int TextWidthOf(int cols) => cols - CardLayout.TextCol - 1;

    [Theory]
    [InlineData(NarrowestCard, 18)]
    [InlineData(LastHalvesCard, 31)]
    [InlineData(PhoneCard, 44)]
    [InlineData(DesktopCard, 52)]
    public void The_words_start_at_column_7_on_row_2_beside_the_avatar_and_are_the_card_less_8_wide_at_any_width(int cols, int textWidth)
    {
        var card = CardLayout.Measure(Post(), cols, false);
        Assert.Equal(CardLayout.TextCol, card.TextX);
        Assert.Equal(textWidth, card.TextWidth);
        Assert.Equal(CardLayout.TextCol, card.Lines[0].Col);
        Assert.Equal(card.Name.Row + 1, card.Lines[0].Row);
        Assert.True(card.Lines[0].Col >= card.Avatar.X + card.Avatar.W, "the words run under the avatar");
    }

    // the desktop card, the card of a 412 pixel phone and the narrowest card a field gives
    private const int DesktopCard = 60, PhoneCard = 52, NarrowestCard = 26;
    // a card cut in halves, the widest such, the narrowest cut in thirds, and one wider than the desktop's
    private const int NarrowCard = 30, LastHalvesCard = CardLayout.NarrowBelow - 1, FirstThirdsCard = CardLayout.NarrowBelow, WideCard = 80;
    private static readonly int PhoneTextWidth = TextWidthOf(PhoneCard);
    // a one-line post in the app is about four text lines tall; a card adds its top and bottom edge rows
    private const int AppPostLines = 4, EdgeRowsOfACard = 2;
    // rows of a card that are not words or a note: the top edge, the head row, the clear row, the action row and the bottom edge
    private const int RowsRoundTheWords = 5;

    [Theory]
    [InlineData(DesktopCard)]
    [InlineData(PhoneCard)]
    [InlineData(NarrowestCard)]
    public void A_one_line_post_is_as_tall_as_the_apps_post_and_the_two_edges(int cols)
    {
        var card = CardLayout.Measure(Post("hi"), cols, false);
        Assert.Single(card.Lines);
        Assert.Equal(AppPostLines + EdgeRowsOfACard, card.Rows);
        Assert.Equal(card.Avatar.Y + card.Avatar.H - 1, card.LikeIcon.Row);
    }

    [Theory]
    [InlineData(DesktopCard, 1, false, 6)]
    [InlineData(DesktopCard, 3, false, 8)]
    [InlineData(PhoneCard, 1, true, 7)]
    [InlineData(PhoneCard, 3, false, 8)]
    [InlineData(PhoneCard, 4, true, 10)]
    [InlineData(PhoneCard, 9, false, 14)]
    [InlineData(NarrowestCard, 3, false, 8)]
    [InlineData(NarrowestCard, 9, true, 15)]
    public void The_height_is_the_words_the_note_and_five_rows_round_them(int cols, int lines, bool note, int rows)
    {
        string words = Lines(lines, TextWidthOf(cols));
        var post = note ? Post(words, kind: AttachmentKind.Picture) : Post(words);
        var card = CardLayout.Measure(post, cols, false);
        Assert.Equal(lines, card.Lines.Count);
        Assert.Equal(rows, card.Rows);
        Assert.Equal(lines + (note ? 1 : 0) + RowsRoundTheWords, card.Rows);
    }

    [Theory]
    [InlineData(DesktopCard, 1)]
    [InlineData(DesktopCard, 3)]
    [InlineData(PhoneCard, 1)]
    [InlineData(PhoneCard, 3)]
    [InlineData(NarrowestCard, 5)]
    public void The_footer_is_three_rows_one_clear_the_action_row_and_the_edge(int cols, int lines)
    {
        var card = CardLayout.Measure(Post(Lines(lines, TextWidthOf(cols))), cols, false);
        int lastOfTheWords = card.Lines[^1].Row;
        Assert.Equal(lastOfTheWords + 1 + CardLayout.ActionAirRows, card.LikeIcon.Row);
        Assert.Equal(card.LikeIcon.Row, card.RepostIcon.Row);
        Assert.Equal(card.LikeIcon.Row + CardLayout.ActionRows + CardLayout.EdgeRows, card.Rows);
        Assert.Equal(CardLayout.FooterRows, card.Rows - 1 - lastOfTheWords);
        Assert.Equal(3, CardLayout.FooterRows);
    }

    [Theory]
    [InlineData(DesktopCard)]
    [InlineData(PhoneCard)]
    [InlineData(NarrowestCard)]
    public void An_icon_is_one_character_on_the_one_action_row_and_the_first_starts_at_the_words_left_edge(int cols)
    {
        var card = CardLayout.Measure(Post(), cols, false);
        Assert.Equal(1, CardLayout.ActionRows);
        Assert.Equal(CardLayout.HeartOff.ToString(), card.LikeIcon.Text);
        Assert.Equal(CardLayout.LoopMark.ToString(), card.RepostIcon.Text);
        Assert.Equal(1, CellWidth.Of(CardLayout.HeartOff));
        Assert.Equal(1, CellWidth.Of(CardLayout.HeartOn));
        Assert.Equal(1, CellWidth.Of(CardLayout.LoopMark));
        Assert.Equal(card.Lines[0].Col, card.LikeIcon.Col);
    }

    [Fact]
    public void Under_40_columns_the_action_row_is_cut_in_halves_and_from_the_phones_width_in_thirds()
    {
        const int halves = 2, thirds = 3;
        var narrow = CardLayout.Measure(Post("one two three"), NarrowCard, false);
        Assert.Equal(TextWidthOf(NarrowCard), narrow.TextWidth);
        Assert.Equal(narrow.TextX + narrow.TextWidth / halves, narrow.RepostIcon.Col);
        foreach (int cols in (int[])[PhoneCard, DesktopCard])
        {
            var card = CardLayout.Measure(Post(), cols, false);
            Assert.Equal(card.TextX + card.TextWidth / thirds, card.RepostIcon.Col);
        }
    }

    // each row: the card's width, the cells from one icon to the next, and the box's width in cells
    [Theory]
    [InlineData(NarrowestCard, 9, 3)]
    [InlineData(LastHalvesCard, 15, 9)]
    [InlineData(FirstThirdsCard, 16, 10)]
    [InlineData(PhoneCard, 14, 8)]
    [InlineData(DesktopCard, 17, 11)]
    [InlineData(WideCard, 24, 12)]
    public void A_buttons_box_is_the_footer_tall_round_its_icon_and_the_two_leave_room_for_the_ship_between_them(int cols, int pitch, int boxWidth)
    {
        var card = CardLayout.Measure(Post(likes: ManyLikes, reposts: ManyReposts), cols, false);
        Assert.Equal(pitch, card.RepostIcon.Col - card.LikeIcon.Col);
        foreach (var (box, icon) in new[] { (card.LikeButton, card.LikeIcon), (card.RepostButton, card.RepostIcon) })
        {
            Assert.Equal(boxWidth, box.W);
            Assert.Equal(CardLayout.FooterRows, box.H);
            Assert.Equal(card.Rows - CardLayout.FooterRows, box.Y);
            Assert.Equal(icon.Col - 1, box.X);
            Assert.Equal(box.Y + CardLayout.ActionAirRows, icon.Row);
            Assert.True(box.X >= 0 && box.X + box.W <= cols, $"width {cols}: the box leaves the card");
        }
        int gap = card.RepostButton.X - (card.LikeButton.X + card.LikeButton.W);
        Assert.True(gap >= CardLayout.ButtonGapCols, $"width {cols}: {gap} cells between the boxes");
        Assert.True(gap * CardField.PixelsPerCell > ShipFactory.W, "a ship between the boxes touches one");
    }

    [Fact]
    public void Text_wraps_on_spaces_and_a_word_longer_than_the_line_is_split()
    {
        const int splitWord = 100;
        var card = CardLayout.Measure(Post("aaaa bbbb cccc " + new string('z', splitWord)), PhoneCard, false);
        Assert.Equal("aaaa bbbb cccc", card.Lines[0].Text);
        Assert.All(card.Lines, line => Assert.True(line.Text.Length <= PhoneTextWidth));
        Assert.Equal(new string('z', PhoneTextWidth), card.Lines[1].Text);
        Assert.Equal(new string('z', PhoneTextWidth), card.Lines[2].Text);
        Assert.Equal(new string('z', splitWord - 2 * PhoneTextWidth), card.Lines[3].Text);
    }

    [Fact]
    public void A_wide_character_counts_two_cells_when_wrapping()
    {
        const int wideChars = 30;
        string wide = "中";   // two cells in xterm
        var card = CardLayout.Measure(Post(string.Concat(Enumerable.Repeat(wide, wideChars))), PhoneCard, false);
        int perLine = PhoneTextWidth / CellText.WideCells;
        Assert.Equal(perLine, card.Lines[0].Text.Length);
        Assert.Equal(wideChars - perLine, card.Lines[1].Text.Length);
    }

    [Fact]
    public void The_whole_text_is_kept_not_cut_short()
    {
        string text = string.Join(" ", Enumerable.Range(0, 80).Select(i => "w" + i));
        var card = CardLayout.Measure(Post(text), PhoneCard, false);
        Assert.Equal(text, string.Join(" ", card.Lines.Select(line => line.Text)));
    }

    [Fact]
    public void An_empty_display_name_puts_the_handle_in_the_names_place()
    {
        var card = CardLayout.Measure(Post(name: null), PhoneCard, false);
        Assert.Equal(HandleMark + SomeHandle, card.Name.Text);
        Assert.Equal(1, card.Name.Row);
        Assert.Equal("", card.Handle.Text);
        Assert.Equal("", CardLayout.Measure(Post(name: "   "), PhoneCard, false).Handle.Text);
    }

    [Fact]
    public void The_name_and_the_handle_share_the_head_row_and_the_handle_is_clipped_at_the_cards_edge()
    {
        var named = CardLayout.Measure(Post(), PhoneCard, false);
        Assert.Equal(SomeName, named.Name.Text);
        Assert.Equal(HandleMark + SomeHandle, named.Handle.Text);
        Assert.Equal(named.Name.Row, named.Handle.Row);
        Assert.Equal(named.Name.Col + SomeName.Length + 1, named.Handle.Col);

        var longHandle = CardLayout.Measure(Post(handle: LongHandle), PhoneCard, false);
        int edge = longHandle.TextX + longHandle.TextWidth;
        Assert.Equal(longHandle.Name.Row, longHandle.Handle.Row);
        Assert.StartsWith(HandleMark + "hhh", longHandle.Handle.Text);
        Assert.EndsWith(Ellipsis, longHandle.Handle.Text);
        Assert.Equal(edge, longHandle.Handle.Col + longHandle.Handle.Text.Length);

        var longName = CardLayout.Measure(Post(name: LongName), PhoneCard, false);
        Assert.Equal("", longName.Handle.Text);
    }

    [Fact]
    public void On_a_gold_card_the_name_and_handle_stop_short_of_the_star_at_the_right()
    {
        var card = CardLayout.Measure(Post(name: new string('N', 20), likes: Tuning.GoldLikes, handle: LongHandle), PhoneCard, false);
        var star = card.Star!.Value;
        Assert.Equal(card.TextX + card.TextWidth, star.Col + star.Text.Length);
        Assert.Equal(card.Name.Row, star.Row);
        Assert.EndsWith(Ellipsis, card.Handle.Text);
        Assert.Equal(star.Col - 1, card.Handle.Col + card.Handle.Text.Length);
    }

    private const string PlainName = "Pixel Pete";

    // U+1F17F is a squared P, drawn by browsers as an emoji, outside the Basic Multilingual Plane: it shows as a plain P, and the
    // variation selector after it and the pill emoji at the end, which no cell can hold, are dropped
    [Theory]
    [InlineData(PlainName)]
    [InlineData("\U0001F17F\uFE0Fixel Pete \U0001F48A")]
    public void The_name_on_the_card_loses_only_what_no_cell_can_hold(string name) =>
        Assert.Equal(PlainName, CardLayout.Measure(Post(name: name), PhoneCard, false).Name.Text);

    [Fact]
    public void The_name_is_clipped_to_the_text_column()
    {
        var card = CardLayout.Measure(Post(name: LongName), PhoneCard, false);
        Assert.Equal(PhoneTextWidth, card.Name.Text.Length);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(ManyLikes, ManyLikesShort)]
    [InlineData(34000, "34K")]
    [InlineData(34500, "34K")]
    [InlineData(1200000, "1.2M")]
    [InlineData(1000, "1K")]
    public void Counts_read_short(int count, string text) => Assert.Equal(text, CardLayout.ShortCount(count));

    [Theory]
    [InlineData(NarrowestCard)]
    [InlineData(NarrowCard)]
    [InlineData(LastHalvesCard)]
    [InlineData(FirstThirdsCard)]
    [InlineData(PhoneCard)]
    [InlineData(WideCard)]
    public void A_count_starts_one_space_after_its_icon_on_the_icons_row_and_ends_before_the_next_icon(int cardCols)
    {
        var card = CardLayout.Measure(Post(likes: ManyLikes, reposts: ManyReposts), cardCols, false);
        Assert.Equal(ManyLikesShort, card.LikeCount.Text);
        Assert.Equal(ManyRepostsShort, card.RepostCount.Text);
        foreach (var (icon, count) in new[] { (card.LikeIcon, card.LikeCount), (card.RepostIcon, card.RepostCount) })
        {
            Assert.Equal(icon.Row, count.Row);
            Assert.Equal(icon.Col + icon.Text.Length + 1, count.Col);
        }
        Assert.True(card.LikeCount.Col + card.LikeCount.Text.Length < card.RepostIcon.Col, $"width {cardCols}: the like count runs into the repost icon");
        Assert.True(card.RepostCount.Col + card.RepostCount.Text.Length < cardCols, $"width {cardCols}: the repost count runs off the card");
    }

    [Theory]
    [InlineData(PhoneCard)]
    [InlineData(DesktopCard)]
    public void From_the_phones_width_up_the_longest_count_ends_inside_its_buttons_box(int cardCols)
    {
        var card = CardLayout.Measure(Post(likes: ManyLikes, reposts: ManyReposts), cardCols, false);
        Assert.True(card.LikeCount.Col + card.LikeCount.Text.Length <= card.LikeButton.X + card.LikeButton.W);
        Assert.True(card.RepostCount.Col + card.RepostCount.Text.Length <= card.RepostButton.X + card.RepostButton.W);
    }

    [Fact]
    public void The_avatar_is_4_by_4_at_column_1_row_1() => Assert.Equal(new CardRect(1, 1, 4, 4), CardLayout.Measure(Post(), PhoneCard, false).Avatar);

    [Fact]
    public void The_attachment_note_is_one_clean_line_after_the_text()
    {
        const string picture = "a cat on a keyboard", link = "Title, example.com";
        var card = CardLayout.Measure(Post(kind: AttachmentKind.Picture, note: picture), PhoneCard, false);
        Assert.Equal($"[image: {picture}]", card.Note!.Value.Text);
        Assert.Equal(card.Lines[^1].Row + 1, card.Note!.Value.Row);
        Assert.Equal("[image]", CardLayout.Measure(Post(kind: AttachmentKind.Picture), PhoneCard, false).Note!.Value.Text);
        Assert.Equal("[video]", CardLayout.Measure(Post(kind: AttachmentKind.Video), PhoneCard, false).Note!.Value.Text);
        Assert.Equal($"[link: {link}]", CardLayout.Measure(Post(kind: AttachmentKind.Link, note: link), PhoneCard, false).Note!.Value.Text);
        Assert.Equal("[quote]", CardLayout.Measure(Post(kind: AttachmentKind.Quote), PhoneCard, false).Note!.Value.Text);
        Assert.Null(CardLayout.Measure(Post(), PhoneCard, false).Note);
        string longNote = CardLayout.Measure(Post(kind: AttachmentKind.Picture, note: new string('q', 200)), PhoneCard, false).Note!.Value.Text;
        Assert.Equal(PhoneTextWidth, longNote.Length);
        Assert.EndsWith(Ellipsis, longNote);
    }

    [Fact]
    public void A_thread_card_carries_the_thread_note_in_place_of_the_attachment_note()
    {
        var card = CardLayout.Measure(Post(kind: AttachmentKind.Picture), PhoneCard, true);
        Assert.Equal(CardLayout.ThreadNote, card.Note!.Value.Text);
    }
}
