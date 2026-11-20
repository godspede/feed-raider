namespace FeedRaider.Engine.Raider;

/// <summary>
/// What the start page's scripted tour needs from the game and a real run never uses: a ship that cannot die, and a cut
/// from one scene to the next. Both are off unless the tour asks, so the rules of a played game are what they were.
/// </summary>
public sealed partial class RaiderGame
{
    /// <summary>The tour's ship: once its shield is gone a hit still flashes and knocks it about, but takes no hit point, so the
    /// tour is never cut short by a death. False in every played game.</summary>
    public bool Charmed { get; init; }

    /// <summary>Hits the charmed ship has shrugged off.</summary>
    public int Shrugged { get; private set; }

    /// <summary>The charmed ship takes a hit: the flash, the ring and the invulnerability of a real one, and nothing lost.</summary>
    private bool Shrug()
    {
        if (!Charmed) return false;
        Shrugged++;
        InvulnIsShield = false;
        Invuln = Tuning.InvulnFrames;
        AddShake(Tuning.ShakeShield);
        HurtRing();
        return true;
    }

    /// <summary>Cuts the tour to stage <paramref name="stage"/>: whatever is on the field flies off, nothing waits to arrive, and
    /// the stage begins, at its boss's warning when <paramref name="boss"/> is set and with its ordinary waves when not.</summary>
    internal void TourCut(int stage, bool boss)
    {
        foreach (var e in Enemies) e.Leaving = true;
        _pending.Clear();
        Bullets.RemoveAll(b => !b.Friendly);
        if (boss) StartAtBoss(stage); else StartAtStage(stage);
    }
}
