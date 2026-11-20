namespace FeedRaider.Engine.Raider;

/// <summary>The speech box of a darkness enemy that carries text, drawn over the enemy. It sits above the enemy with its
/// pointer facing down, and the label ("SPAM", "RUDE", the labeler's own value, <see cref="HiddenReplyHeader"/>) stands where an author's name would.
/// The author is never known here, so there is nothing else to show. Only carriers and gunships carry text, so only they speak;
/// the other enemies of a wave are bare sprites.</summary>
public static class EnemySpeechBox
{
    /// <summary>The rows the HUD takes at the top of the screen, as the view draws it.</summary>
    public const int HudRows = RaiderView.HudRows;
    /// <summary>The row under the HUD, where a boss high on the field has its health bar drawn.</summary>
    private const int HealthBarRows = 1;
    /// <summary>The first row a box may take: under the HUD and the boss's health bar row.</summary>
    public const int MinRow = HudRows + HealthBarRows;
    /// <summary>The most characters a header keeps before it is clipped.</summary>
    public const int MaxLabel = 24;
    /// <summary>The cells a box adds to its widest line: an edge and a blank cell on each side.</summary>
    public const int TextSide = 4;
    /// <summary>The rows a box adds to its text: the top edge, the title and the bottom edge.</summary>
    public const int Chrome = 3;
    /// <summary>The age a box opens at: a box of age 0 has not started typing.</summary>
    private const int OpenAge = 1;
    /// <summary>The fewest text cells a box is wide, so a blank title over blank lines still makes a box.</summary>
    private const int MinTextCells = 1;
    /// <summary>Rows left clear between an enemy's lowest cell and a box put below it.</summary>
    private const int BelowGapRows = 1;
    /// <summary>The header a hidden reply gets when its label is blank.</summary>
    private const string HiddenReplyHeader = "HIDDEN REPLY";
    /// <summary>How far onto the field (in pixel rows) an enemy flies before its box starts typing, so the first words aren't lost above the top.</summary>
    public const double OpenY = 6;
    /// <summary>Cells kept clear around the ship's sprite when judging whether a box covers it.</summary>
    private const int ShipMarginCells = 1;
    /// <summary>The cell the ship's box gains on its far side, which flooring its near corner to a cell can lose.</summary>
    private const int FlooredCornerCells = 1;
    /// <summary>Pixel rows kept between a buried enemy pinned inside the field and the field's bottom edge, so its echo stays on screen.</summary>
    public const double BuryBottomMargin = 4;
    /// <summary>A few fixed solid colours, so the glyph atlas gains a handful of entries and no more.</summary>
    public const uint BoxBg = 0x140608, Edge = 0xff2a2a, HeaderFg = 0xff5050, TextFg = 0xffe4e4, UnreadFg = 0xc08a8a;

    /// <summary>The header for a label value (blank falls back to <see cref="HiddenReplyHeader"/> for a hidden reply, else the kind's name).</summary>
    public static string Header(string? label, DarknessKind kind, DarkSource source)
    {
        string text = CellText.Clean(label);   // what the terminal cannot draw is dropped, so a label of only that falls back too
        if (text.Length == 0) text = source == DarkSource.Hidden ? HiddenReplyHeader : kind.ToString();
        return CellText.Clip(text.ToUpperInvariant(), MaxLabel, ellipsis: true);
    }

    /// <summary>Gives an enemy words under a header of the caller's choosing and restarts its box, even if it had none: the cached
    /// wrap and the age are cleared so the box types it out again.</summary>
    public static void Say(Enemy enemy, string header, string text)
    {
        enemy.Header = header;
        enemy.Text = text;
        enemy.SpeechBoxAge = 0;
        enemy.SpeechLines = null;
        enemy.SpeechBoxShift = default;
        enemy.SpeechBoxHeld = false;
    }

    /// <summary>What the box's title row reads: the speaker's own prefix (a counter), then the header.</summary>
    public static string Title(Enemy enemy) =>
        enemy.Voice?.Prefix is { Length: > 0 } prefix ? prefix + " " + enemy.Header : enemy.Header ?? "";

    public static bool Speaks(Enemy enemy) => enemy.Header is not null && enemy.Text is { Length: > 0 };

    /// <summary>Frames the box stays open: the typing speed, then the hold.</summary>
    public static int Frames(Enemy enemy) => SpeechBoxText.TotalFrames(enemy.SpeechLines ?? [], enemy.PageLines, tail: 0);

    /// <summary>Lines of text on one page: about half the field less the chrome, so a rare overlong post pages.</summary>
    public static int PageLinesFor(int fieldRows) => Math.Clamp(fieldRows / 2 - Chrome, SpeechBoxText.MinPageLines, SpeechBoxText.TextLines);

