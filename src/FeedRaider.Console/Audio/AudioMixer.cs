using FeedRaider.Engine.Raider;

namespace FeedRaider.Console.Audio;

/// <summary>A decoded music track read as interleaved stereo floats at <see cref="SfxSynth.SampleRate"/>.</summary>
public interface IMusicStream : IDisposable
{
    /// <summary>The length in frames, or 0 when not known.</summary>
    long LengthFrames { get; }
    /// <summary>Fills up to <paramref name="count"/> floats and returns how many it wrote; 0 is the end of the track.</summary>
    int Read(float[] buffer, int offset, int count);
    /// <summary>Starts the track again, for a track that loops.</summary>
    void Rewind();
}

/// <summary>
/// The terminal's whole audio path as a pure sample source: the music (one track at a time, crossfaded on a cue change, the
/// gameplay rotation handing over before a track ends), the effects (the page's voice limit, per-sound gap and music duck) and
/// the master volume and mute, all mixed into interleaved stereo. A device or a WAV writer pulls <see cref="Read"/>. It does
/// no I/O on that path: opening a track is <see cref="Pump"/>'s job, run on another thread, so a slow disk never stalls the
/// output. The clock is the number of frames read, so tests and the WAV dump are deterministic.
/// </summary>
public sealed class AudioMixer(MusicRotation? rotation, Func<string, IMusicStream?>? open, Random rng)
{
    public const int Channels = 2;
    /// <summary>The master gain at start, the page's default volume.</summary>
    public const float DefaultVolume = 0.5f;
    public const int MaxVoices = 6;
    private const int StealFadeMs = 12;
    private const int MusicFadeMs = 1500, MusicCrossfadeMs = 2000;
    private const double DuckDb = -6;
    private const int DuckInMs = 40, DuckOutMs = 400;
    private const float SfxBusLevel = 0.8f;
    private const float MasterSmoothing = 0.002f;     // per-frame step toward the master target, so a mute does not click
    private const int Block = 64;                      // frames between envelope updates

    /// <summary>The ms of the page's per-sound gap, so a caller that does not go through the game's bus cannot stack a sound.</summary>
    private static readonly Dictionary<string, int> GapMs = new()
    {
        ["shot"] = 60,
        ["hit"] = 45,
        ["kill"] = 40,
        ["hurt"] = 90,
        ["like"] = 80,
        ["nolike"] = 150,
        ["bomb"] = 150,
        ["comm"] = 250,
        ["clear"] = 400,
        ["darkness"] = 1500,
        ["pickup_shield"] = 100,
        ["pickup_bomb"] = 100,
        ["pickup_power"] = 100,
        ["shield_hit"] = 90,
    };

    /// <summary>The sounds that dip the music under them, with the hold in ms.</summary>
    private static readonly Dictionary<string, int> DuckHoldMs = new() { ["bomb"] = 250, ["boom"] = 500, ["death"] = 400 };

    private sealed class Voice(float[] pcm, float left, float right)
    {
        public readonly float[] Pcm = pcm;
        public readonly float Left = left, Right = right;
        public int Pos, FadeLeft = -1;      // FadeLeft >= 0: being stolen, frames of fade-out left
    }

    private sealed class Slot(IMusicStream stream, bool loop, bool rotationTrack)
    {
        public readonly IMusicStream Stream = stream;
        public readonly bool Loop = loop, RotationTrack = rotationTrack;
        public float Gain, Target = 1, Step;
        public long Frames;
        public bool Ended, HandedOver;
    }

    private readonly object _gate = new();
    private readonly List<Voice> _voices = [];
    private readonly List<Slot> _slots = [];
    private readonly Dictionary<string, long> _lastAt = [];
    private long _clock;                 // frames mixed so far
    private long _duckFrom = long.MinValue / 2;
    private int _duckHold;
    private float _master = DefaultVolume, _masterNow = DefaultVolume;
    private bool _muted;
    private string? _cue;
    private (string Cue, bool AfterBoss)? _pendingCue;
    private bool _pendingNext;

