namespace FeedRaider.Console.Audio;

/// <summary>
/// The game's sound effects as PCM, ported from the page's <c>sfx.js</c> (Web Audio oscillators, filtered noise and envelopes) so
/// the terminal sounds like the web. Pure: a name, the combo step and the size in, mono samples out. The randomness (a detuned
/// shot, the noise) comes from the caller's <see cref="Random"/>, so a seeded one renders the same samples every time.
/// </summary>
public static class SfxSynth
{
    public const int SampleRate = 44100;
    /// <summary>The combo pitch rise tops out at this many semitones, as on the page.</summary>
    public const int MaxStep = 7;

    private const double FloorGain = 0.0001;       // an exponential envelope cannot reach 0
    private const double TailSeconds = 0.02;       // how long a source runs past its envelope, as on the page
    private const double MinFreq = 20;
    private const double NoiseDriveGain = 4;       // the grit stage overdrives the noise by this before saturating
    private const double SaturateKnee = 0.9;       // soft saturation, slope 1 at zero, tops out at this
    private const int FilterBlock = 32;            // samples between filter coefficient updates while a filter sweeps
    private const double BrownLeak = 0.02, BrownGain = 3.5;
    private const double ShotDetuneSpread = 0.08;
    private const int InitialBufferSamples = SampleRate / 4;
    private const double MaxCutoffFraction = 0.45;   // of the sample rate: a filter corner is held below this, short of Nyquist (0.5)

    /// <summary>Per-sound level trim, a multiplier on every tone and noise in the sound.</summary>
    private static readonly Dictionary<string, double> Trim = new()
    {
        ["shot"] = 0.4,
        ["hit"] = 0.52,
        ["kill"] = 0.92,
        ["boom"] = 1.51,
        ["hurt"] = 1.37,
        ["death"] = 1,
        ["bomb"] = 1.67,
        ["comm"] = 2.68,
        ["like"] = 1.56,
        ["nolike"] = 1.34,
        ["darkness"] = 2.2,
        ["clear"] = 1.58,
        ["pickup_shield"] = 1.48,
        ["pickup_bomb"] = 1.4,
        ["pickup_power"] = 1.85,
        ["shield_hit"] = 0.97,
    };

    public static IEnumerable<string> Names => Trim.Keys;

    /// <summary>The soft limiter on the sfx bus: stops a pile-up of voices from clipping.</summary>
    public static float Saturate(float x) => (float)(SaturateKnee * Math.Tanh(x / SaturateKnee));

