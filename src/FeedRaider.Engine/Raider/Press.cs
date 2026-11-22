namespace FeedRaider.Engine.Raider;

/// <summary>How a pressed like or repost looks on its button: the icon at its widest and near white in a halo for the frozen
/// frames, then falling back into the button; and the colours of the outline round the buttons the keys act on.</summary>
public static class PressLook
{
    /// <summary>The tallest a pressed icon is drawn, in pixels: the card's footer, which is its button's box, so it never covers the
    /// words above it or the next card below.</summary>
    public const int MaxIconHeight = CardLayout.FooterRows * CardField.PixelsPerCell;
    /// <summary>The widest a pressed icon is drawn: the art's shape at <see cref="MaxIconHeight"/>.</summary>
    public const int MaxIconWidth = MaxIconHeight * CardLayout.IconWidthPx / CardLayout.IconHeightPx;
    /// <summary>How far the halo reaches past the icon, in pixels; it reaches no higher or lower than the button's box.</summary>
    public const int HaloMargin = 3;
    /// <summary>The frames a pressed icon takes to fall back into its button, after the frozen ones.</summary>
    public static readonly int[] SettleWidths = [6, 5];

    private const uint LikeDim = 0x86384f, RepostDim = 0x2f6c4f;
    private const uint LikeLit = 0xff2d55, RepostLit = 0x1fd67a;
    private const uint LikeFlash = 0xffd9e0, RepostFlash = 0xd9ffe9;

    /// <summary>The shake of a press: over <see cref="Tuning.ShakeFloor"/>, so the picture moves for a few frames and settles.</summary>
    public const double Shake = 1.6;
    /// <summary>The frames a press freezes the game, so the sparks land on a still picture.</summary>
    public const int HitStop = 2;

    /// <summary>The colour of the outline round the target's buttons and of an undo's shrinking ring: the button's own colour, dimmed, so it marks without shouting.</summary>
    public static uint Dim(PostAction a) => a == PostAction.Like ? LikeDim : RepostDim;
    /// <summary>The colour of a press's rings and sparks, and of the icon as it settles.</summary>
    public static uint Lit(PostAction a) => a == PostAction.Like ? LikeLit : RepostLit;
    /// <summary>The near white of the icon in the frozen frames, and of every third spark.</summary>
    public static uint FlashColour(PostAction a) => a == PostAction.Like ? LikeFlash : RepostFlash;
    /// <summary>How strongly the halo tints the slab: enough to read as a glow, weak enough to leave the slab's colour.</summary>
    private const double HaloStrength = 0.38;
    /// <summary>The colour the halo leaves on the slab: one colour, so the terminal's glyph atlas gains little.</summary>
    public static uint HaloColour(PostAction a) => Surface.Mix(CardColours.Slab, Lit(a), HaloStrength);

    /// <summary>The height of an icon drawn <paramref name="width"/> pixels wide: the icon art, scaled.</summary>
    public static int IconHeight(int width) => (int)Math.Round(width * CardLayout.IconHeightPx / (double)CardLayout.IconWidthPx);
}

/// <summary>The button pressed last, and the frame it was pressed on: what the flash and the settle read.</summary>
public sealed class PressTracker
{
    /// <summary>The frame of a press that has not happened: far enough back that any frame minus it is past every effect, and
    /// half the range so the subtraction cannot overflow.</summary>
    private const long NeverFrame = long.MinValue / 2;
    /// <summary>The frames since a press on a button it did not fall on: past every effect, and half the range so a caller
    /// adding to it cannot overflow.</summary>
    private const long FarFrames = long.MaxValue / 2;

    public PostMarks? Marks { get; internal set; }
    public PostAction Action { get; internal set; }
    public long Frame { get; internal set; } = NeverFrame;

    /// <summary>Frames since this button was pressed, or <see cref="FarFrames"/>.</summary>
    public long Since(PostMarks m, PostAction a, long frame) => ReferenceEquals(Marks, m) && Action == a ? frame - Frame : FarFrames;
}

public sealed partial class RaiderGame
{
    /// <summary>The last like or repost pressed, for the view's flash and settle.</summary>
    public PressTracker LastPress { get; } = new();

    private bool _saidSignIn;

    private static void SetState(PostMarks m, PostAction a, ButtonState s)
    {
        if (a == PostAction.Like) m.Like = s; else m.Repost = s;
    }

    /// <summary>Whether this button is locked: the player's sign-in may not make its write, and it is not made already.</summary>
    public bool IsLocked(Card card, PostAction action) =>
        !MayWrite(action) && card.Marks.Of(action) == ButtonState.Off && Cards.ButtonBox(card, action).Y >= CardPainter.HudPixelRows;

    /// <summary>Whether a press on this button likes or reposts: not lit and not waiting on its write, not locked, and clear of the HUD's rows.</summary>
    public bool CanPress(Card card, PostAction action)
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

