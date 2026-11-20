namespace FeedRaider.Engine.Raider;

/// <summary>The build-up of a hold, cut into eight steps. Everything the player sees and hears of a hold reads
/// this one table: the icon's width and colour, the halo, the shake and the pitch of the tick.</summary>
public static class HoldBeats
{
    /// <summary>The progress at which each step begins. A beat fires when progress rises to an entry.</summary>
    public static readonly int[] Ticks = [0, 15, 30, 45, 58, 69, 78, 85];
    /// <summary>The icon's width in pixels at each step; its height follows (<see cref="IconHeight"/>).</summary>
    public static readonly int[] Widths = [7, 9, 11, 13, 15, 17, 19, 21];
    /// <summary>The icon's width in the frozen frames of a finish.</summary>
    public const int FinishedWidth = 25;
    /// <summary>How far the halo reaches past the icon, in pixels.</summary>
    public const int HaloMargin = 3;
    /// <summary>On a beat the icon is drawn this much wider for this many frames.</summary>
    public const int ThumpPixels = 2, ThumpFrames = 3;
    /// <summary>The frames a finished icon takes to fall back into its button, after the frozen ones.</summary>
    public static readonly int[] SettleWidths = [17, 11];

    private static readonly uint[] LikeSteps = [0x86384f, 0xa03c55, 0xba3f59, 0xd2405b, 0xe63f5a, 0xf23c59, 0xfa3657, 0xff2d55];
    private static readonly uint[] RepostSteps = [0x2f6c4f, 0x2e7f58, 0x2b9261, 0x28a56a, 0x25b871, 0x22c876, 0x20d079, 0x1fd67a];
    private const uint LikeFinished = 0xffd9e0, RepostFinished = 0xd9ffe9;

    // The shake of each step's beat, rising with the build-up. Each is over Tuning.ShakeFloor, where the shake is cut off,
    // so it moves the picture for a frame or two and is gone before the next beat.
    private static readonly double[] Shakes = [0, 0.6, 0.6, 0.7, 0.8, 0.9, 1.0, 1.2];
    public const double FinishedShake = 1.6;
    public const int FinishedHitStop = 2;

    public static uint[] Colours(PostAction a) => a == PostAction.Like ? LikeSteps : RepostSteps;
    public static uint Finished(PostAction a) => a == PostAction.Like ? LikeFinished : RepostFinished;
    /// <summary>The halo's strength at the first step, and how much each further step adds.</summary>
    private const double HaloBase = 0.10, HaloStep = 0.04;
    public static double Halo(int step) => HaloBase + HaloStep * Math.Clamp(step, 0, Ticks.Length - 1);
    public static double Shake(int step) => Shakes[Math.Clamp(step, 0, Shakes.Length - 1)];
    /// <summary>The colour the halo leaves on the slab at a step: one colour a step, so the terminal's glyph atlas gains little.</summary>
    public static uint HaloColour(PostAction a, int step) => Surface.Mix(CardColours.Slab, Colours(a)[Math.Clamp(step, 0, Ticks.Length - 1)], Halo(step));

    /// <summary>The height of an icon drawn <paramref name="width"/> pixels wide: the icon art, scaled.</summary>
    public static int IconHeight(int width) => (int)Math.Round(width * CardLayout.IconHeightPx / (double)CardLayout.IconWidthPx);

    /// <summary>The step progress is in: the last beat passed.</summary>
    public static int StepAt(int progress)
    {
        int s = 0;
        for (int i = 1; i < Ticks.Length; i++) if (progress >= Ticks[i]) s = i;
        return s;
    }
}

/// <summary>Which button the ship is on, and the buttons that are part filled. The rule: the ship's sprite box
/// touching a button holds it; of two touched buttons the one whose centre is nearer the ship's; one button at a time.</summary>
public sealed class HoldTracker
{
    internal sealed class Entry { public required PostMarks Marks; public required PostAction Action; public int Off; }

    internal readonly List<Entry> Live = [];

