using System.Reflection;
using FeedRaider.Engine.Raider;
using Xunit;

namespace FeedRaider.Tests.Enemies;

/// <summary>
/// The colour rule every enemy shares, read off the field as the view drew it: gold on crimson is where to shoot, blue is a shield and
/// what makes it, white is about to hurt. A text cell averages a two by two block of field pixels, so the checks read field pixels:
/// what holds for every pixel of a part holds for every cell it is shown in.
/// </summary>
public class ColourRuleTests
{
    /// <summary>Frames a kind is watched with no shot fired at it: longer than the slowest boss takes to run through every attack once.</summary>
    private const int QuietFrames = 900;

    /// <summary>Frames a kind is fought for at most: the slowest boss falls to the bot well inside this.</summary>
    private const int FightFrames = 2400;

    /// <summary>The hues, in degrees, that read as blue or cyan, and the least saturation at which a pixel has a hue at all:
    /// the three shield blues, ice and teal fall inside, and the silver, frost and plum greys and violets fall outside.</summary>
    private const double BlueFrom = 165, BlueTo = 250, ColouredFrom = 0.2;

    /// <summary>The parts that make a shield: blue by the rule though a shot hurts them.</summary>
    private static readonly (EnemyKind Kind, string Part)[] ShieldMakers =
    [
        (EnemyKind.Bulwark, "pod-l"), (EnemyKind.Bulwark, "pod-r"), (EnemyKind.Warden, "emitter"),
    ];

    private static readonly PropertyInfo Invuln = typeof(RaiderGame).GetProperty(nameof(RaiderGame.Invuln))!;

    /// <summary>Frames of invulnerability handed to the ship each frame, so it lives through a whole fight and the enemy keeps attacking.</summary>
    private const int InvulnFrames = 2;

    internal static void KeepShipAlive(RaiderGame g) => Invuln.SetValue(g, InvulnFrames);

    internal static bool IsBlue(uint rgb)
    {
        int r = (int)(rgb >> 16 & 0xff), gr = (int)(rgb >> 8 & 0xff), b = (int)(rgb & 0xff);
        int max = Math.Max(r, Math.Max(gr, b)), min = Math.Min(r, Math.Min(gr, b));
        if (max == 0 || (max - min) / (double)max < ColouredFrom) return false;
        double span = max - min;
        const double sixth = 60, full = 360;
        double hue = max == r ? sixth * ((gr - b) / span) : max == gr ? sixth * (2 + (b - r) / span) : sixth * (4 + (r - gr) / span);
        if (hue < 0) hue += full;
        return hue >= BlueFrom && hue <= BlueTo;
    }

    private static readonly PropertyInfo ShipY = typeof(RaiderGame).GetProperty(nameof(RaiderGame.PY))!;

    /// <summary>How far under the field the ship is parked for a drawing, so neither it nor the ring round it lies over an enemy on a small field.</summary>
    private const int ParkedBelow = 40;

    /// <summary>The field with only the enemies and the page on it: the ship, shots, sparks and rings pass over a part without being the part.</summary>
    internal static Surface DrawEnemiesOnly(RaiderGame g, RaiderView view, Screen scr)
    {
        double shipY = g.PY;
        ShipY.SetValue(g, (double)(g.PH + ParkedBelow));
        var bullets = g.Bullets.ToArray(); var particles = g.Particles.ToArray(); var rings = g.Rings.ToArray(); var popups = g.Popups.ToArray();
        g.Bullets.Clear(); g.Particles.Clear(); g.Rings.Clear(); g.Popups.Clear();
        var f = EnemyKit.ClearPickupsAndDraw(g, view, scr);
        ShipY.SetValue(g, shipY);
        g.Bullets.AddRange(bullets); g.Particles.AddRange(particles); g.Rings.AddRange(rings); g.Popups.AddRange(popups);
        return f;
    }

    private static Sprite? ArtOf(Part p) => p.State == PartState.Dead ? p.Wreck : p.State == PartState.Open ? p.OpenArt ?? p.Closed : p.Closed;

