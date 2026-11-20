using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using TUImation;

namespace FeedRaider.Tests;

// xterm's WebGL renderer rasterises every (glyph, foreground, background) it meets once, into an atlas that only grows. On a
// phone at DPR 3 each new key is a slow draw and measure, and a full atlas means a renewal and the whole screen drawn again.
// Truecolour quadrant cells mint keys without end (about 670 a second in play) unless they are snapped to a small palette.
public class AtlasKeyTests
{
    private const uint Charcoal = 0x101316, Khaki = 0xd8cba3, Olive = 0xa3b35f, Navy = 0x4f5f8c, Wine = 0xa8324d;

    // a Pixel 9 Pro in portrait: 412 x 915 CSS pixels at the 12 px font
    internal const int PhoneCols = 56, PhoneRows = 58;

    // every 24-bit colour, the bound for a random channel triple
    private const int ColourSpace = 0x1000000;

    // a cell of the hold's progress line is all track, half lit or all lit
    private const int ProgressLineCellStates = 3;

    private const int GameSeed = 5;
    private const ulong BackgroundSeed = RaiderView.DefaultBackgroundSeed;

    // the length of the phone runs that measure the cards
    private const int SixtySeconds = 60 * Tuning.Fps;

    // the posts' authors, and so the avatars the run asks for
    private const int Authors = 40;

    // the cards that carry pictures: every third one
    private const int PicturedEvery = 3;

    // the posts a run keeps waiting in the queue: every ceiling in this file was measured at this depth
    private const int QueuedPosts = 4;

    [Fact]
    public void The_first_colours_stay_exact_and_near_ones_fold_onto_them()
    {
        var snap = new ColourSnap();
        uint[] first = [Charcoal, Khaki, Olive, Navy, Wine];
        foreach (uint colour in first) Assert.Equal(colour, snap[colour]);
        Assert.Equal(Charcoal, snap[Charcoal + 0x050505]);
        Assert.Equal(Khaki, snap[Khaki - 0x0a0a0a]);
        Assert.Equal(first.Length, snap.Count);
        // further than the tolerance on one channel is a colour of its own
        uint far = Olive + Surface.Pack(ColourSnap.DefaultTolerance + 1, 0, 0);
        Assert.Equal(far, snap[far]);
        Assert.Equal(first.Length + 1, snap.Count);
    }

    [Fact]
    public void A_full_palette_gives_the_nearest_colour_and_grows_no_more()
    {
        const int capacity = 3;
        var snap = new ColourSnap(tolerance: 4, capacity: capacity);
        _ = snap[0x000000]; _ = snap[0x808080]; _ = snap[0xffffff];
        Assert.Equal(0x808080u, snap[0x9a9a9a]);
        Assert.Equal(0xffffffu, snap[0xe0e0e0]);
        Assert.Equal(capacity, snap.Count);
    }

    [Fact]
    public void A_colour_diverted_from_a_primed_one_goes_to_the_nearer_target_and_no_target_joins_the_palette()
    {
        var snap = new ColourSnap();
        snap.Prime([Charcoal]);
        uint target = Charcoal + Surface.Pack(ColourSnap.DefaultTolerance / 2, 0, 0);
        snap.Divert(Charcoal, [target]);
        Assert.Equal(target, snap[target]);
        Assert.Equal(Charcoal, snap[Charcoal]);
        // nearer the primed colour than the target: the primed colour still takes it
        Assert.Equal(Charcoal, snap[Charcoal + Surface.Pack(1, 0, 0)]);
        Assert.Equal(1, snap.Count);
        // past the tolerance of the primed colour it is a colour of its own, as if nothing were diverted
        uint beyond = Charcoal + Surface.Pack(ColourSnap.DefaultTolerance + 1, 0, 0);
        Assert.Equal(beyond, snap[beyond]);
        Assert.Equal(2, snap.Count);
    }