    /// <summary>Caches the wrapped text (once) and ages the box while the enemy is on the field.</summary>
    public static void Update(Enemy enemy, int fieldCols, int fieldRows, bool hold = false)
    {
        if (!Speaks(enemy)) return;
        if (enemy.SpeechLines is null)
        {
            enemy.SpeechLines = CellText.Wrap(CellText.Clean(enemy.Text), SpeechBoxText.TextWidth(fieldCols)).ToArray();   // by cells, no line cap: a post is never cut; one with nothing drawable has no lines, and no box
            enemy.PageLines = PageLinesFor(fieldRows);
        }
        if (enemy.Y >= OpenY && !hold) enemy.SpeechBoxAge++;
    }

    /// <summary>An enemy leaving the field (killed, flown off) with words still to read: a copy of it, pinned inside the field,
    /// goes to <see cref="RaiderGame.Echoes"/> and keeps the box up until every page has been read out.</summary>
    public static void Bury(RaiderGame game, Enemy enemy)
    {
        if (!Speaks(enemy)) return;
        Update(enemy, game.Layout.PCols, game.Layout.Rows);
        if (enemy.SpeechLines is not { Length: > 0 } || enemy.SpeechBoxAge >= Frames(enemy)) return;
        enemy.SpeechBoxAge = Math.Max(enemy.SpeechBoxAge, OpenAge);
        enemy.X = Math.Clamp(enemy.X, 0, game.Layout.PW);
        enemy.Y = Math.Clamp(enemy.Y, OpenY, game.Layout.PH - BuryBottomMargin);
        game.Echoes.Add(enemy);
    }

    public static bool IsOpen(Enemy enemy) => Speaks(enemy) && enemy.SpeechLines is { Length: > 0 } && enemy.SpeechBoxAge >= OpenAge && enemy.SpeechBoxAge < Frames(enemy);

    /// <summary>The highest and lowest pixel rows of everything the enemy shows: its sprite and the art of each of its parts.</summary>
    public static (double Top, double Bottom) Shown(Enemy enemy)
    {
        double top = enemy.Y - enemy.Sprite.H / 2.0, bottom = enemy.Y + enemy.Sprite.H / 2.0;
        foreach (var part in enemy.Parts ?? [])
        {
            var art = RaiderView.PartArt(part);
            if (art is null) continue;
            top = Math.Min(top, enemy.Y + part.OY - art.H / 2.0); bottom = Math.Max(bottom, enemy.Y + part.OY + art.H / 2.0);
        }
        return (top, bottom);
    }

    /// <summary>The cells of the box: centred over the enemy, its bottom edge on the row above the sprite, clamped inside the play
    /// columns and from <see cref="MinRow"/> down.</summary>
    public static CellRect Box(Enemy enemy, FieldLayout lay)
    {
        int textW = Math.Max(Math.Max(CellText.Width(Title(enemy)), enemy.SpeechLines!.Max(CellText.Width)), MinTextCells);
        int width = Math.Min(textW + TextSide, lay.PCols), height = Math.Min(enemy.SpeechLines!.Length, enemy.PageLines) + Chrome;
        int col = Math.Clamp(lay.Col0 + (int)Math.Floor(enemy.X / CardField.PixelsPerCell) - width / 2, lay.Col0, Math.Max(lay.Col0, lay.Col0 + lay.PCols - width));
        var shown = Shown(enemy);   // a thing made of parts is as tall as its parts
        int top = (int)Math.Floor(shown.Top / CardField.PixelsPerCell);
        int row = Math.Max(MinRow, top - height);
        if (row + height > top)
        {
            // no room above: it would sit on the sprite, so it goes below when it fits there (else it stays pinned and the sprite draws over it)
            int below = (int)Math.Ceiling(shown.Bottom / CardField.PixelsPerCell) + BelowGapRows;
            if (below + height <= lay.Rows) row = below;
        }
        return new CellRect(col, row, width, height);
    }

    /// <summary>True when <paramref name="box"/> sits below the enemy (pointer facing up), because there was no room above it.</summary>
    public static bool IsBelow(Enemy enemy, CellRect box) => box.R * CardField.PixelsPerCell >= Shown(enemy).Bottom;

    /// <summary>The cells round the ship that no box may cover.</summary>
    public static CellRect ShipCells(RaiderGame game, FieldLayout lay) =>
        new(lay.Col0 + (int)Math.Floor((game.PX - game.Ship.W / 2.0) / CardField.PixelsPerCell) - ShipMarginCells, (int)Math.Floor((game.PY - game.Ship.H / 2.0) / CardField.PixelsPerCell) - ShipMarginCells,
            game.Ship.W / CardField.PixelsPerCell + 2 * ShipMarginCells + FlooredCornerCells, game.Ship.H / CardField.PixelsPerCell + 2 * ShipMarginCells + FlooredCornerCells);

