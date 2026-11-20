namespace FeedRaider.Engine.Raider;

/// <summary>What one speaker remembers of its own talking. Lives on the <see cref="Enemy"/>; never shared.</summary>
internal sealed class VoiceState
{
    /// <summary>It has reached its hold row.</summary>
    public bool Arrived;
    /// <summary>Its arrival has not yet been answered with words (it had no floor, or nothing to say).</summary>
    public bool OwesArrive;
    /// <summary>Frames its box has been shut.</summary>
    public int Closed;
    /// <summary>The one turn held until the box on show has been read.</summary>
    public VoiceCue? Held;
    /// <summary>Its leaving has been answered; it is not answered twice.</summary>
    public bool LeftCued;
    /// <summary>Read from <see cref="EnemyBehaviour.Prefix"/> each frame.</summary>
    public string? Prefix;
}

/// <summary>
/// The voice bosses, mini-bosses and lesser gunships speak the feed's rude posts. A rude post makes no
/// enemy; it waits in <see cref="Rude"/> until a speaker with the floor takes it at a cue, and stays known to every later speaker, who
/// says it again when nothing waits. Nothing here draws from the random stream:
/// a run with posts and the same run without put every enemy and bullet in the same place.
/// </summary>
public sealed partial class RaiderGame
{
    /// <summary>The rude posts waiting for a speaker, and those the run has heard.</summary>
    public RudeStore Rude { get; } = new();

    /// <summary>The one speaker that may take a turn now. While a gate is Warning it is nobody (the boss is not on the field yet), while
    /// it is Fighting the boss; otherwise the oldest living speaker that is not leaving.</summary>
    internal Enemy? Floor()
    {
        if (Gate == GateState.Warning) return null;
        if (Gate == GateState.Fighting) return Boss;
        foreach (var e in Enemies)
            if (!e.Leaving && BehaviourFor(e.Kind).Speaks) return e;
        return null;
    }

    /// <summary>True while the speaker's box is being read, or is about to be: a text not yet typed out to its last page, or held.</summary>
    private bool Reading(Enemy e, EnemyBehaviour behaviour) => Typing(e) || EnemySpeechBox.Speaks(e) && behaviour.HoldBox(this, e);

    /// <summary>True while the speaker's text is not yet typed out to its last page.</summary>
    private static bool Typing(Enemy e) => EnemySpeechBox.Speaks(e) && (e.SpeechLines is null || e.SpeechBoxAge < EnemySpeechBox.Frames(e));

    /// <summary>A cue for a speaker. Ignored unless it speaks and holds the floor; the kind answers with a turn.</summary>
    internal void Cue(Enemy e, VoiceCue cue)
    {
        var behaviour = BehaviourFor(e.Kind);
        if (!behaviour.Speaks) return;
        var voice = e.Voice ??= new VoiceState();
        if (cue == VoiceCue.Arrive) voice.OwesArrive = true;
        if (!ReferenceEquals(Floor(), e)) return;   // without the floor it only remembers that it owes an arrival
        bool done = Apply(e, behaviour, voice, cue);
        if (cue == VoiceCue.Arrive && done) voice.OwesArrive = false;
    }

    /// <summary>Carries out the turn the kind picks for a cue. False: it had nothing to say (no post and no line).</summary>
    private bool Apply(Enemy e, EnemyBehaviour behaviour, VoiceState voice, VoiceCue cue)
    {
        switch (behaviour.TurnFor(this, e, cue))
        {
            case Turn.Skip:
                return true;
            case Turn.Cut:
                voice.Held = null;
                return Deliver(e, behaviour, cue);
            case Turn.Stack:
                {
                    voice.Held = null;
                    var old = e.Snapshot();
                    bool open = EnemySpeechBox.IsOpen(e);
                    if (!Deliver(e, behaviour, cue)) return false;
                    if (open) LeaveBehind(old, e);
                    return true;
                }
            default:   // Wait
                if (Reading(e, behaviour)) { voice.Held = cue; return true; }
                return Deliver(e, behaviour, cue);
        }
    }

    // an echo stands this many of its box widths and cells from the new speaker, and no lower than this above the field's bottom
    private const int EchoWidths = 2, EchoGap = 2, EchoBottomMargin = 4;

    /// <summary>The box on show stays up as an echo, moved to the side of the new one that has the room.</summary>
    private void LeaveBehind(Enemy old, Enemy now)
    {
        old.Voice = null;
        old.SpeechBoxShift = default;
        int width = EnemySpeechBox.Box(old, Layout).W;
        old.X = Math.Clamp(now.X + (now.X < PW / 2.0 ? 1 : -1) * (width * EchoWidths + EchoGap), 0, PW);
        old.Y = Math.Clamp(old.Y, EnemySpeechBox.OpenY, PH - EchoBottomMargin);
        Echoes.Add(old);
    }

