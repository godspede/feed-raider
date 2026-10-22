using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class WanderTests
{
    [Fact]
    public void Same_did_and_time_always_give_same_position()
    {
        for (long t = 0; t < 60_000; t += 1234)
            Assert.Equal(Wander.Offset("did:plc:alice111", t), Wander.Offset("did:plc:alice111", t));
    }

    [Fact]
    public void Two_rooms_agree_on_where_everyone_is()
    {
        var follows = new[] { new Follow("did:plc:b", "b"), new Follow("did:plc:a", "a"), new Follow("did:plc:c", "c") };
        var one = new Room(follows);
        var two = new Room(follows.Reverse());
        foreach (var f in follows)
            Assert.Equal(one.IdlePosition(f.Did, 98_765), two.IdlePosition(f.Did, 98_765));
        Assert.Equal(one.Render(80, 24, 98_765), two.Render(80, 24, 98_765));
    }

    [Fact]
    public void Different_dids_wander_differently_and_time_moves_them()
    {
        Assert.NotEqual(Wander.Offset("did:plc:a", 5000), Wander.Offset("did:plc:b", 5000));
        Assert.NotEqual(Wander.Offset("did:plc:a", 0), Wander.Offset("did:plc:a", 7000));
    }

    [Fact]
    public void Offsets_stay_in_unit_box_and_move_smoothly()
    {
        var prev = Wander.Offset("did:plc:a", 0);
        for (long t = 100; t < 120_000; t += 100)
        {
            var o = Wander.Offset("did:plc:a", t);
            Assert.InRange(o.X, -1, 1);
            Assert.InRange(o.Y, -1, 1);
            Assert.True(Math.Abs(o.X - prev.X) < 0.2 && Math.Abs(o.Y - prev.Y) < 0.2);
            prev = o;
        }
    }
}
