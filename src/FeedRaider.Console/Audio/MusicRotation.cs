using System.Text.Json;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Console.Audio;

/// <summary>One entry of <c>music/tracks.json</c>: the file name and the cue it plays for.</summary>
public sealed record MusicTrack(string File, string Role);

/// <summary>
/// Which track plays for which cue, the same rules as the page's <c>music.js</c>: menu, boss and gameover pick their track;
/// gameplay plays a shuffled rotation, each track once, never starting on the track played last, and a boss that ends goes on
/// to the next rotation track. Pure: the randomness is the caller's <see cref="Random"/>.
/// </summary>
public sealed class MusicRotation(IReadOnlyList<MusicTrack> tracks, Random rng)
{
    private List<MusicTrack> _order = [];
    private int _i = -1;
    private MusicTrack? _last;

    /// <summary>Reads the manifest's JSON array; entries with an unknown role or no file are dropped.</summary>
    public static List<MusicTrack> ParseManifest(string json)
    {
        string[] roles = [MusicCues.Menu, MusicCues.Gameplay, MusicCues.Boss, MusicCues.GameOver];
        var list = new List<MusicTrack>();
        using var doc = JsonDocument.Parse(json);
        // A manifest that is not an array is treated as having no tracks: the music is optional, and throwing here would
        // also lose the effects, because TryStart gives up on all sound when anything throws.
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string? file = e.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
            string? role = e.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            if (!string.IsNullOrEmpty(file) && role is not null && Array.IndexOf(roles, role) >= 0) list.Add(new MusicTrack(file, role));
        }
        return list;
    }

    /// <summary>True when the cue's track is a rotation one, which plays through once and then hands over to the next.</summary>
    public bool Plays(string cue) => cue == MusicCues.Gameplay && Of(MusicCues.Gameplay).Count > 1;

    /// <summary>The track for a cue, or null when the manifest has none for it. <paramref name="afterBoss"/>: the gameplay cue
    /// returns from a boss, so the rotation goes on instead of starting over.</summary>
    public MusicTrack? ForCue(string cue, bool afterBoss)
    {
        if (cue != MusicCues.Gameplay)
        {
            _order = []; _i = -1;
            var l = Of(cue);
            return l.Count == 0 ? null : l[rng.Next(l.Count)];
        }
        return afterBoss && _order.Count > 0 ? Next() : NewRun();
    }

    /// <summary>The next rotation track, after the current one ends.</summary>
    public MusicTrack? Next()
    {
        if (_order.Count == 0) return NewRun();
        if (++_i >= _order.Count) { _order = Shuffled(); _i = 0; }
        return _last = _order[_i];
    }

    private MusicTrack? NewRun()
    {
        _order = Shuffled();
        _i = 0;
        return _last = _order.Count > 0 ? _order[0] : null;
    }

    private List<MusicTrack> Of(string role) => tracks.Where(t => t.Role == role).ToList();

    private List<MusicTrack> Shuffled()
    {
        var a = Of(MusicCues.Gameplay);
        for (int i = a.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
        if (a.Count > 1 && a[0] == _last) (a[0], a[1]) = (a[1], a[0]);
        return a;
    }
}
