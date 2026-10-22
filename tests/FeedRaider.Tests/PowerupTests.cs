using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests;

// Drops, pickups, the shield and the weapon levels. Deterministic: the sim is a pure function of its seed and inputs.
public class PowerupTests
{
    private readonly record struct Ev(string Name, long Frame);

    private static (RaiderGame Game, List<Ev> Played) New(ulong seed = 5)
    {
        var g = new RaiderGame(seed);
        g.SetLayout(FieldLayout.Compute(120, 46));
        var played = new List<Ev>();
        g.Sfx = new SfxBus((n, _, _, _) => played.Add(new Ev(n, g.Frame)));
        return (g, played);
    }

    private static void Run(RaiderGame g, int frames) { for (int i = 0; i < frames; i++) g.Step(); }

    /// <summary>Puts an enemy on the field and shoots it dead on the next step. Returns after that step.</summary>
    private static void Kill(RaiderGame g, EnemyKind kind, double x, double y = 40, bool live = false, bool alone = true)
    {
        if (alone) g.Enemies.Clear();                          // the waves keep coming; the bullet must hit this one
        var sprite = kind == EnemyKind.Gunship ? Art.Gunship : kind == EnemyKind.Carrier ? Art.Carrier : Art.Grunt;
        g.Enemies.Add(new Enemy { Kind = kind, Sprite = sprite, X = x, BaseX = x, Y = y, Hp = 0.5, MaxHp = 0.5, Live = live });
        g.Bullets.Add(new Bullet { X = x, Y = y + 1, Dmg = 5, Friendly = true });
        int kills = g.Kills;
        for (int i = 0; i < 20 && g.Kills == kills; i++) g.Step();   // a big kill's hit-stop freezes the steps before it
    }

    /// <summary>Steps until the pickup has lived this many sim ticks (hit-stop freezes its clock).</summary>
    private static void AgeTo(RaiderGame g, Pickup p, int age) { for (int i = 0; i < 2000 && p.Age < age; i++) g.Step(); }

    /// <summary>A hostile bullet right on the ship.</summary>
    private static void Shoot(RaiderGame g) { g.Bullets.Add(new Bullet { X = g.PX, Y = g.PY }); g.Step(); }

    private static void Collect(RaiderGame g, PickupKind kind)
    {
        g.SpawnPickup(kind, g.PX, g.PY);
        g.Step();
    }

    // ---------------------------------------------------------------- drops

    [Fact]
    public void Grunts_and_carriers_drop_at_their_chance_and_it_is_the_same_every_run()
    {
        double Rate(EnemyKind kind, ulong seed)
        {
            var (g, _) = New(seed);
            int kills = 0, drops = 0;
            for (int i = 0; i < 1500; i++)
            {
                g.Pickups.Clear();
                Kill(g, kind, 10 + i % 40);
                kills++;
                drops += g.Pickups.Count;
            }
            return drops / (double)kills;
        }
        Assert.InRange(Rate(EnemyKind.Grunt, 5), 0.05, 0.11);
        Assert.InRange(Rate(EnemyKind.Carrier, 5), 0.19, 0.31);
        Assert.True(RaiderGame.DropChance(EnemyKind.Carrier) > RaiderGame.DropChance(EnemyKind.Grunt));
        Assert.Equal(Rate(EnemyKind.Grunt, 9), Rate(EnemyKind.Grunt, 9));
        Assert.Equal(0.08, RaiderGame.DropChance(EnemyKind.Grunt));
    }

    [Fact]
    public void The_drop_sequence_is_a_function_of_the_seed()
    {
        List<(PickupKind, double)> Run20(ulong seed)
        {
            var (g, _) = New(seed);
            var got = new List<(PickupKind, double)>();
            for (int i = 0; i < 20; i++) { g.Pickups.Clear(); Kill(g, EnemyKind.Gunship, 20 + i * 3); got.Add((g.Pickups[0].Kind, g.Pickups[0].X)); }
            return got;
        }
        Assert.Equal(Run20(3), Run20(3));
        Assert.NotEqual(Run20(3).Select(x => x.Item1), Run20(4).Select(x => x.Item1));
    }

