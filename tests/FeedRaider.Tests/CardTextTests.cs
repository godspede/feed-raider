using System.Text.RegularExpressions;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

// Card text: a card's words are cells written over its pixels, but only where nothing the fight drew
// touched the cell. Each test draws through the view's FitAndRender, as CardPixelsTests does.
public class CardTextTests
{
    private const int ScrCols = CardRig.ScrCols, ScrRows = CardRig.ScrRows;
    private const uint Slab = CardColours.Slab;
    /// <summary>The seed of the speech box scene, one under which a speech box opens within <see cref="SpeechBoxWaitSteps"/>.</summary>
    private const int SpeechSeed = 3;
    /// <summary>The spam post's own seed; any seed serves.</summary>
    private const ulong SpamPostSeed = 5;
    /// <summary>A card top on the field, clear of the HUD.</summary>
    private const int OnFieldCardTop = 8;
    /// <summary>A card top on the field for the row step test, with the row below it on the field too.</summary>
    private const int RowStepCardTop = 6;
    /// <summary>A card top above the screen, so the card's first rows pass under the HUD.</summary>
    private const int CardTopUnderHud = -2;
    /// <summary>The pop-up's words, sought on the row it is drawn on.</summary>
    private const string PopupText = "+250";
    /// <summary>The test post's like and repost counts; the likes are past a thousand so the card abbreviates them.</summary>
    private const int PostLikes = 1234, PostReposts = 34;
    /// <summary>Fewer letters than the test post shows, so a check over every letter is never a check over a handful.</summary>
    private const int MinCardLetters = 80;
    /// <summary>Where a shot sits from a cell's pixel (<see cref="PixelOf"/>) so that its sprite lands in that cell.</summary>
    private const double ShotOffsetX = -1, ShotOffsetY = 1;
    /// <summary>The cells from a speech box's left edge to its words: half of what the box adds across.</summary>
    private const int BoxTextInset = EnemySpeechBox.TextSide / 2;
    /// <summary>The rows from a speech box's top edge to its first line: the edge and the title, the chrome less the bottom edge.</summary>
    private const int BoxFirstTextRow = EnemySpeechBox.Chrome - 1;
    /// <summary>The name every test post is shown under.</summary>
    private const string DisplayName = "Display Name";
    /// <summary>Frames past the full reveal of the box's first page, so every word of it is typed.</summary>
    private const int RevealSettleFrames = 2;
    /// <summary>Rows the card's top sits above the speech box, so the box lies over the card's text.</summary>
    private const int CardAboveBoxRows = 2;
    /// <summary>A screen row clear of the HUD, so the letter the pop-up is put on lies on the field.</summary>
    private const int BelowHudRow = 4;
    /// <summary>A flash longer than any the game sets, beside the one-frame flash, so both ends are covered.</summary>
    private const int LongFlashFrames = 6;
    /// <summary>Cells right of a wide character past which a shot on either of its cells touches nothing.</summary>
    private const int WideShotReachCells = 3;
    /// <summary>The most steps spent waiting for the card to step down a row.</summary>
    private const int RowStepWaitSteps = 200;
    /// <summary>Steps taken with no row step between them, for the encoder test.</summary>
    private const int QuietSteps = 3;
    /// <summary>A pop-up's colour; any colour serves.</summary>
    private const uint PopupColour = 0xffee88;
    /// <summary>A pop-up's life in frames, any that keeps it on screen for the frame drawn.</summary>
    private const int PopupLife = 30;
    /// <summary>The most steps spent waiting for the game to open an enemy's speech box.</summary>
    private const int SpeechBoxWaitSteps = 1500;

    /// <summary>Three wide characters, each taking two cells.</summary>
    private const string Cjk = "日本語";

    /// <summary>The id of the one post each test puts on the field, which its links and handle are built from.</summary>
    private const int PostId = 1;

