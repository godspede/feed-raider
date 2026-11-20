namespace FeedRaider.Engine.Raider;

/// <summary>Where the vertical field sits in the terminal: centred, at most <see cref="Tuning.FieldMaxCols"/> columns wide, full height.</summary>
public readonly record struct FieldLayout(int Cols, int Rows, int Col0, int PCols)
{
    public int PW => PCols * CardField.PixelsPerCell;
    public int PH => Rows * CardField.PixelsPerCell;
    public bool FitsMinimum => PCols >= Tuning.FieldMinCols && Rows >= Tuning.FieldMinRows;
    /// <summary>Width in columns of each side panel, or 0 when the margin left of the field (<see cref="Col0"/> spare columns) cannot hold one: as wide as the margin allows
    /// after the gap to the field and one spare column, up to <see cref="Tuning.SidePanelMaxCols"/>.</summary>
    public int PanelCols
    {
        get
        {
            int width = Math.Min(Tuning.SidePanelMaxCols, Col0 - Tuning.SideGapCols - 1);
            return width >= Tuning.SidePanelMinCols ? width : 0;
        }
    }

    public static FieldLayout Compute(int cols, int rows)
    {
        int fieldCols = Math.Min(cols, Tuning.FieldMaxCols) & ~1;
        return new FieldLayout(cols, rows, (cols - fieldCols) / 2, fieldCols);
    }

    /// <summary>A terminal cell position (fractional) as a field pixel position.</summary>
    public (double X, double Y) ToField(double col, double row) => ((col - Col0) * CardField.PixelsPerCell, row * CardField.PixelsPerCell);
}
