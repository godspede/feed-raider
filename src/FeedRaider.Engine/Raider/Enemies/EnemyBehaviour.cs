namespace FeedRaider.Engine.Raider;

/// <summary>What happened to a speaker that it may want to answer with words.</summary>
internal enum VoiceCue
{
    /// <summary>It has come onto the field.</summary>
    Arrive,
    /// <summary>A part of it was shot off.</summary>
    PartLost,
    /// <summary>It changed phase (an enemy raises this itself with <see cref="RaiderGame.Cue"/>).</summary>
    Phase,
    /// <summary>It opened up (an enemy raises this itself).</summary>
    Opens,
    /// <summary>The ship died.</summary>
    ShipDied,
    /// <summary>Its box has been shut a while and the run knows a post: one waiting, or one heard.</summary>
    Idle,
    /// <summary>It was killed.</summary>
    Dying,
    /// <summary>It is leaving the field alive.</summary>
    Leaving,
}

/// <summary>What a cue does to the box on show: wait for it to be read, cut to the new words, stack a new box beside the old, or do
/// nothing.</summary>
internal enum Turn { Wait, Cut, Stack, Skip }

/// <summary>
/// One kind of enemy, as the game sees it. Each kind is one class in one file under <c>Enemies/</c>, a nested class of
/// <see cref="RaiderGame"/> so it can call the game's private helpers; the shared state of an enemy lives on its
/// <see cref="Enemy"/>, never on the class, because one instance serves every enemy of the kind (and every game).
/// </summary>
internal abstract class EnemyBehaviour
{
    /// <summary>The kind this class stands for. The table looks an enemy up by it.</summary>
    public abstract EnemyKind Kind { get; }
    /// <summary>The speech box's header: upper case, <see cref="EnemySpeechBox.MaxLabel"/> characters at most.</summary>
    public abstract string Name { get; }
    /// <summary>The sprite; for an enemy made only of parts, its whole silhouette.</summary>
    public abstract Sprite Body { get; }
    /// <summary>Debris and tell colour.</summary>
    public abstract uint Colour { get; }
    /// <summary>Hit points at stage 1 (the ramp multiplies them). Ignored once <see cref="Spawn"/> adds parts.</summary>
    public abstract double Hp { get; }
    public abstract int Score { get; }
    /// <summary>A boss dies like a gunship (the big blast), speaks a live post and shows the health bar.</summary>
    public virtual bool Boss => false;
    public virtual double DropChance => Tuning.DropGrunt;
    /// <summary>False: its hit points are <see cref="Hp"/> at every stage, never multiplied by the ramp.</summary>
    public virtual bool RampsHp => true;
    /// <summary>The frames it cannot touch the ship after it is made (<see cref="Enemy.Grace"/>).</summary>
    public virtual int Grace => 0;
    /// <summary>The speed it falls at from its first frame; null draws one from the common range on the game's random stream.</summary>
    public virtual double? EntryFall => null;
    /// <summary>The score for killing this one enemy; a kind with two forms answers for each.</summary>
    public virtual int ScoreFor(Enemy e) => Score;
    /// <summary>The chance this one enemy drops a pickup when killed; a kind with two forms answers for each.</summary>
    public virtual double DropChanceFor(Enemy e) => DropChance;
    /// <summary>The enemy has just been made (its position and hit points are set). Add parts here with <see cref="RaiderGame.SetParts"/>.</summary>
    public virtual void Spawn(RaiderGame g, Enemy e) { }
    /// <summary>Once a frame, after the enemy has aged. Move the owner and its parts, fire, count timers.</summary>
    public abstract void Update(RaiderGame g, Enemy e);
    /// <summary>A ship shot met a part; <paramref name="stopped"/> is true when armour or a shield stopped it. Called before the damage.
    /// The shot is removed afterwards unless the hook sent it back with <see cref="RaiderGame.Reflect"/>.</summary>
    public virtual void PartHit(RaiderGame g, Enemy e, Part p, Bullet b, bool stopped) { }
    /// <summary>A part has just gone to <see cref="PartState.Dead"/>. The owner's hit points already count without it.</summary>
    public virtual void PartDown(RaiderGame g, Enemy e, Part p) { }
    /// <summary>The owner has been killed (every vital part dead, or its hit points gone). It is already off the field.</summary>
    public virtual void Down(RaiderGame g, Enemy e) { }
    /// <summary>Lethal shapes that are not sprites: a beam, a bar, a blast. True while the ship is inside one.</summary>
    public virtual bool Touches(RaiderGame g, Enemy e) => false;
    /// <summary>Warnings and overlays, drawn after the sprite and the parts.</summary>
    public virtual void Draw(RaiderView.Canvas c, Enemy e) { }

    // ---------------------------------------------------------------- the voice

    /// <summary>True: this kind speaks (takes a waiting rude post, says a heard one again, or says its own line). A boss always does; a mini-boss may.</summary>
    public virtual bool Speaks => Boss;
    /// <summary>Which of the rude posts offered (oldest first) it takes: the waiting ones, or with none waiting the heard ones it may say
    /// again. An index past the list is held to the last post.</summary>
    public virtual int Taste(Enemy e, IReadOnlyList<RudePost> waiting) => 0;
    /// <summary>Its pace: the frames its box stays shut before <see cref="VoiceCue.Idle"/> is raised.</summary>
    public virtual int IdleFrames(Enemy e) => Tuning.VoiceIdleFrames;
    /// <summary>The most lines it shows on one page of its box; a post longer than that pages.</summary>
    public virtual int PageCap => SpeechBoxText.TextLines;
    /// <summary>What a cue does to the box on show. The base answers with the default turn for each cue.</summary>
    public virtual Turn TurnFor(RaiderGame g, Enemy e, VoiceCue cue) => cue switch
    {
        VoiceCue.Dying or VoiceCue.Leaving => Turn.Cut,
        VoiceCue.Idle => Boss ? Turn.Wait : Turn.Skip,
        _ => Turn.Wait,
    };
    /// <summary>The kind's own words for a cue, said under its name while the run has heard no rude post (and, for
    /// <see cref="VoiceCue.Dying"/>, always: its sign-off); null for none.</summary>
    public virtual string? Line(Enemy e, VoiceCue cue) => null;
    /// <summary>Text that stands before the header in the box (a counter such as "3/7"); null for none. Read each frame.</summary>
    public virtual string? Prefix(RaiderGame g, Enemy e) => null;
    /// <summary>True: the box is held at its first character (no typing, no page clock) while the enemy does something it should not talk over.</summary>
    public virtual bool HoldBox(RaiderGame g, Enemy e) => false;
}