    /// <summary>The frame of a beat or a finish that has not happened: far enough back that any frame minus it is past every
    /// effect, and half the range so the subtraction cannot overflow.</summary>
    private const long NeverFrame = long.MinValue / 2;
    /// <summary>The frames since a beat or a finish on a button it did not fall on: past every effect, and half the range so a
    /// caller adding to it cannot overflow.</summary>
    private const long FarFrames = long.MaxValue / 2;

    /// <summary>The button last held, whether the ship is still on it or not: the ring round the ship shows it for as long as it has progress.</summary>
    public PostMarks? LastMarks { get; internal set; }
    public PostAction LastAction { get; internal set; }

    /// <summary>The button the last beat fell on, and the frame it did: the thump.</summary>
    public PostMarks? BeatMarks { get; internal set; }
    public PostAction BeatAction { get; internal set; }
    public long BeatFrame { get; internal set; } = NeverFrame;

    /// <summary>The button finished last, and the frame it finished on: the flash and the settle.</summary>
    public PostMarks? FinishedMarks { get; internal set; }
    public PostAction FinishedAction { get; internal set; }
    public long FinishedFrame { get; internal set; } = NeverFrame;

    /// <summary>The ship's sprite as drawn, in field pixels.</summary>
    public static PixelRect ShipBox(RaiderGame g) =>
        new((int)Math.Round(g.PX - g.Ship.W / 2.0), (int)Math.Round(g.PY - g.Ship.H / 2.0), g.Ship.W, g.Ship.H);

    /// <summary>The button the ship is touching and may hold, on the game's current frame.</summary>
    public static (Card Card, PostAction Action)? Touching(RaiderGame g) =>
        Touched(g) is { } t && g.CanHold(t.Card, t.Action) ? t : null;

    /// <summary>The button the ship is on, whether it may be held or is locked: a locked button under the ship is not passed over
    /// for its neighbour.</summary>
    internal static (Card Card, PostAction Action)? Touched(RaiderGame g) =>
        g.Alive ? Pick(g.Cards, ShipBox(g), (c, a) => g.CanHold(c, a) || g.IsLocked(c, a)) : null;

    private static readonly PostAction[] BothButtons = [PostAction.Like, PostAction.Repost];

    /// <summary>The button <paramref name="ship"/> overlaps that <paramref name="can"/> allows; of several, the one whose centre is
    /// nearest the box's centre.</summary>
    public static (Card Card, PostAction Action)? Pick(CardField field, PixelRect ship, Func<Card, PostAction, bool> can)
    {
        (Card, PostAction)? best = null;
        double bestD = double.MaxValue;
        double sx = ship.X + ship.W / 2.0, sy = ship.Y + ship.H / 2.0;
        foreach (var c in field.Cards)
        {
            foreach (var a in BothButtons)
            {
                var p = field.ButtonBox(c, a);
                if (ship.X >= p.X + p.W || ship.X + ship.W <= p.X || ship.Y >= p.Y + p.H || ship.Y + ship.H <= p.Y) continue;
                if (!can(c, a)) continue;
                double dx = p.X + p.W / 2.0 - sx, dy = p.Y + p.H / 2.0 - sy, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = (c, a); }
            }
        }
        return best;
    }

    /// <summary>The button the ring round the ship is showing: the one the ship is on, else the last one held while it still has progress.</summary>
    public (PostMarks Marks, PostAction Action)? Shown(RaiderGame g)
    {
        if (Touching(g) is { } t) return (t.Card.Marks, t.Action);
        if (LastMarks is { } m && m.Progress(LastAction) > 0 && g.Alive) return (m, LastAction);
        return null;
    }

    /// <summary>Frames since the beat on this button, or <see cref="FarFrames"/>.</summary>
    public long SinceBeat(PostMarks m, PostAction a, long frame) => ReferenceEquals(BeatMarks, m) && BeatAction == a ? frame - BeatFrame : FarFrames;

    /// <summary>Frames since this button finished, or <see cref="FarFrames"/>.</summary>
    public long SinceFinished(PostMarks m, PostAction a, long frame) => ReferenceEquals(FinishedMarks, m) && FinishedAction == a ? frame - FinishedFrame : FarFrames;
}

