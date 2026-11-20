using FeedRaider.Engine;
using FeedRaider.Engine.Raider;

namespace FeedRaider.Tests.Enemies;

/// <summary>The parts budget every kind keeps to, and a boss's health bar that never lands in the HUD rows.</summary>
public class MachineryGapTests
{
    [Fact]
    public void No_kind_carries_more_parts_than_the_tuning_allows_on_any_field()
    {
        foreach (var kind in EnemyKit.NewKinds)
            foreach (var (cols, rows) in EnemyKit.FieldSizes)
            {
                var g = EnemyKit.Game(cols, rows);
                var e = g.SpawnKind(kind, 0.5);
                Assert.True((e.Parts?.Count ?? 0) <= Tuning.MaxParts, $"{kind} on {cols}x{rows}");
            }
    }

    [Fact]
    public void A_boss_whose_top_is_on_row_six_has_its_bar_on_row_four_under_the_hud()
    {
        var g = EnemyKit.Game();
        EnemyKit.OverrideBehaviour(g, new ScriptBehaviour(EnemyKind.Bulwark) { IsBoss = true });
        var shell = EnemyKit.MakePart("shell", 0, 0, 40, 5, 100, closed: EnemyKit.Solid(81, 11, 0x445566));
        var e = g.SpawnParts(EnemyKind.Bulwark, 60, 11, shell);   // 11 rows tall, centre row 11: top on row 6 (5.5 rounds to 6)
        e.Hp = e.MaxHp / 2;
        var f = EnemyKit.ClearPickupsAndDraw(g);
        int l = 60 - 40, w = 81;
        int filled = (int)(w * e.Hp / e.MaxHp);
        for (int x = 0; x < w; x++) Assert.Equal(x < filled ? Palette.Crimson : Palette.Plum, f.Get(l + x, 4));
    }
}
