namespace FeedRaider.Engine.Raider;

/// <summary>The four music states. The page maps each to a role in <c>music/tracks.json</c>; gameplay plays a rotation.</summary>
public static class MusicCues
{
    public const string Menu = "menu", Gameplay = "gameplay", Boss = "boss", GameOver = "gameover";
}

/// <summary>What the music cares about in one game state; <see cref="RaiderGame"/> fills it in each step. Only Boss moves the
/// music; the rest is here so the tests can show that a short event (a death, a life, an ally, a wave) changes nothing.</summary>
public readonly record struct MusicSnapshot(int Deaths, bool Boss, int Hp, int Wings, int LiveWavesCleared, int Score, int Record);

/// <summary>
/// Turns the game's state into cues: gameplay, or boss while a boss or rude gunship lives. The ship respawns after a death and
/// the run goes on, so a death is not a musical event. The only gameover is <see cref="End"/>, when the host ends the run.
/// Pure: it reads snapshots and calls <c>emit</c> only when the cue changes.
/// </summary>
public sealed class MusicDirector(Action<string> emit)
{
    public string? Current { get; private set; }

    private bool _ended;

    public void Update(in MusicSnapshot s, double t)
    {
        if (_ended) return;
        Switch(s.Boss ? MusicCues.Boss : MusicCues.Gameplay);
    }

    /// <summary>The run is over (the host stopped it): the game-over song, and no more gameplay cues.</summary>
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
