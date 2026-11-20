namespace FeedRaider.Engine.Raider;

public sealed partial class RaiderGame
{
    /// <summary>Every kind, in enum order from <see cref="EnemyKind.Grunt"/>. A plain array of constructors, not a reflection scan,
    /// because the browser build may be trimmed. An enemy holds no per-enemy state, so one instance serves every game.</summary>
    private static readonly EnemyBehaviour[] Table =
    [
        new GruntBehaviour(),
        new CarrierBehaviour(),
        new GunshipBehaviour(),
        new MineBehaviour(),
        new DiverBehaviour(),
        new SplitterBehaviour(),
        new ShardBehaviour(),
        new TurretBehaviour(),
        new GnatBehaviour(),
        new BrickBehaviour(),
        new FlankerBehaviour(),
        new WardenBehaviour(),
        new LobberBehaviour(),
        new NestBehaviour(),
        new SowerBehaviour(),
        new YokeBehaviour(),
        new LurkBehaviour(),
        new FuseBehaviour(),
        new BulwarkBehaviour(),
        new ShutterBehaviour(),
        new ThreadBehaviour(),
        new SpotlightBehaviour(),
        new BroodBehaviour(),
        new SchismBehaviour(),
        new PileOnBehaviour(),
        new MirrorBehaviour(),
        new DoomscrollBehaviour(),
        new ChorusBehaviour(),
        new GlintBehaviour(),
    ];

    /// <summary>The field height under which a boss holds with its top this far from the field's top; taller fields hold it a share of the way down.</summary>
    private const int ShortFieldHeight = 48, ShortFieldTopMargin = 5;
    /// <summary>The row a boss of the first seven holds on a tall field: this share of the field's height, kept between the two bounds.</summary>
    private const double BossRowShare = 0.24, BossRowMin = 14, BossRowMax = 30;

    /// <summary>The row a boss's centre holds on a field of height <paramref name="ph"/>, ignoring the short-field rule.</summary>
    private static double BossRowFor(int ph) => Math.Clamp(BossRowShare * ph, BossRowMin, BossRowMax);

    /// <summary>Where a boss <paramref name="height"/> tall holds its centre: on a short field with its top on the margin under the HUD, else <see cref="BossRowFor"/>.</summary>
    private static double BossHoldRow(RaiderGame g, int height) => g.PH < ShortFieldHeight ? ShortFieldTopMargin + height / 2.0 : BossRowFor(g.PH);

    /// <summary>The enemy that defines a kind: its sprite, hit points, score, drop chance, colour and flight.</summary>
    internal static EnemyBehaviour TableBehaviour(EnemyKind kind) => Table[kind - EnemyKind.Grunt];

    /// <summary>For a test: answers for a kind instead of the table, so a test can script a kind through the real code paths.</summary>
    internal Func<EnemyKind, EnemyBehaviour?>? BehaviourOverride { get; set; }

    /// <summary>The enemy that drives a kind in this game: a test's override when it answers, else the table's.</summary>
    internal EnemyBehaviour BehaviourFor(EnemyKind kind) => BehaviourOverride?.Invoke(kind) ?? TableBehaviour(kind);
}