    private static FeedPost Post(string? text = null, bool liked = false) =>
        new($"at://did:plc:t{PostId}/app.bsky.feed.post/{PostId}", $"cid{PostId}", $"did:plc:t{PostId}", $"t{PostId}.test",
            text ?? "the first words of a post that is long enough to wrap onto a second line and then a third one besides, ending tail" + PostId,
            PostLikes, PostReposts, 0, null, null, DisplayName: DisplayName, Attachment: AttachmentKind.Picture, AttachmentText: "a cat on a keyboard", AlreadyLiked: liked);

    /// <summary>A game with one card on the field and nothing else on it, the ship parked off the left edge.</summary>
    private static (RaiderGame Game, Card Card) Scene(FeedPost? post = null, int top = CardRig.DefaultCardTop)
    {
        var game = CardRig.Game();
        game.Posts.Offer(post ?? Post());
        game.Step();
        CardRig.ClearAndParkShip(game);
        var card = game.Cards.Cards[0];
        card.Top = top;
        return (game, card);
    }

    /// <summary>The screen and the field pixels the view drew it from.</summary>
    private static (Screen Scr, Surface Field) Draw(RaiderGame game)
    {
        var scr = new Screen(ScrCols, ScrRows);
        var view = new RaiderView();
        view.FitAndRender(game, scr);
        return (scr, view.Field!);
    }

    private readonly record struct Letter(int Col, int Row, char Ch, uint Fg);

    /// <summary>Every visible char of the card, with the cell it belongs in (the card moved by (colShift, rowShift) cells) and its colour.</summary>
    private static List<Letter> Expected(RaiderGame game, Card card, int colShift = 0, int rowShift = 0)
    {
        var letters = new List<Letter>();
        int left = game.Layout.Col0 + game.Cards.CardLeft + colShift, top = card.Top + rowShift;
        void Run(CardText run, uint colour)
        {
            int col = left + run.Col, row = top + run.Row;
            foreach (char ch in run.Text)
            {
                if (ch != ' ' && row >= 0 && row < ScrRows) letters.Add(new Letter(col, row, ch, colour));
                col += CellWidth.Of(ch);
            }
        }
        var shape = card.Shape;
        Run(shape.Name, CardColours.NameText);
        Run(shape.Handle, CardColours.DimText);
        foreach (var line in shape.Lines) Run(line, CardColours.PostText);
        if (shape.Note is { } note) Run(note, CardColours.DimText);
        Run(CardPainter.IconAt(card, PostAction.Like), CardPainter.IconColour(card.Marks.Like, PostAction.Like, game.Frame));
        Run(CardPainter.IconAt(card, PostAction.Repost), CardPainter.IconColour(card.Marks.Repost, PostAction.Repost, game.Frame));
        Run(shape.LikeCount, card.Post.AlreadyLiked ? CardColours.LikeCountLit : CardColours.DimText);
        Run(shape.RepostCount, card.Post.AlreadyReposted ? CardColours.RepostCountLit : CardColours.DimText);
        return letters;
    }

    private static bool Shown(Screen scr, Letter letter) => scr.CharAt(letter.Col, letter.Row) == letter.Ch;

    /// <summary>The cells (column, row) of the screen in which the two fields differ by a pixel.</summary>
    private static HashSet<(int Col, int Row)> Touched(Surface bare, Surface drawn, int col0)
    {
        var cells = new HashSet<(int, int)>();
        for (int at = 0; at < bare.P.Length; at++)
            if (bare.P[at] != drawn.P[at]) cells.Add((col0 + at % bare.W / CardField.PixelsPerCell, at / bare.W / CardField.PixelsPerCell));
        return cells;
    }

    /// <summary>A letter from the middle of the card's first line of post text.</summary>
    private static Letter Target(RaiderGame game, Card card)
    {
        var line = Expected(game, card).Where(letter => letter.Row == card.Top + card.Shape.Lines[0].Row).ToList();
        return line[line.Count / 2];
    }

    /// <summary>The field pixel half a cell in from a screen cell's top left corner.</summary>
    private static (double X, double Y) PixelOf(RaiderGame game, int col, int row) =>
        (CardField.PixelsPerCell * (col - game.Layout.Col0) + CardField.PixelsPerCell / 2, CardField.PixelsPerCell * row + CardField.PixelsPerCell / 2);

