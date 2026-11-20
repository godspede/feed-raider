namespace FeedRaider.Engine.Raider;

public enum ParticleKind { Spark, Debris, Heart }

/// <summary>Anything fired, by either side, is a bullet. The ship's bullet is a shot and an enemy's is a bolt; a shot an enemy
/// sends back is still a shot, a returned one, so the hostile bullets are the bolts and the returned shots.</summary>
public sealed class Bullet
{
    public double X, Y, VX, VY, Dmg;
    public bool Friendly;
    /// <summary>An enemy bullet fired by a turret: drawn as a diamond in the turret's colour.</summary>
    public bool Aimed;
    /// <summary>A ship's shot an enemy sent back at the ship (<see cref="RaiderGame.Reflect"/>): hostile from then on, and drawn in the ship's colour.</summary>
    public bool Reflected;
}

public enum PartState { Armoured, Open, Dead }

/// <summary>A part of an enemy with its own box and hit points.</summary>
public sealed class Part
{
    /// <summary>"pod-l", "eye-c": tests and the kind find parts by name.</summary>
    public string Name = "";
    /// <summary>Centre, relative to the owner's X and Y; a kind may move it every frame.</summary>
    public double OX, OY;
    /// <summary>Drawn while armoured, and while open when <see cref="OpenArt"/> is null.</summary>
    public Sprite Closed = null!;
    public Sprite? OpenArt;
    /// <summary>Drawn while dead; null means nothing is drawn and shots pass through.</summary>
    public Sprite? Wreck;
    /// <summary>Half-width and half-height of the hit box; may be smaller than the art, and has no ceiling.</summary>
    public int HW, HH;
    public double Hp, MaxHp;
    public PartState State;
    /// <summary>Frames left in <see cref="State"/>; the kind sets and reads it.</summary>
    public int Timer;
    public int Flash;
    /// <summary>The owner dies when every vital part is dead.</summary>
    public bool Vital = true;
    public int Score;
}

public sealed class Enemy
{
    public required EnemyKind Kind;
    public required Sprite Sprite;
    public double X, Y, BaseX, KX, KY;
    public double Hp, MaxHp;
    public int Age, Flash, FireCd, Hits;
    public double Amp, Phase;
    public string? Text;
    public bool Live;
    public DarknessKind? Source;
    /// <summary>The header of a darkness enemy's speech box ("SPAM"), built once on arrival; null for filler.</summary>
    public string? Header;
    public string[]? SpeechLines;
    public int SpeechBoxAge, PageLines = SpeechBoxText.MinPageLines;
    /// <summary>Cells the speech box was moved from its home to stay clear of another box; it keeps them while it fits there.</summary>
    public (int Col, int Row) SpeechBoxShift;
    /// <summary>The speech box is open but has no clear place to show, so it is not aged until it has (<see cref="EnemySpeechBox.Settle"/>).</summary>
    public bool SpeechBoxHeld;
    public bool Leaving;
    /// <summary>The fall in pixels a frame of an enemy whose maker sets none; an arriving enemy sets its own.</summary>
    public const double DefaultFall = 0.55;
    public double VY = DefaultFall;
    public double VX;
    /// <summary>Frames from arrival in which it cannot touch the ship (an unarmed mine, a new shard).</summary>
    public int Grace;
    /// <summary>Frames left of a wind-up (diver, turret); above zero the tell is on screen and nothing has been fired yet.
    /// <see cref="WindupNotBegun"/>: the wind-up has not begun.</summary>
    public int Windup;
    public const int WindupNotBegun = -1;
    /// <summary>The spot a diver or turret locked when its wind-up began; the charge or the burst goes there, not where the ship went.</summary>
    public double AimX, AimY;
    public int Volleys, Burst;
    /// <summary>A diver past its wind-up, charging.</summary>
    public bool Dashing;
    /// <summary>Frames left of the shiver before a sidestep, of the slide itself, and until it may sidestep again.</summary>
    public int EvadeWind, EvadeSlide, EvadeCd;
    /// <summary>-1 left, +1 right: where the sidestep goes.</summary>
    public int EvadeDir;
    public bool Evading => EvadeWind > 0 || EvadeSlide > 0;
    public bool Armed => Age >= Grace;

    /// <summary>The parts of an enemy that is more than a sprite; null for an enemy that is one sprite. While set, <see cref="Hp"/> and
    /// <see cref="MaxHp"/> are kept equal to the sum over its vital parts, so a health bar and an <c>Hp &gt; 0</c> test keep working.</summary>
    public List<Part>? Parts;
    /// <summary>The box of the living parts and wrecks, offsets from X and Y, and the frame it is made for (<see cref="PartBoxStale"/>: none). Kept by <c>HitParts</c>; a shot outside it tests no part.</summary>
    internal (long Frame, double L, double T, double R, double B) PartBox = (PartBoxStale, 0, 0, 0, 0);
    /// <summary>A <see cref="PartBox"/> frame no game frame equals, so the box is made again on its next use.</summary>
    internal const long PartBoxStale = -1;
    /// <summary>A kind's own phase and the frames it has lasted. The game counts <see cref="ModeAge"/> up once a frame; the kind sets both.</summary>
    public int Mode, ModeAge;
    /// <summary>One private state object per kind (a timer table, a queue of marked cells). Never shared between enemies.</summary>
    public object? Kit;
    /// <summary>A warden's charge: set each frame by the shielding enemy, it makes this enemy shrug off shots the way armour does. Cleared at the end of each frame.</summary>
    public Enemy? ShieldedBy;
    /// <summary>True: shots and the ship pass through it and bombs skip it (a pane that is not lit, an enemy that has not surfaced).</summary>
    public bool Untouchable;
    /// <summary>The smaller visit of a bigger enemy (a lesser gunship).</summary>
    public bool Lesser;
    /// <summary>The enemy that made this one (<see cref="RaiderGame.SpawnAdd"/>).</summary>
    public Enemy? Parent;
    /// <summary>What the enemy remembers of its own talking; null until it is found to be a speaker.</summary>
    internal VoiceState? Voice;
    /// <summary>A copy of the enemy as it is, for the box a speaker leaves behind. It shares the lines and parts, so it is not to be updated.</summary>
    internal Enemy Snapshot() => (Enemy)MemberwiseClone();
}

public sealed class Particle
{
    public double X, Y, VX, VY;
    public int Life, MaxLife;
    public uint Colour;
    public int Size = 1;
    public ParticleKind Kind;
}

public sealed class Ring { public double X, Y, R0, R1; public int Life, Max; public uint Colour; }
public sealed class Popup { public string Text = ""; public double X, Y; public int Life; public uint Colour; }

