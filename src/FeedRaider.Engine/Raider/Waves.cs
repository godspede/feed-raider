namespace FeedRaider.Engine.Raider;

/// <summary>The enemies. Grunt, Carrier and Gunship fly from stage 1; the rest unlock with the ramp (<see cref="Ramp.UnlockOrder"/>).
/// A shard only comes out of a splitter.</summary>
public enum EnemyKind { Grunt, Carrier, Gunship, Mine, Diver, Splitter, Shard, Turret }
public enum WavePattern { Line, Vee, Arc, Column, Sine }

/// <summary>One enemy of a wave: where it enters (x as a 0..1 fraction of the field width), how many frames after the
/// wave starts, and how it flies.</summary>
public sealed record EnemySpawn(EnemyKind Kind, double X, int Delay, double Amp, double Phase, string? Text);

/// <summary>A planned wave. <see cref="Live"/> waves come from a real darkness event, filler ones from the generator.</summary>
public sealed record WaveSpec(bool Live, DarknessKind? Kind, WavePattern Pattern, IReadOnlyList<EnemySpawn> Enemies, string? Text, string? Label = null);

/// <summary>Turns darkness events (and the filler generator) into waves. Pure: the same post always gives the same wave.
/// The text a wave carries went through <see cref="DarknessText"/> already; nothing here knows an author.</summary>
public static class WaveSpawner
{
    /// <summary>A darkness event's wave. From <see cref="Tuning.LiveSpecialsFromStage"/> a spam wave swaps a few plain grunts for
    /// newer kinds that can be shot down (never a mine, so the wave can always be cleared); the carrier and its text stay as they are.</summary>
    public static WaveSpec FromPost(DarkPost post, int stage = 1)
    {
        ulong s = post.Seed;
        string text = DarknessText.Clean(post.Text, 44);
        if (post.Kind == DarknessKind.Rude)
        {
            var gunship = new EnemySpawn(EnemyKind.Gunship, 0.3 + Hashing.Unit(s, 1) * 0.4, 0, 0.22, Hashing.Unit(s, 2) * 6.28, text);
            return new WaveSpec(true, DarknessKind.Rude, WavePattern.Line, [gunship], text, post.Label);
        }
        int n = Tuning.SpamMin + (int)(Hashing.Mix(s) % (ulong)(Tuning.SpamMax - Tuning.SpamMin + 1));
        var pattern = (WavePattern)(int)(Hashing.Unit(s, 3) * 4);   // line, vee, arc, column (sine is filler-only)
        int carrier = (int)(Hashing.Unit(s, 4) * n);
        var frag = DarknessText.Fragments(text, 16);
        string? carried = frag.Count > 0 ? frag[(int)(Hashing.Unit(s, 5) * frag.Count)] : null;
        var list = Formation(pattern, n, s, carrier, carried);
        if (stage >= Tuning.LiveSpecialsFromStage)
            for (int i = 0, swaps = Math.Min(Ramp.Specials(stage), n - 1); i < swaps; i++)
            {
                int at = (carrier + 1 + i * 2) % n;
                var kind = PickKind(s, i, stage, shootable: true);
                if (at != carrier && kind is { } k) list[at] = list[at] with { Kind = k };
            }
        return new WaveSpec(true, DarknessKind.Spam, pattern, list, text, post.Label);
    }

    /// <summary>The nth filler wave: grunts (small ones only at stage 1), never carrying text, never a gunship. Later stages add
    /// grunts and groups of the newer kinds (<see cref="Ramp.Specials"/>).</summary>
    public static WaveSpec Filler(ulong seed, int stage = 1)
    {
        int n = 4 + Ramp.FillerExtra(stage) + (int)(Hashing.Mix(seed) % 5);
        var pattern = (WavePattern)(int)(Hashing.Unit(seed, 3) * 5);
        var list = Formation(pattern, n, seed, -1, null);
        for (int i = 0, groups = Ramp.Specials(stage); i < groups; i++)
            if (PickKind(seed, i, stage, shootable: false) is { } kind) AddGroup(list, kind, seed, i);
        return new WaveSpec(false, null, pattern, list, null);
    }

    /// <summary>The newer kind for the <paramref name="i"/>th group of a wave. While kinds are still unlocking, a stage brings only
    /// its own new kind, so each is met alone; after that the pick is spread over every kind unlocked.</summary>
    internal static EnemyKind? PickKind(ulong seed, int i, int stage, bool shootable)
    {
        int have = Ramp.UnlockedCount(stage);
        if (have == 0) return null;
        EnemyKind kind = stage <= Ramp.LastUnlockStage ? Ramp.UnlockOrder[have - 1]
            : Ramp.UnlockOrder[(int)(Hashing.Unit(seed, 20 + i) * have) % have];
        if (shootable && kind == EnemyKind.Mine) kind = have > 1 ? Ramp.UnlockOrder[1 + (int)(Hashing.Unit(seed, 30 + i) * (have - 1)) % (have - 1)] : EnemyKind.Grunt;
        return kind;
    }

    /// <summary>Adds one group of a newer kind behind the grunts: a mine group is a spaced pair, the rest come alone.</summary>
    private static void AddGroup(List<EnemySpawn> list, EnemyKind kind, ulong s, int i)
    {
        double x = 0.15 + Hashing.Unit(s, 40 + i) * 0.7;
        int delay = 20 + i * 35 + (int)(Hashing.Unit(s, 50 + i) * 20);
        double phase = Hashing.Unit(s, 60 + i) * 6.28;
        if (kind == EnemyKind.Mine)
        {
            for (int k = 0; k < Tuning.MinesPerSpecial; k++)
            {
                double mx = x + (k - (Tuning.MinesPerSpecial - 1) / 2.0) * 0.32;
                list.Add(new EnemySpawn(kind, Math.Clamp(mx, 0.08, 0.92), delay + k * 12, 0, phase + k, null));
            }
            return;
        }
        list.Add(new EnemySpawn(kind, Math.Clamp(x, 0.1, 0.9), delay, kind == EnemyKind.Splitter ? 0.18 : 0, phase, null));
    }

    private static List<EnemySpawn> Formation(WavePattern p, int n, ulong s, int carrier, string? text)
    {
        var list = new List<EnemySpawn>(n);
        double cx = 0.25 + Hashing.Unit(s, 7) * 0.5;
        double phase = Hashing.Unit(s, 8) * 6.28;
        for (int i = 0; i < n; i++)
        {
            double t = n == 1 ? 0.5 : i / (double)(n - 1);
            double x; int delay; double amp = 0;
            switch (p)
            {
                case WavePattern.Line: x = 0.12 + t * 0.76; delay = (i % 2) * 6; break;
                case WavePattern.Vee: x = cx + (t - 0.5) * 0.6; delay = (int)(Math.Abs(t - 0.5) * 2 * 20); break;
                case WavePattern.Arc: x = 0.1 + t * 0.8; delay = (int)(Math.Sin(t * Math.PI) * 14); break;
                case WavePattern.Column: x = cx; delay = i * 9; break;
                default: x = 0.5; delay = i * 8; amp = 0.3; break;
            }
            bool isCarrier = i == carrier;
            list.Add(new EnemySpawn(isCarrier ? EnemyKind.Carrier : EnemyKind.Grunt, Math.Clamp(x, 0.08, 0.92), delay, amp, phase, isCarrier ? text : null));
        }
        return list;
    }
}