    public float Volume { get { lock (_gate) return _master; } set { lock (_gate) _master = Math.Clamp(value, 0, 1); } }
    public bool Muted { get { lock (_gate) return _muted; } set { lock (_gate) _muted = value; } }
    public int VoiceCount { get { lock (_gate) return _voices.Count; } }
    public string? CurrentCue { get { lock (_gate) return _cue; } }

    /// <summary>A music cue from the game. Cheap: it only notes the request; <see cref="Pump"/> opens the track.</summary>
    public void Cue(string cue)
    {
        lock (_gate)
        {
            if (cue == _cue) return;
            _pendingCue = (cue, _cue == MusicCues.Boss && cue == MusicCues.Gameplay);
            _cue = cue;
        }
    }

    /// <summary>An effect from the game. Renders the samples on the caller's thread (a few ms at most) and queues them.</summary>
    public void Play(string name, double pan, int step, double size)
    {
        lock (_gate)
        {
            if (GapMs.TryGetValue(name, out int gap) && _lastAt.TryGetValue(name, out long prev) && _clock - prev < gap * SfxSynth.SampleRate / 1000) return;
            _lastAt[name] = _clock;
        }
        float[] pcm = SfxSynth.Render(name, step, size, rng);
        if (pcm.Length == 0) return;
        double angle = (Math.Clamp(double.IsFinite(pan) ? pan : 0, -1, 1) + 1) * Math.PI / 4;   // equal power, like a StereoPanner
        var voice = new Voice(pcm, (float)Math.Cos(angle), (float)Math.Sin(angle));
        lock (_gate)
        {
            int live = 0;
            foreach (var v in _voices) if (v.FadeLeft < 0) live++;
            foreach (var v in _voices)
            {
                if (live < MaxVoices) break;
                if (v.FadeLeft >= 0) continue;
                v.FadeLeft = StealFadeMs * SfxSynth.SampleRate / 1000;
                live--;
            }
            _voices.Add(voice);
            if (DuckHoldMs.TryGetValue(name, out int hold)) { _duckFrom = _clock; _duckHold = hold; }
        }
    }

    /// <summary>Opens the track a cue or a track ending asked for and fades it in. Run it on a worker, off the audio thread.</summary>
    public void Pump()
    {
        string? cue = null; bool afterBoss = false, next = false;
        lock (_gate)
        {
            if (_pendingCue is { } p) { cue = p.Cue; afterBoss = p.AfterBoss; _pendingCue = null; _pendingNext = false; }
            else if (_pendingNext) { next = true; _pendingNext = false; }
        }
        if (rotation is null || open is null || (cue is null && !next)) return;
        MusicTrack? track;
        lock (_gate) track = next ? rotation.Next() : rotation.ForCue(cue!, afterBoss);
        if (track is null) return;
        var stream = open(track.File);
        if (stream is null) return;
        bool rot = track.Role == MusicCues.Gameplay && rotation.Plays(MusicCues.Gameplay);
        int fadeMs = next ? MusicCrossfadeMs : MusicFadeMs;
        var slot = new Slot(stream, loop: !rot, rotationTrack: rot) { Gain = 0, Step = 1f / (fadeMs * SfxSynth.SampleRate / 1000) };
        lock (_gate)
        {
            foreach (var s in _slots) { s.Target = 0; s.Step = slot.Step; }
            _slots.Add(slot);
        }
    }

    /// <summary>Mixes <paramref name="count"/> floats (whole stereo frames) into the buffer; always fills all of them.</summary>
    public int Read(float[] buffer, int offset, int count)
    {
        count -= count % Channels;
        for (int done = 0; done < count;)
        {
            int n = Math.Min(Block * Channels, count - done);
            lock (_gate) MixBlock(buffer, offset + done, n / Channels);
            done += n;
        }
        return count;
    }

