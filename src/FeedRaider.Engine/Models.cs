namespace FeedRaider.Engine;

/// <summary>An account in the room: the DID is identity, the handle is just a label.</summary>
public sealed record Follow(string Did, string Handle);

/// <summary>A post seen on Jetstream. <see cref="ReplyParentDid"/> is the author of the post being
/// replied to (taken from record.reply.parent.uri), or null for a plain post.</summary>
public sealed record PostEvent(string Did, string Text, string? ReplyParentDid, long TimeUs);

/// <summary>Where finished ANSI frames go. A console writes them to stdout, the web host to xterm.js.</summary>
public interface IFrameSink
{
    void Write(string frame);
}

/// <summary>Pushes post events at the engine. Implementations own their connection.</summary>
public interface IEventSource
{
    IAsyncEnumerable<PostEvent> ReadAsync(CancellationToken ct);
}

/// <summary>An event source that can say what its connection is doing, for the status line.</summary>
public interface IHasState
{
    string State { get; }
}
