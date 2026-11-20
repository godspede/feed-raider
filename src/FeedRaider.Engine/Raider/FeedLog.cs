namespace FeedRaider.Engine.Raider;

/// <summary>One post in the log with the marks its card had, so what was done on the field shows here at once.</summary>
public sealed record LogEntry(FeedPost Post, PostMarks Marks);

/// <summary>The posts that have passed, for the wide screen's left side panel: it reads like a feed, the newest at the bottom.
/// A card that leaves the bottom of the field adds an entry. One entry carries the mark, the one the keys act on: it starts on the
/// newest and follows each arrival until it is moved up, and then stays with its post until that post leaves the log.</summary>
public sealed class FeedLog
{
    /// <summary>More than the tallest terminal shows; older entries have long scrolled off the top.</summary>
    public const int MaxEntries = 32;

    private readonly List<LogEntry> _entries = [];

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <summary>The entry the keys act on, or null while the log is empty.</summary>
    public LogEntry? Marked { get; private set; }

    public void Add(LogEntry entry)
    {
        bool follows = Marked is null || ReferenceEquals(Marked, _entries[^1]);
        _entries.Add(entry);
        if (_entries.Count > MaxEntries)
        {
            var gone = _entries[0];
            _entries.RemoveAt(0);
            if (ReferenceEquals(gone, Marked)) follows = true;
        }
        if (follows) Marked = entry;
    }

    /// <summary>Moves the mark one entry: <c>-1</c> to the older, <c>+1</c> to the newer. At either end it stays.</summary>
    public void MoveMark(int step)
    {
        if (Marked is null) return;
        int i = _entries.IndexOf(Marked) + Math.Sign(step);
        if (i >= 0 && i < _entries.Count) Marked = _entries[i];
    }
}
