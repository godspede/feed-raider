namespace FeedRaider.Engine;

/// <summary>
/// The whole show, host-agnostic: fetch the follows, open the event source, then draw frames until
/// cancelled. Hosts supply a size and a frame sink.
/// </summary>
public sealed class Spectator
{
    private readonly HttpClient _http;
    private readonly Func<(int Cols, int Rows)> _size;
    private readonly IFrameSink _sink;

    public string Hint { get; set; } = "";
    public int PostsSeen { get; private set; }
    private const int FramesPerSecond = 12;
    private const int DemoFirstDelayMs = 3000, DemoDelayMs = 5000;
    /// <summary>The demo offers a made-up post only while fewer enemies than this are on screen.</summary>
    private const int DemoMaxEnemies = 3;
    /// <summary>Forces of darkness: enemies from Bluesky's moderation labels (spam, rude). On unless a host turns it off.</summary>
    public bool Darkness { get; set; } = true;
    /// <summary>When the labeler has sent nothing usable for a while, summon made-up enemies (clearly marked in the status line).
    /// Off by default: the real labeler almost never labels a post spam or rude.</summary>
    public bool DarknessDemo { get; set; }
    /// <summary>The live threadgate stream's connection state, once it is running.</summary>
    public string HiddenState => _threadgates?.State ?? "off";
    private ThreadgateSource? _threadgates;
    public DarknessStats DarkStats { get; } = new();
    public int EnemiesSpawned { get; private set; }
    public int SpawnedFromLabels { get; private set; }
    public int SpawnedFromHidden { get; private set; }

    public Spectator(HttpClient http, Func<(int Cols, int Rows)> size, IFrameSink sink)
    {
        _http = http;
        _size = size;
        _sink = sink;
    }

    public async Task RunAsync(string handle, CancellationToken ct)
    {
        handle = handle.Trim().TrimStart('@');
        var loading = new Room([]) { Status = $"fetching who @{handle} follows…" };
        AdvanceAndDraw(loading);

        List<Follow> follows;
        try { follows = await BskyClient.FetchFollowsAsync(_http, handle, ct: ct); }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // the host shows the error bar; this replaces the stale "fetching" line and says what to do (nothing retries here)
            loading.Status = $"could not load who @{handle} follows; reload to try again";
            AdvanceAndDraw(loading);
            throw;
        }
        if (follows.Count == 0)
        {
            loading.Status = $"@{handle} follows nobody the game can show";
            AdvanceAndDraw(loading);
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }

        var room = new Room(follows);
        var source = new JetstreamSource(follows.Select(f => f.Did));
        void UpdateStatus() { EnemiesSpawned = room.Dark.Spawned; SpawnedFromLabels = room.Dark.SpawnedFromLabels; SpawnedFromHidden = room.Dark.SpawnedFromHidden; Status(); }
        void Status() => room.Status = $"@{handle} | {room.FigureCount} figures | {source.State} | {PostsSeen} posts seen" + (Darkness ? $" | {room.Dark.Enemies.Count} enemies ({room.Dark.Enemies.Count(m => m.Source == DarkSource.Label)} labels, {room.Dark.Enemies.Count(m => m.Source == DarkSource.Hidden)} hidden)" + (DarknessDemo ? " (demo)" : "") : "") + (Hint.Length > 0 ? " | " + Hint : "");
        UpdateStatus();

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = Task.Run(async () =>
        {
            await foreach (var e in source.ReadAsync(pumpCts.Token).ConfigureAwait(false))
            {
                lock (room)
                {
                    PostsSeen++;
                    room.Handle(e, NowMs());
                }
            }
        }, pumpCts.Token);

        var background = new List<Task> { pump };
        // the spectator view has no session: its viewer is always signed out
        Func<string, CancellationToken, Task<string?>> fetch = (uri, c) => PostTextFetcher.FetchAsync(_http, uri, signedOut: true, c);
        if (Darkness)
        {
            var labels = new LabelSource().ReadAsync(pumpCts.Token);
            background.Add(Task.Run(() => DarknessFeed.RunAsync(labels, fetch,
                post => { lock (room) { room.Dark.Offer(post); DarkStats.Haunted++; } }, DarkStats, pumpCts.Token), pumpCts.Token));
        }

        if (Darkness)
        {
            _threadgates = new ThreadgateSource();
            var gates = _threadgates.ReadAsync(pumpCts.Token);
            background.Add(Task.Run(() => DarknessFeed.RunHiddenAsync(gates, fetch,
                post => { lock (room) { room.Dark.Offer(post); DarkStats.Haunted++; } }, DarkStats, pumpCts.Token), pumpCts.Token));
        }

        if (Darkness && DarknessDemo)
            background.Add(Task.Run(async () =>
            {
                for (int i = 0; !pumpCts.Token.IsCancellationRequested; i++)
                {
                    await Task.Delay(i == 0 ? DemoFirstDelayMs : DemoDelayMs, pumpCts.Token).ConfigureAwait(false);
                    lock (room)
                        if (DarkStats.Haunted == 0 || room.Dark.Pending == 0 && room.Dark.Enemies.Count < DemoMaxEnemies)
                            room.Dark.Offer(DarknessDemoPosts.Next(i));
                }
            }, pumpCts.Token));

        async Task RenderLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / FramesPerSecond));
            while (await timer.WaitForNextTickAsync(pumpCts.Token).ConfigureAwait(false))
            {
                lock (room)
                {
                    UpdateStatus();
                    AdvanceAndDraw(room);
                }
            }
        }
        var render = RenderLoopAsync();
        try
        {
            // a fault in any background task must end the show instead of leaving frames drawing a frozen room
            await await Task.WhenAny(render, ThrowFirstFaultAsync(background)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }   // cancelling is how this ends
        finally
        {
            pumpCts.Cancel();
            foreach (var task in background.Append(render))
                try { await task.ConfigureAwait(false); } catch (OperationCanceledException) { }   // cancelling is how this ends
        }
    }

    private static async Task ThrowFirstFaultAsync(List<Task> tasks)
    {
        var pending = new List<Task>(tasks);
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(done);
            await done.ConfigureAwait(false);
        }
        // every background task ended cleanly; only the render loop may now end the show
        await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void AdvanceAndDraw(Room room)
    {
        var (cols, rows) = _size();
        _sink.Write(room.AdvanceAndRender(cols, rows, NowMs()));
    }
}
