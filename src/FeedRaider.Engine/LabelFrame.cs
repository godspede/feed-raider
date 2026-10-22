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

    /// <summary>Decodes one frame into its #labels labels, or an empty list for any other frame, or on malformed bytes.
    /// <paramref name="seq"/> is the frame's sequence number (-1 if absent).</summary>
    public static List<LabelEvent> Decode(ReadOnlyMemory<byte> frame, out long seq)
    {
        seq = -1;
        var result = new List<LabelEvent>();
        try
        {
            var r = new CborReader(frame, CborConformanceMode.Lax, allowMultipleRootLevelValues: true);
            string? type = null;
            long op = 0;
            int n = r.ReadStartMap() ?? 0;
            for (int i = 0; i < n; i++)
            {
                string k = r.ReadTextString();
                if (k == "t") type = r.ReadTextString();
                else if (k == "op") op = r.ReadInt64();
                else r.SkipValue();
            }
            r.ReadEndMap();
            if (op != 1 || type != "#labels") return result;

            var labels = new List<LabelEvent>();
            n = r.ReadStartMap() ?? 0;
            for (int i = 0; i < n; i++)
            {
                string k = r.ReadTextString();
                if (k == "seq") seq = r.ReadInt64();
                else if (k == "labels")
                {
                    int m = r.ReadStartArray() ?? 0;
                    for (int j = 0; j < m; j++) labels.Add(ReadLabel(r));
                    r.ReadEndArray();
                }
                else r.SkipValue();
            }
            r.ReadEndMap();
            long s = seq;
            result = labels.Select(l => l with { Seq = s }).ToList();
        }
        catch (Exception e) when (e is CborContentException or InvalidOperationException or FormatException or OverflowException)
        {
            result.Clear();
        }
        return result;
    }

    private static LabelEvent ReadLabel(CborReader r)
    {
        string uri = "", val = "";
        bool neg = false;
        int n = r.ReadStartMap() ?? 0;
        for (int i = 0; i < n; i++)
        {
            string k = r.ReadTextString();
            switch (k)
            {
                case "uri": uri = r.ReadTextString(); break;
                case "val": val = r.ReadTextString(); break;
                case "neg":
                    if (r.PeekState() == CborReaderState.Boolean) neg = r.ReadBoolean(); else r.SkipValue();
                    break;
                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();
        return new LabelEvent(uri, val, neg, -1);
    }
}
