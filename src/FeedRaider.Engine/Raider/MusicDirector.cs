namespace FeedRaider.Engine.Raider;

/// <summary>
/// The music states. The page maps each to a role in <c>music/tracks.json</c>; gameplay plays a rotation. A boss is cued as
/// <c>boss:&lt;key&gt;</c>, the key being the boss's <see cref="EnemyKind"/> name in lower case, and the page picks that boss's
/// track. <c>ahead:boss:&lt;key&gt;</c> is not a state: it says which boss the stage ahead holds, so a player can fetch the track.
/// </summary>
public static class MusicCues
{
    public const string Start = "start", Gameplay = "gameplay", Boss = "boss", GameOver = "gameover";
    public const string BossPrefix = "boss:", AheadPrefix = "ahead:boss:";

    /// <summary>The cue that starts a boss's track.</summary>
    public static string ForBoss(string key) => BossPrefix + key;

    /// <summary>True for the boss state: <c>boss:&lt;key&gt;</c>, or the bare role name.</summary>
    public static bool IsBoss(string? cue) => cue == Boss || cue is not null && cue.StartsWith(BossPrefix, StringComparison.Ordinal);

    /// <summary>The boss a cue names, or null for the bare role name and for any other cue.</summary>
    public static string? BossKey(string? cue) =>
        cue is not null && cue.StartsWith(BossPrefix, StringComparison.Ordinal) && cue.Length > BossPrefix.Length ? cue[BossPrefix.Length..] : null;

    /// <summary>True for the fetch-ahead hint, which is no state and which a player that has every track already ignores.</summary>
    public static bool IsAhead(string? cue) => cue is not null && cue.StartsWith(AheadPrefix, StringComparison.Ordinal);
}

/// <summary>What the music cares about in one game state; <see cref="RaiderGame"/> fills it in each step. Boss (the key of the boss
/// on the field, null for none) moves the music, and Ahead (the key of the boss the stage in progress ends with, null for none)
/// only the fetch-ahead hint.</summary>
public readonly record struct MusicSnapshot(string? Boss, string? Ahead = null);

/// <summary>
/// Turns the game's state into cues: gameplay, or boss:&lt;key&gt; while a boss is announced or fights, and a hint that names the next boss. A new ship arrives after a death and
/// the run goes on, so a death is not a musical event. The only gameover is <see cref="End"/>, when the host ends the run.
/// Pure: it reads snapshots and calls <c>emit</c> only when the cue changes.
/// </summary>
public sealed class MusicDirector(Action<string> emit)
{
    public string? Current { get; private set; }

    private bool _ended;
    private string? _ahead;

    public void Update(in MusicSnapshot s)
    {
        if (_ended) return;
        Switch(string.IsNullOrEmpty(s.Boss) ? MusicCues.Gameplay : MusicCues.ForBoss(s.Boss));
        // the hint is said when a stage begins (when its boss changes), after the cue of the moment, and not again for the same boss
        if (!string.IsNullOrEmpty(s.Ahead) && s.Ahead != _ahead) { _ahead = s.Ahead; emit(MusicCues.AheadPrefix + s.Ahead); }
    }

    /// <summary>The run is over (the host stopped it): the game-over theme, and no more gameplay cues.</summary>
    public void End()
    {
        if (_ended) return;
        _ended = true;
        Switch(MusicCues.GameOver);
    }

    // Emits only a change of cue, so an unchanged state never repeats.
    private void Switch(string cue)
    {
        if (cue == Current) return;
        Current = cue;
        emit(cue);
    }
}
