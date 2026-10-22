namespace FeedRaider.Engine.Raider;

public enum ParticleKind { Spark, Debris, Heart, Smoke }

public sealed class Bullet
{
    public double X, Y, VX, VY, Dmg;
    public bool Friendly, Wing;
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
    /// <summary>The badge drawn above a darkness enemy (" SPAM "), built once at spawn; null for filler.</summary>
    public string? Badge;
    public bool Leaving;
    public double VY = 0.55;
}

public sealed class Particle
{
    public double X, Y, VX, VY;
    public int Life, MaxLife;
    public uint Color;
    public int Size = 1;
    public ParticleKind Kind;
}

public sealed class Ring { public double X, Y, R0, R1; public int Life, Max; public uint Color; }
public sealed class Popup { public string Text = ""; public double X, Y; public int Life; public uint Color; }

/// <summary>
/// Feed Raider's simulation: a fixed 30 Hz step, no clock and no network. Time is the number of calls to <see cref="Step"/>;
/// randomness is a seeded generator, so a run is a pure function of the seed and the inputs. Hosts feed it input
/// (<see cref="Input"/>), darkness events (<see cref="OfferDark"/>) and feed posts (<see cref="OfferPost"/>), and read its
/// state to draw. The only outward effect is <see cref="OnLike"/>, called at one place: <see cref="TryLike"/>.
/// </summary>
public sealed partial class RaiderGame
{
    public RaiderGame(ulong seed = 1)
    {
        _rs = Hashing.Mix(seed) | 1;
        _seed = seed;
        SetLayout(FieldLayout.Compute(100, 46));
    }

    private readonly ulong _seed;
    private ulong _rs;
    private void Sound(string name, double x = double.NaN, double size = 0) =>
        Sfx?.Play(name, double.IsNaN(x) || PW <= 0 ? 0 : x / PW * 2 - 1, Frame, size);
    private double R() { _rs ^= _rs >> 12; _rs ^= _rs << 25; _rs ^= _rs >> 27; return ((_rs * 0x2545F4914F6CDD1DUL) >> 11) / (double)(1UL << 53); }

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
    public bool LoggedIn { get; set; }
    /// <summary>The one outward effect of the game: the player pressed like on this wingman's post.</summary>
    public Action<FeedPost>? OnLike { get; set; }
    /// <summary>Asks the host for the author's avatar; answer with <see cref="SetAvatar"/>.</summary>
    public Action<FeedPost>? AvatarWanted { get; set; }
    /// <summary>Where the music cues go; null is silence. Fed once per step, so it only fires when the musical moment changes.</summary>
    public MusicDirector? Music { get; set; }
    /// <summary>Where the sound effects go; null is silence. Called when something happens, never per frame.</summary>
    public SfxBus? Sfx { get; set; }
    /// <summary>The best score from earlier runs (0 = none). Passing it cues the record music.</summary>
    public int RecordToBeat { get; set; }
    /// <summary>Called with the run's score when it is above <see cref="RecordToBeat"/> and the player dies or a second passes.</summary>
    public Action<int>? OnBest { get; set; }

    // --- player ---
    public double PX { get; private set; }
    public double PY { get; private set; }
    public int Hp { get; private set; } = Tuning.PlayerHp;
    public int Bombs { get; private set; } = Tuning.PlayerBombs;
    public int Invuln { get; private set; }
    public int RespawnIn { get; private set; }
    public bool Alive => RespawnIn <= 0;
    public int Score { get; private set; }
    public int Deaths { get; private set; }
    public int Kills { get; private set; }
    public int MuzzleFrames { get; private set; }
    public string PlayerDid { get; set; } = "did:plc:player";
    public Sprite Plane { get; private set; } = PlaneFactory.Make("did:plc:player");
    private int _fireCd;
    private long _powerUntil = -1;
    public int PowerLeft => (int)Math.Max(0, _powerUntil - Tick);
    public bool Powered => PowerLeft > 0;

    public void SetPlayer(string did)
    {
        PlayerDid = did;
        Plane = PlaneFactory.Make(did);
    }