    /// <summary>The samples of one effect, or an empty array for an unknown name. <paramref name="step"/> is the combo rise in
    /// semitones (kill and hit only); <paramref name="size"/> is the enemy's size, 0 to 1 (kill only).</summary>
    public static float[] Render(string name, int step, double size, Random rng)
    {
        if (!Trim.TryGetValue(name, out double trim)) return [];
        int rise = name is "kill" or "hit" ? Math.Clamp(step, 0, MaxStep) : 0;
        var v = new Voice(Math.Pow(2, rise / 12.0), trim, rng);
        size = Math.Clamp(size, 0, 1);
        switch (name)
        {
            case "shot":
                {
                    double k = 1 + (rng.NextDouble() - 0.5) * ShotDetuneSpread;
                    v.Tone(Wave.Square, 1250 * k, 320 * k, dur: 0.07, vol: 0.5, lp: 4200);
                    v.Tone(Wave.Triangle, 190 * k, 85, dur: 0.1, vol: 0.5);
                    v.Noise(Filter.HighPass, 5000, dur: 0.012, vol: 0.2);
                    break;
                }
            case "hit":
                v.Noise(Filter.BandPass, 3200, q: 1.2, dur: 0.05, vol: 0.7);
                v.Tone(Wave.Triangle, 260, 110, dur: 0.07, vol: 0.7);
                v.Tone(Wave.Sine, 140, 80, dur: 0.06, vol: 0.5);
                break;
            case "kill":
                v.Noise(Filter.HighPass, 3500, dur: 0.014, vol: 0.5);
                v.Tone(Wave.Sine, 90, 48, dur: 0.4 + 0.2 * size, vol: 0.8 + 0.2 * size, attack: 0.002);
                v.Noise(Filter.LowPass, 3800, 220, q: 1, at: 0.01, dur: 0.7 + 0.2 * size, vol: 0.55 + 0.1 * size, shape: true);
                v.Tone(Wave.Square, 420, 55, at: 0.01, dur: 0.45, vol: 0.3, lp: 1800);
                break;
            case "boom":
                for (int i = 0; i < 3; i++)
                {
                    v.Noise(Filter.LowPass, 3400 - i * 600, 200, q: 1, at: i * 0.2, dur: 0.5, vol: 0.9, shape: true);
                    v.Tone(Wave.Square, 360 - i * 60, 50, at: i * 0.2, dur: 0.4, vol: 0.25, lp: 1500);
                }
                v.Noise(Filter.HighPass, 3500, dur: 0.02, vol: 0.5);
                v.Tone(Wave.Sine, 95, 32, at: 0.02, dur: 2.2, vol: 0.95);
                v.Noise(Filter.LowPass, 1700, 60, at: 0.3, dur: 2, vol: 1, attack: 0.03, shape: true, brown: true);
                v.Tone(Wave.Saw, 120, 26, at: 0.3, dur: 2, vol: 0.45, lp: 400);
                v.Tone(Wave.Square, 1760, 220, at: 0.45, dur: 1.4, vol: 0.1, lp: 3000);
                break;
            case "hurt":
                v.Tone(Wave.Saw, 330, 90, dur: 0.3, vol: 0.55, lp: 1800);
                v.Tone(Wave.Saw, 466, 128, dur: 0.3, vol: 0.35, lp: 1800);
                v.Tone(Wave.Sine, 110, 45, dur: 0.25, vol: 0.6);
                v.Noise(Filter.BandPass, 1400, 400, q: 1, dur: 0.22, vol: 0.5, shape: true);
                break;
            case "death":
                v.Tone(Wave.Square, 640, 38, dur: 0.56, vol: 0.45, lp: 2400);
                v.Tone(Wave.Saw, 320, 30, dur: 0.56, vol: 0.4, lp: 900, detune: 40);
                v.Tone(Wave.Sine, 100, 34, dur: 0.58, vol: 0.7);
                v.Noise(Filter.LowPass, 4200, 150, q: 1, dur: 0.57, vol: 0.75, shape: true);
                break;
            case "bomb":
                v.Noise(Filter.HighPass, 3000, dur: 0.02, vol: 0.5);
                v.Tone(Wave.Sine, 170, 34, dur: 1.9, vol: 1);
                v.Tone(Wave.Saw, 90, 300, dur: 0.14, vol: 0.3, lp: 900);
                v.Noise(Filter.LowPass, 500, 3600, dur: 0.2, vol: 0.9, brown: true);
                v.Noise(Filter.LowPass, 3600, 140, q: 0.9, at: 0.2, dur: 1.6, vol: 0.8, shape: true);
                break;
            case "comm":
                {
                    v.Noise(Filter.BandPass, 2400, q: 2, dur: 0.035, vol: 0.4);
                    double at = 0.045;
                    foreach (var (f, d) in new[] { (1320.0, 0.04), (1760.0, 0.04), (1480.0, 0.07) })
                    {
                        v.Tone(Wave.Square, f, f * 0.985, at: at, dur: d, vol: 0.35, attack: 0.002, lp: 2600);
                        at += d + 0.02;
                    }
                    v.Noise(Filter.BandPass, 2000, q: 2, at: 0.25, dur: 0.03, vol: 0.25);
                    break;
                }
            case "like":
                {
                    double[] fs = [880, 1319, 1760, 2637];
                    for (int i = 0; i < fs.Length; i++)
                    {
                        double at = i * 0.055; bool last = i == fs.Length - 1;
                        v.Tone(Wave.Triangle, fs[i], at: at, dur: last ? 0.42 : 0.2, vol: last ? 0.55 : 0.45);
                        v.Tone(Wave.Sine, fs[i] * 2.76, at: at, dur: last ? 0.28 : 0.1, vol: 0.14);
                    }
                    break;
                }
            case "nolike":
                v.Tone(Wave.Triangle, 247, 233, dur: 0.09, vol: 0.45, lp: 700);
                v.Tone(Wave.Triangle, 185, 165, at: 0.09, dur: 0.12, vol: 0.45, lp: 600);
                break;
            case "darkness":
                v.Tone(Wave.Saw, 55, 49, dur: 2.4, vol: 0.55, attack: 0.5, lp: 260);
                v.Tone(Wave.Saw, 55.8, 49.6, dur: 2.4, vol: 0.55, attack: 0.5, lp: 380, detune: 9);
                v.Tone(Wave.Sine, 77.8, 69, dur: 2.4, vol: 0.5, attack: 0.9);
                v.Tone(Wave.Square, 110, 98, at: 0.5, dur: 1.8, vol: 0.1, attack: 0.8, lp: 500);
                v.Noise(Filter.BandPass, 140, 900, q: 1.5, dur: 2.4, vol: 0.9, attack: 1, brown: true);
                v.Swell(1, 1.5, 2.4);
                break;
            case "clear":
                {
                    double[] fs = [440, 523, 659, 880];
                    for (int i = 0; i < fs.Length; i++)
                    {
                        bool last = i == fs.Length - 1; double at = i * 0.075;
                        v.Tone(Wave.Square, fs[i], at: at, dur: last ? 0.27 : 0.1, vol: last ? 0.4 : 0.32, lp: 3200);
                        if (last) v.Tone(Wave.Triangle, fs[i] * 2, at: at, dur: 0.27, vol: 0.25);
                    }
                    break;
                }
            case "pickup_shield":
                {
                    double[] fs = [523, 659, 784, 1047];
                    for (int i = 0; i < fs.Length; i++)
                    {
                        double at = i * 0.07; bool last = i == fs.Length - 1;
                        v.Tone(Wave.Sine, fs[i], at: at, dur: last ? 0.4 : 0.16, vol: 0.5);
                        v.Tone(Wave.Sine, fs[i] * 3.01, at: at, dur: last ? 0.3 : 0.08, vol: 0.14);
                    }
                    v.Tone(Wave.Triangle, 1047, at: 0.21, dur: 0.4, vol: 0.2, detune: 12);
                    break;
                }
            case "pickup_bomb":
                {
                    v.Tone(Wave.Sine, 150, 70, dur: 0.14, vol: 0.3);
                    double[] fs = [220, 262, 330, 440];
                    for (int i = 0; i < fs.Length; i++)
                    {
                        double at = i * 0.065; bool last = i == fs.Length - 1;
                        v.Tone(Wave.Square, fs[i], at: at, dur: last ? 0.3 : 0.1, vol: last ? 0.34 : 0.28, lp: 1800);
                        v.Tone(Wave.Triangle, fs[i] / 2, at: at, dur: last ? 0.3 : 0.1, vol: 0.3);
                    }
                    break;
                }
            case "pickup_power":
                {
                    double[] fs = [659, 880, 1109, 1319, 1760];
                    for (int i = 0; i < fs.Length; i++)
                    {
                        double at = i * 0.05; bool last = i == fs.Length - 1;
                        v.Tone(Wave.Square, fs[i], at: at, dur: last ? 0.42 : 0.1, vol: last ? 0.3 : 0.24, lp: 5000);
                        v.Tone(Wave.Saw, fs[i] * 2.005, at: at, dur: last ? 0.3 : 0.08, vol: 0.12, lp: 6000);
                    }
                    v.Tone(Wave.Sine, 3520, at: 0.25, dur: 0.35, vol: 0.2);
                    v.Noise(Filter.HighPass, 6500, at: 0.25, dur: 0.1, vol: 0.12);
                    break;
                }
            case "shield_hit":
                v.Noise(Filter.BandPass, 4200, q: 2, dur: 0.02, vol: 0.4);
                v.Tone(Wave.Sine, 2200, 1900, dur: 0.22, vol: 0.5);
                v.Tone(Wave.Sine, 2200 * 2.76, 5200, dur: 0.1, vol: 0.18);
                v.Tone(Wave.Sine, 170, 70, dur: 0.14, vol: 0.7);
                break;
        }
        return v.Samples();
    }

