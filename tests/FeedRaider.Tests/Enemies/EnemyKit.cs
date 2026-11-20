using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// What every enemy's tests share: games on the four field sizes, a placed enemy, a shot put exactly where it will land, the
/// field as the view drew it, "stays inside the field" checks that take the kind's own size (a boss may be most of the field wide),
/// and a plain bot for the fight tests. Each enemy adds its own helpers in its own test file and leaves this one alone.
/// </summary>
internal static class EnemyKit
{
    /// <summary>The four fields every kind is tested on, as terminal columns and rows: a phone, a tall portrait, a short wide window and a big one.</summary>
    public static readonly (int Cols, int Rows)[] FieldSizes = [(30, 16), (48, 60), (80, 24), (BigCols, BigRows)];

    /// <summary>The kinds from the gnat on, in enum order.</summary>
    public static readonly EnemyKind[] NewKinds = Enum.GetValues<EnemyKind>().Where(kind => kind >= EnemyKind.Gnat).ToArray();

    /// <summary>The big desktop field, in terminal columns and rows: the size a test uses when the size is not what it checks.</summary>
    public const int BigCols = 120, BigRows = 46;

    /// <summary>The seed a test game uses when the run's luck is not what it checks.</summary>
    public const ulong PlainSeed = 5;

    /// <summary>A game with the field emptied, so a test places exactly what it checks. Signed in.</summary>
    public static RaiderGame Game(int cols = BigCols, int rows = BigRows, ulong seed = PlainSeed)
    {
        var game = new RaiderGame(seed) { SignedIn = true };
        game.SetLayout(FieldLayout.Compute(cols, rows));
        Clear(game);
        return game;
    }

    /// <summary>One enemy of a kind, alone in a fresh game: the speaker to hand a behaviour that is asked for its words or its taste.</summary>
    public static Enemy Speaker(EnemyKind kind) => Game().SpawnKind(kind, 0.5);

    /// <summary>Takes every unit and pop-up off the field, leaving the ship, the cards and the sky.</summary>
    public static void Clear(RaiderGame game)
    {
        game.Enemies.Clear(); game.Bullets.Clear(); game.Particles.Clear(); game.Rings.Clear(); game.Pickups.Clear(); game.Popups.Clear();
    }