    /// <summary>The field pixels each part's drawing covers, and which part shows at each: a pixel belongs to the last part drawn there.</summary>
    private static (Dictionary<(int X, int Y), Part> Owner, Dictionary<Part, int> Size) Owners(Enemy e)
    {
        var owner = new Dictionary<(int X, int Y), Part>();
        var size = new Dictionary<Part, int>();
        foreach (var p in e.Parts!)
        {
            if (ArtOf(p) is not { } art) continue;
            int x0 = (int)Math.Round(e.X + p.OX - art.W / 2.0), y0 = (int)Math.Round(e.Y + p.OY - art.H / 2.0), n = 0;
            for (int y = 0; y < art.H; y++)
                for (int x = 0; x < art.W; x++)
                    if ((art.Px[y * art.W + x] & Sprite.Opaque) != 0) { owner[(x0 + x, y0 + y)] = p; n++; }
            size[p] = n;
        }
        return (owner, size);
    }

    /// <summary>The share of a part that must be clear of the parts drawn over it for it to be judged: with a fifth or more covered,
    /// as when two halves cross, the mark may be what is hidden. And the share that must show below the score line: under half is a
    /// sliver of an enemy still stepping on.</summary>
    private const double UncoveredFrom = 0.8, ShowsFrom = 0.5;

    private static readonly (int DX, int DY)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>What is wrong with the colours of one enemy's parts this frame, one line each; empty when the rule holds. Every part
    /// it judged is added to <paramref name="judged"/> with whether a shot hurt it. <paramref name="sinking"/> says the enemy moved up or down the field this frame.</summary>
    private static IEnumerable<string> Breaks(RaiderGame g, Enemy e, bool sinking, Surface f, HashSet<(EnemyKind Kind, string Part, bool Hurt)> judged)
    {
        if (e.Flash > 0 || e.Hp <= 0) yield break;
        var (owner, size) = Owners(e);
        foreach (var p in e.Parts!)
        {
            if (p.Flash > 0 || p.State == PartState.Dead) continue;   // a hit whitens the whole part for a few frames
            if (ShieldMakers.Contains((e.Kind, p.Name))) continue;
            var mine = owner.Where(o => o.Value == p).Select(o => o.Key).ToList();
            if (mine.Count < size[p] * UncoveredFrom) continue;   // another part lies over it, and a shot meets that one first
            if (mine.Any(m => m.X < 0 || m.Y < 0 || m.X >= g.PW || m.Y >= g.PH)) continue;   // still sliding on
            if (sinking && mine.Any(m => m.Y < CardPainter.HudPixelRows)) continue;   // passing behind the score line on its way in or out
            mine.RemoveAll(m => m.Y < CardPainter.HudPixelRows);   // an enemy that holds under the score line is judged on what shows
            if (mine.Count < size[p] * ShowsFrom) continue;   // a sliver under the score line, on an enemy that comes on in steps
            bool hurt = p.State == PartState.Open && !e.Untouchable;
            judged.Add((e.Kind, p.Name, hurt));
            bool mark = false, strayMark = false, blue = false;
            foreach (var (x, y) in mine)
            {
                uint c = f.Get(x, y) & EnemyKit.RgbMask;
                if (IsBlue(c)) blue = true;
                if (c != Palette.WeakMark) continue;
                foreach (var (dx, dy) in Sides)
                {
                    if ((f.Get(x + dx, y + dy) & EnemyKit.RgbMask) != Palette.WeakBody) continue;
                    strayMark = true;
                    if (owner.TryGetValue((x + dx, y + dy), out var o) && o == p) mark = true;
                }
            }
            string what = $"{e.Kind} {p.Name} {p.State}";
            if (hurt && !mark) yield return $"{what}: takes damage and shows no gold on crimson";
            if (hurt && blue) yield return $"{what}: takes damage and shows blue";
            if (!hurt && strayMark) yield return $"{what}: takes no damage and shows gold on crimson";
        }
    }

    /// <summary>A kind with no parts takes damage anywhere on its body, so by the rule its body holds no blue.</summary>
    [Fact]
    public void A_kind_with_no_parts_has_no_blue_in_its_body()
    {
        var breaks = new SortedSet<string>();
        int judged = 0;
        foreach (var kind in Enum.GetValues<EnemyKind>().Where(k => k >= EnemyKind.Grunt))
        {
            if (EnemyKit.Game().SpawnKind(kind, 0.5).Parts is not null) continue;
            judged++;
            var body = RaiderGame.TableBehaviour(kind).Body;
            if (body.Px.Any(px => (px & Sprite.Opaque) != 0 && IsBlue(px & EnemyKit.RgbMask))) breaks.Add(kind.ToString());
        }
        Assert.True(breaks.Count == 0, string.Join(", ", breaks));
        Assert.True(judged >= 8, $"only {judged} kinds without parts were judged");
    }