    [Fact]
    public void A_snapped_encoder_sends_each_colour_pair_in_one_orientation_and_a_one_colour_quad_as_a_space()
    {
        var screen = new Screen(3, 1);
        screen.Clear(Charcoal);
        char left = QuadRenderer.GlyphFor(0b0101), right = QuadRenderer.GlyphFor(0b1010);
        screen.Put(0, 0, left, Khaki, Navy);
        screen.Put(1, 0, right, Navy, Khaki);
        screen.Put(2, 0, left, Olive, Olive);
        var keys = Keys(screen);
        Assert.Equal(keys[0], keys[1]);
        Assert.Equal((' ', Olive), (keys[2].Ch, keys[2].Bg));
    }

    [Fact]
    public void The_game_sends_through_a_snapped_encoder()
    {
        var runner = new RaiderRunner(new RaiderGame(GameSeed), () => (60, 20), new NullSink());
        Assert.NotNull(runner.Encoder.Snap);
    }

    [Fact]
    public void Play_at_phone_size_mints_under_a_fifth_of_the_atlas_keys_it_did_unsnapped()
    {
        const int frames = 90 * Tuning.Fps;
        var (snapped, plain) = (new HashSet<(char, uint, uint)>(), new HashSet<(char, uint, uint)>());
        int atTwoThirds = 0;
        Play(PhoneCols, PhoneRows, frames, DiffEncoder.Snapped(), snapped, frame => { if (frame == frames * 2 / 3) atTwoThirds = snapped.Count; });
        Play(PhoneCols, PhoneRows, frames, new DiffEncoder(), plain, _ => { });
        int lastThird = snapped.Count - atTwoThirds;
        // The bounds are the keys measured snapped, in the whole run and in its last 30 s with the denser enemy fire, and a margin of
        // about one in a hundred. The ship rests on a repost button long enough to finish the hold, and the build-up mints keys of its
        // own (the scripted hold below measures them). Unsnapped, a phone at DPR 3 would renew its atlas over and over and spend its
        // frames rasterising; past these bounds the phone build is heading back there.
        var seen = $"snapped {snapped.Count} ({lastThird} in the last 30 s), unsnapped {plain.Count}";
        Assert.True(plain.Count > 5 * snapped.Count, seen);
        Assert.True(snapped.Count <= 13_310, seen);
        Assert.True(lastThird <= 3_735, seen);
    }

    // Real cards: each distinct picture is up to 16 cells of its own colour pairs (about 1.1k keys for the 40 authors here, and noise
    // is the worst case), and a lit button's ground adds the rest wherever a shot crosses it. The sky keeps the steps of its fade
    // (Background.SkySteps) apart from the card's colours, and the cells where they meet the cards are keys of their own. The cards as
    // pixels with no words minted 9477 in this run; with the words, 9481. The ceiling is the measured number and a margin of about one
    // in a hundred, so the cards cannot grow past it unseen. The count takes in the build-up of every button the wandering ship rests on.
    private const int KeysWithRealCards = 9_575;

    // The same run with the card words left out: what the words are measured against.
    private const int KeysWithCardsAndNoWords = 9_477;

    [Fact]
    public void A_sixty_second_phone_run_with_real_card_pixels_stays_inside_the_measured_atlas_keys_of_the_first_cards()
    {
        var keys = new HashSet<(char, uint, uint)>();
        Play(PhoneCols, PhoneRows, SixtySeconds, DiffEncoder.Snapped(), keys, _ => { }, gates: false);
        Assert.True(keys.Count <= KeysWithRealCards, $"minted {keys.Count}, the ceiling is {KeysWithRealCards}");
    }

