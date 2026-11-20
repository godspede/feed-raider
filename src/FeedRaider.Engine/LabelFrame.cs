using System.Formats.Cbor;

namespace FeedRaider.Engine;

public enum DarknessKind { Spam, Rude }

/// <summary>One label from the moderation labeler. Only what the filter needs: no author is kept beyond the
/// post URI, and the URI is used to fetch text, never to draw.</summary>
public sealed record LabelEvent(string Uri, string Val, bool Neg, long Seq);

/// <summary>Decodes subscribeLabels frames: a DAG-CBOR header map then a body map, concatenated.</summary>
public static class LabelFrame
{
    public static readonly IReadOnlyDictionary<string, DarknessKind> Allowed = new Dictionary<string, DarknessKind>(StringComparer.Ordinal)
    {
        ["spam"] = DarknessKind.Spam,
        ["engagement-farming"] = DarknessKind.Spam,
        ["rude"] = DarknessKind.Rude,
    };

    /// <summary>The sequence number of a frame that carries none.</summary>
    public const long NoSeq = -1;

    // the header's op for a message; an error frame carries -1
    private const long MessageOp = 1;

    /// <summary>Decodes one frame into its #labels labels, or an empty list for any other frame, or on malformed bytes.
    /// <paramref name="seq"/> is the frame's sequence number, or <see cref="NoSeq"/>.</summary>
    public static List<LabelEvent> Decode(ReadOnlyMemory<byte> frame, out long seq)
    {
        seq = NoSeq;
        var result = new List<LabelEvent>();
        try
        {
            var reader = new CborReader(frame, CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
            string? type = null;
            long op = 0;
            int fields = reader.ReadStartMap() ?? 0;
            for (int i = 0; i < fields; i++)
            {
                string key = reader.ReadTextString();
                if (key == "t") type = reader.ReadTextString();
                else if (key == "op") op = reader.ReadInt64();
                else reader.SkipValue();
            }
            reader.ReadEndMap();
            if (op != MessageOp || type != "#labels") return result;

            var labels = new List<LabelEvent>();
            fields = reader.ReadStartMap() ?? 0;
            for (int i = 0; i < fields; i++)
            {
                string key = reader.ReadTextString();
                if (key == "seq") seq = reader.ReadInt64();
                else if (key == "labels")
                {
                    int count = reader.ReadStartArray() ?? 0;
                    for (int j = 0; j < count; j++) labels.Add(ReadLabel(reader));
                    reader.ReadEndArray();
                }
                else reader.SkipValue();
            }
            reader.ReadEndMap();
            long frameSeq = seq;
            result = labels.Select(label => label with { Seq = frameSeq }).ToList();
        }
        catch (Exception error) when (error is CborContentException or InvalidOperationException or FormatException or OverflowException)
        {
            // malformed bytes: nothing was kept (result is only assigned once the whole body has been read)
        }
        return result;
    }

    private static LabelEvent ReadLabel(CborReader reader)
    {
        string uri = "", val = "";
        bool neg = false;
        int fields = reader.ReadStartMap() ?? 0;
        for (int i = 0; i < fields; i++)
        {
            switch (reader.ReadTextString())
            {
                case "uri": uri = reader.ReadTextString(); break;
                case "val": val = reader.ReadTextString(); break;
                case "neg":
                    if (reader.PeekState() == CborReaderState.Boolean) neg = reader.ReadBoolean(); else reader.SkipValue();
                    break;
                default: reader.SkipValue(); break;
            }
        }
        reader.ReadEndMap();
        return new LabelEvent(uri, val, neg, NoSeq);
    }
}
