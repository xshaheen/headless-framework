// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Thread-safe registry that tracks <see cref="LeaseMonitor"/> instances by resource and lease ID.
/// Used by the lock/semaphore providers to deregister monitors on release and to nudge active monitors
/// when a <see cref="DistributedLockReleased"/> event is received.
/// </summary>
/// <remarks>
/// Monitors are stored as <see cref="WeakReference{T}"/> inside per-resource <c>MonitorBucket</c>s.
/// Dead references (GC-collected monitors) are pruned lazily during any mutating operation so the
/// registry does not prevent handles from being collected when callers drop them without disposing.
/// </remarks>
internal sealed class LeaseMonitorRegistry(ILogger logger)
{
    private readonly ConcurrentDictionary<string, MonitorBucket> _activeMonitors = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds a <paramref name="monitor"/> for the given <paramref name="resource"/> and <paramref name="leaseId"/>.
    /// Safe to call concurrently; retries internally when a bucket is concurrently removed.
    /// </summary>
    public void Register(string resource, string leaseId, LeaseMonitor monitor)
    {
        while (true)
        {
            var bucket = _activeMonitors.GetOrAdd(resource, static _ => new MonitorBucket());

            if (bucket.TryRegister(leaseId, monitor))
            {
                logger.LogLeaseMonitorRegistered(resource, leaseId);

                return;
            }

            _TryRemoveBucket(resource, bucket);
        }
    }

    /// <summary>
    /// Removes and returns the <see cref="LeaseMonitor"/> registered for the given
    /// <paramref name="resource"/> and <paramref name="leaseId"/>, or <see langword="null"/> when no
    /// live registration exists (not found, GC-collected, or already deregistered).
    /// </summary>
    public LeaseMonitor? TryDeregister(string resource, string leaseId)
    {
        if (!_activeMonitors.TryGetValue(resource, out var bucket))
        {
            return null;
        }

        if (bucket.TryDeregister(leaseId, out var monitor, out var removeBucket))
        {
            logger.LogLeaseMonitorDeregistered(resource, leaseId);
        }

        if (removeBucket)
        {
            _TryRemoveBucket(resource, bucket);
        }

        return monitor;
    }

    /// <summary>
    /// Triggers an immediate validation iteration on all live monitors for the given
    /// <paramref name="resource"/>. Called when a <see cref="DistributedLockReleased"/> event is
    /// received to let monitors surface lease-loss quickly rather than waiting for the next cadence.
    /// Dead weak references in the bucket are pruned during the nudge pass.
    /// </summary>
    public void NudgeActive(string resource)
    {
        if (!_activeMonitors.TryGetValue(resource, out var bucket))
        {
            return;
        }

        var liveMonitors = bucket.GetLiveMonitorsForNudge(out var removeBucket);

        if (removeBucket)
        {
            _TryRemoveBucket(resource, bucket);
        }

        foreach (var (leaseId, monitor) in liveMonitors)
        {
            monitor.TriggerImmediateValidation();
            logger.LogLeaseMonitorNudged(resource, leaseId);
        }
    }

    /// <summary>Returns the number of live (non-GC'd) monitors for the given <paramref name="resource"/>.</summary>
    public int GetMonitorCount(string resource)
    {
        if (!_activeMonitors.TryGetValue(resource, out var bucket))
        {
            return 0;
        }

        var count = bucket.GetMonitorCount(out var removeBucket);

        if (removeBucket)
        {
            _TryRemoveBucket(resource, bucket);
        }

        return count;
    }

    /// <summary>Returns the number of resources that have at least one live monitor registered.</summary>
    public int GetResourceCount()
    {
        var count = 0;

        foreach (var (resource, bucket) in _activeMonitors)
        {
            _ = bucket.GetMonitorCount(out var removeBucket);

            if (removeBucket)
            {
                _TryRemoveBucket(resource, bucket);
                continue;
            }

            count++;
        }

        return count;
    }

    private void _TryRemoveBucket(string resource, MonitorBucket bucket)
    {
        var pair = new KeyValuePair<string, MonitorBucket>(resource, bucket);
        ((ICollection<KeyValuePair<string, MonitorBucket>>)_activeMonitors).Remove(pair);
    }

    private sealed class MonitorBucket
    {
        private readonly Lock _syncRoot = new();
        private readonly Dictionary<string, WeakReference<LeaseMonitor>> _monitors = new(StringComparer.Ordinal);
        private bool _isRemoved;

        public bool TryRegister(string leaseId, LeaseMonitor monitor)
        {
            lock (_syncRoot)
            {
                if (_isRemoved)
                {
                    return false;
                }

                _PruneDeadEntries();
                _monitors[leaseId] = new WeakReference<LeaseMonitor>(monitor);

                return true;
            }
        }

        public bool TryDeregister(string leaseId, out LeaseMonitor? monitor, out bool removeBucket)
        {
            lock (_syncRoot)
            {
                if (_isRemoved)
                {
                    monitor = null;
                    removeBucket = false;

                    return false;
                }

                monitor =
                    _monitors.Remove(leaseId, out var weakReference) && weakReference.TryGetTarget(out var target)
                        ? target
                        : null;
                removeBucket = _MarkRemovedIfEmpty();

                return true;
            }
        }

        public List<(string LeaseId, LeaseMonitor Monitor)> GetLiveMonitorsForNudge(out bool removeBucket)
        {
            lock (_syncRoot)
            {
                if (_isRemoved)
                {
                    removeBucket = false;

                    return [];
                }

                List<(string LeaseId, LeaseMonitor Monitor)>? liveMonitors = null;

                foreach (var (leaseId, weakReference) in _monitors)
                {
                    if (!weakReference.TryGetTarget(out var monitor))
                    {
                        continue;
                    }

                    liveMonitors ??= [];
                    liveMonitors.Add((leaseId, monitor));
                }

                _PruneDeadEntries();
                removeBucket = _MarkRemovedIfEmpty();

                return liveMonitors ?? [];
            }
        }

        public int GetMonitorCount(out bool removeBucket)
        {
            lock (_syncRoot)
            {
                if (_isRemoved)
                {
                    removeBucket = false;

                    return 0;
                }

                _PruneDeadEntries();
                removeBucket = _MarkRemovedIfEmpty();

                return _monitors.Count;
            }
        }

        private void _PruneDeadEntries()
        {
            List<string>? deadKeys = null;

            foreach (var (leaseId, weakReference) in _monitors)
            {
                if (weakReference.TryGetTarget(out _))
                {
                    continue;
                }

                deadKeys ??= [];
                deadKeys.Add(leaseId);
            }

            if (deadKeys is null)
            {
                return;
            }

            foreach (var leaseId in deadKeys)
            {
                _monitors.Remove(leaseId);
            }
        }

        private bool _MarkRemovedIfEmpty()
        {
            if (_monitors.Count > 0)
            {
                return false;
            }

            _isRemoved = true;

            return true;
        }
    }
}
