namespace FeedRaider.Tests;

/// <summary>Waits for something running on another thread to become true, and fails with a timeout rather than hanging.</summary>
internal static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (!condition()) await Task.Delay(5, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"gave up waiting for {what}");
        }
    }
}
