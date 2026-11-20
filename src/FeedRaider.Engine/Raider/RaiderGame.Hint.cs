namespace FeedRaider.Engine.Raider;

/// <summary>
/// The hint for a kind that unlocks part way through a stage (the mines, halfway through stage 1). It shows the way a stage
/// banner's hint does, with no stage number, the first time the run reaches that point.
/// </summary>
public sealed partial class RaiderGame
{
    /// <summary>The kind whose hint is showing; null when none is.</summary>
    public EnemyKind? KindHint { get; private set; }

    /// <summary>Frames left of the hint; 0 when it is off.</summary>
    public int KindHintLeft { get; private set; }

    private readonly HashSet<EnemyKind> _hinted = [];

    private void UpdateKindHint()
    {
        if (KindHintLeft > 0 && --KindHintLeft == 0) KindHint = null;
        if (Ramp.MidStageKind(Stage) is { } k && Ramp.StageShare(Progress) >= Ramp.UnlockShare(k) && _hinted.Add(k))
        {
            KindHint = k;
            KindHintLeft = Tuning.StageBannerFrames;
        }
    }
}