/// <summary>
/// Feed Raider's simulation: a fixed 30 Hz step, no clock and no network. Time is the number of calls to <see cref="Step"/>;
/// randomness is a seeded generator, so a run is a pure function of the seed and the inputs. Hosts feed it input
/// (<see cref="Input"/>), darkness events (<see cref="OfferDark"/>) and feed posts (<see cref="Posts"/>), and read its
/// state to draw. Posts wait in <see cref="Posts"/> and become cards of <see cref="Cards"/>, which scroll down the field on every
/// simulation tick and land in <see cref="Log"/> when they leave. Resting the ship on a card's heart for three seconds likes the post
/// and three on its loop reposts it (<see cref="Hold"/>): signed in, the game asks the host for the one write through
/// <see cref="OnLike"/> or <see cref="OnRepost"/>.
/// </summary>
public sealed partial class RaiderGame
{
    private const double OffFieldMargin = 10, OffSideMargin = 4;   // how far past the field's bottom and sides an enemy goes before it is dropped
    private const double EvadeShiver = 0.8;
    private const int ShiverPeriod = 4;   // a shiver flips side every half of this many frames
    private const double ShakeCarryShare = 0.3;   // a new shake adds this share of the weaker of the two
    private const int TouchDragCellsToPixels = 2;   // a dragged cell is two field pixels wide
    private const double ShipMinY = 10;
    private const double BombRingStart = 4, BombRingShare = 0.8;
    private const int BombRingFrames = 20, BombDamage = 30;
    private const double BombReachY = -4;   // a bomb reaches enemies whose centre is below this row
    private const int FillerMaxAlive = 3;   // a filler wave starts only while fewer enemies than this are up
    private const double ShardSpawnOffset = 3;
    private const double BoltHitHalfWidth = 1.8, BoltHitHalfHeight = 2.2;
    private const int ShipHitFlashFrames = 2, ShipHitSparks = 14, SparkLifeMin = 10, SparkLifeSpan = 8;
    private const double SparkSpeed = 3, SparkDrag = 0.92, PopupRise = 0.35;
    private const int DeathScoreDivisor = 4;   // a death costs a quarter of the score
    private const double BulletMargin = 6;   // how far past the field's edge a bullet flies before it is dropped
    private const double RamDamage = 4, RamHitOffsetY = 2;   // a ship ramming a body: the damage dealt, and how far above the ship the hit lands
    private const int HeavyHitStopEvery = 4;   // every this many hits on a gunship stops the frame
    private const double EnemyTopHitRow = -2;   // an enemy above this row cannot be hit by a shot yet
    private const double BigSoundSize = 1, MidSoundSize = 0.5;
    private const double KillRingStart = 2, MineRingExtra = 4;
    private const double MineDebrisMinSpeed = 0.4, MineDebrisSpeedSpan = 1.4;
    private const int MineDebrisMinLife = 12, MineDebrisLifeSpan = 10;
    private const double BigDebrisMinSpeed = 0.6, BigDebrisSpeedSpan = 2.0, DebrisScatterShare = 0.6;
    private const double BigKillInnerRingStart = 1, BigKillInnerRingShare = 0.6;
    private const int BigKillInnerRingFewerFrames = 4;
    /// <summary>The chance a random draw lands on the first of two equal choices.</summary>
    private const double CoinFlip = 0.5;
    private const double DebrisMinSpeed = 0.3, DebrisSpeedSpan = 1.2, DebrisLift = 0.3, DebrisBigChance = 0.3;
    private const int DebrisMinLife = 14, DebrisLifeSpan = 14, DebrisBigSize = 2, DebrisSmallSize = 1;
    private const uint SmallKillRingColour = 0xff8fb1u;
    private const double ScorePopupRise = 2;
    private const double DeathDebrisMinSpeed = 0.5, DeathDebrisSpeedSpan = 2;
    private const int DeathDebrisMinLife = 16, DeathDebrisLifeSpan = 14;
    private const double ShipEdgeGap = 1;   // pixels the ship's sprite keeps from the side and bottom edges
    private const double BombPopupRise = 8;
    private const int EvadeFlipPeriod = 7;   // frames a dead-centre sidestep keeps its side before it may flip
    private const double EvadePhaseSpread = 3, EvadeCentreBand = 0.5, EvadeRoomShare = 0.5;
    private const ulong XorshiftMultiplier = 0x2545F4914F6CDD1DUL, FrameHashStep = 0x9E3779B97F4A7C15UL;

    // the terminal size the layout starts at, before a host reports its own
    private const int StartCols = 100, StartRows = 46;

    public RaiderGame(ulong seed = 1)
    {
        _rs = Hashing.Mix(seed) | 1;
        _seed = seed;
        SetLayout(FieldLayout.Compute(StartCols, StartRows));
        Cards = new CardField(Posts, Book, Layout.PCols, Layout.Rows, Raider.FeedSpeed.Default);
        Cards.Skip = post => Rude.Holds(Hashing.MixedFnv64(post.Uri));
        Cards.Entered += card => WantAvatar(card.Post);
        Cards.Entered += WantPictures;
        Cards.Left += card =>
        {
            // a button part filled when its card leaves is not carried into the log
            card.Marks.LikeProgress = card.Marks.RepostProgress = 0;
            Log.Add(new LogEntry(card.Post, card.Marks));
        };
    }

    private readonly ulong _seed;
    private ulong _rs;
    private void Sound(string name, double? x = null, double size = 0) =>
        Sfx?.Play(name, x is { } at ? Pan(at) : 0, Frame, size);
    /// <summary>A field x as a stereo pan, -1 at the left edge to 1 at the right; 0 while the field has no width.</summary>
    private double Pan(double x) => PW <= 0 ? 0 : x / PW * 2 - 1;
    private double R() => NextUnit(ref _rs);

    /// <summary>The bits of a double's mantissa: a draw keeps the top this many bits of the 64, so every value is exact.</summary>
    private const int UnitBits = 53;
    /// <summary>One step of xorshift64* (shifts 12, 25, 27 and the final multiplier) on <paramref name="state"/>, read as a double in
    /// [0, 1) from its top <see cref="UnitBits"/> bits.</summary>
    private static double NextUnit(ref ulong state)
    {
        state ^= state >> 12; state ^= state << 25; state ^= state >> 27;
        return ((state * XorshiftMultiplier) >> (64 - UnitBits)) / (double)(1UL << UnitBits);
    }

    // --- time ---
    /// <summary>Every call to <see cref="Step"/>, including frozen (hit-stop) ones: drives pulses and the shake.</summary>
    public long Frame { get; private set; }
    /// <summary>Sim time: advances only on frames that were not frozen.</summary>
    public long Tick { get; private set; }
    public double ScrollFrames { get; private set; }

    // --- field ---
    public FieldLayout Layout { get; private set; }
    public int PW => Layout.PW;
    public int PH => Layout.PH;

    public InputState Input { get; } = new();

    // --- host hooks ---
    public bool SignedIn { get; set; }

    /// <summary>True in Random mode: a thread continuation that carries a moderation label ends the thread, as a labelled feed post is never served.</summary>
    public bool PlainOnly { get; set; }

    /// <summary>The like of a post: called once when a hold on a card's heart finishes and the player is signed in. The host makes
    /// the write and answers with <see cref="WriteConfirmed"/> or <see cref="WriteFailed"/>.</summary>
    public Action<FeedPost>? OnLike { get; set; }
    /// <summary>The repost of a post, asked for and answered as a like is.</summary>
    public Action<FeedPost>? OnRepost { get; set; }
    /// <summary>What the player's sign-in may write. A sign-in made before the game could repost may only like: its repost
    /// buttons are locked. The host may change this in the middle of a run, after a fresh sign-in.</summary>
    public Grants Grants { get; set; } = Grants.All;
    /// <summary>Whether a hold on this kind of button may go ahead: always for a player who is not signed in (nothing is
    /// written for them), else only with the grant.</summary>
    public bool MayWrite(PostAction action) => !SignedIn || (action == PostAction.Like ? Grants.Like : Grants.Repost);
    /// <summary>The delete of the player's own like or repost record of a post: called once when a lit button is toggled off in the feed log
    /// and the player is signed in. The record's address is in the post's marks. The host answers with <see cref="WriteConfirmed"/> or
    /// <see cref="WriteFailed"/>, each with <c>undo</c> set.</summary>
    public Action<FeedPost, PostAction>? OnDelete { get; set; }

