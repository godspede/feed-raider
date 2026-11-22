namespace FeedRaider.Engine.Raider;

/// <summary>How a shot is drawn: the light line, the wide heavy line, the short slug of the guns, a missile and its trail, the fan's violet.</summary>
public enum ShotLook { Laser, Heavy, Slug, Missile, Spread }

/// <summary>One weapon the ship can hold: its name in a popup, its short name on the HUD, the frames between volleys, the shots of a
/// volley, how they look and the sound of a volley. A kind that <paramref name="Alternates"/> fires one of its shots a volley, in turn.</summary>
public sealed record WeaponKind(string Name, string Short, int Interval, Shot[] Shots, ShotLook Look, string Sound, bool Alternates = false);

/// <summary>The ten weapon kinds, weakest first; a weapon pickup moves the ship up one and a death down one. The numbers are in
/// <see cref="Tuning"/>.</summary>
public static class Weapons
{
    private const double Ahead = 1;
    private static readonly double HeavyAhead = Tuning.HeavySpeed / Tuning.ShipShotSpeed, SlugAhead = Tuning.SlugSpeed / Tuning.ShipShotSpeed;
    private static readonly double LaunchAhead = Tuning.MissileLaunchSpeed / Tuning.ShipShotSpeed;

    private static Shot Missile(double dx, double dmg) => new(dx, 0, 0, LaunchAhead, dmg, Homing: true);

    /// <summary>The offset of the <paramref name="i"/>th of <paramref name="count"/> launchers set <paramref name="gap"/> apart and centred on the middle.</summary>
    private static double AcrossWing(int i, int count, double gap) => (i - (count - 1) / 2.0) * gap;

    public static readonly WeaponKind[] Kinds =
    [
        new("LIGHT LASER", "LASER", Tuning.FireInterval, [new(0, 0, 0, Ahead, Tuning.GruntHp)], ShotLook.Laser, SfxNames.Shot),
        new("HEAVY LASER", "HEAVY", Tuning.HeavyInterval, [new(0, 0, 0, HeavyAhead, Tuning.HeavyDamage)], ShotLook.Heavy, SfxNames.ShotHeavy),
        new("TWIN LASER", "TWIN", Tuning.TwinInterval,
            [new(-Tuning.TwinGap, 0, 0, Ahead, Tuning.TwinDamage), new(Tuning.TwinGap, 0, 0, Ahead, Tuning.TwinDamage)], ShotLook.Laser, SfxNames.ShotTwin),
        new("MISSILES", "MISSILE", Tuning.MissileInterval,
            [Missile(-Tuning.MissileGap, Tuning.MissileDamage), Missile(Tuning.MissileGap, Tuning.MissileDamage)], ShotLook.Missile, SfxNames.ShotMissile),
        new("TWIN HEAVY", "TWIN-H", Tuning.TwinHeavyInterval,
            [new(-Tuning.TwinGap, 0, 0, HeavyAhead, Tuning.TwinHeavyDamage), new(Tuning.TwinGap, 0, 0, HeavyAhead, Tuning.TwinHeavyDamage)], ShotLook.Heavy, SfxNames.ShotTwinHeavy),
        new("SPREAD", "SPREAD", Tuning.SpreadInterval,
            [
                new(0, 0, 0, Ahead, Tuning.SpreadMidDamage),
                new(-Tuning.SpreadInnerGap, Tuning.SpreadInnerBack, -Tuning.SpreadInnerSlant, Ahead, Tuning.SpreadInnerDamage),
                new(Tuning.SpreadInnerGap, Tuning.SpreadInnerBack, Tuning.SpreadInnerSlant, Ahead, Tuning.SpreadInnerDamage),
                new(-Tuning.SpreadOuterGap, Tuning.SpreadOuterBack, -Tuning.SpreadOuterSlant, Ahead, Tuning.SpreadOuterDamage),
                new(Tuning.SpreadOuterGap, Tuning.SpreadOuterBack, Tuning.SpreadOuterSlant, Ahead, Tuning.SpreadOuterDamage),
            ], ShotLook.Spread, SfxNames.ShotSpread),
        new("MACHINE GUN", "MACHINE GUN", Tuning.MachineGunInterval,
            [new(-Tuning.MachineGunBarrel, 0, 0, SlugAhead, Tuning.MachineGunDamage), new(Tuning.MachineGunBarrel, 0, 0, SlugAhead, Tuning.MachineGunDamage)], ShotLook.Slug, SfxNames.ShotMachineGun, Alternates: true),
        new("GATLING", "GATLING", Tuning.GatlingInterval,
            [new(-Tuning.GatlingGap, 0, 0, SlugAhead, Tuning.GatlingDamage), new(Tuning.GatlingGap, 0, 0, SlugAhead, Tuning.GatlingDamage)], ShotLook.Slug, SfxNames.ShotGatling),
        new("SWARM", "SWARM", Tuning.SwarmInterval,
            [.. Enumerable.Range(0, Tuning.SwarmMissiles).Select(i => Missile(AcrossWing(i, Tuning.SwarmMissiles, Tuning.SwarmGap), Tuning.SwarmDamage))],
            ShotLook.Missile, SfxNames.ShotSwarm),
        new("STORM", "STORM", Tuning.StormInterval,
            [
                new(-Tuning.StormGap, 0, 0, HeavyAhead, Tuning.StormDamage), new(0, 0, 0, HeavyAhead, Tuning.StormDamage),
                new(Tuning.StormGap, 0, 0, HeavyAhead, Tuning.StormDamage),
            ], ShotLook.Heavy, SfxNames.ShotStorm),
    ];

    /// <summary>The kind at a weapon level, 1 to <see cref="Tuning.MaxWeaponLevel"/>; a level outside is held to the nearest end.</summary>
    public static WeaponKind At(int level) => Kinds[Math.Clamp(level, 1, Kinds.Length) - 1];
}
