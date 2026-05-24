// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class LeaseMonitorRegistry(ILogger logger)
{
    private readonly ConcurrentDictionary<string, MonitorBucket> _activeMonitors = new(StringComparer.Ordinal);

    public void Register(string resource, string lockId, LeaseMonitor monitor)
    {
        while (true)
        {
            var bucket = _activeMonitors.GetOrAdd(resource, static _ => new MonitorBucket());

            if (bucket.TryRegister(lockId, monitor))
            {
                logger.LogLeaseMonitorRegistered(resource, lockId);

                return;
            }

            _TryRemoveBucket(resource, bucket);
        }
    }

    public LeaseMonitor? TryDeregister(string resource, string lockId)
    {
        if (!_activeMonitors.TryGetValue(resource, out var bucket))
        {
            return null;
        }

        if (bucket.TryDeregister(lockId, out var monitor, out var removeBucket))
        {
            logger.LogLeaseMonitorDeregistered(resource, lockId);
        }

        if (removeBucket)
        {
            _TryRemoveBucket(resource, bucket);
        }

        return monitor;
    }

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

        foreach (var (lockId, monitor) in liveMonitors)
        {
            monitor.TriggerImmediateValidation();
            logger.LogLeaseMonitorNudged(resource, lockId);
        }
    }

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

        public bool TryRegister(string lockId, LeaseMonitor monitor)
        {
            lock (_syncRoot)
            {
                if (_isRemoved)
                {
                    return false;
                }

                _PruneDeadEntries();
                _monitors[lockId] = new WeakReference<LeaseMonitor>(monitor);

                return true;
            }
        }

        public bool TryDeregister(string lockId, out LeaseMonitor? monitor, out bool removeBucket)
        {
            lock (_syncRoot)
            {
                if (_isRemoved)
                {
                    monitor = null;
                    removeBucket = false;

                    return false;
                }

                monitor = _monitors.Remove(lockId, out var weakReference) && weakReference.TryGetTarget(out var target)
                    ? target
                    : null;
                removeBucket = _MarkRemovedIfEmpty();

                return true;
            }
        }

        public List<(string LockId, LeaseMonitor Monitor)> GetLiveMonitorsForNudge(out bool removeBucket)
        {
            lock (_syncRoot)
            {
                if (_isRemoved)
                {
                    removeBucket = false;

                    return [];
                }

                List<(string LockId, LeaseMonitor Monitor)>? liveMonitors = null;

                foreach (var (lockId, weakReference) in _monitors)
                {
                    if (!weakReference.TryGetTarget(out var monitor))
                    {
                        continue;
                    }

                    liveMonitors ??= [];
                    liveMonitors.Add((lockId, monitor));
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

            foreach (var (lockId, weakReference) in _monitors)
            {
                if (weakReference.TryGetTarget(out _))
                {
                    continue;
                }

                deadKeys ??= [];
                deadKeys.Add(lockId);
            }

            if (deadKeys is null)
            {
                return;
            }

            foreach (var lockId in deadKeys)
            {
                _monitors.Remove(lockId);
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
