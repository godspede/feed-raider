namespace FeedRaider.Engine.Raider;

/// <summary>The enemies. Grunt, Carrier and Gunship fly from stage 1; the rest unlock with the ramp (<see cref="Ramp.Unlocks"/>).
/// A shard only comes out of a splitter.</summary>
public enum EnemyKind
{
    Grunt, Carrier, Gunship, Mine, Diver, Splitter, Shard, Turret,
    // The roster, appended in this order and never reordered. Each is one file in Enemies/ (see EnemyBehaviour).
    Gnat, Brick, Flanker, Warden, Lobber, Nest, Sower, Yoke, Lurk, Fuse, Bulwark, Shutter, Thread, Spotlight, Brood, Schism, PileOn, Mirror, Doomscroll,
    Chorus, Glint,
}
public enum WavePattern { Line, Vee, Arc, Column, Sine }

/// <summary>One enemy of a wave: where it enters (x as a 0..1 fraction of the field width), how many frames after the
/// wave starts, and how it flies.</summary>
public sealed record EnemySpawn(EnemyKind Kind, double X, int Delay, double Amp, double Phase, string? Text);

/// <summary>A planned wave. <see cref="Live"/> waves come from a real darkness event, filler ones from the generator.</summary>
public sealed record WaveSpec(bool Live, DarknessKind? Kind, WavePattern Pattern, IReadOnlyList<EnemySpawn> Enemies, string? Text, string? Label = null, DarknessSource Source = DarknessSource.Label);

/// <summary>Turns darkness events (and the filler generator) into waves. Pure: the same post always gives the same wave.
/// The text a wave carries went through <see cref="DarknessText"/> already; nothing here knows an author.</summary>
public static class WaveSpawner
{
    /// <summary>How near the field's edges (as 0..1 fractions of its width) a wave's formation or mine pair may enter.</summary>
    private const double SpawnXMin = 0.08, SpawnXMax = 0.92;

    // a group of a newer kind: where it enters (a fraction of the width), when, and how far apart a pair stands
    private const double GroupXMin = 0.15, GroupXSpan = 0.7;
    private const int GroupXSeed = 40, GroupDelaySeed = 50, GroupPhaseSeed = 60;
    private const int GroupDelayBase = 20, GroupDelayStep = 35, GroupDelayJitter = 20, PairStaggerFrames = 12;
    private const double MinePairGap = 0.32, LobberPairGap = 0.4, SplitterSway = 0.18;
    private const int LobberPairFromStage = 7, LobberPairSize = 2;

    // the formations: the middle of the wave's centre line may drift this far, a line spans this much of the width, and a vee or an arc
    // staggers its ends by this many frames
    private const double CentreXMin = 0.25, CentreXSpan = 0.5;
    private const double LineXMin = 0.12, LineXSpan = 0.76, VeeXSpan = 0.6, ArcXMin = 0.1, ArcXSpan = 0.8, SineMidX = 0.5, SineSway = 0.3;
    private const int LineStaggerFrames = 6, VeeDelaySpan = 20, ArcDelaySpan = 14, ColumnStepFrames = 9, SineStepFrames = 8;
    // how far along a wave (0 to 1) a vee's point stands, and how many ranks a line staggers into
    private const double VeePoint = 0.5;
    private const int LineRanks = 2;
    // the carrier index a filler wave's formation takes, matching no member
    private const int NoCarrier = -1;
    // the share of a stage earlier than the run's own: wholly through it, so all it unlocks is out
    private const double WholeStage = 1;

    // salts that keep the draws of one post independent of each other
    private const int PatternSeed = 3, CarrierSeed = 4, CentreXSeed = 7, PhaseSeed = 8, PickSeed = 20;
    // a live wave takes its turn among a stage's own kinds from one of this many numbers, derived from the post
    private const int LiveWaveTurns = 8;
    // the newest stages with a kind to offer count this many times in the pool of a group's kind
    private const int NewestStagesCounted = 2, NewestStageWeight = 2;
    private const int SwapStride = 2;   // a live wave swaps every second grunt after the carrier

    // every pattern but Sine can come from a live post; filler waves draw from all of them
    private static readonly int FillerPatterns = Enum.GetValues<WavePattern>().Length;
    private static readonly int LivePatterns = FillerPatterns - 1;