    [Fact]
    public void A_part_shows_gold_on_crimson_and_no_blue_exactly_while_a_shot_hurts_it_on_every_field_size()
    {
        var breaks = new SortedSet<string>();
        var seen = new HashSet<EnemyKind>();
        var hurtSeen = new SortedSet<string>();
        var hurtJudged = new SortedSet<string>();
        foreach (var (cols, rows) in EnemyKit.FieldSizes)
            foreach (var kind in EnemyKit.NewKinds)
                foreach (bool fight in new[] { false, true })
                {
                    var g = EnemyKit.Game(cols, rows);
                    var first = g.SpawnKind(kind, 0.5);
                    if (first.Parts is null) break;
                    var view = new RaiderView(); var scr = new Screen(cols, rows);
                    var lastY = new Dictionary<Enemy, double>();
                    for (int i = 0; i < (fight ? FightFrames : QuietFrames) && g.Enemies.Count > 0; i++)
                    {
                        KeepShipAlive(g);
                        if (fight) EnemyKit.BotFrame(g);
                        EnemyKit.StepSweeping(g);
                        if (g.ScreenFlash > 0) continue;   // a blast whitens the whole field for a few frames
                        var f = DrawEnemiesOnly(g, view, scr);
                        var judged = new HashSet<(EnemyKind Kind, string Part, bool Hurt)>();
                        foreach (var e in g.Enemies.Where(e => e.Parts is not null))
                        {
                            seen.Add(e.Kind);
                            foreach (var p in e.Parts!.Where(p => p.State == PartState.Open && !e.Untouchable && !ShieldMakers.Contains((e.Kind, p.Name))))
                                hurtSeen.Add($"{cols}x{rows} {e.Kind} {p.Name}");
                            bool sinking = !lastY.TryGetValue(e, out double y) || e.Y != y;
                            lastY[e] = e.Y;
                            foreach (string b in Breaks(g, e, sinking, f, judged)) breaks.Add($"{cols}x{rows} {b}");
                        }
                        foreach (var j in judged.Where(j => j.Hurt)) hurtJudged.Add($"{cols}x{rows} {j.Kind} {j.Part}");
                    }
                }
        Assert.True(breaks.Count == 0, string.Join("\n", breaks));
        // a part that is skipped whenever a shot can hurt it (hidden, flashing, off the field) would pass unread
        Assert.Empty(hurtSeen.Except(hurtJudged));
        foreach (var kind in EnemyKit.NewKinds.Where(k => RaiderGame.TableBehaviour(k) is { Boss: true })) Assert.Contains(kind, seen);
    }

    /// <summary>White shows on an enemy at most this many frames before the attack it warns of: a second and a half. The longest
    /// warning in the game, the outline of the Mirror's glare, runs 40.</summary>
    private const int WarnFrames = 45;

    /// <summary>The spacing in field pixels of the places the ship is tried at when a frame is asked whether any enemy's beam, blast or
    /// glare is live: finer than the narrowest of them.</summary>
    private const int ProbeStep = 3;

    /// <summary>Frames a body must have stood still for its next move down the field to count as a dive at the ship, so a boss that comes
    /// on in whole-pixel steps every other frame is not diving.</summary>
    private const int StillFrames = 3;

    private static readonly PropertyInfo ShipX = typeof(RaiderGame).GetProperty(nameof(RaiderGame.PX))!;

    /// <summary>Whether a beam, blast or glare of any enemy would hurt a ship somewhere on the field this frame.</summary>
    private static bool HarmIsLive(RaiderGame g)
    {
        double shipX = g.PX, shipY = g.PY;
        bool live = false;
        for (int y = 1; y < g.PH && !live; y += ProbeStep)
            for (int x = 1; x < g.PW && !live; x += ProbeStep)
            {
                ShipX.SetValue(g, (double)x); ShipY.SetValue(g, (double)y);
                live = g.Enemies.Any(e => g.BehaviourFor(e.Kind).Touches(g, e));
            }
        ShipX.SetValue(g, shipX); ShipY.SetValue(g, shipY);
        return live;
    }

