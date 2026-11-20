namespace FeedRaider.Engine.Raider;

/// <summary>
/// The machinery an enemy made of parts stands on: hit tests per part, owner hit points summed from the parts, adds born mid-frame,
/// shots sent back, and the voices a boss speaks with. Every hook an enemy needs is here or in <c>Enemies/</c>, so a new enemy
/// never edits <c>RaiderGame.cs</c>. Nothing in this file runs for an enemy without parts, an enemy or a voice, and nothing here draws
/// from the main random stream except on a path such an enemy never reaches.
/// </summary>
public sealed partial class RaiderGame
{
    /// <summary>True while <see cref="UpdateEnemies"/> walks <see cref="Enemies"/>: an add born then waits in <see cref="_born"/>.</summary>
    private bool _walking;
    private readonly List<Enemy> _born = [];

    // ---------------------------------------------------------------- making and adding

    /// <summary>Puts a new enemy on the field, or holds it until the walk over the enemies ends.</summary>
    private void Add(Enemy e)
    {
        if (_walking) _born.Add(e); else Enemies.Add(e);
    }

    private void EndWalk()
    {
        _walking = false;
        if (_born.Count == 0) return;
        Enemies.AddRange(_born);
        _born.Clear();
    }

    /// <summary>The enemy's own arrival hook, then the owner's hit points from its parts.</summary>
    private void Born(Enemy e, EnemyBehaviour behaviour)
    {
        behaviour.Spawn(this, e);
        if (e.Parts is not null) SyncHp(e);
    }

    /// <summary>Puts a whole enemy of a kind at a spot: a boss's gnat, a nest's brood. It is born at once, or at the end of the frame's
    /// walk when the caller is an enemy's update. A parent makes it count as part of the same live wave.</summary>
    internal Enemy SpawnAdd(EnemyKind kind, double x, double y, Enemy? parent = null) =>
        SpawnEnemy(new EnemySpawn(kind, FieldShare(x), 0, 0, 0, null), parent?.Live ?? false, parent?.Source, parent?.Header, e =>
        {
            e.X = x; e.BaseX = x; e.Y = y;
            e.Parent = parent;
        });

    /// <summary>A field x as a share of the width, 0 to 1; the middle while the field has no width.</summary>
    private double FieldShare(double x) => PW > 0 ? x / PW : 0.5;

    /// <summary>Puts an enemy with parts at a spot, as the enemy's own arrival would, or as a test builds one by hand.</summary>
    internal Enemy SpawnParts(EnemyKind kind, double x, double y, params Part[] parts)
    {
        var e = SpawnKind(kind, FieldShare(x));
        e.X = x; e.BaseX = x; e.Y = y;
        SetParts(e, parts);
        return e;
    }

    /// <summary>Gives an enemy these parts, replacing any it had (a phase that changes the silhouette), and sets its hit points to match.</summary>
    internal void SetParts(Enemy e, params Part[] parts)
    {
        e.Parts = [.. parts];
        e.PartBox.Frame = Enemy.PartBoxStale;
        SyncHp(e);
    }

    /// <summary>A dead part comes back with this many hit points, open.</summary>
    internal void Revive(Enemy e, Part p, double hp)
    {
        p.State = PartState.Open;
        p.Hp = hp;
        p.MaxHp = Math.Max(p.MaxHp, hp);
        p.Timer = 0;
        e.PartBox.Frame = Enemy.PartBoxStale;
        SyncHp(e);
    }

    /// <summary>The owner's hit points are the sum over its vital parts, so a bar and an <c>Hp &gt; 0</c> test keep working.
    /// An owner with no vital part at all keeps its starting hit points.</summary>
    private static void SyncHp(Enemy e)
    {
        if (e.Parts is null) return;
        double hp = 0, max = 0;
        bool any = false;
        foreach (var p in e.Parts)
        {
            if (!p.Vital) continue;
            any = true;
            max += p.MaxHp;
            if (p.State != PartState.Dead) hp += Math.Max(0, p.Hp);
        }
        if (!any) return;
        e.Hp = hp; e.MaxHp = max;
    }

    private static bool AllVitalDead(Enemy e)
    {
        bool any = false;
        foreach (var p in e.Parts!)
        {
            if (!p.Vital) continue;
            any = true;
            if (p.State != PartState.Dead) return false;
        }
        return any;
    }

    /// <summary>True when everything an enemy with parts shows has left the field, judged by its own extent and not by a constant size.</summary>
    private bool OffField(Enemy e)
    {
        double l = -e.Sprite.W / 2.0, r = e.Sprite.W / 2.0, t = -e.Sprite.H / 2.0, b = e.Sprite.H / 2.0;
        foreach (var p in e.Parts!)
        {
            l = Math.Min(l, p.OX - p.HW); r = Math.Max(r, p.OX + p.HW);
            t = Math.Min(t, p.OY - p.HH); b = Math.Max(b, p.OY + p.HH);
        }
        return e.Y + t > PH + OffFieldMargin || e.Leaving && e.Y + b < 0 || e.X + r < -OffSideMargin || e.X + l > PW + OffSideMargin;
    }