    /// <summary>A darkness event's wave. From <see cref="Tuning.LiveSpecialsFromStage"/> a live wave swaps a few plain grunts for
    /// newer kinds that can be killed (never a mine, so the wave can always be cleared); the carrier and its text stay as they are.</summary>
    public static WaveSpec FromPost(DarknessPost post, int stage = 1)
    {
        ulong s = post.Seed;
        string text = DarknessText.Clean(post.Text, DarknessText.MaxPostChars);
        if (post.Kind == DarknessKind.Rude) return new WaveSpec(true, DarknessKind.Rude, WavePattern.Line, [], text, post.Label, post.Source);   // a rude post makes no enemy: it waits in RudeStore for a speaker
        int n = Tuning.SpamMin + (int)(Hashing.Mix(s) % (ulong)(Tuning.SpamMax - Tuning.SpamMin + 1));
        var pattern = (WavePattern)(int)(Hashing.Unit(s, PatternSeed) * LivePatterns);
        int carrier = (int)(Hashing.Unit(s, CarrierSeed) * n);
        string? carried = text.Length > 0 ? text : null;   // the carrier's speech box shows the whole post
        var list = Formation(pattern, n, s, carrier, carried);
        if (stage >= Tuning.LiveSpecialsFromStage)
            for (int i = 0, swaps = Math.Min(Ramp.Specials(stage), n - 1); i < swaps; i++)
            {
                int at = (carrier + 1 + i * SwapStride) % n;
                var kind = PickKind(s, i, stage, shootable: true, wave: (int)(Hashing.Mix(s) % LiveWaveTurns));
                if (at != carrier && kind is { } k) list[at] = list[at] with { Kind = k };
            }
        return new WaveSpec(true, DarknessKind.Spam, pattern, list, text, post.Label, post.Source);
    }

    /// <summary>The nth filler wave: grunts (small ones only at stage 1), never carrying text, never a gunship. Later stages add
    /// grunts and groups of the newer kinds (<see cref="Ramp.Specials"/>). <paramref name="wave"/> is the filler wave's number, which
    /// sets whose turn it is among the kinds a stage brings. <paramref name="share"/> is how far into the stage the run is (0 to 1, from
    /// <see cref="Ramp.StageShare"/>): in stage 1 the first newer kind unlocks halfway through.</summary>
    public static WaveSpec Filler(ulong seed, int stage = 1, int wave = 0, double share = 0)
    {
        int n = Tuning.FillerBase + Ramp.FillerExtra(stage) + (int)(Hashing.Mix(seed) % (ulong)Tuning.FillerGruntsSpread);
        var pattern = (WavePattern)(int)(Hashing.Unit(seed, PatternSeed) * FillerPatterns);
        var list = Formation(pattern, n, seed, NoCarrier, null);
        for (int i = 0, groups = Ramp.Specials(stage, share); i < groups; i++)
            if (PickKind(seed, i, stage, shootable: false, wave, share: share) is { } kind) AddGroup(list, kind, seed, i, stage);
        return new WaveSpec(false, null, pattern, list, null);
    }

    /// <summary>The newer kind for the <paramref name="i"/>th group of wave number <paramref name="wave"/>. Group 0 is the stage's own
    /// unlocks (<see cref="Ramp.Unlocks"/>), turn and turn about, so each is met in its own stage. Every further group
    /// draws from a weighted pool of every kind unlocked so far, the newest two stages that have one counting double; so does
    /// every group of a stage with none of its own (and of every lap past the cap). A live wave (<paramref name="shootable"/>)
    /// never gets a mine, so it can always be cleared. Null when nothing fits. <paramref name="share"/> is how far into the stage the
    /// run is; <paramref name="available"/> says which kinds may be drawn, the default being all of them.</summary>
    internal static EnemyKind? PickKind(ulong seed, int i, int stage, bool shootable, int wave = 0, Func<EnemyKind, bool>? available = null, double share = 0)
    {
        available ??= _ => true;
        bool Fits(EnemyKind k) => available(k) && !(shootable && k == EnemyKind.Mine);
        var own = Ramp.Unlocks(stage, share).Where(Fits).ToArray();
        if (i == 0 && own.Length > 0) return own[(int)(Math.Max(0, wave) % own.Length)];
        var top = Math.Min(stage, Tuning.StageCap);
        var pool = new List<EnemyKind>();
        var newest = new List<int>();
        for (int st = top; st >= 1; st--)
            if (Ramp.Unlocks(st, st == top ? share : WholeStage).Any(Fits)) { if (newest.Count < NewestStagesCounted) newest.Add(st); }
        for (int st = 1; st <= top; st++)
            foreach (var k in Ramp.Unlocks(st, st == top ? share : WholeStage).Where(Fits))
                for (int copy = 0, weight = newest.Contains(st) ? NewestStageWeight : 1; copy < weight; copy++) pool.Add(k);
        if (pool.Count == 0) return null;
        return pool[(int)(Hashing.Unit(seed, PickSeed + i) * pool.Count) % pool.Count];
    }