    // The same sixty seconds with one to four pictures on every third card. Scenes of soft bands and a shape minted 14403 keys and
    // pictures of noise 17362, against the cards with no pictures above: about half as many again for pictures like photographs and
    // nearly twice as many for the worst a feed can send. A card is short, so more cards and more of their pictures are on the screen
    // at once. Taking the pictures' colours down to steps of 16 or 32 a channel changed neither number by more than 3 in 100 (the
    // encoder already folds near colours together), so the pictures are left as they come. The ceilings are the measured numbers and
    // a margin of about one in a hundred.
    [Theory]
    [InlineData(Pictures.Scenes, 14_550)]
    [InlineData(Pictures.Noise, 17_535)]
    public void A_sixty_second_phone_run_with_pictures_on_every_third_card_stays_inside_its_measured_atlas_keys(object kind, int ceiling)
    {
        Assert.NotEmpty(Pictured(PicturedEvery).Pictures!);
        var keys = new HashSet<(char, uint, uint)>();
        Play(PhoneCols, PhoneRows, SixtySeconds, DiffEncoder.Snapped(), keys, _ => { }, gates: false, pictures: (Pictures)kind);
        Assert.True(keys.Count <= ceiling, $"{kind}: minted {keys.Count}, the ceiling is {ceiling} (the cards without pictures: {KeysWithRealCards})");
    }

    [Fact]
    public void The_card_words_mint_no_more_keys_than_one_per_distinct_character_per_text_colour_over_the_cards_without_them()
    {
        var keys = new HashSet<(char, uint, uint)>();
        Play(PhoneCols, PhoneRows, SixtySeconds, DiffEncoder.Snapped(), keys, _ => { }, gates: false);
        var chars = new HashSet<char>("@.test0123456789");
        // every post's text is the same words around its number, and the digits are in the set already
        chars.UnionWith(Post(1).Text);
        chars.Remove(' ');
        chars.UnionWith([CardLayout.HeartOff, CardLayout.HeartOn, CardLayout.LoopMark]);
        uint[] colours = [CardColours.NameText, CardColours.PostText, CardColours.DimText, CardColours.LikeIcon, CardColours.RepostIcon, CardColours.LikeCountLit, CardColours.RepostCountLit];
        int allowed = KeysWithCardsAndNoWords + chars.Count * colours.Length;
        Assert.True(keys.Count <= allowed, $"minted {keys.Count}; the cards without words minted {KeysWithCardsAndNoWords} and {chars.Count} characters in {colours.Length} colours add at most {chars.Count * colours.Length}");
        // every key that is a letter in a text colour sits on a ground the game draws: a card's, the HUD's or the feed log's
        uint[] grounds = [CardColours.Slab, CardColours.LikeButtonLit, CardColours.RepostButtonLit, RaiderView.HudBg, RaiderView.PanelBg];
        foreach (var (ch, fg, bg) in keys)
            if (chars.Contains(ch) && colours.Contains(fg)) Assert.Contains(bg, grounds);
    }

    [Theory]
    [InlineData(PostAction.Like)]
    [InlineData(PostAction.Repost)]
    public void A_scripted_hold_at_phone_size_mints_no_more_keys_than_the_run_without_it_plus_the_cells_of_the_ships_ring_at_each_step_and_of_the_progress_line(PostAction action)
    {
        // the ship rests on the button of one card for the whole build-up, or stays where it starts, clear of every button
        int Run(bool holds)
        {
            var keys = new HashSet<(char, uint, uint)>();
            var game = new RaiderGame(GameSeed) { SignedIn = false, BossGates = false };
            game.SetLayout(FieldLayout.Compute(PhoneCols, PhoneRows));
            game.Posts.Offer(HoldRig.Post(1));
            game.Step();
            var card = game.Cards.Cards[0];
            card.Top = 10;
            var screen = new Screen(PhoneCols, PhoneRows);
            var view = new RaiderView(BackgroundSeed);
            var term = new Term(PhoneCols, PhoneRows, keys);
            var encoder = DiffEncoder.Snapped();
            while (game.Tick < Tuning.HoldFrames)
            {
                if (holds) HoldRig.Park(game, card, action);
                HoldRig.Step(game);
                view.FitAndRender(game, screen);
                term.Feed(encoder.Encode(screen));
            }
            Assert.Equal(holds ? Tuning.HoldFrames - 1 : 0, card.Marks.Progress(action));
            return keys.Count;
        }
        int without = Run(false), with = Run(true);
        // each step recolours the ring of dots round the ship, and the icon and its halo lie inside the ring: the ring's square in
        // cells (and one more for a row step); the progress line along the card's foot passes each of its cells through its states
        static int CellsOver(int pixels) => (pixels + CardField.PixelsPerCell - 1) / CardField.PixelsPerCell + 1;
        int ring = CellsOver(2 * RaiderView.HoldRingRadius + 1), cells = ring * ring;
        Assert.True(HoldBeats.MaxIconWidth + 2 * HoldBeats.HaloMargin <= 2 * RaiderView.HoldRingRadius, "the icon and its halo lie inside the ring");
        Assert.True(with > without, "the build-up is on the screen");
        int steps = HoldBeats.Widths.Length, line = PhoneCols * ProgressLineCellStates;
        Assert.True(with <= without + steps * cells + line,
            $"minted {with} with the hold and {without} without; {steps} steps of {cells} cells and {line} for the line allow {steps * cells + line} more");
    }

