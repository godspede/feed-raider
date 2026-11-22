using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// A darkness enemy that carries text speaks in a speech box, above it, with its label where a name would be.
public class EnemySpeechBoxTests
{
    private const string Words = "you are all idiots and nobody likes your takes";
    /// <summary>The labels a stranger's post is offered with, and the headers its box shows for them.</summary>
    private const string RudeLabel = "rude", SpamLabel = "spam", AnyLabel = "x", RudeHeader = "RUDE", SpamHeader = "SPAM";
    /// <summary>What a clipped line ends in.</summary>
    private const string Ellipsis = "…";
    /// <summary>The game's seed and an offered post's seed: any will do.</summary>
    private const int Seed = 3, PostSeed = 5;
    /// <summary>Where a lone speaker stands on the field, in pixels.</summary>
    private const double SpotX = 40, SpotY = 30;
    /// <summary>The field pixel the left of two or more speakers stands at.</summary>
    private const double LeftX = 20;

    [Theory]
    [InlineData(DarknessKind.Spam, null, DarknessSource.Label, SpamHeader)]
    [InlineData(DarknessKind.Rude, null, DarknessSource.Label, RudeHeader)]
    [InlineData(DarknessKind.Rude, RudeLabel, DarknessSource.Label, RudeHeader)]
    [InlineData(DarknessKind.Spam, "scam", DarknessSource.Label, "SCAM")]
    [InlineData(DarknessKind.Rude, null, DarknessSource.Hidden, "HIDDEN REPLY")]
    public void Header_is_the_upper_case_label_else_the_kind(DarknessKind kind, string? label, DarknessSource src, string expected) =>
        Assert.Equal(expected, EnemySpeechBox.Header(label, kind, src));

    /// <summary>The screen most of these tests lay the field out on and draw to.</summary>
    private const int BoxCols = 48;
    private const int BoxRows = 60;
    /// <summary>Long enough for any offered post's wave to arrive and open its box.</summary>
    private const int MaxWaitFrames = 1500;
    /// <summary>A phone held upright.</summary>
    private const int PhoneCols = 40, PhoneRows = 70;
    private const int WideCols = 120, WideRows = 40;
    /// <summary>The classic terminal.</summary>
    private const int TerminalCols = 80, TerminalRows = 24;
    /// <summary>A speaker's height in pixels deep enough in the field that its box goes above it.</summary>
    private const double MidFieldY = 40;
    /// <summary>Where the box's title starts: inside the border and one blank cell.</summary>
    private const int TitleInset = 2;
    /// <summary>The age of a box on the frame it opens: <see cref="EnemySpeechBox.IsOpen"/> needs an age above zero.</summary>
    private const int JustOpenedAge = 1;

    private static RaiderGame OpenBoxGame(DarknessKind kind, string label) =>
        Offered(kind, Words, BoxCols, BoxRows, label);

    /// <summary>A game where a post of this kind has been offered and its box is open. A rude post makes no enemy, so a lesser gunship says it.</summary>
    private static RaiderGame Offered(DarknessKind kind, string text, int cols, int rows, string label = AnyLabel)
    {
        var game = new RaiderGame(Seed) { BossGates = kind != DarknessKind.Rude };
        game.SetLayout(FieldLayout.Compute(cols, rows));
        if (kind == DarknessKind.Rude) game.SpawnLesser();
        game.OfferDarkness(new DarknessPost(kind, PostSeed, text, DarknessSource.Label, label));
        for (int i = 0; i < MaxWaitFrames && !game.Enemies.Any(EnemySpeechBox.IsOpen); i++) game.Step();
        return game;
    }

    private static Screen Render(RaiderGame game, int cols, int rows)
    {
        var scr = new Screen(cols, rows);
        new RaiderView().FitAndRender(game, scr);
        return scr;
    }

    private static string Row(Screen screen, int row) => new(Enumerable.Range(0, screen.Cols).Select(col => screen.CharAt(col, row)).ToArray());

    private static string[] Rows(Screen screen) => Enumerable.Range(0, screen.Rows).Select(row => Row(screen, row)).ToArray();