    public enum Wave { Sine, Square, Saw, Triangle }
    public enum Filter { LowPass, HighPass, BandPass }

    /// <summary>One sound being built: tones and noise summed into a growing mono buffer, like the page's voice node.</summary>
    private sealed class Voice(double k, double trim, Random rng)
    {
        private float[] _buf = new float[InitialBufferSamples];
        private int _len;

        public float[] Samples()
        {
            var o = new float[_len];
            Array.Copy(_buf, o, _len);
            return o;
        }

        /// <summary>Reserves the samples a source covers, growing the buffer and the sound's length to fit, and returns them.</summary>
        private Span<float> ReserveRegion(double at, double dur)
        {
            int start = (int)(at * SampleRate), n = (int)((dur + TailSeconds) * SampleRate);
            if (start + n > _buf.Length) Array.Resize(ref _buf, Math.Max(_buf.Length * 2, start + n));
            _len = Math.Max(_len, start + n);
            return _buf.AsSpan(start, n);
        }

        // linear attack to peak, then exponential decay to the floor at dur, as the page's gain ramps do
        private static double Envelope(int i, double attack, double dur, double peak)
        {
            double t = (double)i / SampleRate;
            if (t >= dur) return 0;
            if (t < attack) return FloorGain + (peak - FloorGain) * (t / attack);
            return peak * Math.Pow(FloorGain / peak, (t - attack) / Math.Max(1e-6, dur - attack));
        }