    // --- world ---
    public readonly List<Enemy> Enemies = [];
    public readonly List<Bullet> Bullets = [];
    public readonly List<Particle> Particles = [];
    public readonly List<Ring> Rings = [];
    public readonly List<Popup> Popups = [];
    public readonly List<Wingman> Wings = [];
    public WingQueue Queue { get; } = new();
    public WingComms Comms { get; } = new();
    private readonly Queue<WaveSpec> _waves = new();
    private readonly List<(long At, EnemySpawn Spawn, bool Live, DarknessKind? Src, string? Badge)> _pending = [];
    private readonly HashSet<string> _liked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint[]> _avatars = new(StringComparer.Ordinal);
    private int _sinceWave, _emptyFor, _fillerN;
    private long _lastWingTick = -Tuning.WingStaggerFrames;

    public int WavesQueued => _waves.Count;
    public int WavesSpawned { get; private set; }
    public int LiveWavesSpawned { get; private set; }
    /// <summary>Waves from a real darkness event whose last enemy was shot down.</summary>
    public int LiveWavesCleared { get; private set; }

    // --- impact state ---
    public int HitStop { get; private set; }
    public double Shake { get; private set; }
    public int ScreenFlash { get; private set; }
    public string? Banner { get; private set; }
    public int BannerLeft { get; private set; }
    public string? Toast { get; private set; }
    public int ToastLeft { get; private set; }
    public Wingman? Prompt { get; private set; }

    // ---------------------------------------------------------------- hosts

    public void SetLayout(FieldLayout lay)
    {
        bool first = Layout.PCols == 0;
        Layout = lay;
        if (first) { PX = PW / 2.0; PY = PH - 14; }
        PX = Math.Clamp(PX, 6, Math.Max(6, PW - 6));
        PY = Math.Clamp(PY, 10, Math.Max(10, PH - 8));
    }

    /// <summary>The terminal cells of the on-screen bomb button (for touch): column, row, width, height.</summary>
    public (int C, int R, int W, int H) BombButton => (Layout.Col0 + Layout.PCols - 11, Layout.Rows - 3, 10, 2);

    /// <summary>A darkness event becomes a wave.</summary>
    public void OfferDark(DarkPost post)
    {
        if (_waves.Count >= Tuning.MaxPendingWaves) _waves.Dequeue();
        _waves.Enqueue(WaveSpawner.FromPost(post));
    }

    public bool OfferPost(FeedPost post) => Queue.Offer(post);

    public void SetAvatar(string did, uint[] px)
    {
        if (px.Length != 64) return;
        _avatars[did] = px;
        foreach (var w in Wings) if (w.Post.AuthorDid == did) w.Avatar = px;
    }

    /// <summary>What a signed-out like press says.</summary>
    public const string SignInHint = "sign in to like";
    public void Say(string text) { Toast = text; ToastLeft = Tuning.Fps * 2; }

    /// <summary>The host's like call failed: allow another try on that post, and say why.</summary>
    public void LikeFailed(string uri, string? reason = null)
    {
        _liked.Remove(uri);
        double x = double.NaN;
        foreach (var w in Wings) if (w.Post.Uri == uri) { w.Liked = false; x = w.X; }
        Say(string.IsNullOrEmpty(reason) ? "like failed" : "like failed: " + reason);
        Sound(SfxNames.NoLike, x);
    }

    /// <summary>The like record was written: a heart pops over the wingman and the toast names the author.</summary>
    public void LikeConfirmed(FeedPost post)
    {
        double x = PX, y = PY - 8;
        foreach (var w in Wings) if (w.Post.Uri == post.Uri) { x = w.X; y = w.Y - w.Plane.H / 2.0 - 2; }
        Say($"liked @{BubbleText.Sanitize(post.AuthorHandle)}");
        Popup("\u2665 liked", x, y, 0xff8fb1);   // \u2665 heart suit
        AddRing(x, y + 2, 2, 12, 0xff5a8a, 12);
    }

    // ---------------------------------------------------------------- the step

    public void Step()
    {
        Frame++;
        UpdateHold();
        DecayShake();
        if (ToastLeft > 0 && --ToastLeft == 0) Toast = null;
        if (ScreenFlash > 0) ScreenFlash--;
        if (HitStop > 0)
        {
            HitStop--;
            Input.EndFrame();
            UpdateMusic();
            return;
        }
        Tick++;
        ScrollFrames++;
        if (BannerLeft > 0 && --BannerLeft == 0) Banner = null;

        UpdatePlayer();
        UpdateWaves();
        UpdateEnemies();
        UpdateWings();
        var speaker = Comms.Speaker;
        Comms.Update(Wings);
        if (Comms.Speaker is { } opened && !ReferenceEquals(opened, speaker)) Sound(SfxNames.Comm, opened.X);
        UpdateBullets();
        Collide();
        UpdatePickups();
        UpdateEffects();
        UpdatePrompt();
        HandleLike();
        Input.EndFrame();
        UpdateMusic();
    }

