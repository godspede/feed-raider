using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Drops, pickups, the shield and the weapon levels. Deterministic: the sim is a pure function of its seed and inputs.
public class PickupTests
{
    /// <summary>Loop guards, in steps: each is far more than its wait needs, so a loop that reaches one has failed
    /// and the assert after it says so. A hit-stop freezes the game clock, which is why the waits are not exact.</summary>
    private const int KillGuardSteps = 20, PickupAgeGuardSteps = 2000, CollectGuardSteps = 40, RespawnGuardSteps = 400;
    /// <summary>A loop guard in hits: far more than a ship takes to die, so a loop that reaches it has failed.</summary>
    private const int DeathGuardHits = 10;
    private const FeedRaider.Engine.PostAction Like = FeedRaider.Engine.PostAction.Like;
    /// <summary>The pop-ups a like shows.</summary>
    internal const string ShieldUp = "SHIELD UP", ShieldFull = "SHIELD FULL", ShieldRecharging = "SHIELD RECHARGING";
    /// <summary>A pop-up's height in pixels: one this far below another does not overlap it.</summary>
    private const int PopupPixelRows = 4;
    /// <summary>The row, in pixels, an enemy is put on to be shot; its hit points, which one shot ends; and that shot's damage.</summary>
    private const double KillRow = 40, OneShotHp = 0.5, KillShotDamage = 5;
    /// <summary>The x, in pixels, the first of a run of kills is put at, and how far across the run spreads its enemies.</summary>
    private const int FirstLane = 20, LaneSpread = 40;

    /// <summary>A game on the big desktop field and the names of the sounds it plays.</summary>
    private static (RaiderGame Game, List<string> Played) NewGame(ulong seed = Enemies.EnemyKit.PlainSeed)
    {
        var g = new RaiderGame(seed);
        g.SetLayout(FieldLayout.Compute(PressRig.ScrCols, PressRig.ScrRows));
        var played = new List<string>();
        g.Sfx = new SfxBus((sound, _, _, _) => played.Add(sound));
        return (g, played);
    }

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    /// <summary>Puts an enemy on the field and shoots it dead on the next step. Returns after that step.</summary>
    private static void Kill(RaiderGame g, EnemyKind kind, double x, bool live = false, bool alone = true)
    {
        if (alone) g.Enemies.Clear();                          // the waves keep coming; the shot must hit this one
        var sprite = kind == EnemyKind.Gunship ? Art.Gunship : kind == EnemyKind.Carrier ? Art.Carrier : Art.Grunt;
        g.Enemies.Add(new Enemy { Kind = kind, Sprite = sprite, X = x, BaseX = x, Y = KillRow, Hp = OneShotHp, MaxHp = OneShotHp, Live = live });
        g.Bullets.Add(new Bullet { X = x, Y = KillRow + 1, Dmg = KillShotDamage, Friendly = true });
        int kills = g.Kills;
        for (int i = 0; i < KillGuardSteps && g.Kills == kills; i++) g.Step();   // a big kill's hit-stop freezes the steps before it
    }

    /// <summary>Steps until the pickup has lived this many sim ticks (hit-stop freezes its clock).</summary>
    private static void AgeTo(RaiderGame g, Pickup pickup, int age) { for (int i = 0; i < PickupAgeGuardSteps && pickup.Age < age; i++) g.Step(); }

