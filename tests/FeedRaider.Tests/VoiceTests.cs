using TUImation;
using FeedRaider.Engine;
using FeedRaider.Engine.Raider;
using FeedRaider.Tests.Enemies;

namespace FeedRaider.Tests;

/// <summary>An enemy whose voice every test sets: which post it takes, how it answers a cue, its own lines, its prefix, its hold.</summary>
internal sealed class VoiceBehaviour(EnemyKind kind) : EnemyBehaviour
{
    public override EnemyKind Kind => kind;
    /// <summary>The name the speaker's own lines are headed with.</summary>
    public const string SpeakerName = "TESTER";
    public override string Name => SpeakerName;
    private const int BodyWidth = 21, BodyHeight = 11;
    private const uint BodyFill = 0x445566;
    private readonly Sprite body = EnemyKit.Solid(BodyWidth, BodyHeight, BodyFill);
    public override Sprite Body => body;
    private const uint DebrisColour = 0x44ccaa;
    /// <summary>Enough that the tests' ordinary shots never kill it by chance.</summary>
    private const double SturdyHp = 1000;
    private const int KillScore = 10;
    /// <summary>Where a speaker rests unless a test sets <see cref="HoldY"/>: well down the field, clear of its top edge.</summary>
    public const double RestY = 30;
    public override uint Colour => DebrisColour;
    public override double Hp => SturdyHp;
    public override int Score => KillScore;
    public bool IsBoss;
    /// <summary>Where it comes to rest; it descends to there from above the field.</summary>
    public double HoldY = RestY;
    public override bool Boss => IsBoss;
    public override bool Speaks => true;
    public Action<RaiderGame, Enemy>? OnSpawn;
    public Func<IReadOnlyList<RudePost>, int>? OnTaste;
    public Func<VoiceCue, Turn?>? OnTurn;
    public Dictionary<VoiceCue, string> Lines = [];
    public string? PrefixText;
    public bool Hold;
    public override void Spawn(RaiderGame game, Enemy enemy) => OnSpawn?.Invoke(game, enemy);
    public override void Update(RaiderGame game, Enemy enemy) { if (enemy.Y < HoldY && !enemy.Leaving) enemy.Y = Math.Min(HoldY, enemy.Y + 1); }
    public override int Taste(Enemy enemy, IReadOnlyList<RudePost> waiting) => OnTaste?.Invoke(waiting) ?? base.Taste(enemy, waiting);
    public override Turn TurnFor(RaiderGame game, Enemy enemy, VoiceCue cue) => OnTurn?.Invoke(cue) ?? base.TurnFor(game, enemy, cue);
    public override string? Line(Enemy enemy, VoiceCue cue) => Lines.GetValueOrDefault(cue);
    public override string? Prefix(RaiderGame game, Enemy enemy) => PrefixText;
    public override bool HoldBox(RaiderGame game, Enemy enemy) => Hold;
}

/// <summary>The voice: who speaks, when, and what a turn does to the box.</summary>
public class VoiceTests
{
    private static RaiderGame NewEmptyGame(int cols = EnemyKit.BigCols, int rows = EnemyKit.BigRows, bool gates = false, ulong seed = EnemyKit.PlainSeed)
    {
        var game = new RaiderGame(seed) { SignedIn = true, BossGates = gates };
        game.SetLayout(FieldLayout.Compute(cols, rows));
        EnemyKit.Clear(game);
        return game;
    }

    private const string RudePostLead = "rude post ", RudeLabel = "rude";

    /// <summary>The header of a rude post with no label of its own, or with the label <see cref="RudeLabel"/>.</summary>
    private const string RudeHeader = "RUDE";

    /// <summary>The header of a hidden reply.</summary>
    private const string HiddenHeader = "HIDDEN REPLY";

    /// <summary>A line of the speaker's own, set by the tests that check a post comes before it.</summary>
    private const string OwnLine = "own line";

    /// <summary>The last line of a dying speaker; the gunship's own last line is the same words.</summary>
    private const string DyingLine = "Ratioed.";

    private static DarknessPost Rude(ulong seed, string? text = null, string? label = RudeLabel, DarknessSource src = DarknessSource.Label) =>
        new(DarknessKind.Rude, seed, text ?? RudeText(seed), src, label);

    /// <summary>The words of the rude post made from <paramref name="seed"/> when a test gives none.</summary>
    private static string RudeText(ulong seed) => RudePostLead + seed;

    private static void Offer(RaiderGame game, int count) { for (ulong i = 1; i <= (ulong)count; i++) game.OfferDarkness(Rude(i)); }

    private static void Use(RaiderGame game, params EnemyBehaviour[] behaviours) => game.BehaviourOverride = kind => behaviours.FirstOrDefault(behaviour => behaviour.Kind == kind);

    /// <summary>Steps the given frames and after each removes the enemy bolts and every filler-sized enemy but the gunship kind,
    /// so a lesser gunship (filler-sized) stays where EnemyKit.StepSweeping would take it.</summary>
    private static void StepWithoutBoltsOrFillers(RaiderGame game, int frames = 1)
    {
        for (int i = 0; i < frames; i++)
        {
            game.Step();
            game.Bullets.RemoveAll(b => !b.Friendly);
            game.Enemies.RemoveAll(enemy => enemy.Kind < EnemyKind.Gnat && enemy.Kind != EnemyKind.Gunship);
        }
    }

    /// <summary>Long enough for a boss to come down from above the field and open its box.</summary>
    private const int ArrivalLimitFrames = 3000;

    /// <summary>Long enough for a lesser gunship to come onto the field and take a waiting post.</summary>
    private const int LesserSpeakLimitFrames = 600;

    /// <summary>Long enough for a gated boss to come on after its warning and open its box.</summary>
    private const int GatedBossLimitFrames = 400;