    /// <summary>Says what the speaker has for this cue. A speaker killed signs off with its own line when it has one. Otherwise, in
    /// this order: a waiting post; a post the run has heard, said again (a boss at any cue, any other speaker only on arriving, so it still
    /// says one thing a visit); the kind's own line, which is therefore said only while the run has heard no post at all.</summary>
    private bool Deliver(Enemy e, EnemyBehaviour behaviour, VoiceCue cue)
    {
        string? line = behaviour.Line(e, cue);
        if (cue == VoiceCue.Dying && line is { Length: > 0 }) return SayLine(e, behaviour, line);
        int Taste(IReadOnlyList<RudePost> offered) => behaviour.Taste(e, offered);
        RudePost? post = null;
        if (Rude.Waiting.Count > 0) post = Rude.Take(Math.Clamp(Taste(Rude.Waiting), 0, Rude.Waiting.Count - 1));
        // never the post on show, and not the one it said last while another is known; a run that knows only one hears it again once it has been read
        else if (behaviour.Boss || cue == VoiceCue.Arrive) post = Rude.Recall(Taste, e.Text) ?? (Reading(e, behaviour) ? null : Rude.Recall(Taste, null));
        if (post is null) return Rude.Heard.Count == 0 && line is { Length: > 0 } && SayLine(e, behaviour, line);
        EnemySpeechBox.Say(e, PostHeader(post), post.Text);
        return true;
    }

    private static bool SayLine(Enemy e, EnemyBehaviour behaviour, string line)
    {
        EnemySpeechBox.Say(e, behaviour.Name, line);
        return true;
    }

    private static string PostHeader(RudePost post) => EnemySpeechBox.Header(post.Label, DarknessKind.Rude, post.Source);

    /// <summary>Once a frame, after the enemies have moved and aged: finds each speaker's arrival, its leaving and its prefix, gives the floor
    /// its owed arrival and its held turn, and raises <see cref="VoiceCue.Idle"/> when its box has been shut long enough and the run knows
    /// a post (one waiting, or one heard), so a boss keeps talking through a fight on a quiet feed.</summary>
    private void UpdateVoice()
    {
        foreach (var e in Enemies)
        {
            var behaviour = BehaviourFor(e.Kind);
            if (!behaviour.Speaks) continue;
            var voice = e.Voice ??= new VoiceState();
            if (e.SpeechLines is not null) e.PageLines = Math.Min(e.PageLines, behaviour.PageCap);
            voice.Prefix = behaviour.Prefix(this, e);
            if (!voice.Arrived && e.Y - e.Sprite.H / 2.0 >= 0)
            {
                voice.Arrived = true;
                Cue(e, VoiceCue.Arrive);
            }
            if (e.Leaving && !voice.LeftCued)
            {
                voice.LeftCued = true;
                if (HeldTheFloor(e)) Apply(e, behaviour, voice, VoiceCue.Leaving);
            }
        }
        var floor = Floor();
        if (floor?.Voice is not { Arrived: true } floorVoice) return;
        var floorBehaviour = BehaviourFor(floor.Kind);
        bool known = Rude.Waiting.Count > 0 || Rude.Heard.Count > 0;
        if (floorVoice.OwesArrive && (known || floorBehaviour.Line(floor, VoiceCue.Arrive) is not null)) Cue(floor, VoiceCue.Arrive);
        if (floorVoice.Held is { } held && !Reading(floor, floorBehaviour))
        {
            floorVoice.Held = null;
            Deliver(floor, floorBehaviour, held);
        }
        // a box read to its end counts as shut even while it is held, so a speaker that holds its box between glances still comes round to the next post
        if (Typing(floor)) floorVoice.Closed = 0;
        else if (++floorVoice.Closed >= floorBehaviour.IdleFrames(floor) && known)
        {
            floorVoice.Closed = 0;
            Cue(floor, VoiceCue.Idle);
        }
    }

    /// <summary>True when the speaker had the floor until it began to leave (a leaver is no longer the floor, so it is judged by the order it came in).</summary>
    private bool HeldTheFloor(Enemy e)
    {
        if (Gate == GateState.Warning) return false;
        if (Gate == GateState.Fighting) return ReferenceEquals(Boss, e);
        foreach (var speaker in Enemies)
            if (BehaviourFor(speaker.Kind).Speaks) return ReferenceEquals(speaker, e);
        return false;
    }
}
