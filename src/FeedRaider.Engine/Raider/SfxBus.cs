namespace FeedRaider.Engine.Raider;

/// <summary>The names of the sound effects. They match the functions in the page's <c>sfx.js</c>.</summary>
public static class SfxNames
{
    public const string Shot = "shot", Hit = "hit", Kill = "kill", Boom = "boom", Hurt = "hurt", Death = "death", Bomb = "bomb",
        SpeechBox = "comm", Like = "like", NoLike = "nolike", Darkness = "darkness", Clear = "clear",
        PickupShield = "pickup_shield", PickupBomb = "pickup_bomb", PickupWeapon = "pickup_weapon", ShieldHit = "shield_hit",
        Warn = "warn", Ping = "ping", Open = "open", Shut = "shut", Charge = "charge", Beam = "beam", Lob = "lob", Blast = "blast",
        Hatch = "hatch", Tick = "tick", Part = "part", BossDown = "boss_down", Sting = "sting", Repost = "repost",
        Undo = "undo", Burst = "burst", BurstEnd = "burst_end",
        ShotHeavy = "shot_heavy", ShotTwin = "shot_twin", ShotMissile = "shot_missile", ShotTwinHeavy = "shot_twin_heavy", ShotSpread = "shot_spread",
        ShotMachineGun = "shot_machine_gun", ShotGatling = "shot_gatling", ShotSwarm = "shot_swarm", ShotStorm = "shot_storm";
}

/// <summary>
/// Where the game's sound effects go. The game calls <see cref="Play"/> at the moment something happens; the bus drops a repeat
/// that comes sooner than that sound's gap (in sim frames, so a run stays deterministic) and passes the rest to <c>emit</c> with
/// the pan, -1 (left) to 1 (right). Kills inside <see cref="ComboFrames"/> of each other make a combo: each one raises the pitch of
/// the kill and hit sounds by a semitone (up to <see cref="MaxStep"/>) until a pause, or the ship being hurt, resets it. `emit`
/// gets (name, pan, step, size): the combo step in semitones, and the size of the thing that died, 0 (grunt) to 1 (gunship).
/// Two cues borrow those fields: `tick` takes its pitch step (0 to <see cref="MaxTickStep"/>), and `charge` its length as `size`,
/// 0 (0.4 s) to 1 (1.6 s).
/// The page does its own voice limiting on top.
/// </summary>
public sealed class SfxBus(Action<string, double, int, double> emit)
{
    /// <summary>A kill this soon after the last one (0.6 s at 30 Hz) continues the combo.</summary>
    public const int ComboFrames = 18;
    /// <summary>The combo's pitch rise stops at this many semitones.</summary>
    public const int MaxStep = 7;
    /// <summary>A fuse's tick rises up to this many semitones (an octave).</summary>
    public const int MaxTickStep = 12;

    // far enough back that no frame counts as soon after it (halved so frame - NoKillYet cannot overflow)
    private const long NoKillYet = long.MinValue / 2;

    /// <summary>The fewest frames between two plays of a sound; 2 frames at 30 Hz is 66 ms.</summary>
    public static int MinGapFrames(string name) => name switch
    {
        SfxNames.Shot or SfxNames.Hit or SfxNames.Kill => 2,
        SfxNames.ShotHeavy or SfxNames.ShotTwin or SfxNames.ShotMissile or SfxNames.ShotTwinHeavy or SfxNames.ShotSpread or SfxNames.ShotSwarm or SfxNames.ShotStorm => 2,
        SfxNames.ShotMachineGun or SfxNames.ShotGatling => 5,       // the guns fire every 3 frames and are heard every second shot
        SfxNames.Hurt or SfxNames.Like or SfxNames.Repost or SfxNames.ShieldHit => 3,
        SfxNames.PickupShield or SfxNames.PickupBomb or SfxNames.PickupWeapon => 4,
        SfxNames.Bomb or SfxNames.NoLike or SfxNames.Undo => 6,
        SfxNames.SpeechBox => 8,
        SfxNames.Clear => 15,
        SfxNames.Darkness => 30,
        SfxNames.Ping => 4,                                    // at most one every 4 frames, however many shots the armour stops
        SfxNames.Tick or SfxNames.Open or SfxNames.Shut or SfxNames.Lob => 3,
        SfxNames.Part or SfxNames.Blast or SfxNames.Hatch => 2,
        SfxNames.Warn or SfxNames.Beam => 18,                  // 0.6 s: the cue plays out before it can start again
        SfxNames.BossDown => 45,
        SfxNames.Sting => 15,                                  // 0.5 s: one visitor at a time
        SfxNames.Burst or SfxNames.BurstEnd => 10,             // 0.35 s: the whoosh plays out; two bursts are 20 s apart anyway
        _ => 0,
    };

    private readonly Dictionary<string, long> _last = new(StringComparer.Ordinal);
    private int _combo;          // kills in the current chain
    private long _lastKill = NoKillYet;

    /// <summary>The combo's pitch rise in semitones as of <paramref name="frame"/>: 0 for the first kill of a chain.</summary>
    public int Step(long frame) => frame - _lastKill > ComboFrames ? 0 : Math.Min(MaxStep, Math.Max(0, _combo - 1));

    public void Play(string name, double pan, long frame, double size = 0, int tickStep = 0)
    {
        if (name is SfxNames.Hurt or SfxNames.Death) { _combo = 0; _lastKill = NoKillYet; }
        int step = 0;
        if (name is SfxNames.Kill or SfxNames.Boom)
        {
            _combo = frame - _lastKill > ComboFrames ? 1 : _combo + 1;   // every kill counts, even one the gap drops
            _lastKill = frame;
            step = Step(frame);
        }
        else if (name == SfxNames.Hit) step = Step(frame);
        else if (name == SfxNames.Tick) step = Math.Clamp(tickStep, 0, MaxTickStep);   // the fuse's count, not the combo
        if (_last.TryGetValue(name, out long prev) && frame - prev < MinGapFrames(name)) return;
        _last[name] = frame;
        emit(name, Math.Clamp(pan, -1, 1), step, Math.Clamp(size, 0, 1));
    }
}