    private static bool HasShot(Screen scr, int col, int row)
    {
        int at = row * ScrCols + col;
        return scr.Fg[at] == Palette.Ice || scr.Bg[at] == Palette.Ice || scr.Fg[at] == Palette.White || scr.Bg[at] == Palette.White;
    }

    // ---------------------------------------------------------------- shown

    [Fact]
    public void A_card_with_nothing_over_it_shows_its_name_handle_lines_note_and_both_counts_in_the_cells_the_layout_names()
    {
        var (game, card) = Scene();
        var (scr, _) = Draw(game);
        var shape = card.Shape;
        Assert.True(shape.Lines.Count >= 2 && shape.Note is not null && shape.Name.Text.Length > 0 && shape.Handle.Text.Length > 0);
        Assert.Equal("1.2K", shape.LikeCount.Text);
        var all = Expected(game, card);
        Assert.True(all.Count > MinCardLetters);
        foreach (var letter in all)
        {
            Assert.True(Shown(scr, letter), $"'{letter.Ch}' is missing at {letter.Col},{letter.Row}");
            Assert.Equal(letter.Fg, scr.Fg[letter.Row * ScrCols + letter.Col]);
            Assert.Equal(Slab, scr.BgAt(letter.Col, letter.Row));
        }
        string Row(CardText run) => new(Enumerable.Range(0, run.Text.Length).Select(offset => scr.CharAt(game.Layout.Col0 + game.Cards.CardLeft + run.Col + offset, card.Top + run.Row)).ToArray());
        Assert.Equal(DisplayName, Row(shape.Name));
        Assert.Equal($"@t{PostId}.test", Row(shape.Handle));
        Assert.Equal(shape.Note!.Value.Text, Row(shape.Note.Value));
        Assert.Equal(PostReposts.ToString(), Row(shape.RepostCount));
    }

    [Fact]
    public void A_lit_buttons_icon_and_count_are_written_in_the_colour_made_legible_on_its_ground()
    {
        var (game, card) = Scene(Post(liked: true));
        var (scr, _) = Draw(game);
        var (col, cols) = CardPainter.ActionSpan(card, PostAction.Like);
        int first = game.Layout.Col0 + game.Cards.CardLeft + col;
        var like = Expected(game, card).Where(letter => letter.Row == card.Top + card.Shape.LikeCount.Row && letter.Col >= first && letter.Col < first + cols).ToList();
        Assert.Equal(card.Shape.LikeIcon.Text.Length + card.Shape.LikeCount.Text.Length, like.Count);
        foreach (var letter in like)
        {
            Assert.True(Shown(scr, letter));
            Assert.Equal(CardColours.LikeCountLit, scr.Fg[letter.Row * ScrCols + letter.Col]);
            Assert.Equal(CardColours.LikeButtonLit, scr.BgAt(letter.Col, letter.Row));
        }
    }

    // ---------------------------------------------------------------- covered