    [Theory]
    [InlineData(DarknessKind.Rude, RudeLabel)]
    [InlineData(DarknessKind.Spam, SpamLabel)]
    public void The_box_is_above_the_enemy_with_the_label_in_its_header_and_the_pointer_down(DarknessKind kind, string label)
    {
        var game = OpenBoxGame(kind, label);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        bool below = EnemySpeechBox.IsBelow(enemy, box);   // right under the top there is no room above, so the box goes under the enemy
        var shown = EnemySpeechBox.Shown(enemy);
        Assert.True(below ? box.R >= (int)Math.Ceiling(shown.Bottom / CardField.PixelsPerCell) : box.Bottom <= (int)Math.Floor(shown.Top / CardField.PixelsPerCell) || box.R == EnemySpeechBox.MinRow);
        var scr = Render(game, BoxCols, BoxRows);
        Assert.Contains(label.ToUpperInvariant(), Row(scr, box.R + 1));
        int pointerRow = below ? box.R : box.Bottom - 1, middle = box.C + box.W / 2;
        Assert.Equal(below ? '▲' : '▼', scr.CharAt(middle, pointerRow));
        Assert.Equal(EnemySpeechBox.Edge, scr.Fg[pointerRow * scr.Cols + middle]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void A_wounded_speakers_health_bar_lies_on_the_bottom_edge_of_its_box_and_never_across_a_line_of_its_words(double dy)
    {
        var game = OpenBoxGame(DarknessKind.Rude, RudeLabel);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        enemy.Y = MidFieldY + dy;
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        Assert.False(EnemySpeechBox.IsBelow(enemy, box));
        int bar = RaiderView.BarRow((int)Math.Round(EnemySpeechBox.Shown(enemy).Top));
        int barCell = bar / CardField.PixelsPerCell, edge = box.Bottom - 1;
        Assert.True(barCell >= edge, $"the bar is on cell row {barCell} and the box's bottom edge on row {edge}");
        Assert.True(bar < EnemySpeechBox.Shown(enemy).Top - 1, "a clear pixel row between the bar and the thing");
    }

    [Fact]
    public void A_box_over_a_thing_made_of_parts_stands_above_its_highest_part()
    {
        var game = OpenBoxGame(DarknessKind.Rude, RudeLabel);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        const double lowY = 60;
        enemy.Y = lowY;
        int plainBottom = EnemySpeechBox.Box(enemy, game.Layout).Bottom;
        const double mastPx = 6;
        enemy.Parts = [new Part { Name = "mast", OY = EnemySpeechBox.Shown(enemy).Top - enemy.Y - mastPx, Closed = enemy.Sprite }];   // its art reaches above the sprite
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        Assert.True(box.Bottom < plainBottom);
        Assert.True(box.Bottom * CardField.PixelsPerCell <= EnemySpeechBox.Shown(enemy).Top);
    }

    [Fact]
    public void The_box_is_red_on_near_black_and_solid()
    {
        var game = OpenBoxGame(DarknessKind.Rude, RudeLabel);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        var scr = Render(game, BoxCols, BoxRows);
        Assert.Equal(EnemySpeechBox.Edge, scr.Fg[box.R * scr.Cols + box.C]);
        for (int y = box.R; y < box.Bottom; y++)
            for (int x = box.C; x < box.Right; x++) Assert.Equal(EnemySpeechBox.BoxBg, scr.Bg[y * scr.Cols + x]);
        Assert.Equal(EnemySpeechBox.HeaderFg, scr.Fg[(box.R + 1) * scr.Cols + box.C + TitleInset]);
        const int strongRed = 0xc0, weakGreenBlue = 0x4000, nearBlack = 0x20, redShift = 16;
        const uint greenBlueMask = 0xffff, blueMask = 0xff;
        Assert.True(EnemySpeechBox.Edge >> redShift > strongRed && (EnemySpeechBox.Edge & greenBlueMask) < weakGreenBlue, "the border is red");
        Assert.True(EnemySpeechBox.BoxBg >> redShift < nearBlack && (EnemySpeechBox.BoxBg & blueMask) < nearBlack, "the box is near black");
    }

    [Fact]
    public void No_tag_is_left_over_the_sprite_and_no_handle_or_did_shows()
    {
        var game = OpenBoxGame(DarknessKind.Spam, SpamLabel);
        game.Enemies.RemoveAll(enemy => !EnemySpeechBox.IsOpen(enemy));
        var enemy = game.Enemies[0];
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        var scr = Render(game, BoxCols, BoxRows);
        for (int y = 0; y < scr.Rows; y++)
        {
            string row = Row(scr, y);
            Assert.DoesNotContain("@", row);
            Assert.DoesNotContain("did:", row);
            if (y < box.R || y >= box.Bottom) Assert.DoesNotContain(SpamHeader, row);
        }
    }

    [Fact]
    public void An_enemy_with_no_text_shows_no_box()
    {
        var enemy = new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = SpotX, Y = SpotY, Header = SpamHeader, Live = true };
        EnemySpeechBox.Update(enemy, BoxCols, TerminalRows);
        Assert.False(EnemySpeechBox.IsOpen(enemy));
    }

    [Fact]
    public void The_box_is_clamped_inside_the_field_and_below_the_hud()
    {
        var game = OpenBoxGame(DarknessKind.Rude, RudeLabel);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        const double nearTopY = 8;
        foreach (double x in new[] { 0.0, 1000.0 })
        {
            enemy.X = x; enemy.Y = nearTopY;
            var box = EnemySpeechBox.Box(enemy, game.Layout);
            Assert.True(box.C >= game.Layout.Col0 && box.Right <= game.Layout.Col0 + game.Layout.PCols);
            Assert.True(box.R >= EnemySpeechBox.MinRow);
        }
    }

    [Fact]
    public void The_box_never_covers_the_ship_and_closes_after_its_hold()
    {
        var game = OpenBoxGame(DarknessKind.Rude, RudeLabel);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        enemy.X = game.PX; enemy.Y = game.PY + CardField.PixelsPerCell;   // a cell below the ship: the box above it would sit on the ship
        Assert.DoesNotContain(enemy, EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes).Select(shown => shown.Enemy).ToList());
        enemy.SpeechBoxAge = EnemySpeechBox.Frames(enemy);
        Assert.False(EnemySpeechBox.IsOpen(enemy));
    }

