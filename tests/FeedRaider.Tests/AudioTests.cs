using FeedRaider.Console.Audio;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class AudioTests
{
    private static readonly List<MusicTrack> Tracks =
    [
        new("start.mp3", MusicCues.Menu), new("a.mp3", MusicCues.Gameplay), new("b.mp3", MusicCues.Gameplay),
        new("c.mp3", MusicCues.Gameplay), new("boss.mp3", MusicCues.Boss), new("over.mp3", MusicCues.GameOver),
    ];

    /// <summary>A constant-level stereo track of a fixed length.</summary>
    private sealed class FakeStream(float level, long frames) : IMusicStream
    {
        private long _pos;
        public long LengthFrames => frames;
        public int Read(float[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, (frames - _pos) * 2);
            Array.Fill(buffer, level, offset, n);
            _pos += n / 2;
            return n;
        }
        public void Rewind() => _pos = 0;
        public void Dispose() { }
    }

    private static float Peak(float[] a, int from = 0, int to = -1)
    {
        float p = 0;
        for (int i = from; i < (to < 0 ? a.Length : to); i++) p = Math.Max(p, Math.Abs(a[i]));
        return p;
    }

    private static float[] Mix(AudioMixer m, double seconds)
    {
        var b = new float[(int)(seconds * SfxSynth.SampleRate) * 2];
        m.Read(b, 0, b.Length);
        return b;
    }

    private static AudioMixer MixerWith(List<string> opened, float level = 0.5f, long frames = SfxSynth.SampleRate * 10)
        => new(new MusicRotation(Tracks, new Random(1)), f => { opened.Add(f); return new FakeStream(level, frames); }, new Random(2));

    [Fact]
    public void ManifestKeepsKnownRolesOnly()
    {
        var t = MusicRotation.ParseManifest("""[{"file":"a.mp3","role":"menu"},{"file":"b.mp3","role":"nope"},{"role":"boss"},5]""");
        Assert.Equal([new MusicTrack("a.mp3", "menu")], t);
    }

    [Fact]
    public void RepoManifestParsesAndEveryFileExists()
    {
        string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FeedRaider.Web", "wwwroot", "music"));
        var t = MusicRotation.ParseManifest(File.ReadAllText(Path.Combine(dir, "tracks.json")));
        Assert.Contains(t, x => x.Role == MusicCues.Boss);
        Assert.All(t, x => Assert.True(File.Exists(Path.Combine(dir, x.File))));
    }

    [Fact]
    public void CuesPickTheirRoleAndGameplayRotatesWithoutRepeating()
    {
        var r = new MusicRotation(Tracks, new Random(7));
        Assert.Equal("start.mp3", r.ForCue(MusicCues.Menu, false)!.File);
        Assert.Equal("over.mp3", r.ForCue(MusicCues.GameOver, false)!.File);
        var seen = new List<string> { r.ForCue(MusicCues.Gameplay, false)!.File };
        for (int i = 0; i < 8; i++) seen.Add(r.Next()!.File);
        Assert.Equal(["a.mp3", "b.mp3", "c.mp3"], seen.Take(3).Order());
        for (int i = 1; i < seen.Count; i++) Assert.NotEqual(seen[i - 1], seen[i]);
    }

    [Fact]
    public void BossReturnContinuesTheRotation()
    {
        var r = new MusicRotation(Tracks, new Random(3));
        r.ForCue(MusicCues.Gameplay, false);
        var fromRotation = r.Next()!;
        Assert.Equal("boss.mp3", r.ForCue(MusicCues.Boss, false)!.File);
        var back = r.ForCue(MusicCues.Gameplay, true)!;
        Assert.Equal(MusicCues.Gameplay, back.Role);
        Assert.NotEqual(fromRotation.File, back.File);
    }

    [Fact]
    public void DirectorCuesReachTheMixerOncePerChange()
    {
        var opened = new List<string>();
        var m = MixerWith(opened);
        var d = new MusicDirector(m.Cue);
        d.Update(new MusicSnapshot(0, false, 3, 0, 0, 0, 0)); m.Pump();
        d.Update(new MusicSnapshot(0, false, 3, 0, 0, 0, 0)); m.Pump();
        d.Update(new MusicSnapshot(0, true, 3, 0, 0, 0, 0)); m.Pump();
        d.End(); m.Pump();
        Assert.Equal(3, opened.Count);
        Assert.Equal(["boss.mp3", "over.mp3"], opened.Skip(1));
        Assert.Equal(MusicCues.GameOver, m.CurrentCue);
    }

    [Fact]
    public void EverySoundRendersAudibleBoundedAndFinite()
    {
        foreach (string name in SfxSynth.Names)
        {
            var s = SfxSynth.Render(name, 0, 0.5, new Random(1));
            Assert.True(s.Length > SfxSynth.SampleRate / 50, name);
            Assert.All(s, x => Assert.True(float.IsFinite(x)));
            Assert.InRange(Peak(s), 0.05f, 8f);
        }
        Assert.Empty(SfxSynth.Render("nope", 0, 0, new Random(1)));
    }

    [Fact]
    public void SynthIsDeterministicForASeedAndComboRaisesPitch()
    {
        Assert.Equal(SfxSynth.Render("kill", 0, 0.3, new Random(5)), SfxSynth.Render("kill", 0, 0.3, new Random(5)));
        static int Crossings(float[] s) { int c = 0; for (int i = 1; i < s.Length; i++) if ((s[i - 1] < 0) != (s[i] < 0)) c++; return c; }
        var low = SfxSynth.Render("hit", 0, 0, new Random(5));
        var high = SfxSynth.Render("hit", SfxSynth.MaxStep, 0, new Random(5));
        Assert.True(Crossings(high) > Crossings(low));
        Assert.True(SfxSynth.Render("kill", 0, 1, new Random(5)).Length > SfxSynth.Render("kill", 0, 0, new Random(5)).Length);
    }

    [Fact]
    public void WithoutMusicOnlyEffectsSoundAndTheyArePanned()
    {
        var m = new AudioMixer(null, null, new Random(1));
        Assert.Equal(0, Peak(Mix(m, 0.1)));
        m.Cue(MusicCues.Gameplay); m.Pump();
        Assert.Equal(0, Peak(Mix(m, 0.1)));
        m.Play("boom", 1, 0, 1);
        var b = Mix(m, 0.5);
        float l = 0, r = 0;
        for (int i = 0; i < b.Length; i += 2) { l = Math.Max(l, Math.Abs(b[i])); r = Math.Max(r, Math.Abs(b[i + 1])); }
        Assert.True(r > 0.05f);
        Assert.True(l < r * 0.1f);
    }

    [Fact]
    public void MusicFadesInAndMuteSilencesEverything()
    {
        var m = MixerWith([]);
        m.Cue(MusicCues.Gameplay); m.Pump();
        var b = Mix(m, 3);
        Assert.True(Peak(b, 0, 2000) < Peak(b, b.Length - 2000), "fades in");
        Assert.True(Peak(b, b.Length - 2000) > 0.1f);
        m.Muted = true;
        m.Play("shot", 0, 0, 0);
        Mix(m, 0.2);   // the master eases to zero
        Assert.Equal(0, Peak(Mix(m, 0.2)));
        m.Muted = false;
        Mix(m, 0.2);
        Assert.True(Peak(Mix(m, 0.2)) > 0.1f);
    }

    [Fact]
    public void TenSoundsAtOnceLeaveOnlySixVoices()
    {
        var m = new AudioMixer(null, null, new Random(1));
        foreach (string n in new[] { "boom", "bomb", "death", "darkness", "hurt", "kill", "clear", "like", "comm", "nolike" }) m.Play(n, 0, 0, 0);
        Mix(m, 0.05);   // the stolen ones finish their short fade
        Assert.Equal(6, m.VoiceCount);   // the literal cap, not the constant: raising MaxVoices must fail this
    }

    [Fact]
    public void ARepeatInsideTheGapIsDropped()
    {
        var m = new AudioMixer(null, null, new Random(1));
        m.Play("darkness", 0, 0, 0);
        m.Play("darkness", 0, 0, 0);
        Assert.Equal(1, m.VoiceCount);
    }

    [Fact]
    public void RotationTrackHandsOverBeforeItEnds()
    {
        var opened = new List<string>();
        var m = MixerWith(opened, 0.3f, SfxSynth.SampleRate * 5);
        m.Cue(MusicCues.Gameplay); m.Pump();
        Mix(m, 2.5);          // 5 s track, 2 s crossfade: the hand-over is due after 3 s
        m.Pump();
        Assert.Single(opened);
        Mix(m, 1);
        m.Pump();
        Assert.Equal(2, opened.Count);
        Assert.NotEqual(opened[0], opened[1]);
    }

    [Fact]
    public void ThePageCueNamesAreTheSynthNames()
    {
        string[] bus = [SfxNames.Shot, SfxNames.Hit, SfxNames.Kill, SfxNames.Boom, SfxNames.Hurt, SfxNames.Death, SfxNames.Bomb, SfxNames.Comm,
            SfxNames.Like, SfxNames.NoLike, SfxNames.Darkness, SfxNames.Clear, SfxNames.PickupShield, SfxNames.PickupBomb, SfxNames.PickupPower, SfxNames.ShieldHit];
        Assert.Equal(bus.Order(), SfxSynth.Names.Order());
    }

    [Fact]
    public void BusEmitsReachTheMixer()
    {
        var m = new AudioMixer(null, null, new Random(1));
        var bus = new SfxBus((n, p, s, z) => m.Play(n, p, s, z));
        bus.Play(SfxNames.Bomb, 0, 100);
        Assert.Equal(1, m.VoiceCount);
    }
}
