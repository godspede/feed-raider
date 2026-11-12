namespace FeedRaider.Engine;

/// <summary>
/// Idle wandering as a pure function of (DID, time): a few incommensurate sines whose phases and
/// periods come from the DID hash. No state and no randomness, so every client that agrees on the
/// clock draws the same scene without talking to anyone.
/// </summary>
public static class Wander
{
    private const int Waves = 3;
    private const double MinPeriodS = 5, PeriodSpreadS = 9;
    private const int YLaneBase = 10;

    /// <summary>Offset in [-1,1] on both axes. Scale it by the room's slack.</summary>
    public static (double X, double Y) Offset(string did, long timeMs)
    {
        ulong h = Hashing.Fnv64(did);
        return (Axis(h, 0, timeMs), Axis(h, YLaneBase, timeMs));
    }

    private static double Axis(ulong h, int laneBase, long timeMs)
    {
        double sum = 0;
        for (int i = 0; i < Waves; i++)
        {
            double periodS = MinPeriodS + PeriodSpreadS * Hashing.Unit(h, laneBase + i * 2);
            double phase = Math.Tau * Hashing.Unit(h, laneBase + i * 2 + 1);
            sum += Math.Sin(Math.Tau * (timeMs / 1000.0) / periodS + phase);
        }
        return sum / Waves;
    }
}
