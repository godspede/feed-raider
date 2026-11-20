namespace FeedRaider.Engine.Raider;

public enum PickupKind { Shield, Bomb, Weapon }

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
    public double Angle => double.RadiansToDegrees(Math.Atan2(VX, VyMul * Tuning.ShipShotSpeed));
}

/// <summary>Drops, pickups, the shield and the weapon levels. Kept apart from the rest of the sim; the hooks in
/// <c>RaiderGame.cs</c> are <see cref="MaybeDrop"/>, <see cref="UpdatePickups"/>, <see cref="AbsorbWithShield"/>,
/// <see cref="LoseWeaponLevel"/> and <see cref="WeaponPattern"/>.</summary>
public sealed partial class RaiderGame
{
    public readonly List<Pickup> Pickups = [];
    public int WeaponLevel { get; private set; } = 1;
    public int Shield { get; private set; } = Tuning.ShieldHits;
    /// <summary>A frame far enough back that no flash is still showing from it.</summary>
    private const long LongAgoFrame = -1000;
    /// <summary>The frame the shield last took a hit, for the ring flash.</summary>
    public long ShieldHitFrame { get; private set; } = LongAgoFrame;
    /// <summary>True while the invulnerability after an absorbed hit runs, so the ship does not flicker for it.</summary>
    public bool InvulnIsShield { get; private set; }

    // a separate generator, so drops never shift the rest of the sim's randomness
    private ulong _dropRs;
    /// <summary>Mixed into the seed so the drop stream starts apart from the main one.</summary>
    private const ulong DropStreamSalt = 0xD20B5EEDUL;
    /// <summary>The next draw of the drop stream, in [0, 1).</summary>
    private double DropR()
    {
        if (_dropRs == 0) _dropRs = Hashing.Mix(_seed ^ DropStreamSalt) | 1;
        return NextUnit(ref _dropRs);
    }

    // ---------------------------------------------------------------- drops

    /// <summary>Rolls the drop for a dead enemy. A beaten boss and the last enemy of a live wave always drop (one pickup between them);
    /// a lesser gunship drops at <see cref="Tuning.LesserDropChance"/>; with <see cref="Tuning.MaxPickups"/> already out, nothing drops.</summary>
    private void MaybeDrop(Enemy e, bool waveCleared)
    {
        if (Pickups.Count >= Tuning.MaxPickups) return;
        double chance = ReferenceEquals(e, Boss) ? 1 : TableBehaviour(e.Kind).DropChanceFor(e);
        if (!waveCleared && DropR() >= chance) return;
        Pickups.Add(new Pickup { Kind = PickDropKind(), X = e.X, BaseX = e.X, Y = e.Y });
    }

    /// <summary>A part shot off a bigger enemy may drop a pickup too: a small roll on the drop stream, which an enemy without parts never reaches.</summary>
    private void MaybeDropFromPart(Enemy e, Part p)
    {
        if (Pickups.Count >= Tuning.MaxPickups) return;
        if (DropR() >= Tuning.PartDropChance) return;
        Pickups.Add(new Pickup { Kind = PickDropKind(), X = e.X + p.OX, BaseX = e.X + p.OX, Y = e.Y + p.OY });
    }

    /// <summary>The weights of the three kinds as the player stands: a kind they are full on comes up a quarter as often.</summary>
    public (double Shield, double Bomb, double Weapon) DropWeights() => (
        Tuning.WeightShield * (Shield >= Tuning.ShieldHits ? Tuning.WeightFull : 1),
        Tuning.WeightBomb * (Bombs >= Tuning.BombCap ? Tuning.WeightFull : 1),
        Tuning.WeightWeapon * (WeaponLevel >= Tuning.MaxWeaponLevel ? Tuning.WeightFull : 1));

    private PickupKind PickDropKind() => KindFor(DropR(), DropWeights());