    private int _bestSaved;

    private void UpdateMusic()
    {
        if (OnBest is not null && Score > RecordToBeat && Score > _bestSaved && Frame % Tuning.Fps == 0) { _bestSaved = Score; OnBest(Score); }
        Music?.Update(new MusicSnapshot(Deaths, Enemies.Any(e => e.Kind == EnemyKind.Gunship && e.Y > 0 && !e.Leaving),
            Hp, Wings.Count, LiveWavesCleared, Score, RecordToBeat), Frame / (double)Tuning.Fps);
    }

    private void DecayShake()
    {
        Shake *= Tuning.ShakeDecay;
        if (Shake < Tuning.ShakeFloor) Shake = 0;
    }

    /// <summary>The current screen shake as a pixel offset; zero when the shake is off.</summary>
    public (int X, int Y) ShakeOffset()
    {
        if (Shake <= 0) return (0, 0);
        ulong h = Hashing.Mix((ulong)Frame * 0x9E3779B97F4A7C15UL + _seed);
        return ((int)Math.Round((Hashing.Unit(h, 1) * 2 - 1) * Shake), (int)Math.Round((Hashing.Unit(h, 2) * 2 - 1) * Shake));
    }

    public void AddShake(double amount, int hitStop = 0)
    {
        Shake = Math.Min(Tuning.ShakeMax, Math.Max(Shake, amount) + Math.Min(Shake, amount) * 0.3);
        if (hitStop > HitStop) HitStop = hitStop;
    }

    // ---------------------------------------------------------------- player

    private void UpdatePlayer()
    {
        var inp = Input;
        if (RespawnIn > 0)
        {
            if (--RespawnIn == 0) { Hp = Tuning.PlayerHp; Bombs = Tuning.PlayerBombs; Invuln = Tuning.InvulnFrames; PX = PW / 2.0; PY = PH - 14; }
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
            PX += mx / n * Tuning.PlayerSpeed; PY += my / n * Tuning.PlayerSpeed;
        }
        else if (inp.DragCols != 0 || inp.DragRows != 0)
        {
            PX += inp.DragCols * 2 * Tuning.TouchGain; PY += inp.DragRows * 2 * Tuning.TouchGain;
        }
        else if (inp.PointerActive && inp.PointerLast)
        {
            var (tx, ty) = Layout.ToField(inp.PointerCol, inp.PointerRow);
            PX += Math.Clamp((tx - PX) * Tuning.PointerEase, -Tuning.PlayerMaxChase, Tuning.PlayerMaxChase);
            PY += Math.Clamp((ty - PY) * Tuning.PointerEase, -Tuning.PlayerMaxChase, Tuning.PlayerMaxChase);
        }
        inp.DragCols = inp.DragRows = 0;
        PX = Math.Clamp(PX, Plane.W / 2.0 + 1, PW - Plane.W / 2.0 - 1);
        PY = Math.Clamp(PY, 10, PH - Plane.H / 2.0 - 1);

        if (inp.HasTap)
        {
            inp.HasTap = false;
            if (InBombButton(inp.TapCol, inp.TapRow)) inp.BombPressed = true;   // a tap never likes; liking takes a hold or E
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
        double v = Tuning.PlayerBulletSpeed;
        double y = PY - Plane.H / 2.0;
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
        AddRing(PX, PY, 4, Math.Max(PW, PH) * 0.8, 0xbfe9ff, 20);
        Bullets.RemoveAll(b => !b.Friendly);
        foreach (var e in Enemies.ToArray())
            if (e.Y > -4) Damage(e, 30, e.X, e.Y, bomb: true);
        Popup("BOMB", PX, PY - 8, 0xbfe9ff);
    }

    // ---------------------------------------------------------------- waves

    private void UpdateWaves()
    {
        bool anyone = Enemies.Count > 0 || _pending.Count > 0;
        _emptyFor = anyone ? 0 : _emptyFor + 1;
        _sinceWave++;

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (at, sp, live, src, badge) = _pending[i];
            if (at > Tick) continue;
            _pending.RemoveAt(i);
            SpawnEnemy(sp, live, src, badge);
        }

        bool gunshipUp = Enemies.Any(e => e.Kind == EnemyKind.Gunship) || _pending.Any(p => p.Spawn.Kind == EnemyKind.Gunship);
        bool real = _waves.Count > 0 && !(gunshipUp && _waves.Peek().Kind == DarknessKind.Rude);
        bool empty = _emptyFor >= Tuning.EmptyScreenFrames && _sinceWave >= 24;
        if (real && (_sinceWave >= Tuning.WaveGapFrames || empty)) Launch(_waves.Dequeue());
        else if (!real && _waves.Count == 0 && (empty || _sinceWave >= Tuning.FillerGapFrames && Enemies.Count < 3))
            Launch(WaveSpawner.Filler(Hashing.Mix(_seed + (ulong)(++_fillerN))));
    }