public sealed partial class RaiderGame
{
    /// <summary>The hold on a post's button: which one the ship is on, and what the view needs of the build-up.</summary>
    public HoldTracker Hold { get; } = new();

    private bool _saidSignIn, _saidLocked;

    private static void SetProgress(PostMarks m, PostAction a, int v)
    {
        if (a == PostAction.Like) m.LikeProgress = v; else m.RepostProgress = v;
    }

    private static void SetState(PostMarks m, PostAction a, ButtonState s)
    {
        if (a == PostAction.Like) m.Like = s; else m.Repost = s;
    }

    /// <summary>Whether this button is locked: the player's sign-in may not make its write, and it is not made already.</summary>
    public bool IsLocked(Card card, PostAction action) =>
        !MayWrite(action) && card.Marks.Of(action) == ButtonState.Off && Cards.ButtonBox(card, action).Y >= CardPainter.HudPixelRows;

    /// <summary>Whether this button can be held: not lit and not waiting on its write, not locked, and clear of the HUD's rows.</summary>
    public bool CanHold(Card card, PostAction action)
    {
        if (!MayWrite(action)) return false;
        if (card.Marks.Of(action) is ButtonState.Lit or ButtonState.Pending) return false;
        return Cards.ButtonBox(card, action).Y >= CardPainter.HudPixelRows;
    }

    /// <summary>The middle of a button in field pixels.</summary>
    private (double X, double Y) ButtonCentre(Card card, PostAction action)
    {
        var p = Cards.ButtonBox(card, action);
        return (p.X + p.W / 2.0, p.Y + p.H / 2.0);
    }

    private void HoldSound(int step, double x) => Sfx?.Play(SfxNames.Hold, Pan(x), Frame, tickStep: step);

    /// <summary>One simulation tick of the hold: the held button gains a tick, the others rest for a moment and then drain.</summary>
    private void UpdateHold()
    {
        var touch = HoldTracker.Touching(this);
        if (touch is null && !_saidLocked && HoldTracker.Touched(this) is { } locked)
        {
            _saidLocked = true;
            Say(NeedsFreshSignIn(locked.Action == PostAction.Like ? "Like" : "Repost"));
            Sound(SfxNames.NoLike, PX);
        }
        if (touch is { } t)
        {
            var m = t.Card.Marks;
            var entry = Hold.Live.Find(e => ReferenceEquals(e.Marks, m) && e.Action == t.Action);
            // a tick on first contact, and on coming back to a button that had begun to drain: the pitch says where it stands
            bool fresh = entry is null || entry.Off > Tuning.HoldGraceFrames;
            if (entry is null) Hold.Live.Add(entry = new HoldTracker.Entry { Marks = m, Action = t.Action });
            entry.Off = 0;
            Hold.LastMarks = m; Hold.LastAction = t.Action;
            int progress = m.Progress(t.Action) + 1;
            SetProgress(m, t.Action, progress);
            var (x, y) = ButtonCentre(t.Card, t.Action);
            int step = HoldBeats.StepAt(progress);
            if (progress >= Tuning.HoldFrames) FinishHold(t.Card, t.Action, x, y);
            else if (step > 0 && progress == HoldBeats.Ticks[step]) Beat(m, t.Action, step, x, y);
            else if (fresh) HoldSound(step, x);
        }
        for (int i = Hold.Live.Count - 1; i >= 0; i--)
        {
            var e = Hold.Live[i];
            int p = e.Marks.Progress(e.Action);
            bool held = touch is { } touched && ReferenceEquals(touched.Card.Marks, e.Marks) && touched.Action == e.Action;
            if (!held && p > 0 && ++e.Off > Tuning.HoldGraceFrames) SetProgress(e.Marks, e.Action, p = Math.Max(0, p - Tuning.HoldDrain));
            if (p <= 0) Hold.Live.RemoveAt(i);
        }
    }

