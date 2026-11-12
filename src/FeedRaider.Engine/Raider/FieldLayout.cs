namespace FeedRaider.Engine.Raider;

/// <summary>Where the vertical playfield sits in the terminal: centred, at most 64 columns wide, full height.</summary>
public readonly record struct FieldLayout(int Cols, int Rows, int Col0, int PCols, int PRows)
{
    public int PW => PCols * 2;
    public int PH => PRows * 2;
    public bool FitsMinimum => PCols >= Tuning.FieldMinCols && PRows >= Tuning.FieldMinRows;
    /// <summary>Width in columns of each side column, or 0 when the margin left of the field (<see cref="Col0"/> spare columns) cannot hold one: as wide as the margin allows
    /// after the gap to the field and one spare column, up to <see cref="Tuning.SidePanelMaxCols"/>.</summary>
    public int PanelCols
    {
        get
        {
            int w = Math.Min(Tuning.SidePanelMaxCols, Col0 - Tuning.SideGapCols - 1);
            return w >= Tuning.SidePanelMinCols ? w : 0;
        }
    }

    public static FieldLayout Compute(int cols, int rows)
    {
        int pc = Math.Min(cols, Tuning.FieldMaxCols) & ~1;
        return new FieldLayout(cols, rows, (cols - pc) / 2, pc, rows);
    }

    /// <summary>A terminal cell position (fractional) as a playfield pixel position.</summary>
    public (double X, double Y) ToField(double col, double row) => ((col - Col0) * 2, row * 2);
}
