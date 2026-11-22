using FeedRaider.Console.Audio;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

public class AudioTests
{
    private static readonly List<MusicTrack> Tracks =
    [
        new("start.mp3", MusicCues.Start), new("a.mp3", MusicCues.Gameplay), new("b.mp3", MusicCues.Gameplay),
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
        => new(new MusicRotation(Tracks, new Random(1)), f => { opened.Add(f); return MusicOpen.Opened(new FakeStream(level, frames)); }, new Random(2));

    [Fact]
    public void ATrackThatCannotBePlayedIsReportedAndLeavesTheMusicAsItWas()
    {
        var m = new AudioMixer(new MusicRotation(Tracks, new Random(1)),
            f => f == "boss.mp3" ? MusicOpen.Failed("boss.mp3 is unreadable") : MusicOpen.Opened(new FakeStream(0.5f, SfxSynth.SampleRate * 10)), new Random(2));
        m.Cue(MusicCues.Gameplay);
        Assert.Null(m.Pump());
        Mix(m, 2);
        m.Cue(MusicCues.Boss);
        Assert.Equal("boss.mp3 is unreadable", m.Pump());
        Assert.True(Peak(Mix(m, 0.5)) > 0);                                       // the gameplay track plays on
    }

    [Fact]
    public void ManifestKeepsKnownRolesOnly()
    {
        var t = MusicRotation.ParseManifest("""[{"file":"a.mp3","role":"start"},{"file":"b.mp3","role":"nope"},{"role":"boss"},5]""");
        Assert.Equal([new MusicTrack("a.mp3", "start")], t);
    }

    [Fact]
    public void RepoManifestParsesAndEveryFileExists()
    {
        string dir = RepoMusicDir();
        var t = MusicRotation.ParseManifest(File.ReadAllText(Path.Combine(dir, "tracks.json")));
        Assert.Contains(t, x => x.Role == MusicCues.Boss);
        Assert.All(t, x => Assert.True(File.Exists(Path.Combine(dir, x.File))));
    }

    [Fact]
    public void CuesPickTheirRoleAndGameplayRotatesWithoutRepeating()
    {
        var r = new MusicRotation(Tracks, new Random(7));
        Assert.Equal("start.mp3", r.ForCue(MusicCues.Start, false)!.File);
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
        d.Update(new MusicSnapshot(null)); m.Pump();
        d.Update(new MusicSnapshot(null)); m.Pump();
        d.Update(new MusicSnapshot("gunship")); m.Pump();
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
        Assert.Throws<ArgumentException>(() => SfxSynth.Render("nope", 0, 0, new Random(1)));
        var constants = typeof(SfxNames).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.NotEmpty(constants);
        foreach (string name in constants) Assert.NotEmpty(SfxSynth.Render(name, 0, 0.5, new Random(1)));
    }

    [Fact]
    public void SynthIsDeterministicForASeedAndComboRaisesPitch()
    {
        Assert.Equal(SfxSynth.Render("kill", 0, 0.3, new Random(5)), SfxSynth.Render("kill", 0, 0.3, new Random(5)));
        static int Crossings(float[] s) { int c = 0; for (int i = 1; i < s.Length; i++) if ((s[i - 1] < 0) != (s[i] < 0)) c++; return c; }
        var low = SfxSynth.Render("hit", 0, 0, new Random(5));
        var high = SfxSynth.Render("hit", SfxBus.MaxStep, 0, new Random(5));
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
        m.Play(SfxNames.Boom, 1, 0, 1);
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
        m.Play(SfxNames.Shot, 0, 0, 0);
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
        foreach (string n in new[] { SfxNames.Boom, SfxNames.Bomb, SfxNames.Death, SfxNames.Darkness, SfxNames.Hurt, SfxNames.Kill, SfxNames.Clear, SfxNames.Like, SfxNames.SpeechBox, SfxNames.NoLike }) m.Play(n, 0, 0, 0);
        Mix(m, 0.05);   // the stolen ones finish their short fade
        Assert.Equal(6, m.VoiceCount);   // the literal cap, not the constant: raising MaxVoices must fail this
    }

    [Fact]
    public void ARepeatInsideTheGapIsDropped()
    {
        var m = new AudioMixer(null, null, new Random(1));
        m.Play(SfxNames.Darkness, 0, 0, 0);
        m.Play(SfxNames.Darkness, 0, 0, 0);
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
    public void EverySfxNameIsASynthName()
    {
        string[] bus = [SfxNames.Shot, SfxNames.Hit, SfxNames.Kill, SfxNames.Boom, SfxNames.Hurt, SfxNames.Death, SfxNames.Bomb, SfxNames.SpeechBox,
            SfxNames.Like, SfxNames.NoLike, SfxNames.Darkness, SfxNames.Clear, SfxNames.PickupShield, SfxNames.PickupBomb, SfxNames.PickupWeapon, SfxNames.ShieldHit,
            SfxNames.Warn, SfxNames.Ping, SfxNames.Open, SfxNames.Shut, SfxNames.Charge, SfxNames.Beam, SfxNames.Lob, SfxNames.Blast, SfxNames.Hatch,
            SfxNames.Tick, SfxNames.Part, SfxNames.BossDown, SfxNames.Sting, SfxNames.Repost, SfxNames.Undo, SfxNames.Burst, SfxNames.BurstEnd,
            SfxNames.ShotHeavy, SfxNames.ShotTwin, SfxNames.ShotMissile, SfxNames.ShotTwinHeavy, SfxNames.ShotSpread, SfxNames.ShotMachineGun,
            SfxNames.ShotGatling, SfxNames.ShotSwarm, SfxNames.ShotStorm];
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

    // ---------------------------------------------------------------- a track for every boss

    private const string KeyedManifest = """
        [{"file":"start.mp3","role":"start"},{"file":"a.mp3","role":"gameplay"},{"file":"b.mp3","role":"gameplay"},
         {"file":"shared.mp3","role":"boss"},{"file":"bulwark.mp3","role":"boss","boss":"bulwark"},
         {"file":"thread.mp3","role":"boss","boss":"thread"},{"file":"over.mp3","role":"gameover"}]
        """;

    [Fact]
    public void TheManifestParsesWithOrWithoutBossFieldsAndAWrongOneIsDropped()
    {
        var t = MusicRotation.ParseManifest(KeyedManifest);
        Assert.Equal([null, null, null, null, "bulwark", "thread", null], t.Select(x => x.Boss));
        var plain = MusicRotation.ParseManifest("""[{"file":"a.mp3","role":"boss"},{"file":"b.mp3","role":"boss","boss":5},{"file":"c.mp3","role":"boss","boss":""}]""");
        Assert.Equal([new MusicTrack("a.mp3", "boss"), new MusicTrack("b.mp3", "boss"), new MusicTrack("c.mp3", "boss")], plain);
    }

    private static string RepoMusicDir() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FeedRaider.Web", "wwwroot", "music"));

    [Fact]
    public void TheRepoManifestKeepsBossFieldsToBossTracksAndListsEveryTrackFileOnce()
    {
        string dir = RepoMusicDir();
        var t = MusicRotation.ParseManifest(File.ReadAllText(Path.Combine(dir, "tracks.json")));
        Assert.Contains(t, x => x.Role == MusicCues.Boss && x.Boss is null);       // the shared track
        Assert.All(t, x => Assert.True(x.Boss is null || x.Role == MusicCues.Boss));
        Assert.Equal(t.Count, t.Select(x => x.File).Distinct().Count());
        var onDisk = Directory.GetFiles(dir, "fr-*.mp3").Select(f => Path.GetFileName(f)!).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(onDisk, t.Select(x => x.File).OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void TheMusicPromptsPageListsEveryShippedTrackInItsTable()
    {
        string root = Path.GetFullPath(Path.Combine(RepoMusicDir(), "..", "..", "..", ".."));
        var t = MusicRotation.ParseManifest(File.ReadAllText(Path.Combine(RepoMusicDir(), "tracks.json")));
        string page = File.ReadAllText(Path.Combine(root, "docs", "music", "prompts.md"));
        var rows = page.Split('\n').Where(l => l.StartsWith("| `fr-", StringComparison.Ordinal)).ToList();
        Assert.All(t, x => Assert.Contains(rows, r => r.StartsWith("| `" + x.File + "` |", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheRepoGameplayRotationHoldsFifteenTracksAndNamesFollowTheShippedPattern()
    {
        var t = MusicRotation.ParseManifest(File.ReadAllText(Path.Combine(RepoMusicDir(), "tracks.json")));
        var play = t.Where(x => x.Role == MusicCues.Gameplay).Select(x => x.File).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(15, play.Count);
        Assert.All(play, f => Assert.Matches(@"^fr-play-[a-z]+(-[a-z]+)*(-[12])?\.mp3$", f));
        Assert.Contains("fr-play-goa-trance.mp3", play);
        Assert.Contains("fr-play-acid-trance.mp3", play);
        Assert.Contains("fr-play-hard-trance-1.mp3", play);
        Assert.Contains("fr-play-hard-trance-2.mp3", play);
        Assert.Contains("fr-play-uplifting-psytrance.mp3", play);
    }

    [Theory]
    [InlineData("gunship", new[] { "fr-boss.mp3" })]
    [InlineData("bulwark", new[] { "fr-boss-bulwark.mp3" })]
    [InlineData("shutter", new[] { "fr-boss-shutter-1.mp3", "fr-boss-shutter-2.mp3", "fr-boss-shutter-3.mp3" })]
    [InlineData("thread", new[] { "fr-boss.mp3" })]
    [InlineData("spotlight", new[] { "fr-boss.mp3" })]
    [InlineData("brood", new[] { "fr-boss.mp3" })]
    [InlineData("schism", new[] { "fr-boss-schism-1.mp3", "fr-boss-schism-2.mp3" })]
    [InlineData("pileon", new[] { "fr-boss.mp3" })]
    [InlineData("mirror", new[] { "fr-boss.mp3" })]
    [InlineData("doomscroll", new[] { "fr-boss.mp3" })]
    public void TheRepoManifestPlaysEachBossItsOwnThemeOrTheSharedTrack(string boss, string[] expected)
    {
        var t = MusicRotation.ParseManifest(File.ReadAllText(Path.Combine(RepoMusicDir(), "tracks.json")));
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < 200; seed++) seen.Add(new MusicRotation(t, new Random(seed)).ForCue(MusicCues.ForBoss(boss), false)!.File);
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen);                // every kept theme can play, no other track does
    }

    [Fact]
    public void ABossCuePicksTheKeyedTrackThenTheSharedTrackThenAnyBossTrackAndNeverGameplay()
    {
        var all = MusicRotation.ParseManifest(KeyedManifest);
        var r = new MusicRotation(all, new Random(1));
        Assert.Equal("bulwark.mp3", r.ForCue(MusicCues.ForBoss("bulwark"), false)!.File);
        Assert.Equal("thread.mp3", r.ForCue(MusicCues.ForBoss("thread"), false)!.File);
        Assert.Equal("shared.mp3", r.ForCue(MusicCues.ForBoss("gunship"), false)!.File);          // no keyed track: the shared track
        Assert.Equal("shared.mp3", r.ForCue(MusicCues.ForBoss("doomscroll"), false)!.File);

        var noShared = new MusicRotation(all.Where(x => x.File != "shared.mp3").ToList(), new Random(1));
        Assert.Contains(noShared.ForCue(MusicCues.ForBoss("gunship"), false)!.File, new[] { "bulwark.mp3", "thread.mp3" });   // neither: any boss track
        Assert.Equal("thread.mp3", noShared.ForCue(MusicCues.ForBoss("thread"), false)!.File);

        var noBoss = new MusicRotation(all.Where(x => x.Role != MusicCues.Boss).ToList(), new Random(1));
        Assert.Null(noBoss.ForCue(MusicCues.ForBoss("bulwark"), false));                         // never a gameplay track
        for (int seed = 0; seed < 20; seed++)
        {
            var any = new MusicRotation(all, new Random(seed));
            Assert.Equal(MusicCues.Boss, any.ForCue(MusicCues.ForBoss("mirror"), false)!.Role);
        }
    }

    [Fact]
    public void ABossCueWithSeveralSharedTracksPicksOnlyAmongTheSharedOnes()
    {
        var t = new List<MusicTrack> { new("h1.mp3", "boss"), new("h2.mp3", "boss"), new("k.mp3", "boss", "bulwark"), new("g.mp3", "gameplay") };
        for (int seed = 0; seed < 20; seed++)
            Assert.Contains(new MusicRotation(t, new Random(seed)).ForCue(MusicCues.ForBoss("gunship"), false)!.File, new[] { "h1.mp3", "h2.mp3" });
    }

    [Fact]
    public void AfterABossCueTheGameplayRotationGoesOnWhereItWas()
    {
        var all = MusicRotation.ParseManifest(KeyedManifest);
        var r = new MusicRotation(all, new Random(4));
        var first = r.ForCue(MusicCues.Gameplay, false)!;
        Assert.Equal("bulwark.mp3", r.ForCue(MusicCues.ForBoss("bulwark"), false)!.File);
        var back = r.ForCue(MusicCues.Gameplay, true)!;
        Assert.NotEqual(first, back);                                              // the other gameplay track of two, not a restart
        Assert.Equal(MusicCues.Gameplay, back.Role);
    }

    [Fact]
    public void TheMixerTreatsABossKeyCueAsTheBossRoleAndIgnoresTheHint()
    {
        var opened = new List<string>();
        var m = new AudioMixer(new MusicRotation(MusicRotation.ParseManifest(KeyedManifest), new Random(1)),
            f => { opened.Add(f); return MusicOpen.Opened(new FakeStream(0.5f, SfxSynth.SampleRate * 10)); }, new Random(2));
        m.Cue(MusicCues.Gameplay); m.Pump();
        string rotation = opened[0];
        m.Cue(MusicCues.AheadPrefix + "thread"); m.Pump();
        Assert.Single(opened);                                                     // fetched nowhere, played nowhere
        Assert.Equal(MusicCues.Gameplay, m.CurrentCue);
        m.Cue(MusicCues.ForBoss("thread")); m.Pump();
        Assert.Equal(["thread.mp3"], opened.Skip(1));
        m.Cue(MusicCues.Gameplay); m.Pump();
        Assert.Equal(3, opened.Count);
        Assert.NotEqual(rotation, opened[2]);                                      // goes on to the next, not the same track again
    }
}
