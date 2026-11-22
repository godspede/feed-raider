using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FeedRaider.Engine.Raider;
using NAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FeedRaider.Console.Audio;

/// <summary>What the command line asks of the sound.</summary>
/// <param name="Sfx">The effects (<c>--sfx off</c> turns them off).</param>
/// <param name="Music">The music (<c>--music off</c> turns it off).</param>
/// <param name="DumpWav">A test mode: write the mixer's output to this WAV (and the cues to <c>.cues.txt</c> beside it) in real
/// time instead of playing on a device, so the sound can be checked without ears.</param>
public sealed record AudioOptions(bool Sfx = true, bool Music = true, string? DumpWav = null);

/// <summary>
/// The sound of the terminal game. It plays on the machine the process runs on (over SSH, the remote machine). The game plays
/// on without it: <see cref="TryStart"/> returns null when there is no device or no player for this OS, and plays the effects
/// alone when the music is missing or cannot be read; each of those is told to the player once, in the game's message line.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const string MusicDirName = "music", ManifestName = "tracks.json";
    private const int DeviceLatencyMs = 100;
    private const int PumpIntervalMs = 100;
    private const int ShutdownJoinMs = PumpIntervalMs * 3;   // how long Dispose waits for a worker to see the stop flag
    private const int DumpChunkFrames = SfxSynth.SampleRate / 100;

    private readonly AudioMixer _mixer;
    private readonly AudioOptions _options;
    private readonly Action<string> _tellMusic;
    private readonly CancellationTokenSource _stop = new();
    private readonly IWavePlayer? _device;
    private readonly WaveFileWriter? _wav;
    private readonly StreamWriter? _cueLog;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Thread _pump;
    private readonly Thread? _dump;

    private AudioEngine(AudioMixer mixer, AudioOptions options, Action<string> tellMusic, IWavePlayer? device, WaveFileWriter? wav, StreamWriter? cueLog)
    {
        _mixer = mixer; _options = options; _tellMusic = tellMusic; _device = device; _wav = wav; _cueLog = cueLog;
        _pump = new Thread(PumpLoop) { IsBackground = true, Name = "audio-pump" };
        _pump.Start();
        if (wav is not null)
        {
            _dump = new Thread(DumpLoop) { IsBackground = true, Name = "audio-dump" };
            _dump.Start();
        }
    }

    public bool Muted => _mixer.Muted;

    /// <summary>Starts the sound, or returns null (silent) when the options turn it all off or there is nothing to play it on.
    /// <paramref name="tell"/> shows the player a line: once when the sound cannot start, once for the first music failure.</summary>
    public static AudioEngine? TryStart(AudioOptions options, Action<string> tell)
    {
        if (!options.Sfx && !options.Music) return null;
        if (options.DumpWav is null && !OperatingSystem.IsWindows())
        {
            tell(SoundTrouble("the terminal game plays sound only on Windows"));
            return null;
        }
        int told = 0;
        void TellMusic(string what) { if (Interlocked.Exchange(ref told, 1) == 0) tell(MusicTrouble(what)); }
        var (rotation, open) = options.Music ? LoadMusic(TellMusic) : (null, null);
        var mixer = new AudioMixer(rotation, open, new Random());
        if (options.DumpWav is { } path)
        {
            var wav = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SfxSynth.SampleRate, AudioMixer.Channels));
            return new AudioEngine(mixer, options, TellMusic, null, wav, new StreamWriter(path + ".cues.txt"));
        }
        var provider = new MixerProvider(mixer);
        var device = new WaveOutEvent { DesiredLatency = DeviceLatencyMs };
        try
        {
            device.Init(provider.ToWaveProvider());
            device.Play();
        }
        catch (MmException e)   // the machine has no audio output device, or none that will open
        {
            device.Dispose();
            tell(SoundTrouble($"no audio device would play ({e.Message})"));
            return null;
        }
        return new AudioEngine(mixer, options, TellMusic, device, null, null);
    }

    /// <summary>The music cue from the game's <c>MusicDirector</c>.</summary>
    public void Cue(string cue)
    {
        if (!_options.Music) return;
        _mixer.Cue(cue);
        Log("cue", cue);
    }

    /// <summary>The effect from the game's <c>SfxBus</c>.</summary>
    public void Sfx(string name, double pan, int step, double size)
    {
        if (!_options.Sfx) return;
        _mixer.Play(name, pan, step, size);
        Log("sfx", name);
    }

    /// <summary>M: mutes or unmutes everything. Returns the new state.</summary>
    public bool ToggleMute() => _mixer.Muted = !_mixer.Muted;

    public void Dispose()
    {
        _stop.Cancel();
        _device?.Dispose();
        _pump.Join(ShutdownJoinMs);
        _dump?.Join(ShutdownJoinMs);
        _wav?.Dispose();
        _cueLog?.Dispose();
    }

    private static string SoundTrouble(string what) => $"Sound: {what}. The game plays on without it.";
    private static string MusicTrouble(string what) => $"Music: {what}. The game plays on without it.";

    private void Log(string kind, string name)
    {
        if (_cueLog is null) return;
        lock (_cueLog) _cueLog.WriteLine($"{_clock.Elapsed.TotalSeconds:F3} {kind} {name}");
    }

    private void PumpLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            if (_mixer.Pump() is { } failure) _tellMusic(failure);
            _stop.Token.WaitHandle.WaitOne(PumpIntervalMs);
        }
    }

    // Pulls the mixer at the pace a device would, so cues and music land in the file where a listener would hear them.
    private void DumpLoop()
    {
        var buf = new float[DumpChunkFrames * AudioMixer.Channels];
        long frames = 0;
        var t = Stopwatch.StartNew();
        while (!_stop.IsCancellationRequested)
        {
            double due = (double)frames / SfxSynth.SampleRate;
            if (t.Elapsed.TotalSeconds < due) { Thread.Sleep(1); continue; }
            _mixer.Read(buf, 0, buf.Length);
            _wav!.WriteSamples(buf, 0, buf.Length);
            frames += DumpChunkFrames;
        }
    }

    // The music folder is copied beside the exe; the repo copy serves a run that was not built here.
    private static string? FindMusicDir()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, MusicDirName);
        if (File.Exists(Path.Combine(beside, ManifestName))) return beside;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string repo = Path.Combine(d.FullName, "src", "FeedRaider.Web", "wwwroot", MusicDirName);
            if (File.Exists(Path.Combine(repo, ManifestName))) return repo;
        }
        return null;
    }

    // The music is optional: each way it can be missing is told, and the effects play without it.
    private static (MusicRotation?, Func<string, MusicOpen>?) LoadMusic(Action<string> tellMusic)
    {
        if (!OperatingSystem.IsWindows())
        {
            tellMusic("the terminal game decodes its tracks only on Windows");
            return (null, null);
        }
        if (FindMusicDir() is not { } dir)
        {
            tellMusic($"no {MusicDirName} folder with a {ManifestName} beside the game");
            return (null, null);
        }
        string manifest = Path.Combine(dir, ManifestName);
        List<MusicTrack> tracks;
        try { tracks = MusicRotation.ParseManifest(File.ReadAllText(manifest)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            tellMusic($"{manifest} could not be read ({e.Message})");
            return (null, null);
        }
        if (tracks.Count == 0)
        {
            tellMusic($"{manifest} lists no tracks");
            return (null, null);
        }
        return (new MusicRotation(tracks, new Random()), file => OpenTrack(Path.Combine(dir, file)));
    }

    // Media Foundation reports a missing or unreadable file as an IOException or UnauthorizedAccessException, and a file it
    // cannot decode as a COMException; the mixer skips that track.
    private static MusicOpen OpenTrack(string path)
    {
        try { return MusicOpen.Opened(new Mp3MusicStream(path)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            return MusicOpen.Failed($"{Path.GetFileName(path)} could not be played ({e.Message})");
        }
    }

    private sealed class MixerProvider(AudioMixer mixer) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SfxSynth.SampleRate, AudioMixer.Channels);
        public int Read(float[] buffer, int offset, int count) => mixer.Read(buffer, offset, count);
    }

    /// <summary>An mp3 decoded by Windows' Media Foundation, as stereo at the mixer's rate.</summary>
    private sealed class Mp3MusicStream : IMusicStream
    {
        private readonly string _path;
        private MediaFoundationReader _reader = null!;
        private ISampleProvider _samples = null!;

        public Mp3MusicStream(string path) { _path = path; Open(); }

        public long LengthFrames { get; private set; }

        private void Open()
        {
            _reader = new MediaFoundationReader(_path);
            ISampleProvider p = _reader.ToSampleProvider();
            if (p.WaveFormat.Channels == 1) p = new MonoToStereoSampleProvider(p);
            if (p.WaveFormat.SampleRate != SfxSynth.SampleRate) p = new WdlResamplingSampleProvider(p, SfxSynth.SampleRate);
            _samples = p;
            LengthFrames = (long)(_reader.TotalTime.TotalSeconds * SfxSynth.SampleRate);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = _samples.Read(buffer, offset + total, count - total);
                if (n == 0) break;
                total += n;
            }
            return total;
        }

        public void Rewind() { _reader.Dispose(); Open(); }
        public void Dispose() => _reader.Dispose();
    }
}