    [Fact]
    public void A_shot_crossing_a_text_cell_shows_the_shot_and_no_letter_there_and_the_next_frame_without_it_the_letter_is_back()
    {
        var (game, card) = Scene();
        var target = Target(game, card);
        var (x, y) = PixelOf(game, target.Col, target.Row);
        game.Bullets.Add(new Bullet { X = x + ShotOffsetX, Y = y + ShotOffsetY, Friendly = true });
        var (scr, _) = Draw(game);
        Assert.False(Shown(scr, target), "the letter shows under the shot");
        Assert.True(HasShot(scr, target.Col, target.Row), "the shot is not in the cell");
        Assert.All(Expected(game, card).Where(letter => Math.Abs(letter.Col - target.Col) > 1), letter => Assert.True(Shown(scr, letter), $"'{letter.Ch}' at {letter.Col},{letter.Row} is gone with a shot a cell away"));
        game.Bullets.Clear();
        (scr, _) = Draw(game);
        Assert.True(Shown(scr, target));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void No_cell_under_the_ships_sprite_carries_a_letter_wherever_on_a_card_the_ship_sits(int where)
    {
        var (game, card) = Scene();
        var (_, bare) = Draw(game);
        var all = Expected(game, card);
        var anchor = where switch
        {
            0 => all.First(),
            1 => Target(game, card),
            2 => all.Last(letter => letter.Row == card.Top + card.Shape.Lines[^1].Row),
            3 => all.First(letter => letter.Row == card.Top + card.Shape.Note!.Value.Row),
            _ => all.Last(),
        };
        var (px, py) = PixelOf(game, anchor.Col, anchor.Row);
        PressRig.Force(game, nameof(RaiderGame.PX), px); PressRig.Force(game, nameof(RaiderGame.PY), py);
        var (scr, withShip) = Draw(game);
        var touched = Touched(bare, withShip, game.Layout.Col0);
        Assert.NotEmpty(touched);
        var under = all.Where(letter => touched.Contains((letter.Col, letter.Row))).ToList();
        Assert.NotEmpty(under);
        Assert.All(under, letter => Assert.False(Shown(scr, letter), $"'{letter.Ch}' shows under the ship at {letter.Col},{letter.Row}"));
        Assert.All(all.Where(letter => !touched.Contains((letter.Col, letter.Row))), letter => Assert.True(Shown(scr, letter), $"'{letter.Ch}' at {letter.Col},{letter.Row} was lost though nothing touched it"));
    }

    [Fact]
    public void A_pickup_an_enemy_a_ring_and_a_particle_each_hide_the_letters_of_the_cells_they_touch_and_only_those()
    {
        var adds = new (string Name, Action<RaiderGame, double, double> Add)[]
        {
            ("pickup", (game, x, y) => game.Pickups.Add(new Pickup { Kind = PickupKind.Shield, X = x, Y = y })),
            ("enemy", (game, x, y) => { var enemy = game.SpawnKind(EnemyKind.Gunship, EnemyKit.MidWidth); enemy.X = x; enemy.Y = y; enemy.Grace = 0; enemy.Age = 0; enemy.Flash = 0; }),
            ("ring", (game, x, y) => game.Rings.Add(new Ring { X = x, Y = y, R0 = CardRig.RingStartRadius, R1 = CardRig.RingEndRadius, Life = CardRig.RingLife, Max = CardRig.RingMaxLife, Colour = Palette.Crimson })),
            ("particle", (game, x, y) => game.Particles.Add(new Particle { X = x, Y = y, Life = CardRig.ParticleLife, MaxLife = CardRig.ParticleLife, Colour = CardRig.ParticleColour, Size = CardRig.ParticleSize })),
        };
        foreach (var (name, add) in adds)
        {
            var (game, card) = Scene();
            var (_, bare) = Draw(game);
            var target = Target(game, card);
            var (x, y) = PixelOf(game, target.Col, target.Row);
            add(game, x, y);
            var (scr, drawn) = Draw(game);
            var touched = Touched(bare, drawn, game.Layout.Col0);
            var all = Expected(game, card);
            var hidden = all.Where(letter => touched.Contains((letter.Col, letter.Row))).ToList();
            Assert.True(hidden.Count > 0, $"{name}: touches no letter");
            Assert.All(hidden, letter => Assert.False(Shown(scr, letter), $"{name}: '{letter.Ch}' shows at {letter.Col},{letter.Row}"));
            Assert.All(all.Where(letter => !touched.Contains((letter.Col, letter.Row))), letter => Assert.True(Shown(scr, letter), $"{name}: '{letter.Ch}' at {letter.Col},{letter.Row} was lost"));
        }
    }

    [Fact]
    public void A_wide_character_is_written_only_when_both_of_its_cells_are_clear()
    {
        var (game, card) = Scene(Post(Cjk + " and then a few plain words"));
        var wide = Expected(game, card).First(letter => CellWidth.Of(letter.Ch) == 2);
        var (scr, _) = Draw(game);
        Assert.Equal(wide.Ch, scr.CharAt(wide.Col, wide.Row));
        Assert.Equal(Screen.Continue, scr.CharAt(wide.Col + 1, wide.Row));
        Assert.Equal(scr.BgAt(wide.Col, wide.Row), scr.BgAt(wide.Col + 1, wide.Row));
        foreach (int hit in new[] { wide.Col, wide.Col + 1 })
        {
            var (x, y) = PixelOf(game, hit, wide.Row);
            game.Bullets.Clear();
            game.Bullets.Add(new Bullet { X = x + ShotOffsetX, Y = y + ShotOffsetY, Friendly = true });
            (scr, _) = Draw(game);
            Assert.NotEqual(wide.Ch, scr.CharAt(wide.Col, wide.Row));
            Assert.NotEqual(Screen.Continue, scr.CharAt(wide.Col + 1, wide.Row));
            Assert.NotEqual(Screen.Continue, scr.CharAt(wide.Col, wide.Row));
        }
        Assert.All(Expected(game, card).Where(letter => letter.Row == wide.Row && letter.Col > wide.Col + WideShotReachCells), letter => Assert.True(Shown(scr, letter)));
    }

    [Fact]
    public void During_a_screen_flash_no_card_text_is_written_and_on_the_first_frame_after_it_all_of_it_is()
    {
        var (game, card) = Scene();
        var all = Expected(game, card);
        foreach (int flash in new[] { 1, LongFlashFrames })
        {
            PressRig.Force(game, nameof(RaiderGame.ScreenFlash), flash);
            var (scr, _) = Draw(game);
            Assert.All(all, letter => Assert.False(Shown(scr, letter), $"'{letter.Ch}' at {letter.Col},{letter.Row} was written in a flash of {flash}"));
        }
        PressRig.Force(game, nameof(RaiderGame.ScreenFlash), 0);
        var (after, _) = Draw(game);
        Assert.All(all, letter => Assert.True(Shown(after, letter)));
    }

    // ---------------------------------------------------------------- over the text

    [Fact]
    public void An_enemy_speech_box_that_overlaps_a_card_shows_the_boxs_text_and_units_still_show_over_the_box()
    {
        var game = new RaiderGame(SpeechSeed) { BossGates = false };
        game.SetLayout(FieldLayout.Compute(ScrCols, ScrRows));
        game.OfferDarkness(new DarknessPost(DarknessKind.Spam, SpamPostSeed, "buy followers cheap click my profile now", DarknessSource.Label, "spam"));
        for (int step = 0; step < SpeechBoxWaitSteps && !game.Enemies.Any(EnemySpeechBox.IsOpen); step++) game.Step();
        game.Posts.Offer(Post());
        game.Step();
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        enemy.SpeechBoxAge = SpeechBoxText.RevealFrames(SpeechBoxText.PageChars(enemy.SpeechLines!, enemy.PageLines, page: 0)) + RevealSettleFrames;
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        var card = game.Cards.Cards[0];
        card.Top = box.R - CardAboveBoxRows;
        var (scr, _) = Draw(game);
        string Row(int row) => new(Enumerable.Range(box.C + BoxTextInset, box.W - EnemySpeechBox.TextSide).Select(col => scr.CharAt(col, row)).ToArray());
        Assert.StartsWith(enemy.SpeechLines![0], Row(box.R + BoxFirstTextRow));
        Assert.False(Expected(game, card).Any(letter => letter.Col >= box.C && letter.Col < box.Right && letter.Row >= box.R && letter.Row < box.Bottom && Shown(scr, letter) && scr.Fg[letter.Row * ScrCols + letter.Col] == letter.Fg),
            "card letters show inside the box");
        var wordCol = Enumerable.Range(box.C + BoxTextInset, enemy.SpeechLines[0].Length).First(col => scr.CharAt(col, box.R + BoxFirstTextRow) != ' ');
        var (x, y) = PixelOf(game, wordCol, box.R + BoxFirstTextRow);
        game.Bullets.Add(new Bullet { X = x + ShotOffsetX, Y = y + ShotOffsetY, Friendly = true });
        (scr, _) = Draw(game);
        Assert.True(HasShot(scr, wordCol, box.R + BoxFirstTextRow), "the shot is not over the box");
    }

    [Fact]
    public void The_hud_rows_and_a_popup_show_their_own_text_where_a_card_passes_under_them()
    {
        var (game, card) = Scene(top: CardTopUnderHud);
        var target = Expected(game, card).First(letter => letter.Row > BelowHudRow);
        var (px, py) = PixelOf(game, target.Col, target.Row);
        game.Popups.Add(new Popup { Text = PopupText, X = px, Y = py, Life = PopupLife, Colour = PopupColour });
        var (scr, _) = Draw(game);
        string Row(int row, int from, int count) => new(Enumerable.Range(from, count).Select(col => scr.CharAt(col, row)).ToArray());
        Assert.Contains("SCORE", Row(0, game.Layout.Col0, game.Layout.PCols));
        Assert.Contains(PopupText, Row(target.Row, game.Layout.Col0, game.Layout.PCols));
        Assert.All(Expected(game, card).Where(letter => letter.Row < EnemySpeechBox.HudRows), letter => Assert.False(scr.CharAt(letter.Col, letter.Row) == letter.Ch && scr.Fg[letter.Row * ScrCols + letter.Col] == letter.Fg, $"card letter '{letter.Ch}' over the HUD at {letter.Col},{letter.Row}"));
    }

    // ---------------------------------------------------------------- moving

    [Fact]
    public void Text_moves_with_its_slab_on_a_row_step()
    {
        var (game, card) = Scene(top: RowStepCardTop);
        int before = card.Top;
        for (int step = 0; step < RowStepWaitSteps && card.Top == before; step++) { game.Step(); CardRig.ClearAndParkShip(game); }
        Assert.Equal(before + 1, card.Top);
        var (scr, _) = Draw(game);
        Assert.All(Expected(game, card), letter => Assert.True(Shown(scr, letter), $"'{letter.Ch}' at {letter.Col},{letter.Row}"));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void Text_moves_a_cell_under_a_whole_cell_shake_and_stays_put_under_a_one_pixel_shake(int pixels, int cells)
    {
        var (game, card) = Scene(top: OnFieldCardTop);
        PressRig.Force(game, nameof(RaiderGame.PX), CardRig.ShipOverCardX); PressRig.Force(game, nameof(RaiderGame.PY), CardRig.ShipBelowCardY);
        CardRig.ShakeBy(game, pixels);
        var (shakeX, shakeY) = game.ShakeOffset();
        var (scr, _) = Draw(game);
        var moved = Expected(game, card, cells * Math.Sign(shakeX), cells * Math.Sign(shakeY));
        Assert.All(moved, letter => Assert.True(Shown(scr, letter), $"'{letter.Ch}' is not at {letter.Col},{letter.Row}"));
    }

    [Fact]
    public void On_a_frame_with_no_row_step_and_nothing_over_the_cards_the_encoder_sends_nothing_for_the_card_cells()
    {
        var (game, card) = Scene(top: OnFieldCardTop);
        var encoder = DiffEncoder.Snapped();
        var view = new RaiderView();
        var scr = new Screen(ScrCols, ScrRows);
        view.FitAndRender(game, scr);
        encoder.Encode(scr);
        int top = card.Top;
        for (int step = 0; step < QuietSteps; step++) { game.Step(); CardRig.ClearAndParkShip(game); }
        Assert.Equal(top, card.Top);
        view.FitAndRender(game, scr);
        string sent = encoder.Encode(scr);
        int col0 = game.Layout.Col0 + game.Cards.CardLeft;
        foreach (Match move in Regex.Matches(sent, "\x1b\\[(\\d+);(\\d+)H"))
        {
            int row = int.Parse(move.Groups[1].Value) - 1, col = int.Parse(move.Groups[2].Value) - 1;
            bool inCard = row >= card.Top + 1 && row < card.Top + card.Shape.Rows - 1 && col >= col0 + 1 && col < col0 + card.Shape.Cols - 1;
            Assert.False(inCard, $"the encoder moves to {col},{row} inside the card");
        }
    }
}
