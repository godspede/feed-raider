using System.Text;
using FeedRaider.Engine;

namespace FeedRaider.Tests;

public class GameHostSinkTests
{
    /// <summary>Counts the bytes that have reached the stream.</summary>
    private sealed class CountingStream : Stream
    {
        public int Bytes;
        public override void Write(byte[] buffer, int offset, int count) => Bytes += count;
        public override void Flush() { }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Fact]
    public void EachFrameReachesTheTerminalAtOnceNotWhenTheBufferFills()
    {
        var stream = new CountingStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), GameHost.StdoutBuffer) { AutoFlush = false };
        IFrameSink sink = new GameHost.Sink(writer);

        sink.Write("frame one");
        Assert.Equal("frame one".Length, stream.Bytes);
        sink.Write("two");
        Assert.Equal("frame onetwo".Length, stream.Bytes);
    }
}
