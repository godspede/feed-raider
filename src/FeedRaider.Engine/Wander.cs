namespace FeedRaider.Engine;

/// <summary>
/// Idle wandering as a pure function of (DID, time): a few incommensurate sines whose phases and
/// periods come from the DID hash. No state and no randomness, so every client that agrees on the
/// clock draws the same scene without talking to anyone.
/// </summary>
public static class Wander
{
    /// <summary>Offset in [-1,1] on both axes. Scale it by the room's slack.</summary>
    public static (double X, double Y) Offset(string did, long timeMs)
    {
        ulong h = Hashing.Fnv64(did);
        return (Axis(h, 0, timeMs), Axis(h, 10, timeMs));
    }

    private static double Axis(ulong h, int laneBase, long timeMs)
    {
        double sum = 0;
        for (int i = 0; i < 3; i++)
        {
            double periodS = 5 + 9 * Hashing.Unit(h, laneBase + i * 2);
            double phase = 2 * Math.PI * Hashing.Unit(h, laneBase + i * 2 + 1);
            sum += Math.Sin(2 * Math.PI * (timeMs / 1000.0) / periodS + phase);
        }
        return sum / 3;
    }
}