    private static FeedPost Post(int number) =>
        new($"at://did:plc:p{number}/app.bsky.feed.post/{number}", $"cid{number}", $"did:plc:p{number % Authors}", $"p{number % Authors}.test",
            $"post number {number} with a few words of text to read out loud", 10, 0, 0, null, null);

    /// <summary>What the host answers a card's picture with: nothing, a scene of soft bands and a few shapes (what a photograph
    /// is like once it is shrunk), or every pixel a colour of its own (the worst a feed can send).</summary>
    private enum Pictures { None, Scenes, Noise }

    /// <summary>The post with one to four pictures on every third one, wide, square and tall by turns.</summary>
    private static FeedPost Pictured(int number)
    {
        var post = Post(number);
        if (number % PicturedEvery != 0) return post;
        var shapes = new[] { (Width: 1600, Height: 900), (Width: 1000, Height: 1000), (Width: 900, Height: 1600) };
        return post with
        {
            HasEmbed = true,
            Attachment = AttachmentKind.Picture,
            Pictures = Enumerable.Range(0, number / PicturedEvery % FeedMapper.MaxPictures + 1).Select(picture =>
            {
                var shape = shapes[(number + picture) % shapes.Length];
                return new PostPicture($"https://cdn.test/{number}/{picture}", shape.Width, shape.Height);
            }).ToArray(),
        };
    }