    /// <summary>The feed log's toggle (E, R, a click, the pause-menu list): turns the post's like or repost on if it is off or marked, off if
    /// it is lit. Signed in, that is one write or delete and the button waits, blinking, for the answer; signed out the mark flips and nothing
    /// is sent. A button already waiting is left alone, and one the sign-in may not use says so. It never pays the hold's reward.</summary>
    public void Toggle(string postUri, PostAction action)
    {
        if (Book.Find(postUri) is not { } m || PostOf(postUri) is not { } post) return;
        var state = m.Of(action);
        if (state == ButtonState.Pending) return;
        bool like = action == PostAction.Like, lit = state == ButtonState.Lit;
        var create = like ? OnLike : OnRepost;
        bool real = SignedIn && (lit ? OnDelete is not null : create is not null);
        if (!real)
        {
            SetState(m, action, state is ButtonState.Off ? ButtonState.Marked : ButtonState.Off);
            return;
        }
        if (!(lit ? (like ? Grants.Unlike : Grants.Unrepost) : MayWrite(action)))
        {
            Say(NeedsFreshSignIn(lit ? "Undo" : like ? "Like" : "Repost"));
            Sound(SfxNames.NoLike, PX);
            return;
        }
        if (lit)
        {
            if ((like ? m.LikeUri : m.RepostUri) is null)
            {
                Say($"That {(like ? "like" : "repost")} cannot be removed from here.");
                Sound(SfxNames.NoLike, PX);
                return;
            }
            SetState(m, action, ButtonState.Pending);
            OnDelete!(post, action);
            return;
        }
        SetState(m, action, ButtonState.Pending);
        Sound(like ? SfxNames.Like : SfxNames.Repost);
        Say(WriteSentText(like, post));
        create!(post);
    }

    /// <summary>What the toast says when the sign-in lacks the grant for <paramref name="what"/> ("Like", "Repost", "Undo").</summary>
    private static string NeedsFreshSignIn(string what) => $"{what} needs a fresh sign-in. See the pause menu.";

    /// <summary>What the toast says when a like or a repost of <paramref name="post"/> goes to the host.</summary>
    private static string WriteSentText(bool like, FeedPost post)
    {
        string handle = CellText.CleanSingle(post.AuthorHandle);
        return like ? $"Liked @{handle}." : $"Reposted @{handle}. Undo in the feed log.";
    }

    /// <summary>The post with this address, from the feed log (the newest entry first) or else a card on the field.</summary>
    private FeedPost? PostOf(string uri)
    {
        for (int i = Log.Entries.Count - 1; i >= 0; i--) if (Log.Entries[i].Post.Uri == uri) return Log.Entries[i].Post;
        foreach (var c in Cards.Cards) if (c.Post.Uri == uri) return c.Post;
        return null;
    }

    /// <summary>The feed-log keys of this tick: the mark moves first, then E and R act on the entry it stands on.</summary>
    private void UpdateLogKeys()
    {
        if (Input.MarkUpPressed) { Input.MarkUpPressed = false; Log.MoveMark(-1); }
        if (Input.MarkDownPressed) { Input.MarkDownPressed = false; Log.MoveMark(1); }
        bool like = Input.LikeLatch > 0, repost = Input.RepostLatch > 0;
        Input.LikeLatch = Input.RepostLatch = 0;
        if (Log.Marked is not { } e) return;
        if (like) Toggle(e.Post.Uri, PostAction.Like);
        if (repost) Toggle(e.Post.Uri, PostAction.Repost);
    }
    /// <summary>Called once for a post when a hold finishes its like, signed in or not: asks the host for the author's own next
    /// reply to it. The host answers with <see cref="ThreadOpened"/>, or never: no answer shows nothing. A repost and a toggle in
    /// the log do not ask. Never awaited by the game.</summary>
    public Action<FeedPost>? ThreadWanted { get; set; }
    /// <summary>Asks the host for the author's avatar; answer with <see cref="SetAvatar"/>.</summary>
    public Action<FeedPost>? AvatarWanted { get; set; }
    /// <summary>Where the music cues go; null is silence. Fed once per step, so it only fires when the musical moment changes.</summary>
    public MusicDirector? Music { get; set; }
    /// <summary>Where the sound effects go; null is silence. Called when something happens, never per frame.</summary>
    public SfxBus? Sfx { get; set; }
    /// <summary>The best score from earlier runs (0 = none). A run that passes it reports its score through <see cref="OnBest"/>.</summary>
    public int BestToBeat { get; set; }
    /// <summary>Called with the run's score when it is above <see cref="BestToBeat"/> and the ship dies or a second passes.</summary>
    public Action<int>? OnBest { get; set; }

    // --- ship ---
    public double PX { get; private set; }
    public double PY { get; private set; }
    public int Hp { get; private set; } = Tuning.ShipHp;
    public int Bombs { get; private set; } = Tuning.ShipBombs;
    public int Invuln { get; private set; }
    public int RespawnIn { get; private set; }
    public bool Alive => RespawnIn <= 0;
    public int Score { get; private set; }
    public int Deaths { get; private set; }
    public int Kills { get; private set; }
    public int MuzzleFrames { get; private set; }
    /// <summary>The player identity the ship is drawn for before a host names the real one.</summary>
    private const string StandInPlayerDid = "did:plc:player";
    public string PlayerDid { get; set; } = StandInPlayerDid;
    public Sprite Ship { get; private set; } = ShipFactory.Make(StandInPlayerDid);
    private int _fireCd;
    /// <summary>A power end tick every tick is past: no power runs.</summary>
    private const long NoPower = -1;
    private long _powerUntil = NoPower;
    public int PowerLeft => (int)Math.Max(0, _powerUntil - Tick);
    public bool Powered => PowerLeft > 0;

    public void SetPlayer(string did)
    {
        PlayerDid = did;
        Ship = ShipFactory.Make(did);
    }

