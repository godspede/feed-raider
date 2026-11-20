using System.Runtime.InteropServices;
using System.Text.Json;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Console.Audio;

/// <summary>One entry of <c>music/tracks.json</c>: the file name and the cue it plays for. Boss, on a boss track, is the key of the one
/// boss it belongs to (the lower-case <c>EnemyKind</c> name); a boss track without it is a shared track, for any boss.</summary>
public sealed record MusicTrack(string File, string Role, string? Boss = null);

/// <summary>
/// Which track plays for which cue, the same rules as the page's <c>music.js</c>: start, boss and gameover pick their track
/// (a <c>boss:&lt;key&gt;</c> cue picks that boss's own track, else a shared boss track, else any boss track, never a gameplay one);
/// gameplay plays a shuffled rotation, each track once, never starting on the track played last, and a boss that ends goes on
/// to the next rotation track. Pure: the randomness is the caller's <see cref="Random"/>.
/// </summary>
public sealed class MusicRotation(IReadOnlyList<MusicTrack> tracks, Random rng)
{
    private List<MusicTrack> _order = [];
    private int _position = -1;
    private MusicTrack? _last;

    /// <summary>Reads the manifest's JSON array; entries with an unknown role or no file are dropped.</summary>
    public static List<MusicTrack> ParseManifest(string json)
    {
        string[] roles = [MusicCues.Start, MusicCues.Gameplay, MusicCues.Boss, MusicCues.GameOver];
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
            string? boss = role == MusicCues.Boss && e.TryGetProperty("boss", out var b) && b.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(b.GetString()) ? b.GetString() : null;
            if (!string.IsNullOrEmpty(file) && role is not null && Array.IndexOf(roles, role) >= 0) list.Add(new MusicTrack(file, role, boss));
        }
        return list;
    }

    /// <summary>True when the cue's track is a rotation one, which plays through once and then hands over to the next.</summary>
    public bool Plays(string cue) => cue == MusicCues.Gameplay && Of(MusicCues.Gameplay).Count > 1;

    /// <summary>The track for a cue, or null when the manifest has none for it. <paramref name="afterBoss"/>: the gameplay cue
    /// returns from a boss, so the rotation goes on instead of starting over.</summary>
    public MusicTrack? ForCue(string cue, bool afterBoss)
    {
        if (MusicCues.IsBoss(cue))
        {
            // the rotation is left as it is, so the gameplay cue that follows goes on with the next track
            string? key = MusicCues.BossKey(cue);
            var boss = Of(MusicCues.Boss);
            var pool = boss.Where(t => key is not null && t.Boss == key).ToList();
            if (pool.Count == 0) pool = boss.Where(t => t.Boss is null).ToList();
            if (pool.Count == 0) pool = boss;
            return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
        }
        if (cue != MusicCues.Gameplay)
        {
            _order = []; _position = -1;
            var l = Of(cue);
            return l.Count == 0 ? null : l[rng.Next(l.Count)];
        }
        return afterBoss && _order.Count > 0 ? Next() : NewRun();
    }

    /// <summary>The next rotation track, after the current one ends.</summary>
    public MusicTrack? Next()
    {
        if (_order.Count == 0) return NewRun();
        if (++_position >= _order.Count) { _order = Shuffled(); _position = 0; }
        return _last = _order[_position];
    }

    private MusicTrack? NewRun()
    {
        _order = Shuffled();
        _position = 0;
        return _last = _order.Count > 0 ? _order[0] : null;
    }

    private List<MusicTrack> Of(string role) => tracks.Where(t => t.Role == role).ToList();

    private List<MusicTrack> Shuffled()
    {
        var a = Of(MusicCues.Gameplay);
        rng.Shuffle(CollectionsMarshal.AsSpan(a));
        if (a.Count > 1 && a[0] == _last) (a[0], a[1]) = (a[1], a[0]);
        return a;
    }
}
