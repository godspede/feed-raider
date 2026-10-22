namespace FeedRaider.Engine;

/// <summary>A pause switch shared by the game loop, the feed pagers and the two live sockets. A host (the browser, when its tab
/// is hidden) flips it; each part checks it at its own pace. While paused, <see cref="PauseToken"/> is cancelled, which is how
/// a socket read is cut short so the connection can be closed.</summary>
public sealed class PauseGate
{
    private readonly object _lock = new();
    private CancellationTokenSource _cts = new();
    private TaskCompletionSource? _resumed;

    public bool IsPaused { get { lock (_lock) return _resumed is not null; } }

    /// <summary>Cancelled while paused; a fresh, live token after <see cref="Resume"/>. Take it after waiting for a resume.</summary>
    public CancellationToken PauseToken { get { lock (_lock) return _cts.Token; } }

    public void Pause()
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_resumed is not null) return;
            _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cts = _cts;
        }
        cts.Cancel();
    }

    public void Resume()
    {
        TaskCompletionSource? resumed;
        lock (_lock)
        {
            resumed = _resumed;
            if (resumed is null) return;
            _resumed = null;
            _cts = new CancellationTokenSource();
        }
        resumed.TrySetResult();
    }

    /// <summary>Completes at once when not paused, else when <see cref="Resume"/> is called. Throws if <paramref name="ct"/> is cancelled first.</summary>
    public Task WaitWhilePausedAsync(CancellationToken ct)
    {
        Task? wait;
        lock (_lock) wait = _resumed?.Task;
        return wait is null ? Task.CompletedTask : wait.WaitAsync(ct);
    }
}