    /// <summary>A few frames: enough for a speaker on the field to take a waiting post, or for its box to start typing.</summary>
    private const int SettleFrames = 5;

    /// <summary>Too few for a boss to come down to its hold, enough for a new enemy or a new box to settle.</summary>
    private const int BriefFrames = 3;

    /// <summary>Steps as <see cref="StepWithoutBoltsOrFillers"/> does until <paramref name="done"/> holds, failing after <paramref name="limit"/> frames.</summary>
    private static void StepWithoutBoltsOrFillersUntil(RaiderGame game, Func<bool> done, int limit = ArrivalLimitFrames)
    {
        for (int i = 0; i < limit && !done(); i++) StepWithoutBoltsOrFillers(game);
        Assert.True(done(), "the condition never came");
    }

    private static Enemy SpawnSpeaker(RaiderGame game, VoiceBehaviour behaviour, double xFrac = MidField, double y = VoiceBehaviour.RestY)
    {
        var enemy = game.SpawnKind(behaviour.Kind, xFrac);
        enemy.X = enemy.BaseX = game.PW * xFrac; enemy.Y = y;
        return enemy;
    }

    private static VoiceBehaviour InstallBossVoice(RaiderGame game)
    {
        var behaviour = new VoiceBehaviour(BossKind) { IsBoss = true };
        Use(game, behaviour);
        return behaviour;
    }

    /// <summary>The kind <see cref="InstallBossVoice"/> gives a voice.</summary>
    private const EnemyKind BossKind = EnemyKind.Bulwark;

    /// <summary>Halfway across the field, as a share of its width.</summary>
    private const double MidField = 0.5;

    /// <summary>Brings on the boss <see cref="InstallBossVoice"/> gives a voice, halfway across and above the field, so it has not arrived.</summary>
    private static Enemy SpawnBoss(RaiderGame game) => game.SpawnKind(BossKind, MidField);

    /// <summary>Steps one frame as <see cref="StepWithoutBoltsOrFillers"/> does so the box wraps its text, then reads it to the last page.</summary>
    private static void ReadOut(RaiderGame game, Enemy enemy)
    {
        StepWithoutBoltsOrFillers(game);
        enemy.SpeechBoxAge = EnemySpeechBox.Frames(enemy);
    }

    private static string Squash(IEnumerable<string> lines) => string.Join(' ', lines);

    private static Screen Render(RaiderGame game, int cols, int rows)
    {
        var scr = new Screen(cols, rows);
        new RaiderView().FitAndRender(game, scr);
        return scr;
    }

    private static string Row(Screen screen, int row) => new(Enumerable.Range(0, screen.Cols).Select(col => screen.CharAt(col, row)).ToArray());

    // ---------------------------------------------------------------- who takes a post

    [Fact]
    public void A_post_offered_with_no_speaker_is_said_by_the_next_speaker()
    {
        var game = NewEmptyGame();
        const string words = "first words";
        game.OfferDarkness(Rude(1, words));
        const int quietFrames = 200;
        StepWithoutBoltsOrFillers(game, quietFrames);
        Assert.Single(game.Rude.Waiting);
        var lesser = game.SpawnLesser();
        StepWithoutBoltsOrFillersUntil(game, () => lesser.Text is not null, LesserSpeakLimitFrames);
        Assert.Equal(words, lesser.Text);
        Assert.Equal(RudeHeader, lesser.Header);
        Assert.Empty(game.Rude.Waiting);
        Assert.True(game.Rude.Holds(1));
    }

    [Fact]
    public void A_lesser_gunship_says_one_post_and_no_more()
    {
        var game = NewEmptyGame();
        Offer(game, 2);
        var lesser = game.SpawnLesser();
        StepWithoutBoltsOrFillersUntil(game, () => lesser.Text is not null, LesserSpeakLimitFrames);
        Assert.Equal(RudeText(1), lesser.Text);
        ReadOut(game, lesser);
        const int quietFrames = 400;
        StepWithoutBoltsOrFillers(game, quietFrames);
        Assert.Equal(RudeText(1), lesser.Text);
        Assert.Single(game.Rude.Waiting);   // the second post waits for another speaker
    }