    private static uint[] Scene(Random paint, int width, int height)
    {
        uint sky = (uint)paint.Next(ColourSpace), ground = (uint)paint.Next(ColourSpace), thing = (uint)paint.Next(ColourSpace);
        int thingX = paint.Next(width), thingY = paint.Next(height), radius = 2 + paint.Next(Math.Max(1, height / 3));
        var pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                bool inside = (x - thingX) * (x - thingX) + (y - thingY) * (y - thingY) <= radius * radius;
                pixels[y * width + x] = inside ? thing : Surface.Mix(sky, ground, y / (double)Math.Max(1, height - 1));
            }
        return pixels;
    }

    // every pixel a colour of its own
    private static uint[] Noise(Random paint, int count) => [.. Enumerable.Range(0, count).Select(_ => (uint)paint.Next(ColourSpace))];

    private static void Play(int cols, int rows, int frames, DiffEncoder encoder, HashSet<(char, uint, uint)> keys, Action<int> tick, bool gates = true,
        Pictures pictures = Pictures.None)
    {
        var game = new RaiderGame(GameSeed) { SignedIn = false, BossGates = gates };
        game.SetLayout(FieldLayout.Compute(cols, rows));
        var dice = new Random(3);
        // avatars of every colour, the worst a feed can send
        game.AvatarWanted = post => game.SetAvatar(post.AuthorDid, Noise(dice, AvatarCsv.Pixels));
        var paint = new Random(11);
        game.PictureWanted = (post, picture, width, height) =>
        {
            var pixels = pictures switch
            {
                Pictures.Scenes => Scene(paint, width, height),
                Pictures.Noise => Noise(paint, width * height),
                _ => new uint[width * height],
            };
            game.SetPicture(post.Pictures![picture].Thumb, width, height, pixels);
        };
        var screen = new Screen(cols, rows);
        var view = new RaiderView(BackgroundSeed);
        var term = new Term(cols, rows, keys);
        var directions = new[] { GameKey.Left, GameKey.Right, GameKey.Up, GameKey.Down };
        // the ship wanders: every two fifths of a second each direction is held or let go at random
        const int framesPerTurn = 2 * Tuning.Fps / 5;
        const double heldChance = 0.3;
        int posted = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            if (game.Posts.Count < QueuedPosts) game.Posts.Offer(pictures == Pictures.None ? Post(++posted) : Pictured(++posted));
            if (frame % framesPerTurn == 0) foreach (var direction in directions) game.Input.Key(direction, dice.NextDouble() < heldChance);
            game.Input.Key(GameKey.Fire, true);
            game.Step();
            view.FitAndRender(game, screen);
            term.Feed(encoder.Encode(screen));
            tick(frame);
        }
    }

    // the atlas key of each printed cell, in the order the snapped encoder prints them
    private static (char Ch, uint Fg, uint Bg)[] Keys(Screen screen)
    {
        var keys = new List<(char, uint, uint)>();
        new Term(screen.Cols, screen.Rows, null, keys).Feed(DiffEncoder.Snapped().Encode(screen));
        return [.. keys];
    }

    private sealed class NullSink : IFrameSink
    {
        public void Write(string frame) { }
    }

    /// <summary>Just enough of a terminal to see what each printed cell's atlas key is: the glyph with the colours in force.</summary>
    private sealed class Term(int cols, int rows, HashSet<(char, uint, uint)>? seen, List<(char, uint, uint)>? order = null)
    {
        // the terminal's own colour, which no 24-bit colour can be
        private const uint Unset = uint.MaxValue;
        // ESC and '[' open every sequence the encoder writes
        private const int CsiLength = 2;
        // the select-graphic-rendition codes for a 24-bit foreground and background
        private const int SgrForeground = 38, SgrBackground = 48;
        // the select-graphic-rendition code that sets the colours back to the terminal's own
        private const int SgrReset = 0;

        private int _row, _col;
        private uint _fg = Unset, _bg = Unset;

        public void Feed(string stream)
        {
            for (int position = 0; position < stream.Length; position++)
            {
                if (stream[position] != '\x1b')
                {
                    if (_row < rows && _col < cols)
                    {
                        seen?.Add((stream[position], _fg, _bg));
                        order?.Add((stream[position], _fg, _bg));
                    }
                    _col++;
                    continue;
                }
                int final = position + CsiLength;
                while (!char.IsLetter(stream[final])) final++;
                var args = stream[(position + CsiLength)..final].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                switch (stream[final])
                {
                    case 'H': _row = args[0] - 1; _col = args[1] - 1; break;
                    case 'C': _col += args.Length > 0 ? args[0] : 1; break;
                    case 'm': Sgr(args); break;
                }
                position = final;
            }
        }

        // where each channel sits after a colour code, which a 2 follows first
        private const int RedArg = 2, GreenArg = 3, BlueArg = 4;

        private static uint ColourAt(int[] args, int code) => Surface.Pack(args[code + RedArg], args[code + GreenArg], args[code + BlueArg]);

        private void Sgr(int[] args)
        {
            if (args.Length == 0) { _fg = _bg = Unset; return; }
            for (int code = 0; code < args.Length; code++)
            {
                switch (args[code])
                {
                    case SgrReset: _fg = _bg = Unset; break;
                    case SgrForeground: _fg = ColourAt(args, code); code += BlueArg; break;
                    case SgrBackground: _bg = ColourAt(args, code); code += BlueArg; break;
                }
            }
        }
    }
}
