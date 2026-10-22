namespace FeedRaider.Engine;

/// <summary>
/// The whole show, host-agnostic: fetch the follows, open the event source, then draw frames until
/// cancelled. Hosts supply a size, a frame sink and (optionally) their own event source.
/// </summary>
public sealed class Spectator
{
    private readonly HttpClient _http;
    private readonly Func<(int Cols, int Rows)> _size;
    private readonly IFrameSink _sink;
    private readonly Func<long> _clockMs;

    public string Hint { get; set; } = "";
    public int PostsSeen { get; private set; }
    /// <summary>Forces of darkness: monsters from Bluesky's moderation labels (spam, rude). On unless a host turns it off.</summary>
    public bool Darkness { get; set; } = true;
    /// <summary>When the labeler has sent nothing usable for a while, summon made-up monsters (clearly marked in the status line).
    /// Off by default: the real labeler almost never labels a post spam or rude.</summary>
    public bool DarknessDemo { get; set; }
    /// <summary>Where labels come from; null means the live labeler. Tests and fixtures can supply their own.</summary>
    public IAsyncEnumerable<LabelEvent>? LabelSource { get; set; }
    /// <summary>Where newly hidden reply URIs come from; null means the live network-wide threadgate Jetstream.</summary>
    public IAsyncEnumerable<string>? HiddenSource { get; set; }
    /// <summary>The live threadgate stream's connection state, once it is running.</summary>
    public string HiddenState => _threadgates?.State ?? "off";
    private ThreadgateSource? _threadgates;
    public DarknessStats DarkStats { get; } = new();
    public int MonstersSpawned { get; private set; }
    public int SpawnedFromLabels { get; private set; }
    public int SpawnedFromHidden { get; private set; }

    public Spectator(HttpClient http, Func<(int Cols, int Rows)> size, IFrameSink sink, Func<long>? clockMs = null)
    {
        _http = http;
        _size = size;
        _sink = sink;
        _clockMs = clockMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public async Task RunAsync(string handle, CancellationToken ct, IEventSource? source = null, int fps = 12)
    {
        handle = handle.Trim().TrimStart('@');
        var loading = new Room([]) { Status = $"fetching who @{handle} follows..." };
        DrawFrame(loading);

        var follows = await BskyClient.FetchFollowsAsync(_http, handle, ct: ct);
        if (follows.Count == 0)
        {
            loading.Status = $"@{handle} follows nobody (or the account is private)";
            DrawFrame(loading);
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }

        var room = new Room(follows);
        source ??= new JetstreamSource(follows.Select(f => f.Did));
        void UpdateStatus() { MonstersSpawned = room.Dark.Spawned; SpawnedFromLabels = room.Dark.SpawnedFromLabels; SpawnedFromHidden = room.Dark.SpawnedFromHidden; Status(); }
        void Status() => room.Status = $"@{handle} | {room.GuyCount} guys | {(source is IHasState st ? st.State + " | " : "")}{PostsSeen} posts seen" + (Darkness ? $" | {room.Dark.Monsters.Count} monsters ({room.Dark.Monsters.Count(m => m.Source == DarkSource.Label)} labels, {room.Dark.Monsters.Count(m => m.Source == DarkSource.Hidden)} hidden)" + (DarknessDemo ? " (demo)" : "") : "") + (Hint.Length > 0 ? " | " + Hint : "");
        UpdateStatus();

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = Task.Run(async () =>
        {
            await foreach (var e in source.ReadAsync(pumpCts.Token).ConfigureAwait(false))
            {
                lock (room)
                {
                    PostsSeen++;
                    room.Handle(e, _clockMs());
                }
            }
        }, pumpCts.Token);

        Task? dark = null;
        if (Darkness)
        {
            var labels = LabelSource ?? new LabelSource().ReadAsync(pumpCts.Token);
            dark = Task.Run(() => DarknessFeed.RunAsync(labels, (uri, c) => PostTextFetcher.FetchAsync(_http, uri, c),
                post => { lock (room) { room.Haunt(post); DarkStats.Haunted++; } }, DarkStats, pumpCts.Token), pumpCts.Token);
        }

        Task? hidden = null;
        if (Darkness)
        {
            var gates = HiddenSource;
            if (gates is null) { _threadgates = new ThreadgateSource(); gates = _threadgates.ReadAsync(pumpCts.Token); }
            hidden = Task.Run(() => DarknessFeed.RunHiddenAsync(gates, (uri, c) => PostTextFetcher.FetchAsync(_http, uri, c),
                post => { lock (room) { room.Haunt(post); DarkStats.Haunted++; } }, DarkStats, pumpCts.Token), pumpCts.Token);
        }

        Task? demo = null;
        if (Darkness && DarknessDemo)
            demo = Task.Run(async () =>
            {
                for (int i = 0; !pumpCts.Token.IsCancellationRequested; i++)
                {
                    await Task.Delay(i == 0 ? 3000 : 5000, pumpCts.Token).ConfigureAwait(false);
                    lock (room)
                        if (DarkStats.Haunted == 0 || room.Dark.Pending == 0 && room.Dark.Monsters.Count < 3)
                            room.Haunt(DarknessDemoPosts.Next(i));
                }
            }, pumpCts.Token);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / fps));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                lock (room)
                {
                    UpdateStatus();
                    DrawFrame(room);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            pumpCts.Cancel();
            try { await pump.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (demo is not null) try { await demo.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (hidden is not null) try { await hidden.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (dark is not null) try { await dark.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    private void DrawFrame(Room room)
    {
        var (cols, rows) = _size();
        _sink.Write(room.Render(cols, rows, _clockMs()));
    }
}