    [Fact]
    public void A_lesser_gunship_with_no_post_waiting_says_nothing()
    {
        var game = NewEmptyGame();
        var lesser = game.SpawnLesser();
        const int quietFrames = 500;
        StepWithoutBoltsOrFillers(game, quietFrames);
        Assert.Null(lesser.Text);
        Assert.False(EnemySpeechBox.Speaks(lesser));
        Assert.Empty(EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes));
    }

    [Fact]
    public void A_lesser_gunship_that_arrived_to_an_empty_store_says_the_post_that_comes_later()
    {
        var game = NewEmptyGame();
        var lesser = game.SpawnLesser();
        const int quietFrames = 400;
        StepWithoutBoltsOrFillers(game, quietFrames);
        Assert.Null(lesser.Text);
        const string words = "late words";
        game.OfferDarkness(Rude(1, words, null, DarknessSource.Hidden));
        StepWithoutBoltsOrFillers(game, SettleFrames);
        Assert.Equal(words, lesser.Text);
        Assert.Equal(HiddenHeader, lesser.Header);
    }

    [Fact]
    public void A_boss_says_the_waiting_post_on_arriving()
    {
        var game = NewEmptyGame();
        InstallBossVoice(game);
        Offer(game, 2);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillers(game, BriefFrames);
        Assert.Null(boss.Text);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        Assert.Equal(RudeText(1), boss.Text);
        Assert.Equal(RudeHeader, boss.Header);
        Assert.Single(game.Rude.Waiting);
    }

    private const int LongPostChars = 300;
    /// <summary>What a clipped line ends in.</summary>
    private const string Ellipsis = "…";
    private static readonly string LongPost = string.Join(' ', Enumerable.Range(1, LongPostChars).Select(i => "w" + i)).Substring(0, LongPostChars).TrimEnd();

    /// <summary>A screen narrower than this is phone-wide, and its box is too narrow to hold a long post on one page.</summary>
    private const int PhoneWideCols = 60;

    [Theory]
    [InlineData(40, 70)]
    [InlineData(120, 40)]
    public void A_300_character_post_is_said_whole_and_paged_to_its_last_word(int cols, int rows)
    {
        var game = NewEmptyGame(cols, rows);
        InstallBossVoice(game);
        const string label = "x";
        game.OfferDarkness(Rude(1, LongPost, label));
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        StepWithoutBoltsOrFillers(game);
        Assert.Equal(LongPost, boss.Text);
        Assert.Equal(EnemySpeechBox.Header(label, DarknessKind.Rude, DarknessSource.Label), boss.Header);
        Assert.Equal("X", boss.Header);
        Assert.Equal(LongPost, Squash(boss.SpeechLines!));
        Assert.DoesNotContain(Ellipsis, Squash(boss.SpeechLines!));
        int pages = SpeechBoxText.Pages(boss.SpeechLines!, boss.PageLines);
        if (cols < PhoneWideCols) Assert.True(pages > 1);
        var last = SpeechBoxText.Progress(boss.SpeechLines!, boss.PageLines, EnemySpeechBox.Frames(boss) - 1);
        Assert.Equal(pages - 1, last.Page);
        Assert.EndsWith(LongPost.Split(' ')[^1], boss.SpeechLines![^1]);
    }

    [Fact]
    public void A_hidden_reply_reads_HIDDEN_REPLY()
    {
        var game = NewEmptyGame();
        InstallBossVoice(game);
        game.OfferDarkness(Rude(1, "hidden words", null, DarknessSource.Hidden));
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        Assert.Equal(HiddenHeader, boss.Header);
        Assert.Equal(HiddenHeader, EnemySpeechBox.Title(boss));
    }

    [Fact]
    public void A_post_with_no_label_reads_RUDE()
    {
        var game = NewEmptyGame();
        InstallBossVoice(game);
        game.OfferDarkness(Rude(1, "plain", null));
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        Assert.Equal(RudeHeader, boss.Header);
    }

    // ---------------------------------------------------------------- when no post waits

    [Fact]
    public void With_no_post_the_boss_says_its_own_line_under_its_own_name()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        const string line = "Nobody asked.";
        behaviour.Lines[VoiceCue.Arrive] = line;
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        Assert.Equal(line, boss.Text);
        Assert.Equal(VoiceBehaviour.SpeakerName, boss.Header);
        Assert.Empty(game.Rude.Echoes);   // its own words are not the feed's
    }

    [Fact]
    public void A_waiting_post_comes_before_the_line()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.Lines[VoiceCue.Arrive] = OwnLine;
        Offer(game, 1);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        Assert.Equal(RudeText(1), boss.Text);
    }

    [Fact]
    public void With_none_waiting_a_boss_says_the_heard_posts_again_in_rounds_and_never_its_own_line()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.Lines[VoiceCue.Phase] = OwnLine;
        behaviour.OnTurn = cue => cue == VoiceCue.Phase ? Turn.Cut : null;
        const int posts = 3, rounds = 3;
        Offer(game, posts);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(1));
        var said = new List<string> { boss.Text! };
        for (int i = 1; i < posts * rounds; i++)
        {
            game.Cue(boss, VoiceCue.Phase);   // a cut, with the last post still on show
            said.Add(boss.Text!);
            Assert.Equal(RudeHeader, boss.Header);
        }
        Assert.Empty(game.Rude.Waiting);
        Assert.Equal(posts, game.Rude.Heard.Count);   // saying a post again does not spend or add one
        for (int i = 1; i < said.Count; i++) Assert.NotEqual(said[i - 1], said[i]);
        for (int round = 0; round < rounds; round++) Assert.Equal(posts, said.Skip(round * posts).Take(posts).Distinct().Count());
        Assert.DoesNotContain(OwnLine, said);
    }

    [Fact]
    public void A_boss_picks_among_the_heard_posts_by_its_taste()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.OnTaste = offered => offered.Count - 1;
        Offer(game, 3);
        while (game.Rude.Waiting.Count > 0) game.Rude.Take(0);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        Assert.Equal(RudeText(3), boss.Text);
        ReadOut(game, boss); game.Cue(boss, VoiceCue.Phase);
        Assert.Equal(RudeText(2), boss.Text);
        ReadOut(game, boss); game.Cue(boss, VoiceCue.Phase);
        Assert.Equal(RudeText(1), boss.Text);
    }

    [Fact]
    public void A_run_that_has_heard_one_post_hears_it_again_once_it_is_read_and_not_before()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.Lines[VoiceCue.Phase] = OwnLine;
        behaviour.OnTurn = cue => cue == VoiceCue.Phase ? Turn.Cut : null;
        Offer(game, 1);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(1));
        StepWithoutBoltsOrFillers(game, SettleFrames);
        int age = boss.SpeechBoxAge;
        game.Cue(boss, VoiceCue.Phase);
        Assert.Equal(RudeText(1), boss.Text);
        Assert.True(boss.SpeechBoxAge >= age, "a post still being read is not started over");
        ReadOut(game, boss);
        game.Cue(boss, VoiceCue.Phase);
        Assert.Equal(RudeText(1), boss.Text);
        Assert.Equal(0, boss.SpeechBoxAge);
    }

    [Fact]
    public void A_boss_arriving_after_a_post_was_heard_opens_with_it_and_not_with_its_line()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.Lines[VoiceCue.Arrive] = OwnLine;
        Offer(game, 1);
        game.Rude.Take(0);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        Assert.Equal(RudeText(1), boss.Text);
        Assert.Equal(RudeHeader, boss.Header);
    }

    [Fact]
    public void With_no_post_known_and_no_line_nothing_is_said_or_drawn()
    {
        var game = NewEmptyGame();
        InstallBossVoice(game);
        var boss = SpawnBoss(game);
        const int quietFrames = 300;
        StepWithoutBoltsOrFillers(game, quietFrames);
        foreach (var cue in Enum.GetValues<VoiceCue>()) game.Cue(boss, cue);
        StepWithoutBoltsOrFillers(game, SettleFrames);
        Assert.Null(boss.Text);
        Assert.Null(boss.Header);
        Assert.Empty(EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes));
    }

    // ---------------------------------------------------------------- the four turns

    /// <summary>The posts offered before each turn is tried: the first is on show and the rest wait.</summary>
    private const int TurnPosts = 3;

    private static (RaiderGame game, Enemy boss) WithTurn(Turn turn)
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.OnTurn = cue => cue == VoiceCue.Phase ? turn : null;
        Offer(game, TurnPosts);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(1));
        const int partReadFrames = 40;   // the box is open and part typed, not yet read to its end
        StepWithoutBoltsOrFillers(game, partReadFrames);
        return (game, boss);
    }

    [Fact]
    public void Wait_holds_the_turn_until_the_box_is_read_to_its_last_page_then_opens()
    {
        var (game, boss) = WithTurn(Turn.Wait);
        Assert.True(EnemySpeechBox.IsOpen(boss));
        game.Cue(boss, VoiceCue.Phase);
        game.Cue(boss, VoiceCue.Opens);   // replaces the held turn: at most one is held
        Assert.Equal(RudeText(1), boss.Text);
        Assert.Equal(TurnPosts - 1, game.Rude.Waiting.Count);
        const int stillReadingFrames = 20;
        StepWithoutBoltsOrFillers(game, stillReadingFrames);
        Assert.Equal(RudeText(1), boss.Text);
        ReadOut(game, boss);
        StepWithoutBoltsOrFillers(game);
        Assert.Equal(RudeText(2), boss.Text);
        Assert.Single(game.Rude.Waiting);   // one turn, one post
        StepWithoutBoltsOrFillers(game, SettleFrames);
        Assert.Equal(RudeText(2), boss.Text);
    }

    [Fact]
    public void Cut_replaces_an_unread_text_at_once()
    {
        var (game, boss) = WithTurn(Turn.Cut);
        Assert.True(EnemySpeechBox.IsOpen(boss));
        game.Cue(boss, VoiceCue.Phase);
        Assert.Equal(RudeText(2), boss.Text);
        Assert.Equal(0, boss.SpeechBoxAge);
        Assert.Single(game.Rude.Waiting);
    }

    [Fact]
    public void Stack_leaves_the_box_on_show_behind_as_an_echo_and_opens_a_new_one()
    {
        var (game, boss) = WithTurn(Turn.Stack);
        Assert.True(EnemySpeechBox.IsOpen(boss));
        game.Cue(boss, VoiceCue.Phase);
        Assert.Equal(RudeText(2), boss.Text);
        var echo = Assert.Single(game.Echoes);
        Assert.Equal(RudeText(1), echo.Text);
        Assert.NotSame(boss, echo);
        StepWithoutBoltsOrFillers(game, BriefFrames);
        Assert.True(EnemySpeechBox.IsOpen(echo));
        Assert.True(EnemySpeechBox.IsOpen(boss));
        var visible = EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes).Select(shown => shown.Enemy).ToList();
        Assert.Contains(echo, visible); Assert.Contains(boss, visible);
        var newBox = EnemySpeechBox.Box(boss, game.Layout); var oldBox = EnemySpeechBox.Box(echo, game.Layout);
        Assert.False(newBox.Overlaps(oldBox), "the new box sits beside the old one");
    }

    [Fact]
    public void Skip_does_nothing()
    {
        var (game, boss) = WithTurn(Turn.Skip);
        int age = boss.SpeechBoxAge;
        game.Cue(boss, VoiceCue.Phase);
        Assert.Equal(RudeText(1), boss.Text);
        Assert.Equal(age, boss.SpeechBoxAge);
        ReadOut(game, boss);
        StepWithoutBoltsOrFillers(game, SettleFrames);
        Assert.Equal(RudeText(1), boss.Text);
        Assert.Equal(TurnPosts - 1, game.Rude.Waiting.Count);
    }

    [Theory]
    [InlineData((int)VoiceCue.Arrive, (int)Turn.Wait)]
    [InlineData((int)VoiceCue.PartLost, (int)Turn.Wait)]
    [InlineData((int)VoiceCue.Phase, (int)Turn.Wait)]
    [InlineData((int)VoiceCue.Opens, (int)Turn.Wait)]
    [InlineData((int)VoiceCue.Idle, (int)Turn.Wait)]
    [InlineData((int)VoiceCue.ShipDied, (int)Turn.Wait)]
    [InlineData((int)VoiceCue.Dying, (int)Turn.Cut)]
    [InlineData((int)VoiceCue.Leaving, (int)Turn.Cut)]
    public void A_boss_answers_each_cue_by_default(int cueNo, int expectedNo)
    {
        var cue = (VoiceCue)cueNo; var expected = (Turn)expectedNo;
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        Assert.Equal(expected, behaviour.TurnFor(game, SpawnSpeaker(game, behaviour), cue));
    }

    [Fact]
    public void A_mini_boss_answers_Idle_with_Skip_and_the_rest_like_a_boss()
    {
        var game = NewEmptyGame();
        var behaviour = new VoiceBehaviour(EnemyKind.Warden);
        Use(game, behaviour);
        var enemy = SpawnSpeaker(game, behaviour);
        Assert.Equal(Turn.Skip, behaviour.TurnFor(game, enemy, VoiceCue.Idle));
        Assert.Equal(Turn.Wait, behaviour.TurnFor(game, enemy, VoiceCue.Arrive));
        Assert.Equal(Turn.Wait, behaviour.TurnFor(game, enemy, VoiceCue.ShipDied));
        Assert.Equal(Turn.Cut, behaviour.TurnFor(game, enemy, VoiceCue.Dying));
    }

    // ---------------------------------------------------------------- the enemy's own say

    [Fact]
    public void Taste_picks_the_waiting_post_and_a_wild_index_is_held_to_the_list()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.OnTaste = waiting => waiting.Count - 1;
        Offer(game, 3);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        Assert.Equal(RudeText(3), boss.Text);
        Assert.Equal([1UL, 2UL], game.Rude.Waiting.Select(p => p.Seed).ToArray());

        var wildGame = NewEmptyGame();
        var wildBehaviour = InstallBossVoice(wildGame);
        const int wildIndex = 99;
        wildBehaviour.OnTaste = _ => wildIndex;
        Offer(wildGame, 2);
        var wildBoss = SpawnBoss(wildGame);
        StepWithoutBoltsOrFillersUntil(wildGame, () => wildBoss.Text is not null);
        Assert.Equal(RudeText(2), wildBoss.Text);
    }

    [Fact]
    public void The_default_taste_is_the_oldest()
    {
        Assert.Equal(0, new VoiceBehaviour(EnemyKind.Bulwark).Taste(EnemyKit.Speaker(EnemyKind.Bulwark), [new RudePost(1, "older", DarknessSource.Label, null, 0), new RudePost(2, "newer", DarknessSource.Label, null, 0)]));
    }

    [Fact]
    public void Prefix_stands_before_the_label_in_the_header_and_on_the_screen()
    {
        const int cols = 80, rows = 24;
        var game = NewEmptyGame(cols, rows);
        var behaviour = InstallBossVoice(game);
        const string prefix = "3/7", PrefixedTitle = prefix + " " + RudeHeader;
        behaviour.PrefixText = prefix;
        Offer(game, 1);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        const int titleShownFrames = 6;
        StepWithoutBoltsOrFillers(game, titleShownFrames);
        Assert.Equal(PrefixedTitle, EnemySpeechBox.Title(boss));
        Assert.Equal(RudeHeader, boss.Header);
        var box = EnemySpeechBox.Box(boss, game.Layout);
        var scr = Render(game, cols, rows);
        Assert.Contains(PrefixedTitle, Row(scr, box.R + 1));
        behaviour.PrefixText = null;
        StepWithoutBoltsOrFillers(game);
        Assert.Equal(RudeHeader, EnemySpeechBox.Title(boss));
    }

    [Fact]
    public void HoldBox_stops_the_typing_and_the_page_clock()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.Hold = true;
        Offer(game, 1);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        const int heldFrames = 60, releasedFrames = 10;
        StepWithoutBoltsOrFillers(game, heldFrames);
        Assert.Equal(0, boss.SpeechBoxAge);
        Assert.False(EnemySpeechBox.IsOpen(boss));
        behaviour.Hold = false;
        StepWithoutBoltsOrFillers(game, releasedFrames);
        Assert.InRange(boss.SpeechBoxAge, releasedFrames / 2, releasedFrames + 1);   // the page clock has run again for most of the released frames
        Assert.True(EnemySpeechBox.IsOpen(boss));
    }

    [Fact]
    public void A_held_box_is_not_read_so_a_Wait_turn_stays_held()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        Offer(game, 2);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        StepWithoutBoltsOrFillers(game, SettleFrames);
        behaviour.Hold = true;
        game.Cue(boss, VoiceCue.Phase);
        const int quietFrames = 500;
        StepWithoutBoltsOrFillers(game, quietFrames);
        Assert.Equal(RudeText(1), boss.Text);
        behaviour.Hold = false;
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(2));
    }

    // ---------------------------------------------------------------- the boxes on show

    [Fact]
    public void Never_more_than_three_boxes_show_and_the_newest_win()
    {
        Assert.Equal(3, Tuning.MaxSpeechBoxes);
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.OnTurn = cue => cue == VoiceCue.Phase ? Turn.Stack : null;
        const int stacks = 5, stackGapFrames = 4;
        Offer(game, stacks + 2);   // the arrival's post, one per stack, one left waiting
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        for (int i = 0; i < stacks; i++) { StepWithoutBoltsOrFillers(game, stackGapFrames); game.Cue(boss, VoiceCue.Phase); }
        StepWithoutBoltsOrFillers(game, BriefFrames);
        int open = game.Enemies.Concat(game.Echoes).Count(EnemySpeechBox.IsOpen);
        Assert.True(open >= stacks, $"{open} boxes are open");
        var shown = EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes).Select(placed => placed.Enemy).ToList();
        Assert.Equal(Tuning.MaxSpeechBoxes, shown.Count);
        Assert.Contains(boss, shown);
        int oldestShown = shown.Max(enemy => enemy.SpeechBoxAge);
        foreach (var enemy in game.Enemies.Concat(game.Echoes).Where(EnemySpeechBox.IsOpen).Except(shown)) Assert.True(enemy.SpeechBoxAge >= oldestShown);
        EnemyKit.ClearPickupsAndDraw(game);   // draws without a throw
    }

    [Fact]
    public void A_box_is_never_drawn_over_the_ship()
    {
        var game = NewEmptyGame();
        InstallBossVoice(game);
        Offer(game, 1);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        StepWithoutBoltsOrFillers(game, SettleFrames);
        Assert.Contains(boss, EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes).Select(shown => shown.Enemy).ToList());
        const double besideShipY = 2;   // a little below the ship's centre, still level with it: its box would sit on the ship
        boss.X = game.PX; boss.Y = game.PY + besideShipY;
        Assert.DoesNotContain(boss, EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes).Select(shown => shown.Enemy).ToList());
    }

    [Theory]
    [InlineData(30, 16)]
    [InlineData(48, 60)]
    [InlineData(80, 24)]
    [InlineData(120, 46)]
    public void A_boss_holding_at_the_top_has_its_box_hung_below_it_inside_the_field(int cols, int rows)
    {
        var game = NewEmptyGame(cols, rows);
        const double topHoldY = 8;
        const double rightOfCentre = 0.7;
        const double shipParkX = 8;   // the ship moves to the far left, away from the boss's box
        var behaviour = InstallBossVoice(game);
        behaviour.HoldY = topHoldY;
        game.OfferDarkness(Rude(1, "no u"));
        var boss = SpawnSpeaker(game, behaviour, rightOfCentre, topHoldY);
        const int walkLimitFrames = 400, openLimitFrames = 200;
        for (int i = 0; i < walkLimitFrames && game.PX > shipParkX; i++) { game.Input.Key(GameKey.Left, true); StepWithoutBoltsOrFillers(game); }
        game.Input.Key(GameKey.Left, false);
        StepWithoutBoltsOrFillersUntil(game, () => EnemySpeechBox.IsOpen(boss), openLimitFrames);
        var box = EnemySpeechBox.Box(boss, game.Layout);
        Assert.True(EnemySpeechBox.IsBelow(boss, box), "no room above, so the box hangs below");
        Assert.True(box.R * CardField.PixelsPerCell >= boss.Y + boss.Sprite.H / 2.0, "below the sprite, not over it");
        Assert.True(box.C >= game.Layout.Col0 && box.Right <= game.Layout.Col0 + game.Layout.PCols);
        Assert.True(box.R >= EnemySpeechBox.MinRow && box.Bottom <= game.Layout.Rows, $"{box} in {game.Layout.Rows} rows");
        Assert.Contains(boss, EnemySpeechBox.Placed(game, game.Layout, Tuning.MaxSpeechBoxes).Select(shown => shown.Enemy).ToList());
        var scr = Render(game, cols, rows);
        Assert.Contains(RudeHeader, Row(scr, box.R + 1));
        Assert.Equal('▲', scr.CharAt(box.C + box.W / 2, box.R));
        Assert.Equal(EnemySpeechBox.BoxBg, scr.Bg[(box.R + 1) * scr.Cols + box.C + 1]);
    }

    // ---------------------------------------------------------------- the floor

    /// <summary>Where a lesser gunship is pinned: on the field, below where a boss holds.</summary>
    private const double LesserY = 40;

    /// <summary>A shot that kills a boss outright, whatever its hit points.</summary>
    private const double BossKillDamage = 100000;

    /// <summary>A shot that kills a speaker brought down to its last hit points.</summary>
    private const double SpeakerKillDamage = 1000;

    [Fact]
    public void During_a_fight_only_the_boss_speaks_and_after_it_the_older_speaker_has_the_floor()
    {
        var game = NewEmptyGame(gates: true);
        const int posts = 3;
        Offer(game, posts);
        game.StartAtBoss(1);
        Assert.Equal(GateState.Warning, game.Gate);
        var lesser = game.SpawnLesser();
        const double lesserX = 20;
        const int intoWarningFrames = 20, warningLimitFrames = 200, fightFrames = 100, gateCloseLimitFrames = 10, floorLimitFrames = 200;
        lesser.Y = LesserY; lesser.BaseX = lesser.X = lesserX;
        StepWithoutBoltsOrFillers(game, intoWarningFrames);
        Assert.Null(lesser.Text);   // the warning belongs to the boss, and he is not here yet
        StepWithoutBoltsOrFillersUntil(game, () => game.Gate == GateState.Fighting, warningLimitFrames);
        StepWithoutBoltsOrFillersUntil(game, () => game.Boss!.Text is not null, GatedBossLimitFrames);
        var boss = game.Boss!;
        Assert.Equal(RudeText(1), boss.Text);
        StepWithoutBoltsOrFillers(game, fightFrames);
        Assert.Null(lesser.Text);
        Assert.Equal(posts - 1, game.Rude.Waiting.Count);
        EnemyKit.Shot(game, boss.X, boss.Y, BossKillDamage);
        StepWithoutBoltsOrFillersUntil(game, () => game.Gate == GateState.None, gateCloseLimitFrames);
        StepWithoutBoltsOrFillersUntil(game, () => lesser.Text is not null, floorLimitFrames);
        Assert.Equal(RudeText(2), lesser.Text);   // the dying boss signed off in its own words and left the waiting posts
    }

    [Fact]
    public void Without_a_gate_the_oldest_living_speaker_has_the_floor()
    {
        var game = NewEmptyGame();
        const double firstX = 30, secondX = 90;   // apart, so neither box hides the other
        var first = game.SpawnLesser(); first.Y = LesserY; first.BaseX = first.X = firstX;
        var second = game.SpawnLesser(); second.Y = LesserY; second.BaseX = second.X = secondX;
        StepWithoutBoltsOrFillers(game, SettleFrames);
        Offer(game, 2);
        StepWithoutBoltsOrFillers(game, SettleFrames);
        Assert.Equal(RudeText(1), first.Text);
        Assert.Null(second.Text);
        const int quietFrames = 200;
        StepWithoutBoltsOrFillers(game, quietFrames);
        Assert.Null(second.Text);   // the first had its say and still holds the floor
        Assert.Single(game.Rude.Waiting);
        game.Enemies.Remove(first);
        StepWithoutBoltsOrFillers(game, SettleFrames);
        Assert.Equal(RudeText(2), second.Text);
    }

    // ---------------------------------------------------------------- the cues

    [Fact]
    public void A_part_lost_raises_PartLost_on_the_speaker()
    {
        var game = NewEmptyGame();
        var cues = new List<VoiceCue>();
        var behaviour = InstallBossVoice(game);
        behaviour.OnTurn = cue => { cues.Add(cue); return null; };
        const int partHalfSize = 4;
        const double sideOffset = 12;   // far enough out that a shot at the centre misses it
        const double oneShotHp = 5, sturdyHp = 50;
        behaviour.OnSpawn = (_, enemy) => enemy.Parts = [EnemyKit.MakePart("a", 0, 0, partHalfSize, partHalfSize, oneShotHp), EnemyKit.MakePart("b", sideOffset, 0, partHalfSize, partHalfSize, sturdyHp)];
        var boss = SpawnSpeaker(game, behaviour);
        StepWithoutBoltsOrFillers(game, BriefFrames);
        EnemyKit.Shot(game, boss.X, boss.Y);
        StepWithoutBoltsOrFillers(game);
        Assert.Equal(PartState.Dead, boss.Parts![0].State);
        Assert.Contains(VoiceCue.PartLost, cues);
        Assert.Contains(VoiceCue.Arrive, cues);
    }

    [Fact]
    public void A_dying_speaker_cuts_to_its_last_line_and_the_box_outlives_it_as_an_echo()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.Lines[VoiceCue.Dying] = DyingLine;
        var boss = SpawnSpeaker(game, behaviour);
        boss.Hp = boss.MaxHp = 1;
        StepWithoutBoltsOrFillers(game, BriefFrames);
        EnemyKit.Shot(game, boss.X, boss.Y, SpeakerKillDamage);
        StepWithoutBoltsOrFillers(game);
        Assert.DoesNotContain(boss, game.Enemies);
        Assert.Contains(boss, game.Echoes);
        Assert.Equal(DyingLine, boss.Text);
        Assert.Equal(VoiceBehaviour.SpeakerName, boss.Header);
    }

    [Fact]
    public void A_dying_speaker_signs_off_with_its_line_and_leaves_the_waiting_post_for_the_next()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.Lines[VoiceCue.Dying] = DyingLine;
        Offer(game, 2);
        var boss = SpawnSpeaker(game, behaviour);
        boss.Hp = boss.MaxHp = 1;
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(1));
        EnemyKit.Shot(game, boss.X, boss.Y, SpeakerKillDamage);
        StepWithoutBoltsOrFillers(game);
        Assert.Equal(DyingLine, boss.Text);
        Assert.Equal(VoiceBehaviour.SpeakerName, boss.Header);
        Assert.Equal(RudeText(2), Assert.Single(game.Rude.Waiting).Text);
    }

    [Fact]
    public void A_dying_speaker_with_no_line_says_a_post()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        Offer(game, 2);
        var boss = SpawnSpeaker(game, behaviour);
        boss.Hp = boss.MaxHp = 1;
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(1));
        EnemyKit.Shot(game, boss.X, boss.Y, SpeakerKillDamage);
        StepWithoutBoltsOrFillers(game);
        Assert.Equal(RudeText(2), boss.Text);
    }

    [Fact]
    public void A_speaker_sent_off_the_field_says_its_leaving_line()
    {
        var game = NewEmptyGame();
        var behaviour = new VoiceBehaviour(EnemyKind.Warden);
        const string line = "I have other feeds.";
        behaviour.Lines[VoiceCue.Leaving] = line;
        Use(game, behaviour);
        var enemy = SpawnSpeaker(game, behaviour);
        StepWithoutBoltsOrFillers(game, BriefFrames);
        enemy.Leaving = true;
        StepWithoutBoltsOrFillers(game);
        Assert.Equal(line, enemy.Text);
    }

    [Fact]
    public void The_ship_dying_brings_the_next_post_once_the_one_on_show_is_read()
    {
        var game = NewEmptyGame();
        InstallBossVoice(game);
        Offer(game, 2);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        StepWithoutBoltsOrFillers(game, SettleFrames);
        game.Cue(boss, VoiceCue.ShipDied);
        Assert.Equal(RudeText(1), boss.Text);   // it is still being read
        ReadOut(game, boss);
        const int nextOpenFrames = 2;
        StepWithoutBoltsOrFillers(game, nextOpenFrames);
        Assert.Equal(RudeText(2), boss.Text);
        Assert.Empty(game.Rude.Waiting);
    }

    [Fact]
    public void The_last_eight_spoken_are_the_echoes_newest_first()
    {
        var game = NewEmptyGame();
        var behaviour = InstallBossVoice(game);
        behaviour.OnTurn = cue => cue == VoiceCue.Phase ? Turn.Cut : null;
        const int posts = Tuning.RudeEchoes + 2;
        Offer(game, posts);
        var boss = SpawnBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null);
        for (int i = 1; i < posts; i++) game.Cue(boss, VoiceCue.Phase);
        Assert.Empty(game.Rude.Waiting);
        Assert.Equal(Tuning.RudeEchoes, game.Rude.Echoes.Count);
        Assert.Equal(RudeText(posts), game.Rude.Echoes[0].Text);
        Assert.Equal(RudeText(posts - Tuning.RudeEchoes + 1), game.Rude.Echoes[^1].Text);
    }

    // ---------------------------------------------------------------- the gunship

    private static Enemy StartGunshipBoss(RaiderGame game)
    {
        game.StartAtBoss(1);
        StepWithoutBoltsOrFillersUntil(game, () => game.Boss is not null, GatedBossLimitFrames);
        Assert.Equal(EnemyKind.Gunship, game.Boss!.Kind);
        return game.Boss;
    }

    /// <summary>How many frames before the Idle cue the gunship tests stop to check nothing has moved yet.</summary>
    private const int IdleMarginFrames = 10;

    /// <summary>A shut box held this long has not yet raised the Idle cue; the next post follows within a few frames.</summary>
    private const int ShortOfIdle = Tuning.VoiceIdleFrames - IdleMarginFrames;

    /// <summary>From <see cref="ShortOfIdle"/>, room for the Idle cue to come and the next post to open.</summary>
    private const int NextPostLimitFrames = 40;

    [Fact]
    public void The_gunship_boss_says_a_post_on_arriving_the_next_at_each_idle_and_starts_over_when_none_waits()
    {
        var game = NewEmptyGame(gates: true);
        Offer(game, 3);
        var boss = StartGunshipBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null, GatedBossLimitFrames);
        Assert.Equal(RudeText(1), boss.Text);
        Assert.Equal(RudeHeader, boss.Header);
        ReadOut(game, boss);
        StepWithoutBoltsOrFillers(game, ShortOfIdle);
        Assert.Equal(RudeText(1), boss.Text);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(2), NextPostLimitFrames);
        ReadOut(game, boss);
        StepWithoutBoltsOrFillers(game, ShortOfIdle);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(3), NextPostLimitFrames);
        ReadOut(game, boss);
        StepWithoutBoltsOrFillers(game, ShortOfIdle);
        Assert.Equal(RudeText(3), boss.Text);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text == RudeText(1), NextPostLimitFrames);   // none waits: the oldest heard post, again
        Assert.Equal(RudeHeader, boss.Header);
    }

    [Fact]
    public void The_gunship_boss_speaks_for_itself_when_no_post_waits()
    {
        var game = NewEmptyGame(gates: true);
        var boss = StartGunshipBoss(game);
        StepWithoutBoltsOrFillersUntil(game, () => boss.Text is not null, GatedBossLimitFrames);
        Assert.Equal("Nobody asked. I answered.", boss.Text);
        const string gunshipHeader = "GUNSHIP";
        Assert.Equal(gunshipHeader, boss.Header);
        Assert.Empty(game.Rude.Echoes);
        EnemyKit.Shot(game, boss.X, boss.Y, BossKillDamage);
        StepWithoutBoltsOrFillers(game);
        Assert.Contains(game.Echoes, enemy => enemy.Text == DyingLine && enemy.Header == gunshipHeader);
    }

    [Fact]
    public void The_gunship_boss_has_its_last_word_when_it_leaves()
    {
        var game = NewEmptyGame(gates: true);
        var boss = StartGunshipBoss(game);
        const int leaveMarginFrames = 10;
        for (int i = 0; i < Tuning.BossStayFrames + leaveMarginFrames && game.Gate == GateState.Fighting; i++) { StepWithoutBoltsOrFillers(game); boss.Hp = boss.MaxHp; }
        Assert.NotEqual(GateState.Fighting, game.Gate);
        Assert.Equal("I have other feeds to ruin.", boss.Text);
    }

    // ---------------------------------------------------------------- no random draw

    private static string Snapshot(RaiderGame game) =>
        string.Join(';', game.Enemies.Select(enemy => $"{enemy.Kind}:{enemy.X:F3}:{enemy.Y:F3}:{enemy.Hp:F1}")) + "|" +
        string.Join(';', game.Bullets.Select(b => $"{b.X:F3}:{b.Y:F3}:{b.Friendly}")) + $"|{game.Score}:{game.Hp}:{game.Deaths}:{game.PX:F2}";

    [Fact]
    public void A_run_with_posts_and_the_same_run_without_put_every_enemy_and_bullet_in_the_same_place()
    {
        const ulong seed = 11;
        const int stage = 3;
        var withPosts = NewEmptyGame(gates: true, seed: seed); var withoutPosts = NewEmptyGame(gates: true, seed: seed);
        withPosts.StartAtStage(stage); withoutPosts.StartAtStage(stage); withPosts.SpawnLesser(); withoutPosts.SpawnLesser();   // a speaker, so the posts are said in one run and only wait in the other
        const int runFrames = 2400, postEvery = 150, firstPostAt = 3, swayFrames = 120;
        for (int i = 0; i < runFrames; i++)
        {
            if (i % postEvery == firstPostAt) withPosts.OfferDarkness(Rude((ulong)i + 1, "words number " + i));
            foreach (var game in new[] { withPosts, withoutPosts })
            {
                game.Input.Key(GameKey.Fire, true);
                game.Input.Key(GameKey.Left, i / swayFrames % 2 == 0);
                game.Input.Key(GameKey.Right, i / swayFrames % 2 == 1);
                game.Step();
            }
            Assert.Equal(Snapshot(withoutPosts), Snapshot(withPosts));
        }
        Assert.True(withPosts.Rude.Echoes.Count > 0);
    }

    [Fact]
    public void Only_bosses_the_four_mini_bosses_and_the_lesser_gunship_speak()
    {
        var game = new RaiderGame(1);
        var lesser = game.SpawnLesser();
        var enemies = Enum.GetValues<EnemyKind>()
            .Select(k => (Kind: k, Lesser: false, Enemy: new Enemy { Kind = k, Sprite = lesser.Sprite }))
            .Append((Kind: EnemyKind.Gunship, Lesser: true, Enemy: lesser))
            .ToList();
        (EnemyKind, bool)[] miniAndLesser = [(EnemyKind.Warden, false), (EnemyKind.Nest, false), (EnemyKind.Yoke, false), (EnemyKind.Chorus, false), (EnemyKind.Gunship, true)];

        var speaking = enemies.Where(x => game.BehaviourFor(x.Enemy.Kind) is { Speaks: true }).Select(x => (x.Kind, x.Lesser)).ToList();
        var expected = enemies.Where(x => game.BehaviourFor(x.Enemy.Kind) is { Boss: true } || miniAndLesser.Contains((x.Kind, x.Lesser))).Select(x => (x.Kind, x.Lesser)).ToList();

        Assert.Equal(expected, speaking);
    }
}
