namespace FeedRaider.Engine.Raider;

/// <summary>Draws the speech box of each enemy that talks. The box is a solid box over the field, cropped by the field's edge;
/// the view then lays the enemies, the ship, the pickups and every shot and bolt back over it (<see cref="Boxes"/>), so a bolt or a
/// warning is never hidden by words.</summary>
public sealed class SpeechBoxPainter
{
    /// <summary>Cells between a box's left edge and its words, and between its right edge and the page count.</summary>
    private const int TextInset = 2, PageCountInset = 5;
    /// <summary>The box's rows: the title one row down from the top edge, the text from two down.</summary>
    private const int TitleRow = 1, FirstTextRow = 2;

    private Screen _scr = null!;
    private CellRect _clip;

    /// <summary>The boxes drawn by the last <see cref="Draw"/>, so the view can lay the units back over them.</summary>
    public List<CellRect> Boxes { get; } = [];

    public void Draw(RaiderGame game, Screen scr, FieldLayout lay)
    {
        _scr = scr;
        Boxes.Clear();
        _clip = new CellRect(lay.Col0, EnemySpeechBox.HudRows, lay.PCols, Math.Min(lay.Rows, scr.Rows) - EnemySpeechBox.HudRows);
        foreach (var (enemy, box) in EnemySpeechBox.Placed(game, lay, Tuning.MaxSpeechBoxes)) DrawEnemySpeechBox(enemy, box);
    }

    /// <summary>One char of a speech box on the box's ground: dropped outside the field (the crop). A wide char needs both its
    /// cells inside it.</summary>
    private void PutCropped(int col, int row, char ch, uint fg)
    {
        if (col < _clip.C || col >= _clip.Right || row < _clip.R || row >= _clip.Bottom) return;
        if (CellWidth.Of(ch) == CellText.WideCells && col + 1 >= _clip.Right) return;
        _scr.Put(col, row, ch, fg, EnemySpeechBox.BoxBg);
    }

    private void TextCropped(int col, int row, string text, uint fg)
    {
        foreach (char ch in text)
        {
            PutCropped(col, row, ch, fg);
            col += CellWidth.Of(ch);
        }
    }

    /// <summary>An enemy's speech box: the label for a name, red on near-black, a pointer facing the enemy.</summary>
    private void DrawEnemySpeechBox(Enemy enemy, CellRect box)
    {
        if (!box.Overlaps(_clip)) return;
        Boxes.Add(box);
        DrawBox(box);
        int pointerCol = box.C + box.W / 2;
        if (EnemySpeechBox.IsBelow(enemy, box)) PutCropped(pointerCol, box.R, '▲', EnemySpeechBox.Edge);
        else PutCropped(pointerCol, box.Bottom - 1, '▼', EnemySpeechBox.Edge);
        TextCropped(box.C + TextInset, box.R + TitleRow, EnemySpeechBox.Title(enemy), EnemySpeechBox.HeaderFg);
        var (page, shown) = SpeechBoxText.Progress(enemy.SpeechLines!, enemy.PageLines, enemy.SpeechBoxAge);
        bool cursorToDraw = true;
        for (int i = 0, first = page * enemy.PageLines; i < enemy.PageLines && first + i < enemy.SpeechLines!.Length; i++)
        {
            string line = enemy.SpeechLines[first + i];
            int read = Math.Min(line.Length, shown);
            shown -= read;
            int cell = box.C + TextInset, cursorAt = cell;   // a wide char takes two cells, so the column is the cells so far, not the chars
            for (int index = 0; index < line.Length; index++)
            {
                if (index == read) cursorAt = cell;
                PutCropped(cell, box.R + FirstTextRow + i, line[index], index < read ? EnemySpeechBox.TextFg : EnemySpeechBox.UnreadFg);
                cell += CellWidth.Of(line[index]);
            }
            if (cursorToDraw && read < line.Length) { PutCropped(cursorAt, box.R + FirstTextRow + i, '▌', EnemySpeechBox.Edge); cursorToDraw = false; }
        }
        int pages = SpeechBoxText.Pages(enemy.SpeechLines!, enemy.PageLines);
        if (pages > 1) TextCropped(box.Right - PageCountInset, box.Bottom - 1, $"{page + 1}/{pages}", EnemySpeechBox.UnreadFg);
    }

    /// <summary>The box's ground and its edge, in the edge's colour.</summary>
    private void DrawBox(CellRect box)
    {
        const uint edge = EnemySpeechBox.Edge;
        for (int y = 0; y < box.H; y++) for (int x = 0; x < box.W; x++) PutCropped(box.C + x, box.R + y, ' ', edge);
        for (int x = 1; x < box.W - 1; x++) { PutCropped(box.C + x, box.R, '─', edge); PutCropped(box.C + x, box.Bottom - 1, '─', edge); }
        for (int y = 1; y < box.H - 1; y++) { PutCropped(box.C, box.R + y, '│', edge); PutCropped(box.Right - 1, box.R + y, '│', edge); }
        PutCropped(box.C, box.R, '┌', edge); PutCropped(box.Right - 1, box.R, '┐', edge);
        PutCropped(box.C, box.Bottom - 1, '└', edge); PutCropped(box.Right - 1, box.Bottom - 1, '┘', edge);
    }
}