    /// <summary>Steps, sweeping away the filler wave's enemies after each frame (the new kinds are never picked for a filler wave).</summary>
    public static void StepSweeping(RaiderGame game, int frames = 1)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            game.Step();
            game.Enemies.RemoveAll(enemy => enemy.Kind < EnemyKind.Gnat);
        }
    }

    /// <summary>Puts one enemy of a kind on the field at a spot, armed and settled, as a filler wave would have left it.</summary>
    public static Enemy Place(RaiderGame game, EnemyKind kind, double x, double y)
    {
        var enemy = game.SpawnKind(kind, x / game.PW);
        enemy.X = x; enemy.BaseX = x; enemy.Y = y; enemy.Grace = 0; enemy.Age = 0; enemy.Windup = 0; enemy.Flash = 0;
        return enemy;
    }

    /// <summary>A part with a hit box of <paramref name="hw"/> by <paramref name="hh"/> half-extents at an offset from the owner.</summary>
    public static Part MakePart(string name, double ox, double oy, int hw, int hh, double hp, PartState state = PartState.Open,
        bool vital = true, int score = PlainPartScore, Sprite? closed = null, Sprite? wreck = null) => new()
        {
            Name = name,
            OX = ox,
            OY = oy,
            HW = hw,
            HH = hh,
            Hp = hp,
            MaxHp = hp,
            State = state,
            Vital = vital,
            Score = score,
            Closed = closed ?? BoxArt(hw, hh, PlainPartGrey),
            Wreck = wreck,
        };

    /// <summary>The colour of a part a test gives no art of its own.</summary>
    private const uint PlainPartGrey = 0x808080;

    /// <summary>What a part pays when a test does not say.</summary>
    private const int PlainPartScore = 100;

    /// <summary>A solid rectangle of one colour exactly the size of a hit box of <paramref name="hw"/> by <paramref name="hh"/> half-extents.</summary>
    private static Sprite BoxArt(int hw, int hh, uint colour) => Solid(hw * 2 + 1, hh * 2 + 1, colour);

    /// <summary>A rectangle of one colour, for part art a test can tell apart pixel by pixel.</summary>
    public static Sprite Solid(int width, int height, uint colour)
    {
        var sprite = new Sprite(width, height);
        Array.Fill(sprite.Px, colour | Sprite.Opaque);
        return sprite;
    }

    /// <summary>Where the hand-built enemy is centred.</summary>
    private const double ThreePartsX = 60, ThreePartsY = 40;

    /// <summary>The hand-built enemy's shell: its offset up from the centre, its half-extents and its hit points.</summary>
    public const int ShellOY = -6, ShellHW = 8, ShellHH = 3, ShellHp = 30, ShellScore = 50;

    /// <summary>The hand-built enemy's pods: their offset either side of the centre and down from it, their half-extent and their hit points.</summary>
    public const int PodOX = 10, PodOY = 4, PodHalf = 3, PodHp = 20, PodLeftScore = 100, PodRightScore = 200;

    /// <summary>The colours of the hand-built enemy's art, each its own so a test can tell the parts apart pixel by pixel.</summary>
    public const uint ShellColour = 0x112233, PodLeftColour = 0x223344, PodRightColour = 0x334455, PodWreckColour = 0x010203;

    /// <summary>The standard hand-built enemy: a wide armoured shell over two open pods, centred on (<see cref="ThreePartsX"/>, <see cref="ThreePartsY"/>). The pods are vital, the shell is not.
    /// Unless the game already consults a test's behaviour, it is given a <see cref="ScriptBehaviour"/> for the kind, so the kind's own
    /// Update never runs on these stand-in parts.</summary>
    public static Enemy ScriptedThreeParts(RaiderGame game, EnemyKind kind = EnemyKind.Bulwark)
    {
        var shell = MakePart("shell", 0, ShellOY, ShellHW, ShellHH, ShellHp, PartState.Armoured, vital: false, score: ShellScore,
            closed: BoxArt(ShellHW, ShellHH, ShellColour));
        var podL = MakePart("pod-l", -PodOX, PodOY, PodHalf, PodHalf, PodHp, score: PodLeftScore,
            closed: BoxArt(PodHalf, PodHalf, PodLeftColour), wreck: BoxArt(PodHalf, PodHalf, PodWreckColour));
        var podR = MakePart("pod-r", PodOX, PodOY, PodHalf, PodHalf, PodHp, score: PodRightScore, closed: BoxArt(PodHalf, PodHalf, PodRightColour));
        if (game.BehaviourOverride is null) OverrideBehaviour(game, new ScriptBehaviour(kind));
        return game.SpawnParts(kind, ThreePartsX, ThreePartsY, shell, podL, podR);
    }

    /// <summary>The damage of a test's shot when the test does not say.</summary>
    public const double PlainShotDamage = 10;

    /// <summary>A friendly shot that will be exactly at (x, y) when this frame's bullets have moved, with this damage.</summary>
    public static Bullet Shot(RaiderGame game, double x, double y, double dmg = PlainShotDamage)
    {
        var shot = new Bullet { X = x, Y = y + Tuning.ShipShotSpeed, VX = 0, VY = -Tuning.ShipShotSpeed, Dmg = dmg, Friendly = true };
        game.Bullets.Add(shot);
        return shot;
    }

    /// <summary>Drops the alpha byte of a drawn pixel, leaving the colour a test compares.</summary>
    public const uint RgbMask = 0xFFFFFF;

    /// <summary>Clears the field's pickups, then renders and returns the field surface the view drew, before the screen shake and the
    /// cell reduction. The pickups go because a part shot off may have dropped one over the pixels a test reads.</summary>
    public static Surface ClearPickupsAndDraw(RaiderGame game, RaiderView? view = null, Screen? scr = null)
    {
        game.Pickups.Clear();
        view ??= new RaiderView();
        view.FitAndRender(game, scr ?? new Screen(BigCols, BigRows));
        return view.Field!;
    }

    // ---------------------------------------------------------------- the kind's own size

    /// <summary>The box of one part in field pixels: left, top, right, bottom.</summary>
    public static (double L, double T, double R, double B) Box(Enemy enemy, Part part) =>
        (enemy.X + part.OX - part.HW, enemy.Y + part.OY - part.HH, enemy.X + part.OX + part.HW, enemy.Y + part.OY + part.HH);

    /// <summary>The whole extent of an enemy: its sprite's box and the art of every part that is drawn.</summary>
    public static (double L, double T, double R, double B) Extent(Enemy enemy)
    {
        double left = enemy.X - enemy.Sprite.W / 2.0, top = enemy.Y - enemy.Sprite.H / 2.0, right = enemy.X + enemy.Sprite.W / 2.0, bottom = enemy.Y + enemy.Sprite.H / 2.0;
        foreach (var part in enemy.Parts ?? [])
        {
            if (RaiderView.PartArt(part) is not { } art) continue;
            left = Math.Min(left, enemy.X + part.OX - art.W / 2.0); right = Math.Max(right, enemy.X + part.OX + art.W / 2.0);
            top = Math.Min(top, enemy.Y + part.OY - art.H / 2.0); bottom = Math.Max(bottom, enemy.Y + part.OY + art.H / 2.0);
        }
        return (left, top, right, bottom);
    }

    /// <summary>How far past an edge of the field an enemy's extent may reach: half a pixel, which rounding a centre to its pixel can add.</summary>
    private const double EdgeRoundingSlack = 0.5;

    /// <summary>True when everything the enemy shows lies inside the field's width. The check uses the enemy's own size, never a constant.</summary>
    public static bool InsideWidth(RaiderGame game, Enemy enemy)
    {
        var extent = Extent(enemy);
        return extent.L >= -EdgeRoundingSlack && extent.R <= game.PW + EdgeRoundingSlack;
    }

    /// <summary>A boss keeps its lowest pixel on or above the floor: this share of the field's height up from the bottom, and never less than the minimum.</summary>
    public const double BossFloorShare = 0.36, BossFloorMin = 13;

    /// <summary>The lowest pixel row a boss may show on a field <paramref name="ph"/> pixels tall.</summary>
    public static double BossFloor(int ph) => ph - Math.Max(BossFloorMin, BossFloorShare * ph);

    /// <summary>A boss holds its centre at this share of the field's height, kept between the two rows.</summary>
    public const double BossRowShare = 0.24, BossRowMin = 14, BossRowMax = 30;

    /// <summary>The row a boss holds on a field <paramref name="ph"/> pixels tall, before a kind rounds it.</summary>
    public static double BossRow(int ph) => Math.Clamp(BossRowShare * ph, BossRowMin, BossRowMax);

    /// <summary>The bottom of the enemy's whole extent, its parts' art included.</summary>
    public static double LowestPixel(Enemy enemy) => Extent(enemy).B;

    /// <summary>How many rows a boss's lowest pixel stays above the ship's lowest row.</summary>
    private const int ShipClearance = 16;

    /// <summary>True when the boss's lowest pixel stays clear of the ship (the ship is clamped to the bottom row of the field).</summary>
    public static bool ClearOfShip(RaiderGame game, Enemy enemy) => LowestPixel(enemy) <= game.PH - 1 - ShipClearance;

    // ---------------------------------------------------------------- a plain bot

    /// <summary>How close to its target column the bot stops steering.</summary>
    public const double SteerDeadZone = 1;

    /// <summary>One frame of the fight bot: fires always, keeps under the lowest open part, steps aside toward the column
    /// <paramref name="danger"/> names (a kind reports where it is safe while it winds up).</summary>
    public static void BotFrame(RaiderGame game, Func<RaiderGame, double?>? danger = null)
    {
        game.Input.Key(GameKey.Fire, true);
        double target = game.PX;
        Part? low = null; Enemy? owner = null;
        foreach (var enemy in game.Enemies)
            foreach (var part in enemy.Parts ?? [])
                if (part.State == PartState.Open && (low is null || enemy.Y + part.OY > owner!.Y + low.OY)) { low = part; owner = enemy; }
        if (low is not null) target = owner!.X + low.OX;
        if (danger?.Invoke(game) is { } avoid) target = avoid;
        game.Input.Key(GameKey.Left, game.PX > target + SteerDeadZone);
        game.Input.Key(GameKey.Right, game.PX < target - SteerDeadZone);
    }

    /// <summary>Makes a game consult <paramref name="behaviour"/> for its kind instead of the table, so a test can script a kind.</summary>
    public static void OverrideBehaviour(RaiderGame game, EnemyBehaviour behaviour) => game.BehaviourOverride = kind => kind == behaviour.Kind ? behaviour : null;
}