    /// <summary>How many field pixels are warning white because an enemy is there: white with the enemies on and not with them off.</summary>
    private static int WhiteOnEnemies(RaiderGame g, RaiderView view, Screen scr)
    {
        var with = (uint[])DrawEnemiesOnly(g, view, scr).P.Clone();
        var enemies = g.Enemies.ToArray();
        g.Enemies.Clear();
        var without = DrawEnemiesOnly(g, view, scr).P;
        g.Enemies.AddRange(enemies);
        int n = 0;
        for (int i = 0; i < with.Length; i++)
            if ((with[i] & EnemyKit.RgbMask) == Palette.Tell && (without[i] & EnemyKit.RgbMask) != Palette.Tell) n++;
        return n;
    }

    /// <summary>One boss watched alone on the big field, fought or left alone: the frames white showed on an enemy, the frames an
    /// attack started (a hostile bolt or an enemy appeared, a beam or blast was live, or a body that stood still dived), and the frames
    /// an attack was cut short (a part or an enemy died, or the boss changed what it was doing or turned to leave).</summary>
    private static (List<int> White, List<int> Attacks, List<int> Cuts, int Frames) Watch(EnemyKind kind, bool fight)
    {
        var g = EnemyKit.Game();
        var boss = g.SpawnKind(kind, 0.5);
        var view = new RaiderView(); var scr = new Screen(EnemyKit.BigCols, EnemyKit.BigRows);
        var white = new List<int>(); var attacks = new List<int>(); var cuts = new List<int>();
        var bolts = new HashSet<Bullet>(); var bodies = new HashSet<Enemy>(g.Enemies);
        double lastY = boss.Y; int still = 0, mode = boss.Mode, alive = 0, i = 0;
        bool leaving = false;
        for (; i < (fight ? FightFrames : QuietFrames) && g.Enemies.Count > 0; i++)
        {
            KeepShipAlive(g);
            if (fight) EnemyKit.BotFrame(g);
            EnemyKit.StepSweeping(g);
            bool attack = HarmIsLive(g);
            foreach (var b in g.Bullets) if (!b.Friendly && bolts.Add(b)) attack = true;
            foreach (var e in g.Enemies) if (bodies.Add(e)) attack = true;
            if (boss.Y > lastY && still >= StillFrames) attack = true;
            still = boss.Y == lastY ? still + 1 : 0;
            lastY = boss.Y;
            if (attack) attacks.Add(i);
            int living = g.Enemies.Count + g.Enemies.Sum(e => e.Parts?.Count(p => p.State != PartState.Dead) ?? 0);
            if (living < alive || boss.Mode != mode || boss.Leaving != leaving) cuts.Add(i);
            alive = living; mode = boss.Mode; leaving = boss.Leaving;
            bool flashing = g.ScreenFlash > 0 || g.Enemies.Any(e => e.Flash > 0 || e.Parts is { } parts && parts.Any(p => p.Flash > 0));
            if (!flashing && WhiteOnEnemies(g, view, scr) > 0) white.Add(i);
        }
        if (g.Enemies.Count == 0) cuts.Add(i);
        return (white, attacks, cuts, i);
    }

    [Fact]
    public void White_shows_on_a_boss_only_in_the_frames_before_one_of_its_attacks()
    {
        var unwarned = new List<string>();
        foreach (var kind in EnemyKit.NewKinds.Where(k => RaiderGame.TableBehaviour(k) is { Boss: true }))
        {
            int whiteFrames = 0, attackFrames = 0;
            foreach (bool fight in new[] { false, true })
            {
                var (white, attacks, cuts, frames) = Watch(kind, fight);
                whiteFrames += white.Count; attackFrames += attacks.Count;
                foreach (int f in white)
                {
                    if (f + WarnFrames >= frames && !cuts.Contains(frames)) continue;   // the watch ended before the attack could come
                    bool Soon(int at) => at >= f && at <= f + WarnFrames;
                    if (!attacks.Any(Soon) && !cuts.Any(Soon)) unwarned.Add($"{kind} {(fight ? "fought" : "left alone")} frame {f}");
                }
            }
            Assert.True(whiteFrames > 0, $"{kind} never showed white");
            Assert.True(attackFrames > 0, $"{kind} never attacked");
        }
        Assert.True(unwarned.Count == 0, "white with no attack inside " + WarnFrames + " frames:\n" + string.Join("\n", unwarned.Take(40)));
    }
}