    /// <summary>Puts a bolt right on the ship and steps once, so it lands.</summary>
    private static void ShootShip(RaiderGame g) { g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY }); g.Step(); }

    /// <summary>Spends the shield on a hit and waits out the invulnerability, so the next hit is the ship's own.</summary>
    private static void DropShield(RaiderGame g)
    {
        ShootShip(g);
        Run(g, Tuning.ShieldInvulnFrames + 1);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
    }

    private static void Collect(RaiderGame g, PickupKind kind)
    {
        g.SpawnPickup(kind, g.PX, g.PY);
        g.Step();
    }

    // ---------------------------------------------------------------- drops

    [Fact]
    public void Grunts_and_carriers_drop_at_their_chance_and_it_is_the_same_every_run()
    {
        const int kills = 1500;
        const ulong otherSeed = 9;
        double Rate(EnemyKind kind, ulong seed = Enemies.EnemyKit.PlainSeed)
        {
            var (g, _) = NewGame(seed);
            int drops = 0;
            for (int i = 0; i < kills; i++)
            {
                g.Pickups.Clear();
                Kill(g, kind, FirstLane + i % LaneSpread);
                drops += g.Pickups.Count;
            }
            return drops / (double)kills;
        }
        Assert.InRange(Rate(EnemyKind.Grunt), 0.01, 0.06);
        Assert.InRange(Rate(EnemyKind.Carrier), 0.08, 0.16);
        Assert.True(RaiderGame.TableBehaviour(EnemyKind.Carrier).DropChance > RaiderGame.TableBehaviour(EnemyKind.Grunt).DropChance);
        Assert.Equal(Rate(EnemyKind.Grunt, otherSeed), Rate(EnemyKind.Grunt, otherSeed));
        Assert.Equal(Tuning.DropGrunt, RaiderGame.TableBehaviour(EnemyKind.Grunt).DropChance);
    }

    [Fact]
    public void The_drop_sequence_is_a_function_of_the_seed()
    {
        const int kills = 20;
        const double step = 3;
        const ulong seed = 3, otherSeed = 4;
        List<(PickupKind Kind, double X)> Drops(ulong runSeed)
        {
            var (g, _) = NewGame(runSeed);
            var got = new List<(PickupKind, double)>();
            for (int i = 0; i < kills; i++) { g.Pickups.Clear(); Kill(g, EnemyKind.Gunship, FirstLane + i * step); got.Add((g.Pickups[0].Kind, g.Pickups[0].X)); }
            return got;
        }
        Assert.Equal(Drops(seed), Drops(seed));
        Assert.NotEqual(Drops(seed).Select(d => d.Kind), Drops(otherSeed).Select(d => d.Kind));
    }

    [Fact]
    public void A_gunship_always_drops_and_so_does_the_last_enemy_of_a_live_wave()
    {
        const int gunships = 3, grunts = 12;
        const double gunshipStep = 10, gruntStep = 5;
        var (g, _) = NewGame();
        for (int i = 0; i < gunships; i++)
        {
            g.Pickups.Clear();
            Kill(g, EnemyKind.Gunship, FirstLane + i * gunshipStep, live: true);
            Assert.Single(g.Pickups);                         // one pickup for the kill and the cleared wave together
        }
        for (int i = 0; i < grunts; i++)
        {
            g.Pickups.Clear();
            Kill(g, EnemyKind.Grunt, FirstLane + i * gruntStep, live: true);  // alone in its wave, so it ends the wave
            Assert.Single(g.Pickups);
        }
        Assert.True(g.LiveWavesCleared >= grunts);
    }

    [Fact]
    public void A_live_enemy_with_wave_mates_left_does_not_force_a_drop()
    {
        const ulong seed = 11;
        const int kills = 100, mostDrops = 25;
        const double mateX = 100, mateY = -50, mateHp = 9;   // above the field, out of the shot's way, and too tough to die
        var (g, _) = NewGame(seed);
        int drops = 0;
        for (int i = 0; i < kills; i++)
        {
            g.Pickups.Clear();
            g.Enemies.Add(new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = mateX, BaseX = mateX, Y = mateY, Hp = mateHp, MaxHp = mateHp, Live = true });
            Kill(g, EnemyKind.Grunt, FirstLane + i % LaneSpread, live: true, alone: false);
            drops += g.Pickups.Count;
            g.Enemies.RemoveAll(e => e.Hp > 1);
        }
        Assert.InRange(drops, 1, mostDrops);
    }

    [Fact]
    public void At_most_three_pickups_are_out_at_once()
    {
        const int kills = 10;
        const double firstX = 15, step = 6;
        var (g, _) = NewGame();
        for (int i = 0; i < kills; i++) Kill(g, EnemyKind.Gunship, firstX + i * step);
        Assert.Equal(Tuning.MaxPickups, g.Pickups.Count);
        Assert.Equal(3, Tuning.MaxPickups);
        Assert.Equal(kills, g.Kills);                         // the kills still count
    }

    [Fact]
    public void The_drop_mix_favours_what_the_player_lacks()
    {
        var (g, _) = NewGame();
        DropShield(g);
        var (s0, b0, p0, z0) = g.DropWeights();
        Assert.True(s0 > 0 && b0 > 0 && p0 > 0 && z0 > 0);
        for (int i = 0; i < Tuning.BombCap; i++) Collect(g, PickupKind.Bomb);
        for (int i = 1; i < Tuning.MaxWeaponLevel; i++) Collect(g, PickupKind.Weapon);
        Collect(g, PickupKind.Shield);
        var (s, b, p, z) = g.DropWeights();
        Assert.True(s < s0 && b < b0 && p < p0);
        Assert.True(b > 0 && p > 0);                          // fewer, not none
        Assert.Equal(z0, z);                                  // a burst is never stored, so nothing fills it
        const int samples = 1000;
        const double countSlack = 2;   // a kind's share of evenly spread samples lands within a sample or two of its weight
        var counts = new Dictionary<PickupKind, int>();
        for (int i = 0; i < samples; i++)
        {
            var kind = RaiderGame.KindFor((i + 0.5) / samples, (s, b, p, z));
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
        double total = s + b + p + z;
        foreach (var (kind, weight) in new[] { (PickupKind.Shield, s), (PickupKind.Bomb, b), (PickupKind.Weapon, p), (PickupKind.Burst, z) })
        {
            double expected = weight / total * samples;
            Assert.InRange(counts[kind], expected - countSlack, expected + countSlack);
        }
        Assert.True(counts[PickupKind.Bomb] > 0 && counts[PickupKind.Weapon] > 0);
    }

    // ---------------------------------------------------------------- drift, expiry, collection

    [Fact]
    public void A_pickup_drifts_down_with_a_bob_blinks_for_two_seconds_and_expires_at_nine()
    {
        const double startX = 12, startY = 10, slack = 0.01, leastBob = 1;
        const int driftAge = 30, xDigits = 3, fewestXs = 10, lifeSeconds = 9, blinkSeconds = 2;
        var (g, _) = NewGame();
        var p = g.SpawnPickup(PickupKind.Shield, startX, startY);
        double y0 = p.Y; var xs = new HashSet<double>();
        for (int i = 0; i < PickupAgeGuardSteps && p.Age < driftAge; i++) { g.Step(); xs.Add(Math.Round(p.X, xDigits)); }
        double drift = driftAge * Tuning.PickupDrift;
        Assert.InRange(p.Y - y0, drift - slack, drift + slack);
        Assert.True(xs.Count > fewestXs);
        Assert.InRange(xs.Max() - xs.Min(), leastBob, 2 * Tuning.PickupBobAmp + slack);   // the bob swings the amplitude each way
        Assert.False(p.Blinking);
        AgeTo(g, p, Tuning.PickupLifeFrames - Tuning.PickupBlinkFrames - 1);
        Assert.False(p.Blinking);
        AgeTo(g, p, Tuning.PickupLifeFrames - Tuning.PickupBlinkFrames);
        Assert.True(p.Blinking);
        Assert.Contains(p, g.Pickups);
        AgeTo(g, p, Tuning.PickupLifeFrames - 1);
        Assert.Contains(p, g.Pickups);
        AgeTo(g, p, Tuning.PickupLifeFrames);
        Assert.DoesNotContain(p, g.Pickups);
        Assert.Equal(lifeSeconds * Tuning.Fps, Tuning.PickupLifeFrames);
        Assert.Equal(blinkSeconds * Tuning.Fps, Tuning.PickupBlinkFrames);
    }

    [Fact]
    public void Flying_over_a_pickup_collects_it_and_a_near_miss_does_not()
    {
        var (g, played) = NewGame();
        DropShield(g);
        played.Clear();
        const double farOff = 40, nearMiss = 4;   // pixels right of the ship, and past its reach
        var far = g.SpawnPickup(PickupKind.Weapon, g.PX + farOff, g.PY);
        var near = g.SpawnPickup(PickupKind.Shield, g.PX + g.Ship.W / 2.0 + Tuning.PickupReach + nearMiss, g.PY);
        g.Step();
        Assert.Contains(far, g.Pickups); Assert.Contains(near, g.Pickups);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        for (int i = 0; i < CollectGuardSteps && g.Pickups.Contains(near); i++) { g.Input.Key(GameKey.Right, true); g.Step(); }
        g.Input.Key(GameKey.Right, false);
        Assert.DoesNotContain(near, g.Pickups);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.Contains(SfxNames.PickupShield, played);
    }

    [Fact]
    public void A_dead_ship_collects_nothing()
    {
        var (g, _) = NewGame();
        for (int i = 0; i < DeathGuardHits && g.Alive; i++) { ShootShip(g); while (g.Alive && g.Invuln > 0) g.Step(); }
        Assert.False(g.Alive);
        g.SpawnPickup(PickupKind.Bomb, g.PX, g.PY);
        g.Step();
        Assert.Single(g.Pickups);
    }

    // ---------------------------------------------------------------- the three effects

    [Fact]
    public void Each_pickup_sparkles_floats_its_text_and_plays_its_sound()
    {
        foreach (var (kind, text, sound) in new[] { (PickupKind.Shield, "+SHIELD", SfxNames.PickupShield), (PickupKind.Bomb, "+BOMB", SfxNames.PickupBomb), (PickupKind.Weapon, "+HEAVY LASER", SfxNames.PickupWeapon) })
        {
            var (g, played) = NewGame();
            if (kind == PickupKind.Shield) DropShield(g);      // a shield that is already up is a wasted pickup, which says something else
            played.Clear();
            int sparks = g.Particles.Count;
            Collect(g, kind);
            Assert.Contains(g.Popups, p => p.Text == text && p.Colour == RaiderGame.PickupColour(kind));
            const int fewestSparks = 10;
            Assert.True(g.Particles.Count >= sparks + fewestSparks);
            Assert.Contains(g.Rings, r => r.Colour == RaiderGame.PickupColour(kind));
            Assert.Equal([sound], played.Where(n => n.StartsWith("pickup_")));
        }
    }

    [Fact]
    public void A_shield_pickup_raises_a_dropped_shield_and_never_takes_it_above_one()
    {
        var (g, _) = NewGame();
        Assert.Equal(1, Tuning.ShieldHits);
        Collect(g, PickupKind.Shield);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        ShootShip(g);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        Run(g, Tuning.ShieldInvulnFrames + 1);
        int score = g.Score;
        Collect(g, PickupKind.Shield);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.Equal(score, g.Score);
        Collect(g, PickupKind.Shield);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.Equal(score + Tuning.ScorePickupOverflow, g.Score);
    }

    [Fact]
    public void A_bomb_pickup_adds_a_bomb_up_to_the_cap_then_gives_score()
    {
        var (g, _) = NewGame();
        Assert.Equal(Tuning.ShipBombs, g.Bombs);
        for (int i = Tuning.ShipBombs; i < Tuning.BombCap; i++) { Collect(g, PickupKind.Bomb); Assert.Equal(i + 1, g.Bombs); }
        Assert.Equal(Tuning.BombCap, g.Bombs);
        int score = g.Score;
        Collect(g, PickupKind.Bomb);
        Assert.Equal(Tuning.BombCap, g.Bombs);
        Assert.Equal(score + Tuning.ScorePickupOverflow, g.Score);
        Assert.Contains(g.Popups, p => p.Text == "+" + Tuning.ScorePickupOverflow);
    }

    [Fact]
    public void A_weapon_pickup_moves_up_one_of_ten_kinds_naming_it_then_gives_score_at_the_top()
    {
        const int kinds = 10;
        var (g, _) = NewGame();
        Assert.Equal(kinds, Tuning.MaxWeaponLevel);
        Assert.Equal(kinds, Weapons.Kinds.Length);
        Assert.Equal(1, g.WeaponLevel);
        for (int lvl = 2; lvl <= Tuning.MaxWeaponLevel; lvl++)
        {
            Collect(g, PickupKind.Weapon);
            Assert.Equal(lvl, g.WeaponLevel);
            Assert.Contains(g.Popups, p => p.Text == "+" + Weapons.At(lvl).Name);
        }
        int score = g.Score;
        Collect(g, PickupKind.Weapon);
        Assert.Equal(Tuning.MaxWeaponLevel, g.WeaponLevel);
        Assert.Equal(score + Tuning.ScorePickupOverflow, g.Score);
        Assert.Contains(g.Popups, p => p.Text == "+" + Tuning.ScorePickupOverflow);
    }

    // ---------------------------------------------------------------- the shield

    [Fact]
    public void A_run_starts_with_one_hit_point_and_the_shield_up()
    {
        var (g, _) = NewGame();
        Assert.Equal(1, Tuning.ShipHp);
        Assert.Equal(Tuning.ShipHp, g.Hp);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
    }

    [Fact]
    public void A_hit_with_the_shield_up_drops_the_shield_and_the_ship_lives_and_a_hit_with_it_down_is_a_death()
    {
        var (g, played) = NewGame();
        int rings = g.Rings.Count;
        ShootShip(g);
        Assert.Equal(Tuning.ShipHp, g.Hp);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        Assert.Equal(0, g.Deaths);
        Assert.True(g.Rings.Count > rings);
        Assert.Equal(1, played.Count(n => n == SfxNames.ShieldHit));
        Assert.DoesNotContain(played, n => n is SfxNames.Hurt or SfxNames.Death);
        Run(g, Tuning.ShieldInvulnFrames + 1);
        ShootShip(g);
        Assert.Equal(0, g.Hp);
        Assert.Equal(1, g.Deaths);
        Assert.Equal(1, played.Count(n => n == SfxNames.ShieldHit));
        Assert.Contains(SfxNames.Death, played);
    }

    [Fact]
    public void A_new_ship_after_a_death_has_its_shield_up()
    {
        var (g, _) = NewGame();
        DropShield(g);
        ShootShip(g);
        Assert.Equal(1, g.Deaths);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        for (int i = 0; i < RespawnGuardSteps && !g.Alive; i++) g.Step();   // a big hit-stop freezes some of the wait
        Assert.True(g.Alive);
        Assert.Equal(Tuning.ShipHp, g.Hp);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        int deaths = g.Deaths;
        Run(g, Tuning.InvulnFrames + 1);
        ShootShip(g);
        Assert.Equal(deaths, g.Deaths);                               // the first hit on the new ship only drops the shield
        Assert.Equal(LikePressTests.NoShield, g.Shield);
    }

    [Fact]
    public void An_absorbed_hit_shoves_a_near_enemy_away_and_one_ram_cannot_cost_the_ship_too()
    {
        const double rightOfShip = 4, toughHp = 50;
        const int laterFrames = 3;
        var (g, _) = NewGame();
        double x = g.PX + rightOfShip;
        var e = new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = x, BaseX = x, Y = g.PY, Hp = toughHp, MaxHp = toughHp };
        g.Enemies.Add(e);
        double kx = e.KX;
        g.Step();
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        Assert.True(e.KX > kx || e.BaseX > x);                // pushed to the right, away from the ship
        Run(g, laterFrames);
        Assert.Equal(LikePressTests.NoShield, g.Shield);       // the invulnerability holds off the next frames
        Assert.Equal(0, g.Deaths);
        Assert.Equal(Tuning.ShipHp, g.Hp);
    }

    // ---------------------------------------------------------------- the weapon

    [Fact]
    public void Every_weapon_pattern_is_mirrored_about_straight_up_and_only_the_spread_slants()
    {
        const int angleDigits = 6, single = 1;
        for (int lvl = 1; lvl <= Tuning.MaxWeaponLevel; lvl++)
        {
            var kind = Weapons.At(lvl);
            var dx = kind.Shots.Select(s => s.Dx).Order().ToArray();
            var angles = kind.Shots.Select(s => s.Angle).Order().ToArray();
            for (int i = 0; i < dx.Length; i++)
            {
                Assert.Equal(-dx[^(i + 1)], dx[i], angleDigits);
                Assert.Equal(-angles[^(i + 1)], angles[i], angleDigits);
            }
            Assert.Equal(kind.Look == ShotLook.Spread, angles.Any(a => a != 0));
            if (kind.Shots.Length == single) Assert.Equal(0, dx[0]);
        }
        // a gunship outlasts a second of the best gun even if every shot lands
        var top = Weapons.At(Tuning.MaxWeaponLevel);
        Assert.True(Tuning.GunshipHp / (top.Shots.Sum(s => s.Dmg) / top.Interval) / Tuning.Fps > 1);
    }

    [Fact]
    public void Firing_makes_the_pattern_of_the_current_level()
    {
        var (g, _) = NewGame();
        const int rearmFrames = 10;   // past the slowest gap between volleys, so the next level's first pull fires at once
        for (int lvl = 1; lvl <= Tuning.MaxWeaponLevel; lvl++)
        {
            if (lvl > 1) Collect(g, PickupKind.Weapon);
            g.Bullets.Clear();
            g.Enemies.Clear();   // a missile steers on its first step, so a wave that came in since would bend it off its pattern
            g.Input.Key(GameKey.Fire, true);
            int before = g.Bullets.Count(b => b.Friendly);
            g.Step();
            var shots = g.Bullets.Where(b => b.Friendly).ToList();
            g.Input.Key(GameKey.Fire, false);
            var kind = Weapons.At(lvl);
            Assert.Equal(kind.Alternates ? 1 : kind.Shots.Length, shots.Count - before);
            Assert.Equal(kind.Shots.Count(s => s.VX != 0), shots.Count(b => b.VX != 0));
            Assert.All(shots, b => Assert.Equal(b.Homing ? ShotLook.Missile : kind.Look, b.Look));
            Run(g, rearmFrames);
        }
    }

    [Fact]
    public void Dying_costs_a_weapon_level_but_never_below_one()
    {
        const int startLevel = 3;
        var (g, _) = NewGame();
        for (int i = 1; i < startLevel; i++) Collect(g, PickupKind.Weapon);
        Assert.Equal(startLevel, g.WeaponLevel);
        void Die()
        {
            int deaths = g.Deaths;
            for (int i = 0; i < DeathGuardHits && g.Deaths == deaths; i++) { ShootShip(g); Run(g, Tuning.InvulnFrames + 1); }
            Assert.Equal(deaths + 1, g.Deaths);
            Run(g, Tuning.RespawnFrames + 2);
        }
        Die(); Assert.Equal(startLevel - 1, g.WeaponLevel);
        Die(); Assert.Equal(1, g.WeaponLevel);
        Die(); Assert.Equal(1, g.WeaponLevel);
    }

    [Fact]
    public void Being_hit_without_dying_keeps_the_level()
    {
        var (g, _) = NewGame();
        Collect(g, PickupKind.Weapon);
        ShootShip(g);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        Assert.Equal(Tuning.ShipHp, g.Hp);
        Assert.Equal(2, g.WeaponLevel);
    }

    // ---------------------------------------------------------------- sound

    [Fact]
    public void The_sound_names_match_the_page_and_the_bus_drops_a_repeat()
    {
        Assert.Equal(["pickup_shield", "pickup_bomb", "pickup_weapon", "shield_hit"],
            [SfxNames.PickupShield, SfxNames.PickupBomb, SfxNames.PickupWeapon, SfxNames.ShieldHit]);
        var got = new List<string>();
        var bus = new SfxBus((n, _, _, _) => got.Add(n));
        const long frame = 10;
        bus.Play(SfxNames.ShieldHit, 0, frame); bus.Play(SfxNames.ShieldHit, 0, frame + 1);   // a repeat inside the gap is dropped
        bus.Play(SfxNames.PickupWeapon, 0, frame); bus.Play(SfxNames.PickupBomb, 0, frame);
        Assert.Equal([SfxNames.ShieldHit, SfxNames.PickupWeapon, SfxNames.PickupBomb], got);
        // the sound table in the page has them (the node test checks the same list from its side)
        string js = SfxTests.SfxJs();
        foreach (var n in got) Assert.Contains($"  {n}(v", js);
    }

    // ---------------------------------------------------------------- the reward of a like

    [Fact]
    public void A_like_key_with_the_shield_down_raises_it_and_the_popup_says_so()
    {
        var (g, c, played) = PressRig.Scene(signedIn: false);
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.RestOn(g, c, Like, Tuning.Fps);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        PressRig.Park(g, c, Like);
        PressRig.PressKey(g, Like);
        PressRig.Step(g);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.Equal(g.Frame, g.ShieldHitFrame);               // the rings snap on with the white flash of a shield hit
        Assert.Equal([ShieldUp], g.Popups.Where(p => p.Text.StartsWith("SHIELD")).Select(p => p.Text));
        Assert.DoesNotContain(g.Popups, p => p.Text.Contains("POWER"));
        Assert.DoesNotContain(played, p => p.Name is SfxNames.PickupShield or SfxNames.PickupWeapon);
    }

    [Fact]
    public void A_like_key_with_the_shield_up_leaves_it_and_says_shield_full()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        long flash = g.ShieldHitFrame;
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        PressRig.Press(g, c, Like);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.Equal(flash, g.ShieldHitFrame);
        Assert.Contains(g.Popups, p => p.Text == ShieldFull);
        Assert.DoesNotContain(g.Popups, p => p.Text == ShieldUp);
    }

    private const int SecondPost = 2, CardGapRows = 2;

    private static Card SecondCardBelow(RaiderGame g, Card first)
    {
        g.Posts.Offer(PressRig.Post(SecondPost));
        PressRig.Until(g, () => g.Cards.Cards.Count >= SecondPost);
        var second = g.Cards.Cards[SecondPost - 1];
        second.Top = first.Top + first.Shape.Rows + CardGapRows;
        return second;
    }

    [Fact]
    public void A_second_like_inside_the_cooldown_leaves_a_spent_shield_down_and_says_recharging()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Press(g, c, Like);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        var c2 = SecondCardBelow(g, c);
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        g.Popups.Clear();
        PressRig.Press(g, c2, Like);
        Assert.True(c2.Marks.Paid);
        Assert.Equal(LikePressTests.NoShield, g.Shield);
        Assert.Contains(g.Popups, p => p.Text == ShieldRecharging);
    }

    [Fact]
    public void A_like_once_the_cooldown_has_run_out_refills_a_spent_shield_again()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Press(g, c, Like);
        var c2 = SecondCardBelow(g, c);
        PressRig.Force(g, nameof(RaiderGame.Shield), LikePressTests.NoShield);
        PressRig.Force(g, LikePressTests.ShieldReadyField, g.Tick);
        PressRig.Press(g, c2, Like);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
    }

    [Fact]
    public void A_volley_after_a_like_does_the_damage_of_one_before_it()
    {
        var (g, c, _) = PressRig.Scene(signedIn: false);
        g.Enemies.Clear();
        double Volley()
        {
            g.Bullets.Clear();
            g.Input.Key(GameKey.Fire, true);
            g.Step();
            g.Input.Key(GameKey.Fire, false);
            return g.Bullets.Where(b => b.Friendly).Sum(b => b.Dmg);
        }
        double before = Volley();
        PressRig.Press(g, c, Like);
        PressRig.Rest(g, LikePressTests.PastFrozenTicks);
        Assert.Equal(before, Volley());
    }
}
