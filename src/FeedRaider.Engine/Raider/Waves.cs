namespace FeedRaider.Engine.Raider;

public enum EnemyKind { Grunt, Carrier, Gunship }
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
    public static WaveSpec FromPost(DarkPost post)
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
        return new WaveSpec(true, DarknessKind.Spam, pattern, Formation(pattern, n, s, carrier, carried), text, post.Label);
    }

    /// <summary>The nth filler wave: small grunts only, never carrying text, never a gunship.</summary>
    public static WaveSpec Filler(ulong seed)
    {
        int n = 4 + (int)(Hashing.Mix(seed) % 5);
        var pattern = (WavePattern)(int)(Hashing.Unit(seed, 3) * 5);
        return new WaveSpec(false, null, pattern, Formation(pattern, n, seed, -1, null), null);
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
