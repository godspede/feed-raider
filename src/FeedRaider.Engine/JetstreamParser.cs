using System.Text.Json;

namespace FeedRaider.Engine;

public static class JetstreamParser
{
    public const string PostCollection = "app.bsky.feed.post";

    /// <summary>Parses one Jetstream message. True only for a newly created app.bsky.feed.post commit.</summary>
    public static bool TryParse(string json, out PostEvent? post)
    {
        post = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "commit") return false;
            if (!root.TryGetProperty("commit", out var commit)) return false;
            if (GetString(commit, "operation") != "create") return false;
            if (GetString(commit, "collection") != PostCollection) return false;
            if (!commit.TryGetProperty("record", out var record) || record.ValueKind != JsonValueKind.Object) return false;

            string? did = GetString(root, "did");
            if (string.IsNullOrEmpty(did)) return false;

            long timeUs = root.TryGetProperty("time_us", out var t) && t.TryGetInt64(out var tv) ? tv : 0;
            string text = GetString(record, "text") ?? "";

            string? parentDid = null;
            if (record.TryGetProperty("reply", out var reply) && reply.ValueKind == JsonValueKind.Object
                && reply.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object)
                parentDid = DidFromAtUri(GetString(parent, "uri"));

            post = new PostEvent(did, text, parentDid, timeUs);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>at://did:plc:abc/app.bsky.feed.post/3k... -> did:plc:abc</summary>
    public static string? DidFromAtUri(string? uri)
    {
        const string prefix = "at://";
        if (uri is null || !uri.StartsWith(prefix, StringComparison.Ordinal)) return null;
        int end = uri.IndexOf('/', prefix.Length);
        string authority = end < 0 ? uri[prefix.Length..] : uri[prefix.Length..end];
        return authority.StartsWith("did:", StringComparison.Ordinal) ? authority : null;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