    /// <summary>The boxes on show and the cells each takes. Of the open boxes the newest <paramref name="max"/> (the lowest age) are
    /// taken; a box whose home (<see cref="Box"/>) lies over the ship is left out. The oldest keeps its place, and each newer box goes
    /// where it covers neither the ship nor a box already placed: its home, or the place it was moved to before, or else the nearest
    /// place clear of them. So no box ever covers another's words, an echo's or a live enemy's. A box with no clear place anywhere is
    /// left out, and <see cref="Settle"/> holds it unread until there is room.</summary>
    public static List<(Enemy Enemy, CellRect Box)> Placed(RaiderGame game, FieldLayout lay, int max)
    {
        var ship = ShipCells(game, lay);
        var placed = new List<(Enemy Enemy, CellRect Box)>();
        foreach (var enemy in Enumerable.Reverse(Taken(game, lay, max, ship)))
            if (FindClearPlace(Box(enemy, lay), enemy.SpeechBoxShift, lay, ship, placed) is { } box) placed.Add((enemy, box));
        return placed;
    }

    /// <summary>The open boxes whose home is clear of the ship, the newest <paramref name="max"/> of them, the newest first.</summary>
    private static List<Enemy> Taken(RaiderGame game, FieldLayout lay, int max, CellRect ship) =>
        game.Enemies.Concat(game.Echoes).Where(enemy => IsOpen(enemy) && !Box(enemy, lay).Overlaps(ship)).OrderBy(enemy => enemy.SpeechBoxAge).Take(max).ToList();

    /// <summary>Where a box whose home is <paramref name="home"/> goes, clear of the ship and of the boxes placed: its home moved by
    /// <paramref name="shift"/>, its home, or the nearest of the places beside, above or below one of those boxes. Null when none is clear.</summary>
    private static CellRect? FindClearPlace(CellRect home, (int Col, int Row) shift, FieldLayout lay, CellRect ship, List<(Enemy Enemy, CellRect Box)> placed)
    {
        int lastCol = Math.Max(lay.Col0, lay.Col0 + lay.PCols - home.W), lastRow = Math.Max(MinRow, lay.Rows - home.H);
        bool Fits(CellRect box) => box.C >= lay.Col0 && box.C <= lastCol && box.R >= MinRow && box.R <= lastRow && !box.Overlaps(ship) && !placed.Exists(other => box.Overlaps(other.Box));
        var moved = home with { C = home.C + shift.Col, R = home.R + shift.Row };
        if (Fits(moved)) return moved;
        if (Fits(home)) return home;
        var cols = new List<int> { home.C };
        var rows = new List<int> { home.R };
        foreach (var other in placed.Select(shown => shown.Box).Append(ship))
        {
            cols.Add(Math.Clamp(other.C - home.W, lay.Col0, lastCol)); cols.Add(Math.Clamp(other.Right, lay.Col0, lastCol));
            rows.Add(Math.Clamp(other.R - home.H, MinRow, lastRow)); rows.Add(Math.Clamp(other.Bottom, MinRow, lastRow));
        }
        CellRect? best = null;
        int bestMove = int.MaxValue;
        foreach (int row in rows)
            foreach (int col in cols)
            {
                var box = home with { C = col, R = row };
                int move = Math.Abs(col - home.C) + Math.Abs(row - home.R);
                if (move < bestMove && Fits(box)) { best = box; bestMove = move; }
            }
        return best;
    }

    /// <summary>Once a frame: each box on show keeps the place it was given, so it stays there while it is read, and each open box
    /// with no clear place is held (<see cref="Enemy.SpeechBoxHeld"/>): it is not aged until it shows, so none of its words are lost.</summary>
    public static void Settle(RaiderGame game)
    {
        var lay = game.Layout;
        foreach (var enemy in game.Enemies.Concat(game.Echoes)) enemy.SpeechBoxHeld = false;
        // a box left out over the ship, or past the most boxes on show, ages normally: only a box with no room is held
        foreach (var enemy in Taken(game, lay, Tuning.MaxSpeechBoxes, ShipCells(game, lay))) enemy.SpeechBoxHeld = true;
        foreach (var (enemy, box) in Placed(game, lay, Tuning.MaxSpeechBoxes))
        {
            var home = Box(enemy, lay);
            enemy.SpeechBoxShift = (box.C - home.C, box.R - home.R);
            enemy.SpeechBoxHeld = false;
        }
    }
}