    /// <summary>The kind that takes the place of a mini-boss the field has no room for. No randomness: the stage's other unlock
    /// if it has one, otherwise the newest kind unlocked that is no mini-boss; a mine only when nothing else is unlocked (and never
    /// in a <paramref name="shootable"/> wave), then a grunt, so the wave keeps all its members.</summary>
    internal static EnemyKind Substitute(EnemyKind kind, int stage, double share, bool shootable, Func<EnemyKind, bool>? available = null)
    {
        available ??= _ => true;
        bool Fits(EnemyKind k) => k != kind && !Ramp.IsMiniBoss(k) && available(k) && !(shootable && k == EnemyKind.Mine);
        int top = Math.Min(stage, Tuning.StageCap);
        for (int st = top; st >= 1; st--)
            foreach (var k in Ramp.Unlocks(st, st == top ? share : WholeStage).Reverse())
                if (k != EnemyKind.Mine && Fits(k)) return k;
        return Fits(EnemyKind.Mine) && Ramp.Unlocked(EnemyKind.Mine, top, share) ? EnemyKind.Mine : EnemyKind.Grunt;
    }

    /// <summary>Adds one group of a newer kind behind the grunts: a mine group is a spaced pair, a lobber group a pair from stage <see cref="LobberPairFromStage"/>,
    /// the rest come alone.</summary>
    private static void AddGroup(List<EnemySpawn> list, EnemyKind kind, ulong s, int i, int stage)
    {
        double x = GroupXMin + Hashing.Unit(s, GroupXSeed + i) * GroupXSpan;
        int delay = GroupDelayBase + i * GroupDelayStep + (int)(Hashing.Unit(s, GroupDelaySeed + i) * GroupDelayJitter);
        double phase = Hashing.Unit(s, GroupPhaseSeed + i) * Math.Tau;
        int size = kind == EnemyKind.Mine ? Tuning.MinesPerSpecial : kind == EnemyKind.Lobber && stage >= LobberPairFromStage ? LobberPairSize : 1;
        if (size > 1)
        {
            double gap = kind == EnemyKind.Mine ? MinePairGap : LobberPairGap;
            for (int k = 0; k < size; k++)
            {
                double mx = x + (k - (size - 1) / 2.0) * gap;
                list.Add(new EnemySpawn(kind, Math.Clamp(mx, SpawnXMin, SpawnXMax), delay + k * PairStaggerFrames, 0, phase + k, null));
            }
            return;
        }
        list.Add(new EnemySpawn(kind, Math.Clamp(x, SpawnXMin, SpawnXMax), delay, kind == EnemyKind.Splitter ? SplitterSway : 0, phase, null));
    }

    private static List<EnemySpawn> Formation(WavePattern p, int n, ulong s, int carrier, string? text)
    {
        var list = new List<EnemySpawn>(n);
        double cx = CentreXMin + Hashing.Unit(s, CentreXSeed) * CentreXSpan;
        double phase = Hashing.Unit(s, PhaseSeed) * Math.Tau;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)(n - 1);   // n is at least 2: both callers pass a wave of four or more
            double x; int delay; double amp = 0;
            switch (p)
            {
                case WavePattern.Line: x = LineXMin + t * LineXSpan; delay = (i % LineRanks) * LineStaggerFrames; break;
                case WavePattern.Vee: x = cx + (t - VeePoint) * VeeXSpan; delay = (int)(Math.Abs(t - VeePoint) / VeePoint * VeeDelaySpan); break;
                case WavePattern.Arc: x = ArcXMin + t * ArcXSpan; delay = (int)(Math.Sin(t * Math.PI) * ArcDelaySpan); break;
                case WavePattern.Column: x = cx; delay = i * ColumnStepFrames; break;
                default: x = SineMidX; delay = i * SineStepFrames; amp = SineSway; break;
            }
            bool isCarrier = i == carrier;
            list.Add(new EnemySpawn(isCarrier ? EnemyKind.Carrier : EnemyKind.Grunt, Math.Clamp(x, SpawnXMin, SpawnXMax), delay, amp, phase, isCarrier ? text : null));
        }
        return list;
    }
}