    [Fact]
    public void A_gunship_always_drops_and_so_does_the_last_enemy_of_a_live_wave()
    {
        var (g, _) = New();
        for (int i = 0; i < 3; i++)
        {
            g.Pickups.Clear();
            Kill(g, EnemyKind.Gunship, 20 + i * 10, live: true);
            Assert.Single(g.Pickups);                         // one pickup for the kill and the cleared wave together
        }
        for (int i = 0; i < 12; i++)
        {
            g.Pickups.Clear();
            Kill(g, EnemyKind.Grunt, 20 + i * 5, live: true);  // alone in its wave, so it ends the wave
            Assert.Single(g.Pickups);
        }
        Assert.True(g.LiveWavesCleared >= 12);
    }

    [Fact]
    public void A_live_enemy_with_wave_mates_left_does_not_force_a_drop()
    {
        var (g, _) = New(11);
        int drops = 0;
        for (int i = 0; i < 100; i++)
        {
            g.Pickups.Clear();
            g.Enemies.Add(new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = 100, BaseX = 100, Y = -50, Hp = 9, MaxHp = 9, Live = true });   // a mate that stays
            Kill(g, EnemyKind.Grunt, 20 + i % 40, live: true, alone: false);
            drops += g.Pickups.Count;
            g.Enemies.RemoveAll(e => e.Hp > 1);
        }
        Assert.InRange(drops, 1, 25);
    }

    [Fact]
    public void At_most_three_pickups_are_out_at_once()
    {
        var (g, _) = New();
        for (int i = 0; i < 10; i++) Kill(g, EnemyKind.Gunship, 15 + i * 6);
        Assert.Equal(Tuning.MaxPickups, g.Pickups.Count);
        Assert.Equal(3, Tuning.MaxPickups);
        Assert.Equal(10, g.Kills);                            // the kills still count
    }

    [Fact]
    public void The_drop_mix_favours_what_the_player_lacks()
    {
        var (g, _) = New();
        var (s0, b0, p0) = g.DropWeights();
        Assert.True(s0 > 0 && b0 > 0 && p0 > 0);
        for (int i = 0; i < Tuning.BombCap; i++) Collect(g, PickupKind.Bomb);
        for (int i = 0; i < 3; i++) Collect(g, PickupKind.Power);
        Collect(g, PickupKind.Shield);
        var (s, b, p) = g.DropWeights();
        Assert.True(s < s0 && b < b0 && p < p0);
        Assert.True(b > 0 && p > 0);                          // fewer, not none
        // and the draws follow the weights
        var counts = new Dictionary<PickupKind, int>();
        for (int i = 0; i < 1000; i++)
        {
            var k = RaiderGame.KindFor((i + 0.5) / 1000, (s, b, p));
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        double total = s + b + p;
        Assert.InRange(counts[PickupKind.Shield], s / total * 1000 - 2, s / total * 1000 + 2);
        Assert.InRange(counts[PickupKind.Bomb], b / total * 1000 - 2, b / total * 1000 + 2);
        Assert.InRange(counts[PickupKind.Power], p / total * 1000 - 2, p / total * 1000 + 2);
        Assert.True(counts[PickupKind.Bomb] > 0 && counts[PickupKind.Power] > 0);
    }

    // ---------------------------------------------------------------- drift, expiry, collection

    [Fact]
    public void A_pickup_drifts_down_with_a_bob_blinks_for_two_seconds_and_expires_at_nine()
    {
        var (g, _) = New();
        var p = g.SpawnPickup(PickupKind.Shield, 12, 10);
        double y0 = p.Y; var xs = new HashSet<double>();
        for (int i = 0; i < 2000 && p.Age < 30; i++) { g.Step(); xs.Add(Math.Round(p.X, 3)); }
        Assert.InRange(p.Y - y0, 30 * Tuning.PickupDrift - 0.01, 30 * Tuning.PickupDrift + 0.01);
        Assert.True(xs.Count > 10);                           // it bobs
        Assert.InRange(xs.Max() - xs.Min(), 1, 2 * Tuning.PickupBobAmp + 0.01);
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
        Assert.Equal(9 * Tuning.Fps, Tuning.PickupLifeFrames);
        Assert.Equal(2 * Tuning.Fps, Tuning.PickupBlinkFrames);
    }

    [Fact]
    public void Flying_over_a_pickup_collects_it_and_a_near_miss_does_not()
    {
        var (g, played) = New();
        var far = g.SpawnPickup(PickupKind.Power, g.PX + 40, g.PY);
        var near = g.SpawnPickup(PickupKind.Shield, g.PX + g.Plane.W / 2.0 + Tuning.PickupReach + 4, g.PY);
        g.Step();
        Assert.Contains(far, g.Pickups); Assert.Contains(near, g.Pickups);
        Assert.Equal(0, g.Shield);
        // steer the ship over the near one
        for (int i = 0; i < 40 && g.Pickups.Contains(near); i++) { g.Input.Key(GameKey.Right, true); g.Step(); }
        g.Input.Key(GameKey.Right, false);
        Assert.DoesNotContain(near, g.Pickups);
        Assert.Equal(Tuning.ShieldHits, g.Shield);
        Assert.Contains(played, e => e.Name == SfxNames.PickupShield);
    }

    [Fact]
    public void A_dead_ship_collects_nothing()
    {
        var (g, _) = New();
        for (int i = 0; i < 10 && g.Alive; i++) { Shoot(g); while (g.Alive && g.Invuln > 0) g.Step(); }
        Assert.False(g.Alive);
        g.SpawnPickup(PickupKind.Bomb, g.PX, g.PY);
        g.Step();
        Assert.Single(g.Pickups);
    }

    // ---------------------------------------------------------------- the three effects

    [Fact]
    public void Each_pickup_sparkles_floats_its_text_and_plays_its_sound()
    {
        foreach (var (kind, text, sound) in new[] { (PickupKind.Shield, "+SHIELD", SfxNames.PickupShield), (PickupKind.Bomb, "+BOMB", SfxNames.PickupBomb), (PickupKind.Power, "POWER UP", SfxNames.PickupPower) })
        {
            var (g, played) = New();
            int sparks = g.Particles.Count;
            Collect(g, kind);
            Assert.Contains(g.Popups, p => p.Text == text && p.Color == RaiderGame.PickupColor(kind));
            Assert.True(g.Particles.Count >= sparks + 10);
            Assert.Contains(g.Rings, r => r.Color == RaiderGame.PickupColor(kind));
            Assert.Equal([sound], played.Where(e => e.Name.StartsWith("pickup_")).Select(e => e.Name));
        }
    }

    [Fact]
    public void A_shield_pickup_fills_the_shield_to_three_and_no_further()
    {
        var (g, _) = New();
        Collect(g, PickupKind.Shield);
        Assert.Equal(3, g.Shield);
        int score = g.Score;
        Collect(g, PickupKind.Shield);
        Assert.Equal(3, g.Shield);
        Assert.Equal(score + Tuning.ScorePickupOverflow, g.Score);   // a wasted one is worth score
        Shoot(g);
        Assert.Equal(2, g.Shield);
        Run(g, Tuning.ShieldInvulnFrames + 1);
        Collect(g, PickupKind.Shield);
        Assert.Equal(3, g.Shield);                                    // stacks back to 3
    }

    [Fact]
    public void A_bomb_pickup_adds_a_bomb_up_to_the_cap_then_gives_score()
    {
        var (g, _) = New();
        Assert.Equal(Tuning.PlayerBombs, g.Bombs);
        for (int i = Tuning.PlayerBombs; i < Tuning.BombCap; i++) { Collect(g, PickupKind.Bomb); Assert.Equal(i + 1, g.Bombs); }
        Assert.Equal(Tuning.BombCap, g.Bombs);
        int score = g.Score;
        Collect(g, PickupKind.Bomb);
        Assert.Equal(Tuning.BombCap, g.Bombs);
        Assert.Equal(score + Tuning.ScorePickupOverflow, g.Score);
        Assert.Contains(g.Popups, p => p.Text == "+" + Tuning.ScorePickupOverflow);
    }

    [Fact]
    public void A_power_pickup_raises_the_weapon_level_to_four_then_gives_score()
    {
        var (g, _) = New();
        Assert.Equal(1, g.WeaponLevel);
        for (int lvl = 2; lvl <= 4; lvl++) { Collect(g, PickupKind.Power); Assert.Equal(lvl, g.WeaponLevel); }
        int score = g.Score;
        Collect(g, PickupKind.Power);
        Assert.Equal(4, g.WeaponLevel);
        Assert.Equal(score + Tuning.ScorePickupOverflow, g.Score);
    }

    // ---------------------------------------------------------------- the shield

    [Fact]
    public void The_shield_absorbs_exactly_three_hits_with_a_ring_a_ping_and_no_damage()
    {
        var (g, played) = New();
        Collect(g, PickupKind.Shield);
        for (int i = 1; i <= 3; i++)
        {
            int rings = g.Rings.Count;
            Shoot(g);
            Assert.Equal(Tuning.PlayerHp, g.Hp);
            Assert.Equal(3 - i, g.Shield);
            Assert.True(g.Rings.Count > rings);
            Assert.Equal(i, played.Count(e => e.Name == SfxNames.ShieldHit));
            Run(g, Tuning.ShieldInvulnFrames + 1);
        }
        Shoot(g);
        Assert.Equal(Tuning.PlayerHp - 1, g.Hp);              // the fourth hurts
        Assert.Equal(3, played.Count(e => e.Name == SfxNames.ShieldHit));
        Assert.Contains(played, e => e.Name == SfxNames.Hurt);
    }

    [Fact]
    public void An_absorbed_hit_shoves_a_near_enemy_away_and_one_ram_cannot_spend_two_charges()
    {
        var (g, _) = New();
        Collect(g, PickupKind.Shield);
        var e = new Enemy { Kind = EnemyKind.Grunt, Sprite = Art.Grunt, X = g.PX + 4, BaseX = g.PX + 4, Y = g.PY, Hp = 50, MaxHp = 50 };
        g.Enemies.Add(e);
        double kx = e.KX;
        g.Step();
        Assert.Equal(2, g.Shield);
        Assert.True(e.KX > kx || e.BaseX > g.PX + 4);         // pushed to the right, away from the ship
        Run(g, 3);
        Assert.Equal(2, g.Shield);                            // the grace holds off the next frames
        Assert.Equal(Tuning.PlayerHp, g.Hp);
    }

    // ---------------------------------------------------------------- the weapon

    [Fact]
    public void Weapon_patterns_per_level_have_the_right_shots_and_angles()
    {
        var l1 = RaiderGame.WeaponPattern(1); var l2 = RaiderGame.WeaponPattern(2);
        var l3 = RaiderGame.WeaponPattern(3); var l4 = RaiderGame.WeaponPattern(4);
        Assert.Equal([1, 2, 4, 5], new[] { l1.Length, l2.Length, l3.Length, l4.Length });
        Assert.All(l1.Concat(l2), s => Assert.Equal(0, s.Angle));
        Assert.Equal(2, l2.Select(s => s.Dx).Distinct().Count());     // twin streams, side by side
        Assert.Equal(2, l3.Count(s => s.Angle != 0));                 // twin plus two angled
        Assert.Equal(2, l3.Count(s => s.Angle == 0));
        var angled = l3.Where(s => s.Angle != 0).Select(s => s.Angle).OrderBy(a => a).ToArray();
        Assert.Equal(-angled[1], angled[0], 6);
        Assert.InRange(angled[1], 5, 20);
        var a4 = l4.Select(s => Math.Round(s.Angle, 3)).OrderBy(a => a).ToArray();
        Assert.Equal(5, a4.Distinct().Count());                       // a 5-way spread, symmetric
        Assert.Equal(0, a4[2]); Assert.Equal(-a4[0], a4[4], 3); Assert.Equal(-a4[1], a4[3], 3);
        Assert.True(a4[4] > angled[1]);                               // wider than level 3
        Assert.True(RaiderGame.FireIntervalFor(4) < RaiderGame.FireIntervalFor(3));
        Assert.Equal(RaiderGame.FireIntervalFor(1), RaiderGame.FireIntervalFor(3));
        // damage per volley rises with level but a single bullet stays modest
        double Dps(int lvl) => RaiderGame.WeaponPattern(lvl).Sum(s => s.Dmg) / RaiderGame.FireIntervalFor(lvl);
        Assert.True(Dps(1) < Dps(2) && Dps(2) < Dps(3) && Dps(3) < Dps(4));
        Assert.True(RaiderGame.WeaponPattern(4).Max(s => s.Dmg) <= 2);
        // a gunship (70 hp) outlasts a second of the best gun even if every bullet lands
        Assert.True(Tuning.GunshipHp / Dps(4) / Tuning.Fps > 1);
    }

    [Fact]
    public void Firing_makes_the_pattern_of_the_current_level()
    {
        var (g, _) = New();
        g.Enemies.Clear();
        for (int lvl = 1; lvl <= 4; lvl++)
        {
            if (lvl > 1) Collect(g, PickupKind.Power);
            g.Bullets.Clear();
            g.Input.Key(GameKey.Fire, true);
            // the first volley is the one fired this step; read it before the next pull
            int before = g.Bullets.Count(b => b.Friendly);
            g.Step();
            var shots = g.Bullets.Where(b => b.Friendly).ToList();
            g.Input.Key(GameKey.Fire, false);
            Assert.Equal(RaiderGame.WeaponPattern(lvl).Length, shots.Count - before);
            Assert.Equal(RaiderGame.WeaponPattern(lvl).Count(s => s.VX != 0), shots.Count(b => b.VX != 0));
            Run(g, 10);
        }
    }

    [Fact]
    public void Dying_costs_a_weapon_level_but_never_below_one()
    {
        var (g, _) = New();
        for (int i = 0; i < 2; i++) Collect(g, PickupKind.Power);
        Assert.Equal(3, g.WeaponLevel);
        void Die()
        {
            int deaths = g.Deaths;
            for (int i = 0; i < 6 && g.Deaths == deaths; i++) { Shoot(g); Run(g, Tuning.InvulnFrames + 1); }
            Assert.Equal(deaths + 1, g.Deaths);
            Run(g, Tuning.RespawnFrames + 2);
        }
        Die(); Assert.Equal(2, g.WeaponLevel);
        Die(); Assert.Equal(1, g.WeaponLevel);
        Die(); Assert.Equal(1, g.WeaponLevel);
    }

    [Fact]
    public void Being_hit_without_dying_keeps_the_level()
    {
        var (g, _) = New();
        Collect(g, PickupKind.Power);
        Shoot(g);
        Assert.Equal(Tuning.PlayerHp - 1, g.Hp);
        Assert.Equal(2, g.WeaponLevel);
    }

    // ---------------------------------------------------------------- sound

    [Fact]
    public void The_new_sound_names_match_the_page_and_the_bus_passes_them()
    {
        Assert.Equal(["pickup_shield", "pickup_bomb", "pickup_power", "shield_hit"],
            [SfxNames.PickupShield, SfxNames.PickupBomb, SfxNames.PickupPower, SfxNames.ShieldHit]);
        var got = new List<string>();
        var bus = new SfxBus((n, _, _, _) => got.Add(n));
        bus.Play(SfxNames.ShieldHit, 0, 10); bus.Play(SfxNames.ShieldHit, 0, 11);
        bus.Play(SfxNames.PickupPower, 0, 10); bus.Play(SfxNames.PickupBomb, 0, 10);
        Assert.Equal([SfxNames.ShieldHit, SfxNames.PickupPower, SfxNames.PickupBomb], got);
        // the sound table in the page has them (the node test checks the same list from its side)
        string js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FeedRaider.Web", "wwwroot", "sfx.js"));
        foreach (var n in got) Assert.Contains($"  {n}(v", js);
    }
}
