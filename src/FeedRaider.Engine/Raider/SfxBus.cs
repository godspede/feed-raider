namespace FeedRaider.Engine.Raider;

/// <summary>The names of the sound effects. They match the functions in the page's <c>sfx.js</c>.</summary>
public static class SfxNames
{
    public const string Shot = "shot", Hit = "hit", Kill = "kill", Boom = "boom", Hurt = "hurt", Death = "death", Bomb = "bomb",
        Comm = "comm", Like = "like", NoLike = "nolike", Darkness = "darkness", Clear = "clear",
        PickupShield = "pickup_shield", PickupBomb = "pickup_bomb", PickupPower = "pickup_power", ShieldHit = "shield_hit";
}

/// <summary>
/// Where the game's sound effects go. The game calls <see cref="Play"/> at the moment something happens; the bus drops a repeat
/// that comes sooner than that sound's gap (in sim frames, so a run stays deterministic) and passes the rest to <c>emit</c> with
/// the pan, -1 (left) to 1 (right). Kills inside <see cref="ComboFrames"/> of each other make a combo: each one raises the pitch of
/// the kill and hit sounds by a semitone (up to <see cref="MaxStep"/>) until a pause, or the ship being hurt, resets it. `emit`
/// gets (name, pan, step, size): the combo step in semitones, and the size of the thing that died, 0 (grunt) to 1 (gunship).
/// The page does its own voice limiting on top.
/// </summary>
public sealed class SfxBus(Action<string, double, int, double> emit)
{
    /// <summary>A kill this soon after the last one (0.6 s at 30 Hz) continues the combo.</summary>
    public const int ComboFrames = 18;
    /// <summary>The combo's pitch rise stops at this many semitones.</summary>
    public const int MaxStep = 7;

    /// <summary>The fewest frames between two plays of a sound; 2 frames at 30 Hz is 66 ms.</summary>
    public static int MinGapFrames(string name) => name switch
    {
        SfxNames.Shot or SfxNames.Hit or SfxNames.Kill => 2,
        SfxNames.Hurt or SfxNames.Like or SfxNames.ShieldHit => 3,
        SfxNames.PickupShield or SfxNames.PickupBomb or SfxNames.PickupPower => 4,
        SfxNames.Bomb or SfxNames.NoLike => 6,
        SfxNames.Comm => 8,
        SfxNames.Clear => 15,
        SfxNames.Darkness => 30,
        _ => 0,
    };

    private readonly Dictionary<string, long> _last = new(StringComparer.Ordinal);
    private int _combo;          // kills in the current chain
    private long _lastKill = long.MinValue / 2;

    /// <summary>The combo's pitch rise in semitones as of <paramref name="frame"/>: 0 for the first kill of a chain.</summary>
    public int Step(long frame) => frame - _lastKill > ComboFrames ? 0 : Math.Min(MaxStep, Math.Max(0, _combo - 1));

    public void Play(string name, double pan, long frame, double size = 0)
    {
        if (name is SfxNames.Hurt or SfxNames.Death) { _combo = 0; _lastKill = long.MinValue / 2; }
        int step = 0;
        if (name is SfxNames.Kill or SfxNames.Boom)
        {
            _combo = frame - _lastKill > ComboFrames ? 1 : _combo + 1;   // every kill counts, even one the gap drops
            _lastKill = frame;
            step = Step(frame);
        }
        else if (name == SfxNames.Hit) step = Step(frame);
        if (_last.TryGetValue(name, out long prev) && frame - prev < MinGapFrames(name)) return;
        _last[name] = frame;
        emit(name, Math.Clamp(pan, -1, 1), step, Math.Clamp(size, 0, 1));
    }
}
