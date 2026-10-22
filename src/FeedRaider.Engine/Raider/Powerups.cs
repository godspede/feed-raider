namespace FeedRaider.Engine.Raider;

public enum PickupKind { Shield, Bomb, Power }

public sealed class Pickup
{
    public required PickupKind Kind;
    public double X, Y, BaseX;
    public int Age;
    public int Left => Tuning.PickupLifeFrames - Age;
    /// <summary>True for the last <see cref="Tuning.PickupBlinkFrames"/> frames of its life.</summary>
    public bool Blinking => Left <= Tuning.PickupBlinkFrames;
}

/// <summary>One bullet of a weapon volley: offset from the muzzle, sideways speed, forward speed as a fraction of the bullet speed, damage.</summary>
public readonly record struct Shot(double Dx, double Dy, double VX, double VyMul, double Dmg)
{
    /// <summary>The shot's angle from straight up, in degrees (negative is left).</summary>
    public double Angle => Math.Atan2(VX, VyMul * Tuning.PlayerBulletSpeed) * 180 / Math.PI;
}

/// <summary>Drops, pickups, the shield and the weapon levels. Kept apart from the rest of the sim; the hooks in
/// <c>RaiderGame.cs</c> are <see cref="MaybeDrop"/>, <see cref="UpdatePickups"/>, <see cref="AbsorbWithShield"/>,
/// <see cref="LoseWeaponLevel"/> and <see cref="WeaponPattern"/>.</summary>
public sealed partial class RaiderGame
{
    public readonly List<Pickup> Pickups = [];
    public int WeaponLevel { get; private set; } = 1;
    public int Shield { get; private set; }
    /// <summary>The frame the shield last took a hit, for the ring flash.</summary>
    public long ShieldHitFrame { get; private set; } = -1000;
    /// <summary>True while the grace after an absorbed hit runs, so the ship does not flicker for it.</summary>
    public bool InvulnIsShield { get; private set; }

    // a separate generator, so drops never shift the rest of the sim's randomness
    private ulong _dropRs;
    private double DropR()
    {
        if (_dropRs == 0) _dropRs = Hashing.Mix(_seed ^ 0xD20B5EEDUL) | 1;
        _dropRs ^= _dropRs >> 12; _dropRs ^= _dropRs << 25; _dropRs ^= _dropRs >> 27;
        return ((_dropRs * 0x2545F4914F6CDD1DUL) >> 11) / (double)(1UL << 53);
    }

    // ---------------------------------------------------------------- drops

    public static double DropChance(EnemyKind kind) => kind switch
    {
        EnemyKind.Gunship => 1,
        EnemyKind.Carrier or EnemyKind.Turret => Tuning.DropCarrier,
        EnemyKind.Shard => 0,                        // its splitter already had its chance
        _ => Tuning.DropGrunt,
    };

    /// <summary>Rolls the drop for a dead enemy. A gunship and the last enemy of a live wave always drop (one pickup between them);
    /// with <see cref="Tuning.MaxPickups"/> already out, nothing drops.</summary>
    private void MaybeDrop(Enemy e, bool waveCleared)
    {
        if (Pickups.Count >= Tuning.MaxPickups) return;
        if (!waveCleared && DropR() >= DropChance(e.Kind)) return;
        Pickups.Add(new Pickup { Kind = PickDropKind(), X = e.X, BaseX = e.X, Y = e.Y });
    }

    /// <summary>The weights of the three kinds as the player stands now: a kind they are full on comes up a quarter as often.</summary>
    public (double Shield, double Bomb, double Power) DropWeights() => (
        Tuning.WeightShield * (Shield >= Tuning.ShieldHits ? Tuning.WeightFull : 1),
        Tuning.WeightBomb * (Bombs >= Tuning.BombCap ? Tuning.WeightFull : 1),
        Tuning.WeightPower * (WeaponLevel >= Tuning.MaxWeaponLevel ? Tuning.WeightFull : 1));

    private PickupKind PickDropKind()
    {
        return KindFor(DropR(), DropWeights());
    }

    /// <summary>The kind a uniform draw <paramref name="u"/> (0..1) picks from these weights.</summary>
    internal static PickupKind KindFor(double u, (double Shield, double Bomb, double Power) w)
    {
        double r = u * (w.Shield + w.Bomb + w.Power);
        return r < w.Shield ? PickupKind.Shield : r < w.Shield + w.Bomb ? PickupKind.Bomb : PickupKind.Power;
    }

    // ---------------------------------------------------------------- pickups

    /// <summary>Puts a pickup on the field (for tests and scripted scenes); it counts against the cap like any other.</summary>
    internal Pickup SpawnPickup(PickupKind kind, double x, double y)
    {
        var p = new Pickup { Kind = kind, X = x, BaseX = x, Y = y };
        Pickups.Add(p);
        return p;
    }