    // --- world ---
    public readonly List<Enemy> Enemies = [];
    /// <summary>The last words of enemies that were killed or left mid-post: their boxes stay up until every page has been read.</summary>
    public readonly List<Enemy> Echoes = [];
    public readonly List<Bullet> Bullets = [];
    public readonly List<Particle> Particles = [];
    public readonly List<Ring> Rings = [];
    public readonly List<Popup> Popups = [];
    /// <summary>The posts the feed loop has offered, waiting to become cards.</summary>
    public PostQueue Posts { get; } = new();
    /// <summary>The marks of every post that has been a card, by post address.</summary>
    public PostBook Book { get; } = new();
    /// <summary>The cards on the field, scrolling down. Stepped once per simulation tick, so hit-stop frames do not move them.</summary>
    public CardField Cards { get; }
    /// <summary>The feed-speed step, 1 to 5 (<see cref="Raider.FeedSpeed"/>). It takes effect at the card field's next row step.</summary>
    public int FeedSpeed { get => _feedSpeed; set { _feedSpeed = Raider.FeedSpeed.Clamp(value); Cards.SetSpeed(_feedSpeed); } }
    private int _feedSpeed = Raider.FeedSpeed.Default;
    /// <summary>The player changed the feed speed (a key, or the pause menu closing): clamps it, sets <see cref="FeedSpeed"/> and says which step it is on.</summary>
    public void SetFeedSpeed(int step)
    {
        FeedSpeed = step;
        Say($"Feed speed {FeedSpeed} of {Raider.FeedSpeed.Max}");
    }
    public FeedLog Log { get; } = new();
    private readonly HashSet<string> _avatarAsked = new(StringComparer.Ordinal);
    private readonly Queue<WaveSpec> _waves = new();
    private readonly List<(long At, EnemySpawn Spawn, bool Live, DarknessKind? Src, string? Header)> _pending = [];
    private readonly Dictionary<string, uint[]> _avatars = new(StringComparer.Ordinal);
    private int _sinceWave, _emptyFor, _fillerN;

    public int WavesQueued => _waves.Count;

    // --- the difficulty ramp (see Ramp) ---
    /// <summary>One point per frame flown alive plus <see cref="Tuning.StageKillFrames"/> per kill; dying gives back part of it (<see cref="Ramp.AfterDeath"/>).</summary>
    public long Progress { get; private set; }
    public int Stage => Ramp.StageAt(Progress);
    /// <summary>The stage the banner announces; it trails <see cref="Stage"/> by at most a frame.</summary>
    public int StageShown { get; private set; } = 1;
    /// <summary>Frames left of the "STAGE n" banner; 0 when it is off.</summary>
    public int StageBannerLeft { get; private set; }

    /// <summary>Starts the run at a later stage (the web host's dev-only <c>?stage=</c>, and tests). Kinds and levers follow at once.</summary>
    public void StartAtStage(int stage)
    {
        Progress = Ramp.StageStart(stage);
        BossesBeaten = Stage - 1;
        ResetGate();
        StageShown = Stage;
        StageBannerLeft = Stage > 1 ? Tuning.StageBannerFrames : 0;
    }
    public int WavesSpawned { get; private set; }
    public int LiveWavesSpawned { get; private set; }
    /// <summary>Waves from a real darkness event whose enemies are all killed.</summary>
    public int LiveWavesCleared { get; private set; }

    // --- impact state ---
    public int HitStop { get; private set; }
    public double Shake { get; private set; }
    public int ScreenFlash { get; private set; }
    public string? Toast { get; private set; }
    public int ToastLeft { get; private set; }

    // ---------------------------------------------------------------- hosts

    // how close the ship may come to the field's edges, in field pixels
    private const int ShipSideMargin = 6, ShipTopMargin = 10, ShipBottomMargin = 8;
    // the bomb button sits this many columns in from the field's right edge and this many rows above the bottom
    private const int BombButtonInset = 11, BombButtonRowsUp = 3, BombButtonW = 10, BombButtonH = 2;

    public void SetLayout(FieldLayout layout)
    {
        bool firstLayout = Layout.PCols == 0;
        Layout = layout;
        Cards?.Resize(layout.PCols, layout.Rows);
        if (firstLayout) { PX = PW / 2.0; PY = PH - Tuning.ShipStartAbove; }
        PX = Math.Clamp(PX, ShipSideMargin, Math.Max(ShipSideMargin, PW - ShipSideMargin));
        PY = Math.Clamp(PY, ShipTopMargin, Math.Max(ShipTopMargin, PH - ShipBottomMargin));
    }

    /// <summary>The terminal cells of the on-screen bomb button (for touch): column, row, width, height.</summary>
    public (int C, int R, int W, int H) BombButton => (Layout.Col0 + Layout.PCols - BombButtonInset, Layout.Rows - BombButtonRowsUp, BombButtonW, BombButtonH);

    /// <summary>Takes a stranger's post: a spam post is queued as a wave whose carrier shows its text, and a rude post
    /// makes no enemy but waits in <see cref="Rude"/> until a speaker says it.</summary>
    public void OfferDark(DarkPost post)
    {
        if (post.Kind == DarknessKind.Rude)
        {
            Rude.Add(new RudePost(post.Seed, DarknessText.Clean(post.Text, DarknessText.MaxPostChars), post.Source, post.Label, Frame));
            return;
        }
        if (_waves.Count >= Tuning.MaxPendingWaves) _waves.Dequeue();
        _waves.Enqueue(WaveSpawner.FromPost(post, Stage));
    }

    public void SetAvatar(string did, uint[] px)
    {
        if (px.Length != AvatarCsv.Pixels) throw new ArgumentException($"an avatar is {AvatarCsv.Pixels} pixels, not {px.Length}", nameof(px));
        _avatars[did] = px;
    }

    /// <summary>The 8x8 picture the host gave for this author, or null while it has not arrived.</summary>
    public uint[]? AvatarOf(string did) => _avatars.TryGetValue(did, out var px) ? px : null;

    /// <summary>Asks the host for the author's avatar the first time a card of theirs is made, unless it is already here.</summary>
    private void WantAvatar(FeedPost post)
    {
        if (_avatars.ContainsKey(post.AuthorDid) || !_avatarAsked.Add(post.AuthorDid)) return;
        AvatarWanted?.Invoke(post);
    }

    /// <summary>Hears every toast, so a host can pass it on to a screen reader. Called under the game lock.</summary>
    public Action<string>? Said { get; set; }
    private const int ToastSeconds = 2;
    private const double HeartDrag = 0.94, HeartRise = 0.02;
    public void Say(string text) { Toast = text; ToastLeft = Tuning.Fps * ToastSeconds; Said?.Invoke(text); }

    /// <summary>The host found the author's next reply to a liked post. When the rules let this player see it, it is the next
    /// card to enter, ahead of every waiting post, and carries the thread note; when they do not, it is dropped.</summary>
    public void ThreadOpened(FeedPost next)
    {
        if (!PostRules.Followable(next) || !PostRules.Allowed(next, signedOut: !SignedIn)) return;
        if (PlainOnly && !RandomFeedSource.Plain(next)) return;
        Posts.OfferFront(next);
    }

    // ---------------------------------------------------------------- the step

    public void Step()
    {
        Frame++;
        DecayShake();
        if (ToastLeft > 0 && --ToastLeft == 0) Toast = null;
        if (ScreenFlash > 0) ScreenFlash--;
        if (HitStop > 0)
        {
            HitStop--;
            Input.EndFrame();
            SaveBest();
            UpdateMusic();
            return;
        }
        Tick++;
        ScrollFrames++;
        Cards.Step();
        UpdateLogKeys();

        UpdateShip();
        UpdateHold();
        UpdateStage();
        UpdateWaves();
        UpdateEnemies();
        UpdateVoice();
        EnemySpeechBox.Settle(this);
        UpdateBullets();
        Collide();
        UpdatePickups();
        UpdateEffects();
        Input.EndFrame();
        SaveBest();
        UpdateMusic();
    }

