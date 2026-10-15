namespace PcControl.Core.Capture;

/// <summary>
/// Remembers which parts of a monitor changed, one entry per new picture, so that "has anything
/// changed in this area since I last looked?" can be answered without reading any pixels. A busy
/// desktop always has something moving somewhere (a clock, a video), but the small area around a
/// resting cursor usually is not one of them.
/// </summary>
public sealed class ChangeLog(int capacity = ChangeLog.DefaultCapacity)
{
    public const int DefaultCapacity = 128;

    private readonly Queue<(long Version, IReadOnlyList<Rect>? Areas)> _entries = new();

    /// <summary>Goes up by one for every picture recorded. Starts at zero (nothing seen yet).</summary>
    public long Version { get; private set; }

    /// <summary>
    /// Notes a new picture. <paramref name="changedAreas"/> are the parts that differ from the one
    /// before; null means "unknown, assume everything".
    /// </summary>
    public void Record(IReadOnlyList<Rect>? changedAreas)
    {
        Version++;
        _entries.Enqueue((Version, changedAreas));

        while (_entries.Count > capacity)
        {
            _entries.Dequeue();
        }
    }

    /// <summary>
    /// True if <paramref name="area"/> may have changed after <paramref name="sinceVersion"/>. When in
    /// doubt (the history no longer reaches back that far, or a picture's changes were unknown) the
    /// answer is yes, so a stale picture is never reused.
    /// </summary>
    public bool ChangedSince(long sinceVersion, Rect area)
    {
        if (sinceVersion >= Version)
        {
            return false;
        }

        if (sinceVersion < 0 || _entries.Count == 0 || _entries.Peek().Version > sinceVersion + 1)
        {
            return true; // not enough history to be sure
        }

        foreach (var (version, areas) in _entries)
        {
            if (version <= sinceVersion)
            {
                continue;
            }

            if (areas is null || areas.Any(a => a.Intersects(area)))
            {
                return true;
            }
        }

        return false;
    }
}