    private void MixBlock(float[] buffer, int offset, int frames)
    {
        int n = frames * Channels;
        Array.Clear(buffer, offset, n);
        var tmp = Scratch(frames);
        for (int si = _slots.Count - 1; si >= 0; si--)
        {
            var s = _slots[si];
            int got = 0;
            if (!s.Ended)
            {
                got = s.Stream.Read(tmp, 0, n);
                if (got == 0 && s.Loop) { s.Stream.Rewind(); got = s.Stream.Read(tmp, 0, n); }
                if (got == 0) s.Ended = true;
            }
            float g = s.Gain;
            for (int i = 0; i < got; i += Channels)
            {
                buffer[offset + i] += tmp[i] * g;
                buffer[offset + i + 1] += tmp[i + 1] * g;
            }
            s.Frames += got / Channels;
            s.Gain = s.Target > s.Gain ? Math.Min(s.Target, s.Gain + s.Step * frames) : Math.Max(s.Target, s.Gain - s.Step * frames);
            // a rotation track hands over to the next one before it ends, and once it has run out
            if (s.RotationTrack && s.Target > 0 && !s.HandedOver && _pendingCue is null)
            {
                long left = s.Stream.LengthFrames > 0 ? s.Stream.LengthFrames - s.Frames : long.MaxValue;
                if (s.Ended || left < MusicCrossfadeMs * (long)SfxSynth.SampleRate / 1000) { s.HandedOver = true; _pendingNext = true; }
            }
            if ((s.Target == 0 && s.Gain <= 0) || (s.Ended && s.HandedOver) || (s.Ended && !s.RotationTrack))
            {
                s.Stream.Dispose();
                _slots.RemoveAt(si);
            }
        }

        float duck = 1;
        double sinceMs = (double)(_clock - _duckFrom) * 1000 / SfxSynth.SampleRate;
        if (sinceMs >= 0)
        {
            float low = (float)Math.Pow(10, DuckDb / 20);
            if (sinceMs < DuckInMs) duck = 1 + (low - 1) * (float)(sinceMs / DuckInMs);
            else if (sinceMs < DuckInMs + _duckHold) duck = low;
            else if (sinceMs < DuckInMs + _duckHold + DuckOutMs) duck = low + (1 - low) * (float)((sinceMs - DuckInMs - _duckHold) / DuckOutMs);
        }
        for (int i = 0; i < n; i++) buffer[offset + i] *= duck;

        for (int vi = _voices.Count - 1; vi >= 0; vi--)
        {
            var v = _voices[vi];
            for (int f = 0; f < frames && v.Pos < v.Pcm.Length; f++, v.Pos++)
            {
                float x = v.Pcm[v.Pos] * SfxBusLevel;
                if (v.FadeLeft == 0) { v.Pos = v.Pcm.Length; break; }
                if (v.FadeLeft > 0) { x *= (float)v.FadeLeft / (StealFadeMs * SfxSynth.SampleRate / 1000); v.FadeLeft--; }
                float y = SfxSynth.Saturate(x);
                buffer[offset + f * Channels] += y * v.Left;
                buffer[offset + f * Channels + 1] += y * v.Right;
            }
            if (v.Pos >= v.Pcm.Length) _voices.RemoveAt(vi);
        }

        float target = _muted ? 0 : _master;
        for (int f = 0; f < frames; f++)
        {
            _masterNow += Math.Clamp(target - _masterNow, -MasterSmoothing, MasterSmoothing);
            for (int c = 0; c < Channels; c++)
            {
                ref float o = ref buffer[offset + f * Channels + c];
                o = Math.Clamp(o * _masterNow, -1f, 1f);
            }
        }
        _clock += frames;
    }

    private float[] _tmp = new float[Block * Channels];

    private float[] Scratch(int frames)
    {
        if (_tmp.Length < frames * Channels) _tmp = new float[frames * Channels];
        return _tmp;
    }
}