    private int _bestSaved;

    private void SaveBest()
    {
        if (OnBest is not null && Score > BestToBeat && Score > _bestSaved && Frame % Tuning.Fps == 0) { _bestSaved = Score; OnBest(Score); }
    }

    private void UpdateMusic()
    {
        if (Music is null) return;
        // with the gates off a gunship on the field is the only boss there is
        string? boss = BossGates ? BossMusicKey : Enemies.Any(e => e.Kind == EnemyKind.Gunship && e.Y > 0 && !e.Leaving) ? Key(EnemyKind.Gunship) : null;
        Music.Update(new MusicSnapshot(boss, AheadMusicKey));
    }

    private void UpdateStage()
    {
        StepProgress();
        if (StageBannerLeft > 0) StageBannerLeft--;
        if (Stage > StageShown) { StageShown = Stage; StageBannerLeft = Tuning.StageBannerFrames; }
        UpdateKindHint();
    }

    private void DecayShake()
    {
        Shake *= Tuning.ShakeDecay;
        if (Shake < Tuning.ShakeFloor) Shake = 0;
    }

    /// <summary>The draws of a frame's hash that move the screen across and down.</summary>
    private const int ShakeXStream = 1, ShakeYStream = 2;

    /// <summary>The current screen shake as a pixel offset; zero when the shake is off.</summary>
    public (int X, int Y) ShakeOffset()
    {
        if (Shake <= 0) return (0, 0);
        ulong h = Hashing.Mix((ulong)Frame * FrameHashStep + _seed);
        return ((int)Math.Round((Hashing.Unit(h, ShakeXStream) * 2 - 1) * Shake), (int)Math.Round((Hashing.Unit(h, ShakeYStream) * 2 - 1) * Shake));
    }

    public void AddShake(double amount, int hitStop = 0)
    {
        Shake = Math.Min(Tuning.ShakeMax, Math.Max(Shake, amount) + Math.Min(Shake, amount) * ShakeCarryShare);
        if (hitStop > HitStop) HitStop = hitStop;
    }

    // ---------------------------------------------------------------- ship

    private void UpdateShip()
    {
        var inp = Input;
        if (RespawnIn > 0)
        {
            if (--RespawnIn == 0) { Hp = Tuning.ShipHp; Shield = Tuning.ShieldHits; Bombs = Tuning.ShipBombs; Invuln = Tuning.InvulnFrames; PX = PW / 2.0; PY = PH - Tuning.ShipStartAbove; }
            inp.BombPressed = false; inp.HasTap = false; inp.DragCols = inp.DragRows = 0;
            return;
        }
        if (Invuln > 0) Invuln--;
        if (MuzzleFrames > 0) MuzzleFrames--;

        double mx = (inp.Held(GameKey.Right) ? 1 : 0) - (inp.Held(GameKey.Left) ? 1 : 0);
        double my = (inp.Held(GameKey.Down) ? 1 : 0) - (inp.Held(GameKey.Up) ? 1 : 0);
        if (mx != 0 || my != 0)
        {
            double n = Math.Sqrt(mx * mx + my * my);
            PX += mx / n * Tuning.ShipSpeed; PY += my / n * Tuning.ShipSpeed;
        }
        else if (inp.DragCols != 0 || inp.DragRows != 0)
        {
            PX += inp.DragCols * TouchDragCellsToPixels * Tuning.TouchGain; PY += inp.DragRows * TouchDragCellsToPixels * Tuning.TouchGain;
        }
        else if (inp.PointerActive && inp.PointerLast)
        {
            var (tx, ty) = Layout.ToField(inp.PointerCol, inp.PointerRow);
            PX += Math.Clamp((tx - PX) * Tuning.PointerEase, -Tuning.ShipMaxChase, Tuning.ShipMaxChase);
            PY += Math.Clamp((ty - PY) * Tuning.PointerEase, -Tuning.ShipMaxChase, Tuning.ShipMaxChase);
        }
        inp.DragCols = inp.DragRows = 0;
        PX = Math.Clamp(PX, Ship.W / 2.0 + ShipEdgeGap, PW - Ship.W / 2.0 - ShipEdgeGap);
        PY = Math.Clamp(PY, ShipMinY, PH - Ship.H / 2.0 - ShipEdgeGap);

        if (inp.HasTap)
        {
            inp.HasTap = false;
            if (InBombButton(inp.TapCol, inp.TapRow)) inp.BombPressed = true;   // a tap does nothing else
        }
        if (inp.BombPressed)
        {
            inp.BombPressed = false;
            if (Bombs > 0) DetonateBomb();
        }

        if (_fireCd > 0) _fireCd--;
        if (inp.Firing && _fireCd <= 0) Fire();
    }

    private void Fire()
    {
        _fireCd = FireIntervalFor(WeaponLevel);
        MuzzleFrames = Tuning.MuzzleFrames;
        double mul = Powered ? Tuning.PowerDamageMul : 1;
        double v = Tuning.ShipShotSpeed;
        double y = PY - Ship.H / 2.0;
        Sound(SfxNames.Shot, PX);
        foreach (var s in WeaponPattern(WeaponLevel, Powered))
            Bullets.Add(new Bullet { X = PX + s.Dx, Y = y + s.Dy, VX = s.VX, VY = -v * s.VyMul, Dmg = s.Dmg * mul, Friendly = true });
    }

    private void DetonateBomb()
    {
        Bombs--;
        Sound(SfxNames.Bomb, PX);
        ScreenFlash = Tuning.ScreenFlashFrames;
        AddShake(Tuning.ShakeBomb, Tuning.HitStopBomb);
        AddRing(PX, PY, BombRingStart, Math.Max(PW, PH) * BombRingShare, Palette.Ice, BombRingFrames);
        Bullets.RemoveAll(b => !b.Friendly);
        foreach (var e in Enemies.ToArray())
        {
            if (e.Untouchable) continue;
            if (e.Parts is not null) { if (e.Y > BombReachY) BombParts(e); }
            else if (e.Y > BombReachY) Damage(e, BombDamage, e.X, e.Y, bomb: true);
        }
        Popup("BOMB", PX, PY - BombPopupRise, Palette.Ice);
    }

    // ---------------------------------------------------------------- waves

    private void UpdateWaves()
    {
        bool anyone = Enemies.Count > 0 || _pending.Count > 0;
        _emptyFor = anyone ? 0 : _emptyFor + 1;
        _sinceWave++;

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (at, sp, live, src, header) = _pending[i];
            if (at > Tick) continue;
            _pending.RemoveAt(i);
            SpawnEnemy(KeepMiniBossesInCheck(sp, live), live, src, header);
        }

        UpdateLessers();
        if (BossGates && Gate != GateState.None) return;   // a gate holds every wave back; live ones wait in the queue