    private void UpdatePickups()
    {
        for (int i = Pickups.Count - 1; i >= 0; i--)
        {
            var p = Pickups[i];
            p.Age++;
            p.Y += Tuning.PickupDrift;
            p.X = Math.Clamp(p.BaseX + Math.Sin(p.Age * Tuning.PickupBobRate) * Tuning.PickupBobAmp, 4, Math.Max(4, PW - 4));
            if (p.Age >= Tuning.PickupLifeFrames || p.Y > PH + 4) { Pickups.RemoveAt(i); continue; }
            if (Alive && Math.Abs(p.X - PX) <= Plane.W / 2.0 + Tuning.PickupReach && Math.Abs(p.Y - PY) <= Plane.H / 2.0 + Tuning.PickupReach)
            {
                Pickups.RemoveAt(i);
                Collect(p);
            }
        }
    }

    public static uint PickupColor(PickupKind k) => k switch { PickupKind.Shield => 0x4fe8ff, PickupKind.Bomb => 0xff9a3c, _ => 0xffd84a };

    private void Collect(Pickup p)
    {
        uint color = PickupColor(p.Kind);
        string sound, text;
        bool wasted;
        switch (p.Kind)
        {
            case PickupKind.Shield:
                wasted = Shield >= Tuning.ShieldHits;
                Shield = Tuning.ShieldHits;
                sound = SfxNames.PickupShield; text = "+SHIELD";
                break;
            case PickupKind.Bomb:
                wasted = Bombs >= Tuning.BombCap;
                if (!wasted) Bombs++;
                sound = SfxNames.PickupBomb; text = "+BOMB";
                break;
            default:
                wasted = WeaponLevel >= Tuning.MaxWeaponLevel;
                if (!wasted) WeaponLevel++;
                sound = SfxNames.PickupPower; text = "POWER UP";
                break;
        }
        if (wasted) { Score += Tuning.ScorePickupOverflow; text = "+" + Tuning.ScorePickupOverflow; }
        Sound(sound, p.X);
        for (int i = 0; i < 12; i++)
        {
            double a = i / 12.0 * Math.PI * 2 + R() * 0.5, sp = 0.5 + R() * 1.3;
            AddParticle(new Particle { X = p.X, Y = p.Y, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp, Life = 12 + (int)(R() * 8), Color = i % 3 == 0 ? 0xffffffu : color, Kind = ParticleKind.Spark });
        }
        AddRing(p.X, p.Y, 2, 10, color, 12);
        Popup(text, PX, PY - 10, color);
    }

    // ---------------------------------------------------------------- shield

    /// <summary>A hit on the ship: if the shield has charge it takes the hit instead, with a ring flash and a shove for whatever is near.</summary>
    private bool AbsorbWithShield()
    {
        if (Shield <= 0) return false;
        Shield--;
        ShieldHitFrame = Frame;
        Invuln = Tuning.ShieldInvulnFrames;
        InvulnIsShield = true;
        Sound(SfxNames.ShieldHit, PX);
        AddShake(Tuning.ShakeShield);
        AddRing(PX, PY, 7, 16, 0x4fe8ff, 10);
        AddRing(PX, PY, 5, 11, 0xffffff, 7);
        foreach (var e in Enemies)
        {
            double dx = e.X - PX, dy = e.Y - PY, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            if (d < 24) { e.KX += dx / d * Tuning.KnockShield; e.KY += dy / d * Tuning.KnockShield; }
        }
        for (int i = 0; i < 8; i++)
        {
            double a = R() * Math.PI * 2;
            AddParticle(new Particle { X = PX + Math.Cos(a) * 8, Y = PY + Math.Sin(a) * 8, VX = Math.Cos(a) * 1.2, VY = Math.Sin(a) * 1.2, Life = 8, Color = 0x4fe8ff, Kind = ParticleKind.Spark });
        }
        return true;
    }

    // ---------------------------------------------------------------- weapon

    public static int FireIntervalFor(int level) => level >= Tuning.MaxWeaponLevel ? Tuning.FireIntervalMax : Tuning.FireInterval;

    /// <summary>The bullets one trigger pull makes at a level. <paramref name="liked"/> (the like power-up) adds the angled pair to a
    /// level that does not have one. Damage per bullet falls as the count rises, so a higher level shreds but a boss still lasts.</summary>
    public static Shot[] WeaponPattern(int level, bool liked = false)
    {
        level = Math.Clamp(level, 1, Tuning.MaxWeaponLevel);
        Shot[] basis = level switch
        {
            1 => [new(0, 0, 0, 1, 2.0)],
            2 => [new(-2, 0, 0, 1, 1.5), new(2, 0, 0, 1, 1.5)],
            3 => [new(-2, 0, 0, 1, 1.5), new(2, 0, 0, 1, 1.5), new(-4, 2, -0.55, 0.95, 1.0), new(4, 2, 0.55, 0.95, 1.0)],
            _ => [new(0, 0, 0, 1, 1.0), new(-2, 1, -0.45, 0.98, 1.0), new(2, 1, 0.45, 0.98, 1.0), new(-4, 2, -0.95, 0.93, 1.0), new(4, 2, 0.95, 0.93, 1.0)],
        };
        if (liked && level < 3) basis = [.. basis, new(-4, 2, -0.55, 0.95, 1.0), new(4, 2, 0.55, 0.95, 1.0)];
        return basis;
    }

    /// <summary>Dying costs a level, never below 1.</summary>
    private void LoseWeaponLevel() => WeaponLevel = Math.Max(1, WeaponLevel - 1);
}