    private void Launch(WaveSpec w)
    {
        _sinceWave = 0;
        WavesSpawned++;
        if (w.Live) { LiveWavesSpawned++; Sound(SfxNames.Darkness); }
        string? badge = w.Live && w.Kind is { } bk ? EnemyBadge.Text(w.Label, bk) : null;
        foreach (var s in w.Enemies) _pending.Add((Tick + 1 + s.Delay, s, w.Live, w.Kind, badge));
    }

    private void SpawnEnemy(EnemySpawn s, bool live, DarknessKind? src, string? badge)
    {
        var sprite = s.Kind switch { EnemyKind.Gunship => Art.Gunship, EnemyKind.Carrier => Art.Carrier, _ => Art.Grunt };
        double hp = s.Kind switch { EnemyKind.Gunship => Tuning.GunshipHp, EnemyKind.Carrier => Tuning.CarrierHp, _ => Tuning.GruntHp };
        double x = s.X * PW;
        Enemies.Add(new Enemy
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
            Badge = badge,
            FireCd = 50 + (int)(R() * 90),
            VY = s.Kind == EnemyKind.Gunship ? 0.5 : 0.5 + R() * 0.15,
        });
    }

    private void UpdateEnemies()
    {
        foreach (var e in Enemies)
        {
            e.Age++;
            if (e.Flash > 0) e.Flash--;
            e.BaseX += e.KX; e.Y += e.KY;
            e.KX *= Tuning.KnockDecay; e.KY *= Tuning.KnockDecay;
            if (e.Kind == EnemyKind.Gunship)
            {
                if (!e.Leaving && e.Age > 640) e.Leaving = true;
                if (e.Leaving) e.Y -= 0.8;
                else if (e.Y < 22) e.Y += e.VY;
                e.X = e.BaseX + Math.Sin(e.Age * 0.035 + e.Phase) * e.Amp * PW;
                e.X = Math.Clamp(e.X, e.Sprite.W / 2.0, PW - e.Sprite.W / 2.0);
                if (!e.Leaving && e.Y >= 16 && --e.FireCd <= 0) GunshipVolley(e);
            }
            else
            {
                e.Y += e.VY;
                e.X = e.BaseX + (e.Amp > 0 ? Math.Sin(e.Age * 0.06 + e.Phase) * e.Amp * PW * 0.5 : Math.Sin(e.Age * 0.05 + e.Phase) * 1.5);
                if (e.Y > 6 && e.Y < PH * 0.6 && --e.FireCd <= 0)
                {
                    e.FireCd = (e.Live ? 90 : 140) + (int)(R() * 80);
                    if (Alive) EnemyShot(e.X, e.Y + 2, PX, PY, 0, Tuning.EnemyBulletSpeed);
                }
            }
        }
        Enemies.RemoveAll(e => e.Y > PH + 10 || e.Leaving && e.Y < -e.Sprite.H);
    }

    private void GunshipVolley(Enemy e)
    {
        e.FireCd = 38;
        e.Hits += 0;
        double y = e.Y + e.Sprite.H / 2.0;
        if ((e.Age / 38) % 2 == 0)
            for (int i = -1; i <= 1; i++) EnemyShot(e.X, y, PX, PY, i * 0.28, Tuning.EnemyBulletSpeed * 1.1);
        else
            for (int i = -2; i <= 2; i++) Bullets.Add(new Bullet { X = e.X + i * 4, Y = y, VX = i * 0.22, VY = Tuning.EnemyBulletSpeed });
    }

    private void EnemyShot(double x, double y, double tx, double ty, double spread, double speed)
    {
        double a = Math.Atan2(ty - y, tx - x) + spread;
        Bullets.Add(new Bullet { X = x, Y = y, VX = Math.Cos(a) * speed, VY = Math.Sin(a) * speed });
    }

    // ---------------------------------------------------------------- wingmen

    private void UpdateWings()
    {
        if (Wings.Count < Tuning.MaxWingmen && Tick - _lastWingTick >= Tuning.WingStaggerFrames && Queue.TryDequeue(out var post))
            SpawnWing(post);

        foreach (var w in Wings)
        {
            w.Age++;
            if (w.MuzzleFrames > 0) w.MuzzleFrames--;
            int enter = Tuning.WingEnterFrames, fight = Tuning.WingFightFrames;
            w.Phase = w.Age < enter ? WingPhase.Enter : w.Age < enter + fight ? WingPhase.Fight : WingPhase.Leave;
            switch (w.Phase)
            {
                case WingPhase.Enter:
                    {
                        double t = w.Age / (double)enter, e = 1 - (1 - t) * (1 - t);
                        w.X = w.HomeX; w.Y = PH + 8 + (w.HomeY - PH - 8) * e;
                        break;
                    }
                case WingPhase.Fight:
                    {
                        double t = w.Age - enter;
                        w.X = w.HomeX + Math.Sin(t * 0.045 + w.Slot * 2) * 9;
                        w.Y = w.HomeY + Math.Sin(t * 0.07 + w.Slot) * 4;
                        if (--w.FireCd <= 0)
                        {
                            w.FireCd = 8; w.MuzzleFrames = Tuning.MuzzleFrames;
                            Bullets.Add(new Bullet { X = w.X, Y = w.Y - w.Plane.H / 2.0, VY = -Tuning.PlayerBulletSpeed, Dmg = 0.9 * w.Firepower, Friendly = true, Wing = true });
                        }
                        break;
                    }
                default:
                    {
                        double t = w.Age - enter - fight;
                        w.Y -= 0.4 + t * 0.12;
                        break;
                    }
            }
        }
        Wings.RemoveAll(w => w.Age >= Tuning.WingEnterFrames + Tuning.WingFightFrames + Tuning.WingLeaveFrames);
    }

    private void SpawnWing(FeedPost post)
    {
        _lastWingTick = Tick;
        int slot = Wings.Any(w => w.Slot == 0) ? 1 : 0;
        var plane = PlaneFactory.Make(post.AuthorDid, wingman: true);
        var w = new Wingman
        {
            Post = post,
            Plane = plane,
            Slot = slot,
            HomeX = PW * (slot == 0 ? 0.27 : 0.73),
            HomeY = PH * 0.4,
            Text = BubbleText.Sanitize(post.Text),
            Handle = BubbleText.Sanitize(post.AuthorHandle),
            Firepower = 1 + 0.5 * Math.Log10(1 + Math.Max(0, post.LikeCount)),
            X = PW * (slot == 0 ? 0.27 : 0.73),
            Y = PH + 8,
            FireCd = 10,
            Liked = post.AlreadyLiked,   // already in the player's likes: never offered again, never liked twice
        };
        if (post.AlreadyLiked) _liked.Add(post.Uri);
        if (_avatars.TryGetValue(post.AuthorDid, out var av)) w.Avatar = av; else AvatarWanted?.Invoke(post);
        Comms.Arrive(w);
        Wings.Add(w);
        if (post.LikeCount >= Tuning.BombLikeThreshold) ScreenClearingBomb(w);
    }

    private void ScreenClearingBomb(Wingman w)
    {
        ScreenFlash = Tuning.ScreenFlashFrames;
        Sound(SfxNames.Bomb, PW / 2.0);
        AddShake(Tuning.ShakeBomb, Tuning.HitStopBomb);
        AddRing(PW / 2.0, PH / 2.0, 6, Math.Max(PW, PH), 0xffffff, 22);
        Bullets.RemoveAll(b => !b.Friendly);
        foreach (var e in Enemies.ToArray()) Damage(e, 9999, e.X, e.Y, bomb: true);
        _pending.Clear();
        string text = w.Text.Length > 90 ? w.Text[..89] + "…" : w.Text;
        Banner = $"{w.Post.LikeCount:N0} likes  -  {text}";
        BannerLeft = Tuning.BannerFrames;
    }

    // ---------------------------------------------------------------- like

    private void UpdatePrompt()
    {
        Prompt = null;
        if (!Alive) return;
        double best = Tuning.LikeRadius;
        foreach (var w in Wings)
        {
            if (w.Liked || w.Phase == WingPhase.Leave || w.Age < Tuning.WingEnterFrames / 2) continue;
            double d = Math.Sqrt((w.X - PX) * (w.X - PX) + (w.Y - PY) * (w.Y - PY));
            if (d <= best) { best = d; Prompt = w; }
        }
    }

    private bool InBombButton(double col, double row)
    {
        var b = BombButton;
        return col >= b.C && col < b.C + b.W && row >= b.R && row < b.R + b.H;
    }

    /// <summary>The wingman the like charge is on, and how full the charge is (0..1). Null/0 when nothing is held on one.</summary>
    public Wingman? HoldTarget { get; private set; }
    public double HoldProgress => HoldTarget is null ? 0 : Math.Min(1, _holdFrames / (double)Tuning.LikeHoldFrames);
    private int _holdFrames;
    private bool _holdSpent;

    /// <summary>A press held on or near a wingman charges a like; it lands when the charge fills. Releasing, dragging off or
    /// moving to another wingman first starts over, so a tap or click never likes. After a like the press must be let go.</summary>
    private void UpdateHold()
    {
        var inp = Input;
        if (!inp.Holding) { _holdSpent = false; HoldTarget = null; _holdFrames = 0; return; }
        Wingman? best = null;
        if (!_holdSpent && !InBombButton(inp.HoldCol, inp.HoldRow))
        {
            var (hx, hy) = Layout.ToField(inp.HoldCol, inp.HoldRow);
            double bd = Tuning.LikeHoldRadius;
            foreach (var w in Wings)
            {
                if (w.Liked || w.Phase == WingPhase.Leave || w.Age < Tuning.WingEnterFrames / 2) continue;
                double d = Math.Sqrt((w.X - hx) * (w.X - hx) + (w.Y - hy) * (w.Y - hy));
                if (d <= bd) { bd = d; best = w; }
            }
        }
        if (!ReferenceEquals(best, HoldTarget)) { HoldTarget = best; _holdFrames = 0; }
        if (best is null) return;
        if (++_holdFrames < Tuning.LikeHoldFrames) return;
        _holdSpent = true;
        HoldTarget = null; _holdFrames = 0;
        TryLike(best);
    }

    private void HandleLike()
    {
        if (Input.LikeLatch <= 0) return;
        if (!LoggedIn) { Input.LikeLatch = 0; Say(SignInHint); Sound(SfxNames.NoLike, PX); return; }
        if (Prompt is null) return;
        Input.LikeLatch = 0;
        TryLike(Prompt);
    }

    /// <summary>The single place a like can leave the game: needs a live like press (key, click or tap), a wingman in range,
    /// a signed-in session, and a post not liked before.</summary>
    private bool TryLike(Wingman w)
    {
        if (!LoggedIn) { Say(SignInHint); Sound(SfxNames.NoLike, w.X); return false; }
        if (w.Liked || !_liked.Add(w.Post.Uri)) return false;
        w.Liked = true;
        Sound(SfxNames.Like, w.X);
        OnLike?.Invoke(w.Post);
        _powerUntil = Tick + Tuning.PowerUpFrames;
        for (int i = 0; i < Tuning.HeartBurst; i++)
        {
            double a = i / (double)Tuning.HeartBurst * Math.PI * 2 + R();
            double sp = 0.5 + R() * 1.1;
            AddParticle(new Particle { X = w.X, Y = w.Y, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp - 0.5, Life = 22 + (int)(R() * 10), Color = 0xff5a8a, Kind = ParticleKind.Heart });
        }
        AddRing(w.X, w.Y, 3, 14, 0xff5a8a, 14);
        Popup("POWER UP", PX, PY - 10, 0xff8fb1);
        AddShake(1.2);
        return true;
    }

    // ---------------------------------------------------------------- bullets, collisions

    private void UpdateBullets()
    {
        foreach (var b in Bullets) { b.X += b.VX; b.Y += b.VY; }
        Bullets.RemoveAll(b => b.Y < -6 || b.Y > PH + 6 || b.X < -6 || b.X > PW + 6);
    }

    private void Collide()
    {
        foreach (var b in Bullets.ToArray())
        {
            if (b.Friendly)
            {
                foreach (var e in Enemies)
                {
                    if (e.Y < -2 || e.Hp <= 0) continue;
                    if (Math.Abs(b.X - e.X) <= e.Sprite.W / 2.0 && Math.Abs(b.Y - e.Y) <= e.Sprite.H / 2.0)
                    {
                        Bullets.Remove(b);
                        Damage(e, b.Dmg, b.X, b.Y);
                        break;
                    }
                }
            }
            else if (Alive && Invuln == 0 && Math.Abs(b.X - PX) <= 1.8 && Math.Abs(b.Y - PY) <= 2.2)
            {
                Bullets.Remove(b);
                PlayerHit();
            }
        }
        if (Alive && Invuln == 0)
            foreach (var e in Enemies)
                if (e.Hp > 0 && Math.Abs(e.X - PX) <= e.Sprite.W / 2.0 + 1 && Math.Abs(e.Y - PY) <= e.Sprite.H / 2.0 + 2)
                {
                    PlayerHit();
                    Damage(e, 4, PX, PY - 2);
                    break;
                }
    }

    private void Damage(Enemy e, double dmg, double hx, double hy, bool bomb = false)
    {
        if (e.Hp <= 0) return;
        e.Hp -= dmg;
        e.Flash = Tuning.FlashFrames;
        e.Hits++;
        bool heavy = e.Kind == EnemyKind.Gunship;
        double k = heavy ? Tuning.KnockHeavy : Tuning.KnockHit;
        e.KY -= k;
        for (int i = 0; i < Tuning.SparksPerHit; i++)
            AddParticle(new Particle { X = hx, Y = hy, VX = (R() - 0.5) * 2.2, VY = -R() * 1.6, Life = 5 + (int)(R() * 5), Color = R() < 0.5 ? 0xffffffu : 0xffd166u, Kind = ParticleKind.Spark });
        if (e.Hp > 0)
        {
            if (!bomb) { Sound(SfxNames.Hit, hx); AddShake(Tuning.ShakeHit, heavy && e.Hits % 4 == 0 ? Tuning.HitStopHeavyHit : 0); }
            return;
        }
        Kill(e, bomb);
    }

    private void Kill(Enemy e, bool bomb)
    {
        Enemies.Remove(e);
        Kills++;
        bool cleared = e.Live && !Enemies.Any(x => x.Live) && !_pending.Any(p => p.Live);
        if (cleared) { LiveWavesCleared++; Sound(SfxNames.Clear); }
        MaybeDrop(e, cleared);
        bool big = e.Kind == EnemyKind.Gunship;
        double size = big ? 1 : e.Kind == EnemyKind.Carrier ? 0.5 : 0;
        if (big) Sound(SfxNames.Boom, e.X, size);
        else if (!bomb) Sound(SfxNames.Kill, e.X, size);   // a bomb has its own sound
        int pts = big ? Tuning.ScoreGunship : e.Kind == EnemyKind.Carrier ? Tuning.ScoreCarrier : Tuning.ScoreGrunt;
        Score += pts;
        int debris = big ? Tuning.DebrisBigKill : Tuning.DebrisSmallKill;
        for (int i = 0; i < debris; i++)
        {
            double a = R() * Math.PI * 2, sp = (big ? 0.6 : 0.3) + R() * (big ? 2.0 : 1.2);
            uint c = R() < 0.5 ? 0x7a3fb0u : R() < 0.5 ? 0xff3d6eu : 0x2a1740u;
            AddParticle(new Particle { X = e.X + (R() - 0.5) * e.Sprite.W * 0.6, Y = e.Y + (R() - 0.5) * e.Sprite.H * 0.6, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp - 0.3, Life = 14 + (int)(R() * 14), Color = c, Size = R() < 0.3 ? 2 : 1, Kind = ParticleKind.Debris });
        }
        AddRing(e.X, e.Y, 2, big ? Tuning.RingRadiusBig : Tuning.RingRadiusSmall, big ? 0xffd166u : 0xff8fb1u, Tuning.RingFrames);
        if (big) AddRing(e.X, e.Y, 1, Tuning.RingRadiusBig * 0.6, 0xffffff, Tuning.RingFrames - 4);
        AddShake(big ? Tuning.ShakeBigKill : Tuning.ShakeSmallKill, big ? Tuning.HitStopBigKill : e.Kind == EnemyKind.Carrier ? Tuning.HitStopCarrierKill : Tuning.HitStopSmallKill);
        Popup("+" + pts, e.X, e.Y - 2, big ? 0xffd166u : 0xffffffu);
    }

    private void PlayerHit()
    {
        if (AbsorbWithShield()) return;
        InvulnIsShield = false;
        Hp--;
        Sound(Hp <= 0 ? SfxNames.Death : SfxNames.Hurt, PX);
        Invuln = Tuning.InvulnFrames;
        AddShake(Tuning.ShakePlayerHit, Tuning.HitStopPlayerHit);
        ScreenFlash = 2;
        AddRing(PX, PY, 2, 12, 0xff3d6e, 12);
        for (int i = 0; i < 14; i++)
            AddParticle(new Particle { X = PX, Y = PY, VX = (R() - 0.5) * 3, VY = (R() - 0.5) * 3, Life = 10 + (int)(R() * 8), Color = R() < 0.5 ? 0xffffffu : 0xff3d6eu, Kind = ParticleKind.Spark });
        if (Hp <= 0)
        {
            Deaths++;
            if (Score > RecordToBeat && Score > _bestSaved) { _bestSaved = Score; OnBest?.Invoke(Score); }
            RespawnIn = Tuning.RespawnFrames;
            Score = Math.Max(0, Score - Score / 4);
            AddShake(Tuning.ShakeBigKill, Tuning.HitStopBigKill);
            AddRing(PX, PY, 2, Tuning.RingRadiusBig, 0xffd166, Tuning.RingFrames);
            for (int i = 0; i < Tuning.DebrisBigKill; i++)
            {
                double a = R() * Math.PI * 2, sp = 0.5 + R() * 2;
                AddParticle(new Particle { X = PX, Y = PY, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp, Life = 16 + (int)(R() * 14), Color = R() < 0.5 ? 0xffd166u : 0xbfe9ffu, Size = R() < 0.3 ? 2 : 1, Kind = ParticleKind.Debris });
            }
            Bullets.RemoveAll(b => !b.Friendly);
            _powerUntil = -1;
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
            else if (p.Kind == ParticleKind.Heart) { p.VX *= 0.94; p.VY = p.VY * 0.94 - 0.02; }
            else p.VX *= 0.92;
            p.Life--;
        }
        Particles.RemoveAll(p => p.Life <= 0);
        foreach (var r in Rings) r.Life--;
        Rings.RemoveAll(r => r.Life <= 0);
        foreach (var p in Popups) { p.Life--; p.Y -= 0.35; }
        Popups.RemoveAll(p => p.Life <= 0);
    }

    private void AddParticle(Particle p)
    {
        if (Particles.Count < 420) { p.MaxLife = p.Life; Particles.Add(p); }
    }

    private void AddRing(double x, double y, double r0, double r1, uint color, int frames) =>
        Rings.Add(new Ring { X = x, Y = y, R0 = r0, R1 = r1, Life = frames, Max = frames, Color = color });

    private void Popup(string text, double x, double y, uint color) =>
        Popups.Add(new Popup { Text = text, X = x, Y = y, Life = Tuning.PopupFrames, Color = color });

    // ---------------------------------------------------------------- read-outs for the HUD

    /// <summary>"◉ LIVE spam ×7" while a wave from a real darkness event is on screen, "○ patrol" for filler.</summary>
    public string WaveLabel()
    {
        int live = 0, spam = 0, rude = 0;
        foreach (var e in Enemies)
            if (e.Live) { live++; if (e.Source == DarknessKind.Rude) rude++; else spam++; }
        if (live > 0) return rude > 0 ? "◉ LIVE rude" : $"◉ LIVE spam ×{spam}";
        return Enemies.Count > 0 ? "○ patrol" : "○ clear";
    }
}