    // ---------------------------------------------------------------- hits

    private const double SparkSpreadX = 2.2, SparkRiseY = 1.6;
    private const int SparkLife = 5, SparkLifeSpread = 5;
    private const double NoDistance = 1e-6;   // a distance under this has no direction
    private const double TouchMarginX = 1, TouchMarginY = 2;   // px of slack a touch allows beyond the box, wider than tall

    /// <summary>One white spark that rises and fades fast: what a shot that does no damage leaves.</summary>
    private void PingSpark(double x, double y) =>
        AddParticle(new Particle { X = x, Y = y, VX = (R() - 0.5) * SparkSpreadX, VY = -R() * SparkRiseY, Life = SparkLife, Colour = Palette.White, Kind = ParticleKind.Spark });

    /// <summary>The white and gold sparks a hit throws.</summary>
    private void HitSparks(double x, double y)
    {
        for (int i = 0; i < Tuning.SparksPerHit; i++)
            AddParticle(new Particle { X = x, Y = y, VX = (R() - 0.5) * SparkSpreadX, VY = -R() * SparkRiseY, Life = SparkLife + (int)(R() * SparkLifeSpread), Colour = R() < CoinFlip ? Palette.White : Palette.Gold, Kind = ParticleKind.Spark });
    }

    private void Shrug(double x, double y)
    {
        PingSpark(x, y);
        Sound(SfxNames.Ping, x);
    }

    /// <summary>How many part boxes shots have been tested against, in all: a shot outside an enemy's <see cref="Enemy.PartBox"/> adds none. For tests.</summary>
    internal int PartBoxTests { get; private set; }

    /// <summary>True when a point lies inside the box of the enemy's living parts and wrecks (made once a frame; parts only die or change
    /// inside a frame, and a hook that changes them drops the box, so a stale one is never smaller than the truth).</summary>
    private bool InPartBox(Enemy e, double x, double y)
    {
        if (e.PartBox.Frame != Frame)
        {
            double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, bt = double.MinValue;
            foreach (var p in e.Parts!)
            {
                if (p.State == PartState.Dead && p.Wreck is null) continue;
                l = Math.Min(l, p.OX - p.HW); r = Math.Max(r, p.OX + p.HW);
                t = Math.Min(t, p.OY - p.HH); bt = Math.Max(bt, p.OY + p.HH);
            }
            e.PartBox = (Frame, l, t, r, bt);
        }
        var box = e.PartBox;
        double dx = x - e.X, dy = y - e.Y;
        return dx >= box.L && dx <= box.R && dy >= box.T && dy <= box.B;
    }