        bool real = _waves.Count > 0;
        bool empty = _emptyFor >= Tuning.EmptyScreenFrames && _sinceWave >= Tuning.MinWaveGapFrames;
        if (real && (_sinceWave >= Tuning.WaveGapFrames || empty)) Launch(_waves.Dequeue());
        else if (!real && (empty || _sinceWave >= Ramp.FillerGap(Stage) && Enemies.Count < FillerMaxAlive))
            Launch(WaveSpawner.Filler(Hashing.Mix(_seed + (ulong)(++_fillerN)), Stage, _fillerN, Ramp.StageShare(Progress)));
    }

    /// <summary>A mini-boss that would take a kind past its cap (<see cref="Ramp.MiniBossCapAt"/>) arrives as another kind instead.</summary>
    private EnemySpawn KeepMiniBossesInCheck(EnemySpawn s, bool live)
    {
        if (!Ramp.IsMiniBoss(s.Kind) || Enemies.Count(e => e.Kind == s.Kind && !e.Leaving) < Ramp.MiniBossCapAt(Stage)) return s;
        return s with { Kind = WaveSpawner.Substitute(s.Kind, Stage, Ramp.StageShare(Progress), shootable: live) };
    }

    /// <summary>Enemies queued by launched waves that have not appeared yet. For tests.</summary>
    internal int PendingSpawns => _pending.Count;

    private void Launch(WaveSpec w)
    {
        _sinceWave = 0;
        WavesSpawned++;
        if (w.Live) { LiveWavesSpawned++; Sound(SfxNames.Darkness); }
        string? header = w.Live && w.Kind is { } bk ? EnemySpeechBox.Header(w.Label, bk, w.Source) : null;
        foreach (var s in w.Enemies) _pending.Add((Tick + 1 + s.Delay, s, w.Live, w.Kind, header));
    }

    /// <summary>A visitor: a lesser gunship, or one of the mini-bosses (the Warden, the Nest, the Yoke, the Chorus). It gets a sting.</summary>
    private static bool IsVisitor(Enemy e) =>
        Ramp.IsMiniBoss(e.Kind) || e.Lesser && e.Kind == EnemyKind.Gunship;

    private Enemy SpawnEnemy(EnemySpawn s, bool live, DarknessKind? src, string? header, Action<Enemy>? setup = null)
    {
        var behaviour = BehaviourFor(s.Kind);
        var sprite = behaviour.Body;
        double hp = behaviour.Hp * (behaviour.RampsHp ? Ramp.HpMul(Stage) : 1);
        double x = s.X * PW;
        var made = new Enemy
        {
            Kind = s.Kind,
            Sprite = sprite,
            X = x,
            BaseX = x,
            Y = -sprite.H,
            Hp = hp,
            MaxHp = hp,
            Amp = s.Amp,
            Phase = s.Phase,
            Text = s.Text,
            Live = live,
            Source = src,
            Header = header,
            FireCd = Tuning.EnemyFirstBoltMin + (int)(R() * Tuning.EnemyFirstBoltJitter),
            VY = behaviour.EntryFall ?? Tuning.EnemyFallMin + R() * Tuning.EnemyFallJitter,
            Grace = behaviour.Grace,
            Windup = Enemy.WindupNotBegun,
        };
        setup?.Invoke(made);
        Add(made);
        Born(made, behaviour);
        if (IsVisitor(made)) Sound(SfxNames.Sting, made.X, BigSoundSize);   // a mini-boss arrives to a sting; the music does not move
        return made;
    }

    /// <summary>Puts one enemy of a kind above the field at <paramref name="x"/> (0..1 of the width), as a filler wave would.</summary>
    public Enemy SpawnKind(EnemyKind kind, double x)
    {
        return SpawnEnemy(new EnemySpawn(kind, x, 0, 0, 0, null), false, null, null);
    }

    /// <summary>A splitter killed bursts into two shards that fan out and down. They cannot touch the ship for
    /// <see cref="Tuning.ShardGraceFrames"/>, so a shard born on top of it never hits in the frame it appears.</summary>
    private void SpawnShards(Enemy parent)
    {
        for (int side = -1; side <= 1; side += 2)
            Enemies.Add(new Enemy
            {
                Kind = EnemyKind.Shard,
                Sprite = Art.Shard,
                X = parent.X + side * ShardSpawnOffset,
                BaseX = parent.X + side * ShardSpawnOffset,
                Y = parent.Y,
                Hp = Tuning.ShardHp,
                MaxHp = Tuning.ShardHp,
                Live = parent.Live,
                Source = parent.Source,
                Header = parent.Header,
                VX = side * Tuning.ShardSpeedX,
                VY = Tuning.ShardSpeedY,
                Grace = Tuning.ShardGraceFrames,
                Windup = Enemy.WindupNotBegun,
            });
    }

    private void UpdateEnemies()
    {
        _walking = true;
        foreach (var enemy in Enemies)
        {
            enemy.Age++;
            enemy.ModeAge++;
            EnemySpeechBox.Update(enemy, Layout.PCols, Layout.Rows, enemy.SpeechBoxHeld || enemy.Voice is not null && BehaviourFor(enemy.Kind).HoldBox(this, enemy));
            if (enemy.Flash > 0) enemy.Flash--;
            if (enemy.Parts is not null) foreach (var part in enemy.Parts) if (part.Flash > 0) part.Flash--;
            enemy.BaseX += enemy.KX; enemy.Y += enemy.KY;
            enemy.KX *= Tuning.KnockDecay; enemy.KY *= Tuning.KnockDecay;
            BehaviourFor(enemy.Kind).Update(this, enemy);
            if (enemy.EvadeWind > 0) enemy.X += enemy.Age % ShiverPeriod < ShiverPeriod / 2 ? -EvadeShiver : EvadeShiver;   // the tell: a shiver, then the slide
            if (enemy.Parts is not null) SyncHp(enemy);
        }
        EndWalk();
        for (int i = Enemies.Count - 1; i >= 0; i--)
        {
            var enemy = Enemies[i];
            if (enemy.Parts is not null ? OffField(enemy) : enemy.Y > PH + OffFieldMargin || enemy.Leaving && enemy.Y < -enemy.Sprite.H || enemy.X < -enemy.Sprite.W - OffSideMargin || enemy.X > PW + enemy.Sprite.W + OffSideMargin) { EnemySpeechBox.Bury(this, enemy); Enemies.RemoveAt(i); }
        }
        for (int i = Echoes.Count - 1; i >= 0; i--)
            if (!Echoes[i].SpeechBoxHeld && ++Echoes[i].SpeechBoxAge >= EnemySpeechBox.Frames(Echoes[i])) Echoes.RemoveAt(i);
    }

    /// <summary>Sidestep a ship shot that is lined up on this enemy. The enemy must be on screen, off cooldown and not already moving;
    /// it shivers for <see cref="Tuning.EvadeWindupFrames"/> (the tell), then slides away from the shot, so a shot already close still lands.
    /// Cheap: one pass over the live bullets, and only for an enemy that is idle.</summary>
    private void Evade(Enemy e)
    {
        if (e.EvadeCd > 0) e.EvadeCd--;
        if (e.EvadeSlide > 0)
        {
            e.EvadeSlide--;
            e.BaseX = Math.Clamp(e.BaseX + e.EvadeDir * Tuning.EvadeSpeed, e.Sprite.W / 2.0, PW - e.Sprite.W / 2.0);
            return;
        }
        if (e.EvadeWind > 0)
        {
            if (--e.EvadeWind == 0) e.EvadeSlide = Tuning.EvadeSlideFrames;
            return;
        }
        if (e.EvadeCd > 0 || e.Y < 0 || e.Hp <= 0) return;
        double lane = e.Sprite.W / 2.0 + Tuning.EvadeLane, bottom = e.Y + e.Sprite.H / 2.0;
        foreach (var b in Bullets)
        {
            if (!b.Friendly || b.VY >= 0) continue;
            double dy = b.Y - bottom;
            if (dy < -e.Sprite.H || dy > Tuning.EvadeSenseY) continue;
            double bx = b.X + b.VX * Math.Max(0, (b.Y - e.Y) / -b.VY);   // where it will cross the enemy's row
            double off = e.X - bx;
            if (Math.Abs(off) > lane) continue;
            int dir = Math.Abs(off) < EvadeCentreBand ? ((e.Age / EvadeFlipPeriod + (int)(e.Phase * EvadePhaseSpread)) % 2 == 0 ? 1 : -1) : Math.Sign(off);
            double room = dir > 0 ? PW - e.Sprite.W / 2.0 - e.BaseX : e.BaseX - e.Sprite.W / 2.0;
            e.EvadeDir = room < Tuning.EvadeSpeed * Tuning.EvadeSlideFrames * EvadeRoomShare ? -dir : dir;   // never into a wall
            e.EvadeWind = Tuning.EvadeWindupFrames;
            e.EvadeCd = Tuning.EvadeWindupFrames + Tuning.EvadeSlideFrames + Ramp.EvadeCooldown(Stage);
            return;
        }
    }

    /// <summary>The spot a wind-up commits to: where the ship is on this frame (the middle of the bottom while it is down).</summary>
    private void LockAim(Enemy e)
    {
        if (Alive) { e.AimX = PX; e.AimY = PY; }
        else { e.AimX = PW / 2.0; e.AimY = PH - Tuning.ShipStartAbove; }
    }

    private void EnemyBolt(double x, double y, double tx, double ty, double spread, double speed)
    {
        double a = Math.Atan2(ty - y, tx - x) + spread;
        Bullets.Add(new Bullet { X = x, Y = y, VX = Math.Cos(a) * speed, VY = Math.Sin(a) * speed });
    }

    private bool InBombButton(double col, double row)
    {
        var b = BombButton;
        return col >= b.C && col < b.C + b.W && row >= b.R && row < b.R + b.H;
    }

    // ---------------------------------------------------------------- bullets, collisions

    private void UpdateBullets()
    {
        foreach (var b in Bullets) { b.X += b.VX; b.Y += b.VY; }
        Bullets.RemoveAll(b => b.Y < -BulletMargin || b.Y > PH + BulletMargin || b.X < -BulletMargin || b.X > PW + BulletMargin);
    }

    private void Collide()
    {
        foreach (var b in Bullets.ToArray())
        {
            if (b.Friendly)
            {
                foreach (var e in Enemies)
                {
                    if (e.Parts is not null)
                    {
                        if (e.Hp > 0 && !e.Untouchable && b.Y >= 0 && HitParts(e, b)) break;
                        continue;
                    }
                    if (e.Y < EnemyTopHitRow || e.Hp <= 0 || e.Untouchable) continue;
                    if (Math.Abs(b.X - e.X) <= e.Sprite.W / 2.0 && Math.Abs(b.Y - e.Y) <= e.Sprite.H / 2.0)
                    {
                        Bullets.Remove(b);
                        if (e.ShieldedBy is not null) Shrug(b.X, b.Y);
                        else Damage(e, b.Dmg, b.X, b.Y);
                        break;
                    }
                }
            }
            else if (Alive && Invuln == 0 && Math.Abs(b.X - PX) <= BoltHitHalfWidth && Math.Abs(b.Y - PY) <= BoltHitHalfHeight)
            {
                Bullets.Remove(b);
                ShipHit();
            }
        }
        if (Alive && Invuln == 0)
            foreach (var e in Enemies)
                if (e.Hp > 0 && !e.Untouchable && e.Armed && TouchesShip(e))
                {
                    ShipHit();
                    if (e.Parts is null && BodyTouchesShip(e))   // a part or a beam hurts the ship and is not hurt by it
                    {
                        if (e.Kind == EnemyKind.Mine) Detonate(e);
                        else Damage(e, e.Kind == EnemyKind.Diver ? e.Hp : RamDamage, PX, PY - RamHitOffsetY, ram: true);   // a diver is spent on the ship
                    }
                    break;
                }
        foreach (var e in Enemies) e.ShieldedBy = null;   // a warden holds its charge only the frame it shields it
    }

    /// <summary>A mine a bullet pings off flashes for a single frame, shorter than the flash of a hit.</summary>
    private const int MinePingFlashFrames = 1;

    private void Damage(Enemy e, double dmg, double hx, double hy, bool bomb = false, bool ram = false)
    {
        if (e.Hp <= 0) return;
        if (e.Kind == EnemyKind.Mine && !bomb)
        {
            // armour: a bullet pings off with a spark and no shove, so firing can neither kill a mine nor hold it back
            e.Flash = MinePingFlashFrames;
            PingSpark(hx, hy);
            return;
        }
        bool armoured = e.Kind == EnemyKind.Diver && e.Dashing && !bomb && !ram;
        if (armoured)
        {
            // the dash plates: shots chip it for a share of their damage, ping off in sparks and cannot shove it off its line
            dmg *= Tuning.DiverDashArmour;
            PingSpark(hx, hy);
        }
        e.Hp -= dmg;
        e.Flash = Tuning.FlashFrames;
        e.Hits++;
        bool heavy = e.Kind == EnemyKind.Gunship;
        double k = heavy ? Tuning.KnockHeavy : Tuning.KnockHit;
        if (!armoured) e.KY -= k;
        HitSparks(hx, hy);
        if (e.Hp > 0)
        {
            if (!bomb) { Sound(SfxNames.Hit, hx); AddShake(Tuning.ShakeHit, heavy && e.Hits % HeavyHitStopEvery == 0 ? Tuning.HitStopHeavyHit : 0); }
            return;
        }
        Kill(e, bomb);
    }

    /// <summary>A mine that met the ship goes off and scores nothing.</summary>
    private void Detonate(Enemy e)
    {
        Enemies.Remove(e);
        Sound(SfxNames.Kill, e.X, MidSoundSize);
        AddRing(e.X, e.Y, KillRingStart, Tuning.RingRadiusSmall + MineRingExtra, Art.MineLit, Tuning.RingFrames);
        for (int i = 0; i < Tuning.DebrisSmallKill; i++)
        {
            double a = R() * Math.Tau, sp = MineDebrisMinSpeed + R() * MineDebrisSpeedSpan;
            AddParticle(new Particle { X = e.X, Y = e.Y, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp, Life = MineDebrisMinLife + (int)(R() * MineDebrisLifeSpan), Colour = R() < CoinFlip ? Art.MineLit : Palette.White, Kind = ParticleKind.Debris });
        }
    }

    private void Kill(Enemy e, bool bomb)
    {
        Cue(e, VoiceCue.Dying);
        Enemies.Remove(e);
        EnemySpeechBox.Bury(this, e);
        var behaviour = BehaviourFor(e.Kind);
        behaviour.Down(this, e);
        Kills++;
        bool cleared = e.Live && !Enemies.Any(x => x.Live) && !_pending.Any(p => p.Live);
        if (cleared) { LiveWavesCleared++; Sound(SfxNames.Clear); }
        MaybeDrop(e, cleared);
        bool big = behaviour.Boss;
        double size = big ? BigSoundSize : e.Kind is EnemyKind.Carrier or EnemyKind.Splitter or EnemyKind.Turret ? MidSoundSize : 0;
        if (big) Sound(SfxNames.Boom, e.X, size);
        else if (!bomb) Sound(SfxNames.Kill, e.X, size);   // a bomb has its own sound
        int pts = behaviour.ScoreFor(e);
        Score += pts;
        Progress += Tuning.StageKillFrames;
        if (e.Kind == EnemyKind.Splitter && !bomb) SpawnShards(e);
        int debris = big ? Tuning.DebrisBigKill : Tuning.DebrisSmallKill;
        for (int i = 0; i < debris; i++)
        {
            double a = R() * Math.Tau, sp = (big ? BigDebrisMinSpeed : DebrisMinSpeed) + R() * (big ? BigDebrisSpeedSpan : DebrisSpeedSpan);
            uint c = DrawDebrisColour(e.Kind);
            AddParticle(new Particle { X = e.X + (R() - 0.5) * e.Sprite.W * DebrisScatterShare, Y = e.Y + (R() - 0.5) * e.Sprite.H * DebrisScatterShare, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp - DebrisLift, Life = DebrisMinLife + (int)(R() * DebrisLifeSpan), Colour = c, Size = DrawDebrisSize(), Kind = ParticleKind.Debris });
        }
        AddRing(e.X, e.Y, KillRingStart, big ? Tuning.RingRadiusBig : Tuning.RingRadiusSmall, big ? Palette.Gold : SmallKillRingColour, Tuning.RingFrames);
        if (big) AddRing(e.X, e.Y, BigKillInnerRingStart, Tuning.RingRadiusBig * BigKillInnerRingShare, Palette.White, Tuning.RingFrames - BigKillInnerRingFewerFrames);
        AddShake(big ? Tuning.ShakeBigKill : Tuning.ShakeSmallKill, big ? Tuning.HitStopBigKill : e.Kind is EnemyKind.Carrier or EnemyKind.Turret ? Tuning.HitStopCarrierKill : Tuning.HitStopSmallKill);
        Popup("+" + pts, e.X, e.Y - ScorePopupRise, big ? Palette.Gold : Palette.White);
        GateAfterKill(e);
    }

    /// <summary>A debris colour drawn from the main stream: the enemy's own colour half the time, else crimson or plum.</summary>
    private uint DrawDebrisColour(EnemyKind kind) => R() < CoinFlip ? TableBehaviour(kind).Colour : R() < CoinFlip ? Palette.Crimson : Palette.Plum;

    /// <summary>A debris size in pixels drawn from the main stream: the big one <see cref="DebrisBigChance"/> of the time.</summary>
    private int DrawDebrisSize() => R() < DebrisBigChance ? DebrisBigSize : DebrisSmallSize;

    private const int HurtRingStart = 2, HurtRingRadius = 12, HurtRingFrames = 12;

    private void HurtRing() => AddRing(PX, PY, HurtRingStart, HurtRingRadius, Palette.Crimson, HurtRingFrames);

    private void ShipHit()
    {
        if (AbsorbWithShield() || Shrug()) return;
        InvulnIsShield = false;
        Hp--;
        Sound(Hp <= 0 ? SfxNames.Death : SfxNames.Hurt, PX);
        Invuln = Tuning.InvulnFrames;
        AddShake(Tuning.ShakeShipHit, Tuning.HitStopShipHit);
        ScreenFlash = ShipHitFlashFrames;
        HurtRing();
        for (int i = 0; i < ShipHitSparks; i++)
            AddParticle(new Particle { X = PX, Y = PY, VX = (R() - 0.5) * SparkSpeed, VY = (R() - 0.5) * SparkSpeed, Life = SparkLifeMin + (int)(R() * SparkLifeSpan), Colour = R() < CoinFlip ? Palette.White : Palette.Crimson, Kind = ParticleKind.Spark });
        if (Hp <= 0)
        {
            Deaths++;
            if (Floor() is { } speaker) Cue(speaker, VoiceCue.ShipDied);
            GateDeath();
            if (Score > BestToBeat && Score > _bestSaved) { _bestSaved = Score; OnBest?.Invoke(Score); }
            RespawnIn = Tuning.RespawnFrames;
            Score = Math.Max(0, Score - Score / DeathScoreDivisor);
            AddShake(Tuning.ShakeBigKill, Tuning.HitStopBigKill);
            AddRing(PX, PY, KillRingStart, Tuning.RingRadiusBig, Palette.Gold, Tuning.RingFrames);
            for (int i = 0; i < Tuning.DebrisBigKill; i++)
            {
                double a = R() * Math.Tau, sp = DeathDebrisMinSpeed + R() * DeathDebrisSpeedSpan;
                AddParticle(new Particle { X = PX, Y = PY, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp, Life = DeathDebrisMinLife + (int)(R() * DeathDebrisLifeSpan), Colour = R() < CoinFlip ? Palette.Gold : Palette.Ice, Size = DrawDebrisSize(), Kind = ParticleKind.Debris });
            }
            Bullets.RemoveAll(b => !b.Friendly);
            _powerUntil = NoPower;
            LoseWeaponLevel();
        }
    }

    // ---------------------------------------------------------------- effects

    private void UpdateEffects()
    {
        foreach (var p in Particles)
        {
            p.X += p.VX; p.Y += p.VY;
            if (p.Kind == ParticleKind.Debris) p.VY += Tuning.DebrisGravity;
            else if (p.Kind == ParticleKind.Heart) { p.VX *= HeartDrag; p.VY = p.VY * HeartDrag - HeartRise; }
            else p.VX *= SparkDrag;
            p.Life--;
        }
        Particles.RemoveAll(p => p.Life <= 0);
        foreach (var r in Rings) r.Life--;
        Rings.RemoveAll(r => r.Life <= 0);
        foreach (var p in Popups) { p.Life--; p.Y -= PopupRise; }
        Popups.RemoveAll(p => p.Life <= 0);
    }

    private void AddParticle(Particle p)
    {
        if (Particles.Count < Tuning.MaxParticles) { p.MaxLife = p.Life; Particles.Add(p); }
    }

    private void AddRing(double x, double y, double r0, double r1, uint colour, int frames) =>
        Rings.Add(new Ring { X = x, Y = y, R0 = r0, R1 = r1, Life = frames, Max = frames, Colour = colour });

    private void Popup(string text, double x, double y, uint colour) =>
        Popups.Add(new Popup { Text = text, X = x, Y = y, Life = Tuning.PopupFrames, Colour = colour });

    // ---------------------------------------------------------------- read-outs for the HUD

    /// <summary>"◉ LIVE spam ×7" while a wave from a real darkness event is on screen, "○ patrol" for filler.</summary>
    public string WaveLabel()
    {
        int live = 0;
        foreach (var e in Enemies)
            if (e.Live) live++;
        if (BossUp) return "◉ BOSS";
        if (live > 0) return $"◉ LIVE spam ×{live}";
        return Enemies.Count > 0 ? "○ patrol" : "○ clear";
    }
}
