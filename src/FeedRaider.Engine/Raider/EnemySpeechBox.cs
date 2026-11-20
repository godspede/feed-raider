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

    /// <summary>The boxes on show: those open, none over the ship, and when more than <paramref name="max"/> are open the
    /// newest <paramref name="max"/> (the lowest age) win. An enemy's box that would lie over a newer enemy's box is left out. They keep the order of the enemies, then the echoes.</summary>
    public static List<Enemy> Visible(RaiderGame game, FieldLayout lay, int max)
    {
        var ship = new CellRect(lay.Col0 + (int)Math.Floor((game.PX - game.Ship.W / 2.0) / CardField.PixelsPerCell) - ShipMarginCells, (int)Math.Floor((game.PY - game.Ship.H / 2.0) / CardField.PixelsPerCell) - ShipMarginCells,
            game.Ship.W / CardField.PixelsPerCell + 2 * ShipMarginCells + FlooredCornerCells, game.Ship.H / CardField.PixelsPerCell + 2 * ShipMarginCells + FlooredCornerCells);
        var list = new List<Enemy>();
        foreach (var enemy in game.Enemies)
            if (IsOpen(enemy) && !Box(enemy, lay).Overlaps(ship)) list.Add(enemy);
        foreach (var enemy in game.Echoes)
            if (IsOpen(enemy) && !Box(enemy, lay).Overlaps(ship)) list.Add(enemy);
        // two enemies' boxes never cover each other's words: where they would, the newer one shows. (A speaker's own echoes are stacked by the voice, and are not judged here.)
        var live = list.Where(game.Enemies.Contains).OrderBy(enemy => enemy.SpeechBoxAge).ToList();
        for (int newer = 0; newer < live.Count; newer++)
            for (int older = live.Count - 1; older > newer; older--)
                if (Box(live[newer], lay).Overlaps(Box(live[older], lay))) { list.Remove(live[older]); live.RemoveAt(older); }
        if (list.Count <= max) return list;
        var newest = list.OrderBy(enemy => enemy.SpeechBoxAge).Take(max).ToHashSet();
        return list.Where(newest.Contains).ToList();
    }
}
