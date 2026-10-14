// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Nito.AsyncEx;

namespace Headless.DistributedLocks;

/// <summary>
/// Per-resource wake-up events for acquirers blocked on a contended resource. A waiter enters before its backoff wait
/// and exits when it stops waiting; a release signal for the resource wakes one waiting acquirer so it retries
/// immediately instead of sleeping out the rest of its backoff.
/// </summary>
/// <remarks>
/// Entries are reference counted and removed when the last waiter exits, so the map holds only resources that
/// currently have a waiter in this process. This type enforces no waiter caps: a provider that caps waiters
/// does that separately, so adopting this registry never adds a new failure mode.
/// </remarks>
internal sealed class ReleaseWaiterRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Registers one more waiter on <paramref name="resource"/> and returns the entry it waits on.</summary>
    public Entry Enter(string resource)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(resource, out var existing))
            {
                existing.RefCount++;

                return existing;
            }

            var entry = new Entry();
            _entries[resource] = entry;

            return entry;
        }
    }

    /// <summary>
    /// Releases one waiter registration taken by <see cref="Enter"/>; a <see langword="null"/> entry means the caller
    /// never entered and is ignored.
    /// </summary>
    public void Exit(string resource, Entry? entry)
    {
        if (entry is null)
        {
            return;
        }

        lock (_gate)
        {
            // Remove only when this is still the current entry, so a stale exit cannot drop a newer entry.
            if (
                --entry.RefCount == 0
                && _entries.TryGetValue(resource, out var current)
                && ReferenceEquals(current, entry)
            )
            {
                _entries.TryRemove(resource, out _);
            }
        }
    }

    /// <summary>Wakes one waiter on <paramref name="resource"/>; a no-op when nothing waits on it.</summary>
    public void Signal(string resource)
    {
        // A lock-free read is enough: the signal is only a hint. A release that lands before its waiter enters is
        // missed, and that waiter retries storage when its backoff ends, as it did before push wake-ups existed.
        // ReSharper disable once InconsistentlySynchronizedField
        if (_entries.TryGetValue(resource, out var entry))
        {
            entry.Event.Set();
        }
    }

    /// <summary>A resource's wake-up event and the number of waiters sharing it.</summary>
    internal sealed class Entry
    {
        // Guarded by the registry's lock.
        internal int RefCount { get; set; } = 1;

        internal AsyncAutoResetEvent Event { get; } = new();

        /// <summary>
        /// Waits until the resource is signalled or <paramref name="cancellationToken"/> fires. Cancellation ends the
        /// wait without throwing, so the caller decides whether a cancelled wait is a timeout or a caller cancel.
        /// </summary>
        public Task WaitAsync(CancellationToken cancellationToken) => Event.SafeWaitAsync(cancellationToken);
    }
}
