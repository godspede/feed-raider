using System.Diagnostics;
using FeedRaider.Engine.Raider;
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
/// The sound of the terminal game. It plays on the machine the process runs on (over SSH, the remote machine). Audio must never
/// cost the game anything: <see cref="TryStart"/> returns null when there is no device, no player for this OS or nothing to
/// play, and every call after that swallows its own failures, so the game runs silent and nothing reaches the screen.
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
    private readonly CancellationTokenSource _stop = new();
    private readonly IWavePlayer? _device;
    private readonly WaveFileWriter? _wav;
    private readonly StreamWriter? _cueLog;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Thread _pump;
    private readonly Thread? _dump;

    private AudioEngine(AudioMixer mixer, AudioOptions options, IWavePlayer? device, WaveFileWriter? wav, StreamWriter? cueLog)
    {
        _mixer = mixer; _options = options; _device = device; _wav = wav; _cueLog = cueLog;
        _pump = new Thread(PumpLoop) { IsBackground = true, Name = "audio-pump" };
        _pump.Start();
        if (wav is not null)
        {
            _dump = new Thread(DumpLoop) { IsBackground = true, Name = "audio-dump" };
            _dump.Start();
        }
    }

    public bool Muted => _mixer.Muted;

    /// <summary>Starts the sound, or returns null (silent) when anything about it fails.</summary>
    public static AudioEngine? TryStart(AudioOptions options)
    {
        try
        {
            if (!options.Sfx && !options.Music) return null;
            if (options.DumpWav is null && !OperatingSystem.IsWindows()) return null;
            if (options.Music) _musicDir = FindMusicDir();
            var rotation = options.Music ? LoadRotation(_musicDir) : null;
            var mixer = new AudioMixer(rotation, options.Music ? OpenTrack : null, new Random());
            if (options.DumpWav is { } path)
            {
                var wav = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SfxSynth.SampleRate, AudioMixer.Channels));
                return new AudioEngine(mixer, options, null, wav, new StreamWriter(path + ".cues.txt"));
            }
            var provider = new MixerProvider(mixer);
            var device = new WaveOutEvent { DesiredLatency = DeviceLatencyMs };
            device.Init(provider.ToWaveProvider());
            device.Play();
            return new AudioEngine(mixer, options, device, null, null);
        }
        catch (Exception) { return null; }   // no device, no codec, no folder: the game runs silent
    }

    /// <summary>The music cue from the game's <c>MusicDirector</c>.</summary>
    public void Cue(string cue)
    {
        try
        {
            if (!_options.Music) return;
            _mixer.Cue(cue);
            Log("cue", cue);
        }
        catch (Exception) { /* audio is best effort */ }
    }

    /// <summary>The effect from the game's <c>SfxBus</c>.</summary>
    public void Sfx(string name, double pan, int step, double size)
    {
        try
        {
            if (!_options.Sfx) return;
            _mixer.Play(name, pan, step, size);
            Log("sfx", name);
        }
        catch (Exception) { /* audio is best effort */ }
    }

    /// <summary>M: mutes or unmutes everything. Returns the new state.</summary>
    public bool ToggleMute()
    {
        try { return _mixer.Muted = !_mixer.Muted; }
        catch (Exception) { return false; }   // audio is best effort: report "not muted" rather than fail the key press
    }

    public void Dispose()
    {
        try
        {
            _stop.Cancel();
            _device?.Dispose();
            _pump.Join(ShutdownJoinMs);
            _dump?.Join(ShutdownJoinMs);
            _wav?.Dispose();
            _cueLog?.Dispose();
        }
        catch (Exception) { /* shutting down */ }
    }

    private void Log(string kind, string name)
    {
        if (_cueLog is null) return;
        lock (_cueLog) _cueLog.WriteLine($"{_clock.Elapsed.TotalSeconds:F3} {kind} {name}");
    }

    private void PumpLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { _mixer.Pump(); } catch (Exception) { /* a bad track leaves the music as it was */ }
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

    private static string? _musicDir;

    private static MusicRotation? LoadRotation(string? dir)
    {
        if (dir is null) return null;
        var tracks = MusicRotation.ParseManifest(File.ReadAllText(Path.Combine(dir, ManifestName)));
        return tracks.Count == 0 ? null : new MusicRotation(tracks, new Random());
    }

    private static IMusicStream? OpenTrack(string file)
    {
        try { return _musicDir is null ? null : new Mp3MusicStream(Path.Combine(_musicDir, file)); }
        catch (Exception) { return null; }   // a missing or undecodable file: that track is skipped, the game plays on
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
