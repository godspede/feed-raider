namespace FeedRaider.Engine.Raider;

/// <summary>Every wingman's post in arrival order, for the wide screen's left column: it reads like a feed, the newest at the
/// bottom. An entry is the wingman itself, so a thread the player opens grows its entry in place.</summary>
public sealed class FeedLog
{
    /// <summary>More than the tallest terminal shows; older entries have long scrolled off the top.</summary>
    public const int MaxEntries = 32;

    private readonly List<Wingman> _entries = [];

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<Wingman> Entries => _entries;

    public void Add(Wingman w)
    {
        _entries.Add(w);
        if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
    }

    /// <summary>Whether the wingman is still on screen: in the game and not yet flown off the top.</summary>
    public static bool OnScreen(Wingman w, List<Wingman> wings) => wings.Contains(w) && w.Y + w.Plane.H / 2.0 >= 0;
}