    /// <summary>The kind a uniform draw <paramref name="u"/> (0..1) picks from these weights.</summary>
    internal static PickupKind KindFor(double u, (double Shield, double Bomb, double Weapon) w)
    {
        double r = u * (w.Shield + w.Bomb + w.Weapon);
        return r < w.Shield ? PickupKind.Shield : r < w.Shield + w.Bomb ? PickupKind.Bomb : PickupKind.Weapon;
    }

    // ---------------------------------------------------------------- pickups

    /// <summary>Puts a pickup on the field (for tests and scripted scenes); it counts against the cap like any other.</summary>
    internal Pickup SpawnPickup(PickupKind kind, double x, double y)
    {
        var p = new Pickup { Kind = kind, X = x, BaseX = x, Y = y };
        Pickups.Add(p);
        return p;
    }

    /// <summary>Pixels a pickup keeps from the side walls, and how far below the field it may drift before it is gone.</summary>
    private const int PickupEdgeMargin = 4;

    private void UpdatePickups()
    {
        for (int i = Pickups.Count - 1; i >= 0; i--)
        {
            var p = Pickups[i];
            p.Age++;
            p.Y += Tuning.PickupDrift;
            p.X = Math.Clamp(p.BaseX + Math.Sin(p.Age * Tuning.PickupBobRate) * Tuning.PickupBobAmp, PickupEdgeMargin, Math.Max(PickupEdgeMargin, PW - PickupEdgeMargin));
            if (p.Age >= Tuning.PickupLifeFrames || p.Y > PH + PickupEdgeMargin) { Pickups.RemoveAt(i); continue; }
            if (Alive && Math.Abs(p.X - PX) <= Ship.W / 2.0 + Tuning.PickupReach && Math.Abs(p.Y - PY) <= Ship.H / 2.0 + Tuning.PickupReach)
            {
                Pickups.RemoveAt(i);
                Collect(p);
            }
        }
    }

    private const uint ShieldColour = 0x4fe8ff, BombColour = 0xff9a3c, WeaponColour = 0xffd84a;

    public static uint PickupColour(PickupKind k) => k switch { PickupKind.Shield => ShieldColour, PickupKind.Bomb => BombColour, _ => WeaponColour };

    /// <summary>Sparks in the ring that bursts from a collected pickup.</summary>
    private const int CollectSparks = 12;
    /// <summary>Every this-many sparks of a pickup, from the first, is white.</summary>
    private const int CollectWhiteEvery = 3;
    /// <summary>The collect burst: jitter of each spark's angle (radians), its speed range and life range (frames).</summary>
    private const double CollectAngleJitter = 0.5, CollectSpeedMin = 0.5, CollectSpeedSpread = 1.3;
    private const int CollectLifeMin = 12, CollectLifeSpread = 8;
    /// <summary>The collect ring's radius at the start and end (pixels) and its length (frames), and how far above the ship the pop-up shows.</summary>
    private const int CollectRingFrom = 2, CollectRingTo = 10, CollectRingFrames = 12, CollectPopupRise = 10;
    /// <summary>The flash when the shield takes a hit: a coloured ring and a white one (start radius, end radius, frames), and a ring of
    /// sparks (count, distance from the ship, speed, frames of life).</summary>
    private const int ShieldRingFrom = 7, ShieldRingTo = 16, ShieldRingFrames = 10;
    private const int ShieldFlashFrom = 5, ShieldFlashTo = 11, ShieldFlashFrames = 7;
    private const int ShieldSparks = 8, ShieldSparkLife = 8;
    private const double ShieldSparkRadius = 8, ShieldSparkSpeed = 1.2;

