// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Cluster;

/// <summary>A non-terminal row whose active lease names an owner the liveness snapshot does not list as live.</summary>
/// <param name="RowId">The row.</param>
/// <param name="Holder">The owner tag the lease names.</param>
/// <param name="Generation">The lease generation, so a reclaim that re-leases the row starts a new interval.</param>
public sealed record OrphanKey(Guid RowId, string Holder, long Generation);

/// <summary>How long one row stayed owned by a non-live owner across consecutive samples.</summary>
public sealed record OrphanInterval(OrphanKey Key, TimeSpan FirstSeen, TimeSpan LastSeen)
{
    public TimeSpan Duration => LastSeen - FirstSeen;
}

/// <summary>
/// Turns periodic samples of orphaned rows into intervals. A row that drops out of one sample closes its interval, so
/// a row the sweep recovered and a later owner orphaned again counts as two intervals, not one long one.
/// </summary>
public sealed class OrphanTracker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<OrphanKey, TimeSpan> _open = [];
    private readonly Dictionary<OrphanKey, TimeSpan> _lastSeen = [];
    private readonly List<OrphanInterval> _closed = [];

    /// <summary>How many samples were taken.</summary>
    public int Samples { get; private set; }

    /// <summary>Records one sample taken at <paramref name="at"/>.</summary>
    public void Observe(TimeSpan at, IEnumerable<OrphanKey> orphans)
    {
        lock (_lock)
        {
            Samples++;
            var current = orphans.ToHashSet();

            foreach (var key in _open.Keys.Where(key => !current.Contains(key)).ToList())
            {
                _closed.Add(new OrphanInterval(key, _open[key], _lastSeen[key]));
                _open.Remove(key);
                _lastSeen.Remove(key);
            }

            foreach (var key in current)
            {
                _open.TryAdd(key, at);
                _lastSeen[key] = at;
            }
        }
    }

    /// <summary>Every interval so far, the ones still open included.</summary>
    public IReadOnlyList<OrphanInterval> Intervals()
    {
        lock (_lock)
        {
            return [.. _closed, .. _open.Select(pair => new OrphanInterval(pair.Key, pair.Value, _lastSeen[pair.Key]))];
        }
    }
}