        // frequency sweeps are exponential from f0 to f1 over dur
        private static double Sweep(double f0, double f1, int i, double dur)
        {
            double t = Math.Min(1, (double)i / SampleRate / dur);
            return f0 == f1 ? f0 : f0 * Math.Pow(f1 / f0, t);
        }

        public void Tone(Wave wave, double f0, double f1 = double.NaN, double at = 0, double dur = 0.1, double vol = 0.2,
            double attack = 0.004, double lp = 0, double detune = 0)
        {
            if (double.IsNaN(f1)) f1 = f0;
            double cents = Math.Pow(2, detune / 1200), peak = vol * trim;
            var region = ReserveRegion(at, dur);
            var filter = lp > 0 ? new Biquad(Filter.LowPass, lp, 0) : null;
            double phase = 0;
            for (int i = 0; i < region.Length; i++)
            {
                double f = Math.Max(MinFreq, Sweep(f0, f1, i, dur) * k) * cents, dt = f / SampleRate;
                double s = wave switch
                {
                    Wave.Sine => Math.Sin(2 * Math.PI * phase),
                    Wave.Square => (phase < 0.5 ? 1 : -1) + PolyBlep(phase, dt) - PolyBlep((phase + 0.5) % 1, dt),
                    Wave.Saw => 2 * phase - 1 - PolyBlep(phase, dt),
                    _ => 4 * Math.Abs(phase - 0.5) - 1,
                };
                phase += dt; if (phase >= 1) phase -= 1;
                if (filter is not null) s = filter.Process(s);
                region[i] += (float)(s * Envelope(i, attack, dur, peak));
            }
        }

        public void Noise(Filter type, double f0, double f1 = double.NaN, double q = 0.7, double at = 0, double dur = 0.1,
            double vol = 0.2, double attack = 0.002, bool shape = false, bool brown = false)
        {
            if (double.IsNaN(f1)) f1 = f0;
            double peak = vol * trim, last = 0;
            var region = ReserveRegion(at, dur);
            var filter = new Biquad(type, Math.Max(MinFreq, f0 * k), q);
            for (int i = 0; i < region.Length; i++)
            {
                double x = rng.NextDouble() * 2 - 1;
                if (brown) { last = (last + BrownLeak * x) / (1 + BrownLeak); x = last * BrownGain; }
                if (i % FilterBlock == 0 && f0 != f1) filter.Retune(Math.Max(MinFreq, Sweep(f0, f1, i, dur) * k));
                double s = filter.Process(x);
                if (shape) s = Saturate((float)(s * NoiseDriveGain));
                region[i] += (float)(s * Envelope(i, attack, dur, peak));
            }
        }

        /// <summary>A slow swell on everything so far: silence, to the peak at <paramref name="peakAt"/>, to silence at <paramref name="dur"/>.</summary>
        public void Swell(double peak, double peakAt, double dur)
        {
            for (int i = 0; i < _len; i++)
            {
                double t = (double)i / SampleRate;
                double g = t < peakAt ? peak * t / peakAt : t < dur ? peak * (1 - (t - peakAt) / (dur - peakAt)) : 0;
                _buf[i] *= (float)g;
            }
        }
    }

    // band-limits the edge of a saw or square so a high note does not alias
    private static double PolyBlep(double t, double dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
        return 0;
    }

    /// <summary>An RBJ biquad, the filter Web Audio's BiquadFilterNode uses. Low and high pass take Q in dB, band pass in linear.</summary>
    private sealed class Biquad
    {
        private readonly Filter _type;
        private readonly double _q;
        private double _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        public Biquad(Filter type, double freq, double q)
        {
            _type = type;
            _q = type == Filter.BandPass ? Math.Max(0.01, q) : Math.Pow(10, q / 20);
            Retune(freq);
        }

        public void Retune(double freq)
        {
            double w = 2 * Math.PI * Math.Min(freq, SampleRate * MaxCutoffFraction) / SampleRate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * _q);
            double b0, b1, b2;
            switch (_type)
            {
                case Filter.LowPass: b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0; break;
                case Filter.HighPass: b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = b0; break;
                default: b0 = alpha; b1 = 0; b2 = -alpha; break;
            }
            double a0 = 1 + alpha;
            _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0; _a1 = -2 * cos / a0; _a2 = (1 - alpha) / a0;
        }

        public double Process(double x)
        {
            double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return y;
        }
    }
}