    /// <summary>A beat's ring starts at the icon's edge and grows this many pixels, and one more for each step, over this many frames.</summary>
    private const int BeatRingGrow = 3, BeatRingFrames = 5;
    /// <summary>Offsets into the burst's hash stream so each spark's speed and life draw from values apart from its angle's.</summary>
    private const int BurstSpeedStream = 100, BurstLifeStream = 200;
    /// <summary>Every this-many sparks, from the first, is the pale colour.</summary>
    private const int BurstPaleEvery = 3;
    /// <summary>The burst of a finished hold: how many particles, the jitter of each one's angle (radians), the speed range, the
    /// extra pull sideways, the upward lift and the life range (frames).</summary>
    private const int BurstCount = 14, BurstLifeMin = 16, BurstLifeSpread = 12;
    private const double BurstAngleJitter = 0.45, BurstSpeedMin = 0.7, BurstSpeedSpread = 1.5, BurstSideStretch = 1.3, BurstLift = 0.35;
    /// <summary>The two rings of a finished hold: radius at the start and the end (pixels), and the frames they last.</summary>
    private const int InnerRingFrom = 3, InnerRingTo = 14, InnerRingFrames = 12;
    private const int OuterRingFrom = 6, OuterRingTo = 22, OuterRingFrames = 18;
    /// <summary>How far above the ship the reward pop-ups appear, in pixels: a pop-up is four pixels tall, so the four stack.</summary>
    private const int PartsPopupRise = 22, GoldPopupRise = 18, ShieldPopupRise = 14, PowerPopupRise = 10;
    /// <summary>The ring round the ship when a gold card pays: radius at the start and the end (pixels), and the frames it lasts.</summary>
    private const int GoldRingFrom = 8, GoldRingTo = 26, GoldRingFrames = 16;
    /// <summary>What a gold card's pop-up says when it gives a bomb, and when the ship already carries every bomb it can.</summary>
    public const string GoldPopup = "BANGER +BOMB", GoldPopupFull = "BANGER";
    /// <summary>What the pop-up says when a finished hold takes a share off the parts of the enemies in the fight.</summary>
    public const string PartsPopup = "PARTS HIT";

    private void Beat(PostMarks m, PostAction a, int step, double x, double y)
    {
        Hold.BeatMarks = m; Hold.BeatAction = a; Hold.BeatFrame = Frame;
        HoldSound(step, x);
        AddShake(HoldBeats.Shake(step));
        double r = HoldBeats.Widths[step] / 2.0;
        AddRing(x, y, r, r + BeatRingGrow + step, HoldBeats.Colours(a)[step], BeatRingFrames);
    }

    private void FinishHold(Card card, PostAction action, double x, double y)
    {
        var m = card.Marks;
        SetProgress(m, action, 0);
        Hold.FinishedMarks = m; Hold.FinishedAction = action; Hold.FinishedFrame = Frame;
        bool like = action == PostAction.Like;
        var write = like ? OnLike : OnRepost;
        bool real = SignedIn && write is not null;
        SetState(m, action, real ? ButtonState.Pending : ButtonState.Marked);

        uint[] steps = HoldBeats.Colours(action);
        uint lit = steps[^1], pale = HoldBeats.Finished(action);
        AddShake(HoldBeats.FinishedShake, HoldBeats.FinishedHitStop);
        // the burst is placed by the post and the frame, not by the game's generator: a hold must not change what a seeded run does next
        ulong h = Hashing.Mix(Hashing.MixedFnv64(card.Post.Uri) ^ (ulong)Frame);
        for (int i = 0; i < BurstCount; i++)
        {
            double ang = i / (double)BurstCount * Math.Tau + Hashing.Unit(h, i) * BurstAngleJitter, sp = BurstSpeedMin + Hashing.Unit(h, BurstSpeedStream + i) * BurstSpeedSpread;
            AddParticle(new Particle
            {
                X = x,
                Y = y,
                VX = Math.Cos(ang) * sp * BurstSideStretch,
                VY = Math.Sin(ang) * sp - BurstLift,
                Life = BurstLifeMin + (int)(Hashing.Unit(h, BurstLifeStream + i) * BurstLifeSpread),
                Colour = i % BurstPaleEvery == 0 ? pale : lit,
                Kind = like ? ParticleKind.Heart : ParticleKind.Spark,
            });
        }
        AddRing(x, y, InnerRingFrom, InnerRingTo, lit, InnerRingFrames);
        AddRing(x, y, OuterRingFrom, OuterRingTo, lit, OuterRingFrames);
        Sound(like ? SfxNames.Like : SfxNames.Repost, x);
        Reward(card);

        if (real)
        {
            Say(WriteSentText(like, card.Post));
            write!(card.Post);
        }
        else if (!_saidSignIn)
        {
            _saidSignIn = true;
            Say($"Sign in to {(like ? "like" : "repost")} for real. Nothing was sent.");
        }
        if (like && !m.ThreadAsked)
        {
            m.ThreadAsked = true;
            ThreadWanted?.Invoke(card.Post);
        }
    }