    /// <summary>Offsets into the sparks' hash stream so each spark's speed and life draw from values apart from its angle's.</summary>
    private const int PressSparkSpeedStream = 100, PressSparkLifeStream = 200;
    /// <summary>Every this-many sparks, from the first, is the pale colour.</summary>
    private const int PressSparkPaleEvery = 3;
    /// <summary>The sparks of a like or repost: how many, the jitter of each one's angle (radians), the speed range, the
    /// extra pull sideways, the upward lift and the life range (frames).</summary>
    private const int PressSparkCount = 14, PressSparkLifeMin = 16, PressSparkLifeSpread = 12;
    private const double PressSparkAngleJitter = 0.45, PressSparkSpeedMin = 0.7, PressSparkSpeedSpread = 1.5, PressSparkSideStretch = 1.3, PressSparkLift = 0.35;
    /// <summary>The two rings of a like or repost: radius at the start and the end (pixels), and the frames they last.</summary>
    private const int InnerRingFrom = 3, InnerRingTo = 14, InnerRingFrames = 12;
    private const int OuterRingFrom = 6, OuterRingTo = 22, OuterRingFrames = 18;
    /// <summary>How far above the ship the reward pop-ups appear, in pixels: a pop-up is four pixels tall, so the four stack.</summary>
    private const int PartsPopupRise = 22, GoldPopupRise = 18, ShieldPopupRise = 14;
    private long _shieldRewardReadyTick;
    /// <summary>The ring round the ship when a gold card pays: radius at the start and the end (pixels), and the frames it lasts.</summary>
    private const int GoldRingFrom = 8, GoldRingTo = 26, GoldRingFrames = 16;
    /// <summary>What a gold card's pop-up says when it gives a bomb, and when the ship already carries every bomb it can.</summary>
    public const string GoldPopup = "BANGER +BOMB", GoldPopupFull = "BANGER";
    /// <summary>What the pop-up says when a like or repost takes a share off the parts of the enemies in the fight.</summary>
    public const string PartsPopup = "PARTS HIT";

    /// <summary>A like or repost made: the button marked or sent, the sparks, the reward and the write.</summary>
    private void LikeOrRepost(Card card, PostAction action, double x, double y)
    {
        var m = card.Marks;
        LastPress.Marks = m; LastPress.Action = action; LastPress.Frame = Frame;
        bool like = action == PostAction.Like;
        var write = like ? OnLike : OnRepost;
        bool real = SignedIn && write is not null;
        SetState(m, action, real ? ButtonState.Pending : ButtonState.Marked);

        uint lit = PressLook.Lit(action), pale = PressLook.FlashColour(action);
        AddShake(PressLook.Shake, PressLook.HitStop);
        // the sparks are placed by the post and the frame, not by the game's generator: a like must not change what a seeded run does next
        ulong h = Hashing.Mix(Hashing.MixedFnv64(card.Post.Uri) ^ (ulong)Frame);
        for (int i = 0; i < PressSparkCount; i++)
        {
            double ang = i / (double)PressSparkCount * Math.Tau + Hashing.Unit(h, i) * PressSparkAngleJitter, sp = PressSparkSpeedMin + Hashing.Unit(h, PressSparkSpeedStream + i) * PressSparkSpeedSpread;
            AddParticle(new Particle
            {
                X = x,
                Y = y,
                VX = Math.Cos(ang) * sp * PressSparkSideStretch,
                VY = Math.Sin(ang) * sp - PressSparkLift,
                Life = PressSparkLifeMin + (int)(Hashing.Unit(h, PressSparkLifeStream + i) * PressSparkLifeSpread),
                Colour = i % PressSparkPaleEvery == 0 ? pale : lit,
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

    /// <summary>What a like or repost pays, once for each post: the shield back if it is down and not refilled in the last <see cref="Tuning.ShieldRewardCooldownFrames"/>, a quarter off every living part of every enemy in the fight, and
    /// on a gold card one bomb, up to the cap. The press pays, not the write, so a write that fails later takes nothing back.</summary>
    private void Reward(Card card)
    {
        var m = card.Marks;
        if (m.Paid) return;
        m.Paid = true;
        if (Tick < _shieldRewardReadyTick) Popup("SHIELD RECHARGING", PX, PY - ShieldPopupRise, ShieldColour);
        else if (Shield > 0) Popup("SHIELD FULL", PX, PY - ShieldPopupRise, ShieldColour);
        else
        {
            Shield = Tuning.ShieldHits;
            ShieldHitFrame = Frame;
            _shieldRewardReadyTick = Tick + Tuning.ShieldRewardCooldownFrames;
            Popup("SHIELD UP", PX, PY - ShieldPopupRise, ShieldColour);
        }
        ChipPartsInFight();
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