    private void Collect(Pickup p)
    {
        uint colour = PickupColour(p.Kind);
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
                sound = SfxNames.PickupWeapon; text = "+WEAPON";
                break;
        }
        if (wasted) { Score += Tuning.ScorePickupOverflow; text = "+" + Tuning.ScorePickupOverflow; }
        Sound(sound, p.X);
        for (int i = 0; i < CollectSparks; i++)
        {
            double a = i / (double)CollectSparks * Math.Tau + R() * CollectAngleJitter, sp = CollectSpeedMin + R() * CollectSpeedSpread;
            AddParticle(new Particle { X = p.X, Y = p.Y, VX = Math.Cos(a) * sp, VY = Math.Sin(a) * sp, Life = CollectLifeMin + (int)(R() * CollectLifeSpread), Colour = i % CollectWhiteEvery == 0 ? Palette.White : colour, Kind = ParticleKind.Spark });
        }
        AddRing(p.X, p.Y, CollectRingFrom, CollectRingTo, colour, CollectRingFrames);
        Popup(text, PX, PY - CollectPopupRise, colour);
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
        AddRing(PX, PY, ShieldRingFrom, ShieldRingTo, PickupColour(PickupKind.Shield), ShieldRingFrames);
        AddRing(PX, PY, ShieldFlashFrom, ShieldFlashTo, Palette.White, ShieldFlashFrames);
        foreach (var e in Enemies)
        {
            double dx = e.X - PX, dy = e.Y - PY, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            if (d < Tuning.ShieldShoveRadius && e.Parts is null) { e.KX += dx / d * Tuning.KnockShield; e.KY += dy / d * Tuning.KnockShield; }   // a thing made of parts holds its place
        }
        for (int i = 0; i < ShieldSparks; i++)
        {
            double a = R() * Math.Tau;
            AddParticle(new Particle { X = PX + Math.Cos(a) * ShieldSparkRadius, Y = PY + Math.Sin(a) * ShieldSparkRadius, VX = Math.Cos(a) * ShieldSparkSpeed, VY = Math.Sin(a) * ShieldSparkSpeed, Life = ShieldSparkLife, Colour = PickupColour(PickupKind.Shield), Kind = ParticleKind.Spark });
        }
        return true;
    }

    // ---------------------------------------------------------------- weapon

    public static int FireIntervalFor(int level) => level >= Tuning.MaxWeaponLevel ? Tuning.FireIntervalMax : Tuning.FireInterval;

    /// <summary>The weapon level whose volley is a twin shot and the slanted pair.</summary>
    private const int AngledPairLevel = 3;
    /// <summary>The slanted pair that <see cref="AngledPairLevel"/> has and the like's power adds to the levels below it.</summary>
    private static readonly Shot[] AngledPair = [new(-4, 2, -0.75, 0.95, 0.4), new(4, 2, 0.75, 0.95, 0.4)];

    /// <summary>The bullets one trigger pull makes at a level. <paramref name="liked"/> (the like's power) adds the angled pair to a
    /// level that does not have one. Damage per bullet falls as the count rises, so a higher level shreds but a boss still lasts.</summary>
    public static Shot[] WeaponPattern(int level, bool liked = false)
    {
        level = Math.Clamp(level, 1, Tuning.MaxWeaponLevel);
        Shot[] basis = level switch
        {
            1 => [new(0, 0, 0, 1, 2.0)],
            2 => [new(-2, 0, 0, 1, 1.1), new(2, 0, 0, 1, 1.1)],
            AngledPairLevel => [new(-2, 0, 0, 1, 1.0), new(2, 0, 0, 1, 1.0), .. AngledPair],
            _ => [new(0, 0, 0, 1, 1.0), new(-2, 1, -0.6, 0.98, 0.5), new(2, 1, 0.6, 0.98, 0.5), new(-4, 2, -1.2, 0.93, 0.4), new(4, 2, 1.2, 0.93, 0.4)],
        };
        if (liked && level < AngledPairLevel) basis = [.. basis, .. AngledPair];
        return basis;
    }

    /// <summary>Dying costs a level, never below 1.</summary>
    private void LoseWeaponLevel() => WeaponLevel = Math.Max(1, WeaponLevel - 1);
}