    /// <summary>What a finished hold pays, once for each post: the shield back if it is down, the power started again, a quarter off every living part of every enemy in the fight, and
    /// on a gold card one bomb, up to the cap. The hold pays, not the write, so a write that fails later takes nothing back.</summary>
    private void Reward(Card card)
    {
        var m = card.Marks;
        if (m.Paid) return;
        m.Paid = true;
        bool shield = Shield <= 0;
        if (shield)
        {
            Shield = Tuning.ShieldHits;
            ShieldHitFrame = Frame;
            Popup("SHIELD UP", PX, PY - ShieldPopupRise, ShieldColour);
        }
        _powerUntil = Tick + Tuning.PowerFrames;
        Popup("POWER UP", PX, PY - PowerPopupRise, WeaponColour);
        ClearPartsInFight();
        if (!card.Gold) return;
        bool room = Bombs < Tuning.BombCap;
        if (room) Bombs++;
        Popup(room ? GoldPopup : GoldPopupFull, PX, PY - GoldPopupRise, BombColour);
        AddRing(PX, PY, GoldRingFrom, GoldRingTo, BombColour, GoldRingFrames);
        Sound(SfxNames.PickupBomb, PX);
    }

    /// <summary>The host's write went through. A like or repost is lit for good and its record's address is kept, for the undo; a delete
    /// (<paramref name="undo"/>) puts the button off and drops the address, and says so.</summary>
    public void WriteConfirmed(string postUri, PostAction action, string? recordUri, bool undo = false)
    {
        if (Book.Find(postUri) is not { } m) return;
        if (undo)
        {
            if (m.Of(action) != ButtonState.Pending) return;
            SetState(m, action, ButtonState.Off);
            if (action == PostAction.Like) m.LikeUri = null; else m.RepostUri = null;
            Say(action == PostAction.Like ? "Like removed." : "Repost removed.");
            return;
        }
        if (m.Of(action) == ButtonState.Pending) SetState(m, action, ButtonState.Lit);
        if (recordUri is null) return;
        if (action == PostAction.Like) m.LikeUri = recordUri; else m.RepostUri = recordUri;
    }

    /// <summary>The host's write failed: the button goes back to its state before the ask (off for an asked-for like or repost, lit
    /// for a delete), and the toast says why. The reward stays.</summary>
    public void WriteFailed(string postUri, PostAction action, string? why = null, bool undo = false)
    {
        string what = action == PostAction.Like ? (undo ? "Undo like failed" : "Like failed") : (undo ? "Undo repost failed" : "Repost failed");
        Say(string.IsNullOrEmpty(why) ? what + "." : what + ": " + (why.EndsWith('.') ? why : why + "."));
        Sound(SfxNames.NoLike, PX);
        if (Book.Find(postUri) is not { } m || m.Of(action) != ButtonState.Pending) return;
        if (undo) { SetState(m, action, ButtonState.Lit); return; }
        SetState(m, action, ButtonState.Off);
        if (action == PostAction.Like) m.LikeUri = null; else m.RepostUri = null;
    }
}
