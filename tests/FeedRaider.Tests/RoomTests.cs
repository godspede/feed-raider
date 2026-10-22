using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class RoomTests
{
    private static Room Make(params string[] names) =>
        new(names.Select(n => new Follow("did:plc:" + n, n + ".test")));

    private static PostEvent Post(string name, string text, string? parent = null) =>
        new("did:plc:" + name, text, parent is null ? null : "did:plc:" + parent, 0);

    [Fact]
    public void Plain_post_is_a_bubble_in_place()
    {
        var room = Make("alice111", "bob22222");
        var a = room.Handle(Post("alice111", "hi"), 1000);
        Assert.Equal(ActionKind.Say, a!.Kind);
        Assert.Null(a.TargetDid);
    }

    [Fact]
    public void Reply_to_someone_in_the_room_walks_to_them()
    {
        Assert.True(JetstreamParser.TryParse(JetstreamParserTests.Fixture("reply.json"), out var e));
        var room = Make("alice111", "bob22222");
        var a = room.Handle(e!, 1000);
        Assert.Equal(ActionKind.Visit, a!.Kind);
        Assert.Equal("did:plc:alice111", a.Did);
        Assert.Equal("did:plc:bob22222", a.TargetDid);
    }

    [Fact]
    public void Reply_to_someone_outside_the_room_or_to_self_stays_put()
    {
        var room = Make("alice111", "bob22222");
        Assert.Equal(ActionKind.Say, room.Handle(Post("alice111", "x", "stranger"), 0)!.Kind);
        Assert.Equal(ActionKind.Say, room.Handle(Post("bob22222", "x", "bob22222"), 0)!.Kind);
    }

    [Fact]
    public void Posts_from_strangers_are_ignored()
    {
        Assert.Null(Make("alice111").Handle(Post("nobody", "hi"), 0));
    }

    [Fact]
    public void Visit_runs_its_course_then_the_visitor_is_idle_again()
    {
        var room = Make("alice111", "bob22222", "carol333");
        room.Handle(Post("alice111", "hey bob", "bob22222"), 10_000);
        room.Render(80, 24, 15_000); // walk (<= 3.5 s) is over, she is talking
        Assert.Equal(ActionKind.Visit, room.CurrentAction("did:plc:alice111")!.Kind);

        room.Render(80, 24, 10_000 + 2 * 3500 + Room.BubbleMs + 1);
        Assert.Null(room.CurrentAction("did:plc:alice111"));
    }

    [Fact]
    public void Visitor_stands_next_to_the_target_while_talking()
    {
        var room = Make("alice", "bob");
        room.Handle(Post("alice", "hey bob", "bob"), 0);
        string frame = room.Render(80, 24, 4000);
        Assert.Contains("hey bob", frame);
        Assert.Contains("alice.test", frame);
        Assert.Contains("bob.test", frame);
    }

    [Fact]
    public void Thirty_guys_fit_on_80x24_and_frames_have_the_right_shape()
    {
        var room = Make(Enumerable.Range(0, 30).Select(i => $"user{i:D4}").ToArray());
        string frame = room.Render(80, 24, 12_345);
        Assert.Equal(23, frame.Count(c => c == '\n')); // 24 rows, the last without newline
        Assert.Contains("\x1b[48;2;", frame);
    }

    [Fact]
    public void Full_room_seats_a_speaker_who_has_no_seat()
    {
        var room = Make(Enumerable.Range(0, 100).Select(i => $"u{i:D4}").ToArray());
        room.Render(80, 24, 0);
        room.Handle(Post("u0099", "let me in"), 1000);
        Assert.Contains("let me in", room.Render(80, 24, 2000));
    }

    [Fact]
    public void Looks_are_stable_and_mostly_distinct()
    {
        Assert.Equal(Look.From("did:plc:a", "a.test"), Look.From("did:plc:a", "a.test"));
        var looks = Enumerable.Range(0, 30).Select(i => Look.From($"did:plc:user{i}", $"user{i}.test")).Distinct().Count();
        Assert.True(looks >= 28, $"only {looks} distinct looks");
    }

    [Theory]
    [InlineData("a very long message that just keeps going and going and going and going until it must be cut off somewhere")]
    [InlineData("\U0001F642 emoji and 漢字 and\nnewlines\tand tabs")]   // \U0001F642 slightly smiling face
    [InlineData("")]
    [InlineData("Supercalifragilisticexpialidocious_Supercalifragilisticexpialidocious")]
    public void Bubble_text_is_short_and_single_width(string text)
    {
        var lines = BubbleText.Format(text);
        Assert.InRange(lines.Length, 1, 3);
        Assert.All(lines, l => { Assert.True(l.Length <= 22); Assert.DoesNotContain('\n', l); });
    }
}