/// <summary>An enemy whose every hook is a delegate a test sets, for checking the machinery without a real kind.</summary>
internal sealed class ScriptBehaviour(EnemyKind kind) : EnemyBehaviour
{
    public override EnemyKind Kind => kind;
    public override string Name => "SCRIPT";
    private const uint ScriptColour = 0x44ccaa;

    /// <summary>The hit points and the score of a scripted enemy with no parts.</summary>
    private const double ScriptHp = 10;
    private const int ScriptScore = 500;

    private static readonly Sprite Pixel = new(1, 1);
    public override Sprite Body => Pixel;
    public override uint Colour => ScriptColour;
    public override double Hp => ScriptHp;
    public override int Score => ScriptScore;
    public bool IsBoss;
    public override bool Boss => IsBoss;
    public Action<RaiderGame, Enemy>? OnSpawn, OnUpdate, OnDown;
    public Action<RaiderGame, Enemy, Part, Bullet, bool>? OnPartHit;
    public Action<RaiderGame, Enemy, Part>? OnPartDown;
    public Func<RaiderGame, Enemy, bool>? OnTouches;
    public Action<RaiderView.Canvas, Enemy>? OnDraw;
    public override void Spawn(RaiderGame g, Enemy e) => OnSpawn?.Invoke(g, e);
    public override void Update(RaiderGame g, Enemy e) => OnUpdate?.Invoke(g, e);
    public override void PartHit(RaiderGame g, Enemy e, Part p, Bullet b, bool stopped) => OnPartHit?.Invoke(g, e, p, b, stopped);
    public override void PartDown(RaiderGame g, Enemy e, Part p) => OnPartDown?.Invoke(g, e, p);
    public override void Down(RaiderGame g, Enemy e) => OnDown?.Invoke(g, e);
    public override bool Touches(RaiderGame g, Enemy e) => OnTouches?.Invoke(g, e) ?? false;
    public override void Draw(RaiderView.Canvas c, Enemy e) => OnDraw?.Invoke(c, e);
}