    [Fact]
    public void Two_enemies_boxes_whose_homes_overlap_both_show_the_older_at_home_and_neither_covering_the_other()
    {
        var game = OpenBoxGame(DarknessKind.Spam, SpamLabel);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        game.Enemies.RemoveAll(other => other != enemy);
        const int olderAge = 40;
        const double overlapPx = 6;   // the newer speaker stands this close to the right: the two boxes overlap
        enemy.X = LeftX; enemy.Y = SpotY; enemy.SpeechBoxAge = olderAge;
        var newer = new Enemy { Kind = enemy.Kind, Sprite = enemy.Sprite, X = LeftX + overlapPx, Y = SpotY, Text = enemy.Text, Header = SpamHeader, Live = true };
        EnemySpeechBox.Update(newer, BoxCols, TerminalRows); newer.SpeechBoxAge = JustOpenedAge; game.Enemies.Add(newer);
        Assert.True(EnemySpeechBox.Box(enemy, game.Layout).Overlaps(EnemySpeechBox.Box(newer, game.Layout)));
        var placed = EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes);
        Assert.Equal(2, placed.Count);
        Assert.Equal(EnemySpeechBox.Box(enemy, game.Layout), placed.Single(shown => shown.Enemy == enemy).Box);
        Assert.False(placed[0].Box.Overlaps(placed[1].Box));
        const double farBelowY = 70;
        newer.Y = farBelowY;   // far enough down the field: both show
        Assert.False(EnemySpeechBox.Box(enemy, game.Layout).Overlaps(EnemySpeechBox.Box(newer, game.Layout)));
        var visible = EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes).Select(shown => shown.Enemy).ToList();
        Assert.Contains(enemy, visible);
        Assert.Contains(newer, visible);
    }

    [Theory]
    [InlineData(PhoneCols, PhoneRows)]
    [InlineData(WideCols, WideRows)]
    public void A_live_box_and_an_echo_box_never_overlap_on_the_screen_while_both_are_read(int cols, int rows)
    {
        var game = Offered(DarknessKind.Rude, LongPost, cols, rows);
        var speaker = game.Enemies.First(EnemySpeechBox.IsOpen);
        game.Enemies.RemoveAll(other => other != speaker);
        game.Bullets.Clear();
        const int echoAge = 20;
        const double besidePx = 4;   // the echo stands this close to the speaker: their homes overlap
        speaker.X = game.PW / 2.0; speaker.Y = MidFieldY;
        var echo = speaker.Snapshot();
        echo.Voice = null; echo.X = speaker.X + besidePx; echo.SpeechBoxAge = echoAge;
        game.Echoes.Add(echo);
        Assert.True(EnemySpeechBox.Box(speaker, game.Layout).Overlaps(EnemySpeechBox.Box(echo, game.Layout)));
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        const int watchedFrames = 90;
        for (int frame = 0; frame < watchedFrames && game.Echoes.Contains(echo); frame++)
        {
            game.Step();
            view.FitAndRender(game, scr);
            var boxes = view.SpeechBoxes;
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                    Assert.False(boxes[i].Overlaps(boxes[j]), $"frame {frame}: {boxes[i]} covers {boxes[j]}");
            if (EnemySpeechBox.IsOpen(speaker) && game.Echoes.Contains(echo)) Assert.Equal(2, boxes.Count);
        }
    }

    [Theory]
    [InlineData(PhoneCols, PhoneRows)]
    [InlineData(WideCols, WideRows)]
    public void No_banner_or_tag_rect_intersects_a_speech_box_rect_wherever_the_box_stands(int cols, int rows)
    {
        var game = Offered(DarknessKind.Rude, LongPost, cols, rows);
        var speaker = game.Enemies.First(EnemySpeechBox.IsOpen);
        game.Enemies.RemoveAll(other => other != speaker);
        PressRig.Force(game, nameof(RaiderGame.RespawnIn), Tuning.RespawnFrames);
        game.StartAtStage(BannerStage);
        game.Say("a toast for the test");
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        int tagsSeen = 0;
        for (double y = EnemySpeechBox.OpenY; y < game.PH; y += SweepStepPx)
        {
            speaker.X = game.PW / 2.0; speaker.Y = y;
            view.FitAndRender(game, scr);
            if (view.SpeechBoxes.Count == 0) continue;
            tagsSeen += view.Tags.Count;
            foreach (var tag in view.Tags)
                foreach (var box in view.SpeechBoxes)
                    Assert.False(tag.Overlaps(box), $"speaker at {y}: tag {tag} covers box {box}");
        }
        Assert.True(tagsSeen > 0, "the stage banner, SHIP DOWN and the toast were drawn beside a box");
    }

    /// <summary>The stage a run is started at to show the stage banner, and the pixels a swept speaker moves between frames.</summary>
    private const int BannerStage = 2, SweepStepPx = 3;

    [Theory]
    [InlineData(PhoneCols, PhoneRows)]
    [InlineData(WideCols, WideRows)]
    public void The_stage_banner_is_drawn_on_the_nearest_row_clear_of_a_speech_box_standing_where_it_goes(int cols, int rows)
    {
        var game = Offered(DarknessKind.Rude, LongPost, cols, rows);
        var speaker = game.Enemies.First(EnemySpeechBox.IsOpen);
        game.Enemies.RemoveAll(other => other != speaker);
        game.StartAtStage(BannerStage);
        var view = new RaiderView();
        var scr = new Screen(cols, rows);
        int bannerRow = RaiderView.BannerRow(game.Layout);
        string title = RaiderView.StageTitle(BannerStage);
        int moved = 0;
        for (double y = EnemySpeechBox.OpenY; y < game.PH; y += SweepStepPx)
        {
            speaker.X = game.PW / 2.0; speaker.Y = y;
            view.FitAndRender(game, scr);
            if (view.SpeechBoxes.Count == 0) continue;
            // the HUD's stage readout says the same words on the top row: the banner is the first field row below the HUD that says them
            int titleRow = Array.FindIndex(Rows(scr), RaiderView.HudRows, row => row.Contains(title, StringComparison.Ordinal));
            Assert.True(titleRow >= 0, $"speaker at {y}: no row shows {title}");
            var banner = Assert.Single(view.Tags, tag => tag.R == titleRow);
            foreach (var box in view.SpeechBoxes)
                Assert.False(banner.Overlaps(box), $"speaker at {y}: the banner at {banner} covers the box at {box}");
            bool homeCovered = view.SpeechBoxes.Any(box => box.Overlaps(banner with { R = bannerRow }));
            Assert.True(homeCovered == (titleRow != bannerRow), $"speaker at {y}: the banner is on row {titleRow}, its own row is {bannerRow}");
            if (titleRow != bannerRow) moved++;
        }
        Assert.True(moved > 0, "a box stood where the banner goes on some frame, and the banner moved off it");
    }

    [Fact]
    public void At_most_the_cap_of_boxes_show_at_once()
    {
        var game = OpenBoxGame(DarknessKind.Rude, RudeLabel);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        const int spacingPx = 12;
        const double rowY = 20;
        for (int i = 0; i < Tuning.MaxSpeechBoxes + 1; i++)
        {
            var other = new Enemy { Kind = enemy.Kind, Sprite = enemy.Sprite, X = LeftX + i * spacingPx, Y = rowY, Text = enemy.Text, Header = RudeHeader, Live = true };
            EnemySpeechBox.Update(other, BoxCols, TerminalRows); other.SpeechBoxAge = JustOpenedAge; game.Enemies.Add(other);
        }
        Assert.True(EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes).Count <= Tuning.MaxSpeechBoxes);
    }

    private const string LongPost = "honestly i cannot believe how little you people understand about anything at all, every single take you post is worse than the last and the worst part is you think everyone is impressed by how loud you are";

    private static string Squash(IEnumerable<string> lines) => string.Join(' ', lines);

    [Theory]
    [InlineData(DarknessKind.Rude)]
    [InlineData(DarknessKind.Spam)]
    public void A_long_post_comes_through_the_wave_and_shows_in_full_with_no_ellipsis(DarknessKind kind)
    {
        var spec = WaveSpawner.FromPost(new DarknessPost(kind, PostSeed, LongPost, DarknessSource.Label, AnyLabel));
        if (kind == DarknessKind.Rude) Assert.Empty(spec.Enemies);   // a rude post makes no enemy
        else Assert.Contains(spec.Enemies, spawn => spawn.Text == LongPost);
        const int fieldCols = 80, fieldRows = 40;
        var game = Offered(kind, LongPost, fieldCols, fieldRows);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        Assert.Equal(LongPost, Squash(enemy.SpeechLines!));
        Assert.DoesNotContain(Ellipsis, Squash(enemy.SpeechLines!));
        Assert.Equal(1, SpeechBoxText.Pages(enemy.SpeechLines!, enemy.PageLines));
        enemy.SpeechBoxAge = EnemySpeechBox.Frames(enemy) - 1;   // typed out
        game.Enemies.RemoveAll(other => other != enemy);      // units draw over boxes; clear the escort so the words are readable
        game.Bullets.Clear();
        var scr = Render(game, fieldCols, fieldRows);
        var rows = Rows(scr);
        foreach (var line in enemy.SpeechLines!) Assert.Contains(rows, row => row.Contains(line));
        Assert.DoesNotContain(rows, row => row.Contains(Ellipsis));
    }

    [Fact]
    public void An_overlong_post_pages_and_is_never_cut()
    {
        const int overlongWords = 120;
        string text = string.Join(' ', Enumerable.Range(1, overlongWords).Select(number => "word" + number));
        var enemy = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = SpotX, Y = SpotY, Header = RudeHeader, Live = true, Text = text };
        EnemySpeechBox.Update(enemy, BoxCols, TerminalRows);
        Assert.Equal(text, Squash(enemy.SpeechLines!));
        int pages = SpeechBoxText.Pages(enemy.SpeechLines!, enemy.PageLines);
        Assert.True(pages > 1);
        Assert.InRange(enemy.PageLines, SpeechBoxText.MinPageLines, SpeechBoxText.TextLines);
        Assert.Equal(0, SpeechBoxText.Progress(enemy.SpeechLines!, enemy.PageLines, 0).Page);
        Assert.Equal(pages - 1, SpeechBoxText.Progress(enemy.SpeechLines!, enemy.PageLines, EnemySpeechBox.Frames(enemy) - 1).Page);
    }

    [Theory]
    [InlineData(TerminalCols, TerminalRows)]
    [InlineData(PhoneCols, PhoneRows)]
    public void The_box_of_a_long_post_fits_the_smallest_fields(int cols, int rows)
    {
        const int longPostCopies = 3;
        var game = new RaiderGame(Seed);
        game.SetLayout(FieldLayout.Compute(cols, rows));
        string text = string.Join(' ', Enumerable.Repeat(LongPost, longPostCopies));
        var enemy = new Enemy { Kind = EnemyKind.Gunship, Sprite = Art.Gunship, X = game.Layout.PW / 2.0, Y = game.Layout.PH / 2.0, Header = RudeHeader, Live = true, Text = text };
        EnemySpeechBox.Update(enemy, game.Layout.PCols, game.Layout.Rows);
        var box = EnemySpeechBox.Box(enemy, game.Layout);
        Assert.True(box.C >= game.Layout.Col0 && box.Right <= game.Layout.Col0 + game.Layout.PCols);
        Assert.True(box.R >= EnemySpeechBox.MinRow && box.Bottom <= game.Layout.Rows, $"box {box} in {game.Layout.Rows} rows");
        Assert.True(box.H <= game.Layout.Rows / 2, "no taller than half the field");
    }

    private const int Post300Chars = 300;
    private static readonly string Post300 = string.Join(' ', Enumerable.Range(1, Post300Chars).Select(number => "w" + number)).Substring(0, Post300Chars).TrimEnd();

    [Theory]
    [InlineData(DarknessKind.Rude, PhoneCols, PhoneRows)]
    [InlineData(DarknessKind.Rude, WideCols, WideRows)]
    [InlineData(DarknessKind.Spam, PhoneCols, PhoneRows)]
    [InlineData(DarknessKind.Spam, WideCols, WideRows)]
    public void A_300_character_post_shows_every_word_across_its_pages_with_no_ellipsis(DarknessKind kind, int cols, int rows)
    {
        string post = DarknessText.Clean(Post300, DarknessText.MaxPostChars);
        Assert.Equal(Post300, post);
        var game = Offered(kind, Post300, cols, rows);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        Assert.Equal(kind == DarknessKind.Rude ? EnemyKind.Gunship : EnemyKind.Carrier, enemy.Kind);
        Assert.Equal(Post300, Squash(enemy.SpeechLines!));
        Assert.DoesNotContain(Ellipsis, Squash(enemy.SpeechLines!));
        int pages = SpeechBoxText.Pages(enemy.SpeechLines!, enemy.PageLines);
        game.Enemies.RemoveAll(other => other != enemy); game.Bullets.Clear();
        var seen = new List<string>();
        for (int page = 0; page < pages; page++)
        {
            enemy.SpeechBoxAge = page == pages - 1
                ? EnemySpeechBox.Frames(enemy) - 1
                : SpeechBoxText.TotalFrames(enemy.SpeechLines!.Take((page + 1) * enemy.PageLines).ToArray(), enemy.PageLines, 0) - Tuning.SpeechBoxPageHoldFrames - 1;
            var scr = Render(game, cols, rows);
            var painted = Rows(scr);
            Assert.DoesNotContain(painted, row => row.Contains(Ellipsis));
            for (int i = page * enemy.PageLines; i < Math.Min(enemy.SpeechLines!.Length, (page + 1) * enemy.PageLines); i++)
            {
                Assert.Contains(painted, row => row.Contains(enemy.SpeechLines[i]));
                seen.Add(enemy.SpeechLines[i]);
            }
        }
        Assert.Equal(Post300, Squash(seen));
    }

    [Fact]
    public void A_lesser_gunship_killed_mid_post_keeps_its_box_until_every_page_is_read()
    {
        var game = Offered(DarknessKind.Rude, Post300, PhoneCols, PhoneRows);
        var enemy = game.Enemies.First(EnemySpeechBox.IsOpen);
        int total = EnemySpeechBox.Frames(enemy);
        enemy.Hp = 0; game.Enemies.Remove(enemy); EnemySpeechBox.Bury(game, enemy);
        Assert.Contains(enemy, game.Echoes);
        game.Enemies.Clear();
        const int maxSteps = 100_000, shortOfEnd = 3, pastEnd = 10;
        long stop = game.Tick + total - enemy.SpeechBoxAge - shortOfEnd;
        for (int i = 0; i < maxSteps && game.Tick < stop; i++) { game.Step(); game.Enemies.Clear(); }
        Assert.Contains(enemy, game.Echoes);
        stop = game.Tick + pastEnd;
        for (int i = 0; i < maxSteps && game.Tick < stop; i++) { game.Step(); game.Enemies.Clear(); }
        Assert.DoesNotContain(enemy, game.Echoes);
    }
}
