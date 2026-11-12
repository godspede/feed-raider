using FeedRaider.Engine.Raider;
using Xunit;

namespace FeedRaider.Tests;

public class FieldLayoutPanelTests
{
    [Theory]
    [InlineData(152, 41)]
    [InlineData(228, 44)]
    [InlineData(304, 44)]
    public void The_side_columns_share_one_width_that_the_margin_allows(int cols, int expected)
    {
        var lay = FieldLayout.Compute(cols, 47);
        Assert.Equal(expected, lay.PanelCols);
        Assert.True(lay.Col0 - Tuning.SideGapCols - lay.PanelCols >= 1, "the log keeps a column from the window edge");
    }

    [Theory]
    [InlineData(Tuning.FieldMaxCols + 2 * (Tuning.SidePanelMinCols + Tuning.SideGapCols))]
    [InlineData(122)]
    [InlineData(57)]
    public void Below_the_threshold_there_is_no_side_column(int cols) => Assert.Equal(0, FieldLayout.Compute(cols, 47).PanelCols);

    [Fact]
    public void The_threshold_is_the_minimum_width_plus_the_gap_and_one_spare_column()
    {
        int need = Tuning.SidePanelMinCols + Tuning.SideGapCols + 1;
        Assert.Equal(Tuning.SidePanelMinCols, FieldLayout.Compute(Tuning.FieldMaxCols + 2 * need, 47).PanelCols);
        Assert.Equal(0, FieldLayout.Compute(Tuning.FieldMaxCols + 2 * (need - 1), 47).PanelCols);
    }
}
