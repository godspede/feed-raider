using FeedRaider.Engine;
using TUImation;

namespace FeedRaider.Tests;

public class GuyBuilderTests
{
    private static readonly Look Plain = new(new Rgb(200, 100, 50), new Rgb(10, 20, 30), Accessory.Antenna, false, 0);

    // column 0 is outside the body block (x 1 to 10), so only an arm can paint it
    private static int[] ArmRows(bool speaking)
    {
        var c = GuyBuilder.Build(Plain, new Pose(0, 0, speaking, false));
        return Enumerable.Range(0, GuyBuilder.Height).Where(y => c[0, y] is not null).ToArray();
    }

    [Fact]
    public void An_arm_hangs_at_the_side_when_the_guy_is_quiet() => Assert.Equal([7, 8, 9], ArmRows(speaking: false));

    [Fact]
    public void An_arm_is_raised_two_rows_while_the_guy_speaks() => Assert.Equal([5, 6, 7], ArmRows(speaking: true));

    [Fact]
    public void Both_arms_move_together()
    {
        var c = GuyBuilder.Build(Plain, new Pose(0, 0, true, false));
        for (int y = 0; y < GuyBuilder.Height; y++) Assert.Equal(c[0, y] is not null, c[11, y] is not null);
    }
}