    /// <summary>A friendly shot against the parts of one enemy, last part first (the one drawn on top). True when the shot met one.
    /// An armoured part, a shield and a wreck stop it with a spark; an open part takes its damage. No part is shoved: there is no knock-back.</summary>
    private bool HitParts(Enemy e, Bullet b)
    {
        var parts = e.Parts!;
        if (!InPartBox(e, b.X, b.Y)) return false;
        var behaviour = BehaviourFor(e.Kind);
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            var p = parts[i];
            PartBoxTests++;
            if (Math.Abs(b.X - (e.X + p.OX)) > p.HW || Math.Abs(b.Y - (e.Y + p.OY)) > p.HH) continue;
            if (p.State == PartState.Dead)
            {
                if (p.Wreck is null) continue;   // nothing there: the shot flies on
                Bullets.Remove(b);
                Shrug(b.X, b.Y);
                return true;
            }
            bool stopped = p.State == PartState.Armoured || e.ShieldedBy is not null;
            behaviour.PartHit(this, e, p, b, stopped);
            e.PartBox.Frame = Enemy.PartBoxStale;   // the hook may have moved or swapped parts
            if (!b.Friendly) return true;   // the kind sent it back
            Bullets.Remove(b);
            if (stopped) Shrug(b.X, b.Y);
            else DamagePart(e, p, b.Dmg, b.X, b.Y, false);
            return true;
        }
        return false;
    }

    /// <summary>Takes hit points off a part. At zero it goes dead, scores, tells the enemy, and may drop a pickup; the owner goes down
    /// when its last vital part does. True when the owner went down.</summary>
    private bool DamagePart(Enemy e, Part p, double dmg, double hx, double hy, bool bomb)
    {
        p.Hp -= dmg;
        p.Flash = Tuning.FlashFrames;
        if (!bomb) HitSparks(hx, hy);
        if (p.Hp > 0)
        {
            SyncHp(e);
            if (!bomb) { Sound(SfxNames.Hit, hx); AddShake(Tuning.ShakeHit); }
            return false;
        }
        p.Hp = 0;
        p.State = PartState.Dead;
        SyncHp(e);
        double px = e.X + p.OX, py = e.Y + p.OY;
        Score += p.Score;
        if (p.Score > 0) Popup("+" + p.Score, px, py - ScorePopupRise, Palette.White);
        AddRing(px, py, KillRingStart, Tuning.RingRadiusSmall, SmallKillRingColour, Tuning.RingFrames);
        if (!bomb) Sound(SfxNames.Kill, px, MidSoundSize);
        AddShake(Tuning.ShakeSmallKill);
        for (int i = 0; i < Tuning.DebrisSmallKill; i++)
        {
            double a = R() * Math.Tau, sp = DebrisMinSpeed + R() * DebrisSpeedSpan;
            uint c = DrawDebrisColour(e.Kind);
            AddParticle(new Particle { X = px + (R() - 0.5) * p.HW, Y = py + (R() - 0.5) * p.HH, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp - DebrisLift, Life = DebrisMinLife + (int)(R() * DebrisLifeSpan), Colour = c, Size = DrawDebrisSize(), Kind = ParticleKind.Debris });
        }
        MaybeDropFromPart(e, p);
        BehaviourFor(e.Kind).PartDown(this, e, p);
        e.PartBox.Frame = Enemy.PartBoxStale;
        if (e.Parts is null || !e.Parts.Contains(p)) return false;   // the enemy swapped its parts: it decides what that means
        if (!AllVitalDead(e))
        {
            Cue(e, VoiceCue.PartLost);
            return false;
        }
        Kill(e, bomb);
        return true;
    }

    /// <summary>A bomb hurts every open part by <see cref="Tuning.BombPartDamage"/>, and armour not at all.</summary>
    private void BombParts(Enemy e)
    {
        foreach (var p in e.Parts!.ToArray())
            if (p.State == PartState.Open && DamagePart(e, p, Tuning.BombPartDamage, e.X + p.OX, e.Y + p.OY, true)) return;
    }

    /// <summary>The hit points the feed-clearing like leaves a part at, at the least: it never kills one.</summary>
    private const double ClearLeavesHp = 1;

    /// <summary>The feed-clearing like takes <see cref="Tuning.ClearPartShare"/> of each living part's full hit points, but never kills one:
    /// a part is left at one hit point at the least, and one already under that is left alone.</summary>
    private static void ClearParts(Enemy e)
    {
        foreach (var p in e.Parts!)
        {
            if (p.State == PartState.Dead) continue;
            p.Hp = Math.Max(Math.Min(p.Hp, ClearLeavesHp), p.Hp - p.MaxHp * Tuning.ClearPartShare);
            p.Flash = Tuning.FlashFrames;
        }
        SyncHp(e);
    }

    /// <summary>A finished hold clears the parts of every enemy that has some, is on the field and can be touched, and says so once.</summary>
    private void ClearPartsInFight()
    {
        bool any = false;
        foreach (var e in Enemies)
        {
            if (e.Parts is null || e.Untouchable || e.Y <= BombReachY) continue;
            ClearParts(e);
            any = true;
        }
        if (any) Popup(PartsPopup, PX, PY - PartsPopupRise, Palette.White);
    }

    /// <summary>Sends a friendly shot back at the ship from where it is: it turns hostile, keeps its damage, and flies at the enemy bullet speed.</summary>
    internal void Reflect(Bullet b)
    {
        double dx = PX - b.X, dy = PY - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < NoDistance) { dx = 0; dy = 1; d = 1; }
        b.VX = dx / d * Tuning.EnemyBoltSpeed;
        b.VY = dy / d * Tuning.EnemyBoltSpeed;
        b.Friendly = false;
        b.Reflected = true;
    }

    // ---------------------------------------------------------------- the ship

    /// <summary>The sprite-box touch of an enemy without parts.</summary>
    private bool BodyTouchesShip(Enemy e) =>
        Math.Abs(e.X - PX) <= e.Sprite.W / 2.0 + TouchMarginX && Math.Abs(e.Y - PY) <= e.Sprite.H / 2.0 + TouchMarginY;

    /// <summary>The ship is in the enemy's body (a sprite box, or any living part's box), or in a shape its enemy reports (a beam).
    /// A dead part is scenery, wreck or not.</summary>
    private bool TouchesShip(Enemy e)
    {
        if (e.Parts is null)
        {
            if (BodyTouchesShip(e)) return true;
        }
        else
        {
            foreach (var p in e.Parts)
                if (p.State != PartState.Dead && Math.Abs(PX - (e.X + p.OX)) <= p.HW + TouchMarginX && Math.Abs(PY - (e.Y + p.OY)) <= p.HH + TouchMarginY) return true;
        }
        return BehaviourFor(e.Kind).Touches(this, e);
    }
}
