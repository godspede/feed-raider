namespace TUImation;

/// <summary>
/// Watches for a quit key (q or Esc) on a background thread. Uses a blocking
/// Console.ReadKey, which handles raw-mode single-key reads itself and leaves
/// output newline processing (ONLCR) intact, so it doesn't disturb the redraw.
/// No-ops when input is redirected (e.g. --dump-ppm or piped runs).
///
/// A sprite's render loop runs `while (!QuitWatcher.Quit)` after calling Start().
/// </summary>
public static class QuitWatcher
{
    public static volatile bool Quit;

    /// <summary>Set when <see cref="Quit"/> turns true, for callers that would rather wait than poll.</summary>
    public static readonly ManualResetEventSlim Signal = new();

    public static void Start()
    {
        if (Console.IsInputRedirected) return;
        var th = new Thread(() =>
        {
            try
            {
                while (!Quit)
                {
                    var k = Console.ReadKey(intercept: true);
                    if (k.Key is ConsoleKey.Q or ConsoleKey.Escape) { Quit = true; Signal.Set(); return; }
                }
            }
            catch (InvalidOperationException) { /* input went away under us (closed or redirected console): nothing left to watch */ }
        })
        { IsBackground = true };
        th.Start();
    }
}
