// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.DistributedLocks.InMemory;

/// <summary>Process-local reader-writer lock storage for tests, local development, and single-instance apps.</summary>
/// <remarks>This storage is in-process only. It does not coordinate across application instances.</remarks>
[PublicAPI]
public sealed class InMemoryDistributedReadWriteLockStorage(TimeProvider timeProvider)
    : IDistributedReadWriteLockStorage
{
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);

    public ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            _PruneExpired(state);

            if (state.Writer is not null || state.WriterWaitingMarker is not null)
            {
                return ValueTask.FromResult(false);
            }

            state.Readers[leaseId] = new LeaseEntry(_GetExpiration(ttl));

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> TryExtendReadAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (!state.Readers.TryGetValue(leaseId, out var existing))
            {
                return ValueTask.FromResult(false);
            }

            if (state.WriterWaitingMarker is not null)
            {
                return ValueTask.FromResult(false);
            }

            // Readers must always carry a finite TTL: a null ttl keeps the existing finite expiry rather than
            // promoting the lease to infinite, which would let a zombie reader block writers forever.
            state.Readers[leaseId] = existing with
            {
                Expiration = _ExtendExpiration(existing.Expiration, _GetExpiration(ttl), allowInfinite: false),
            };

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask ReleaseReadAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_resources.TryGetValue(resource, out var state))
        {
            lock (state)
            {
                _PruneExpired(state);
                state.Readers.Remove(leaseId);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string leaseId,
        string waitingId,
        TimeSpan? ttl = null,
        TimeSpan? markerTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        Argument.IsNotNullOrEmpty(waitingId);
        cancellationToken.ThrowIfCancellationRequested();

        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            _PruneExpired(state);

            if (state.Writer is not null)
            {
                return ValueTask.FromResult(false);
            }

            if (state.Readers.Count is 0)
            {
                state.Writer = new WriterEntry(leaseId, _GetExpiration(ttl));
                // A successful claim drops any pending waiting marker (mirrors Redis's single-key overwrite).
                state.WriterWaitingMarker = null;

                return ValueTask.FromResult(true);
            }

            // Only plant when no waiter holds the slot so concurrent writers don't clobber the first waiter.
            state.WriterWaitingMarker ??= new WriterEntry(waitingId, _GetExpiration(markerTtl));

            return ValueTask.FromResult(false);
        }
    }

    public ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string leaseId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (state.Writer is not { } existing || !string.Equals(existing.LeaseId, leaseId, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            // Writers may hold infinite leases, so a null ttl is allowed to extend to non-expiring.
            state.Writer = existing with
            {
                Expiration = _ExtendExpiration(existing.Expiration, _GetExpiration(ttl), allowInfinite: true),
            };

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask ReleaseWriteAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_resources.TryGetValue(resource, out var state))
        {
            lock (state)
            {
                _PruneExpired(state);

                if (state.Writer is { } writer && string.Equals(writer.LeaseId, leaseId, StringComparison.Ordinal))
                {
                    state.Writer = null;
                }

                var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(leaseId);
                if (
                    state.WriterWaitingMarker is { } marker
                    && string.Equals(marker.LeaseId, waitingId, StringComparison.Ordinal)
                )
                {
                    state.WriterWaitingMarker = null;
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ValidateReadAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Readers.ContainsKey(leaseId));
        }
    }

    public ValueTask<bool> ValidateWriteAsync(
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        _ValidateLockId(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(
                state.Writer is { } writer && string.Equals(writer.LeaseId, leaseId, StringComparison.Ordinal)
            );
        }
    }

    public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Readers.Count > 0);
        }
    }

    public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Writer is not null);
        }
    }

    public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(0L);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult((long)state.Readers.Count);
        }
    }

    private DateTimeOffset? _GetExpiration(TimeSpan? ttl)
    {
        return ttl is { } value ? timeProvider.GetUtcNow().Add(value) : null;
    }

    private static DateTimeOffset? _ExtendExpiration(
        DateTimeOffset? existing,
        DateTimeOffset? candidate,
        bool allowInfinite
    )
    {
        if (existing is null)
        {
            return existing;
        }

        // An infinite (null) candidate only wins where infinite leases are allowed (writers); for readers it
        // collapses to "keep the existing finite expiry" so the lease never becomes non-expiring.
        if (candidate is null)
        {
            return allowInfinite ? null : existing;
        }

        return candidate > existing ? candidate : existing;
    }

    private void _PruneExpired(ResourceState state)
    {
        var now = timeProvider.GetUtcNow();

        List<string>? expiredReaders = null;

        foreach (var (leaseId, entry) in state.Readers)
        {
            if (_IsExpired(entry.Expiration, now))
            {
                expiredReaders ??= [];
                expiredReaders.Add(leaseId);
            }
        }

        if (expiredReaders is not null)
        {
            foreach (var leaseId in expiredReaders)
            {
                state.Readers.Remove(leaseId);
            }
        }

        if (state.Writer is { } writer && _IsExpired(writer.Expiration, now))
        {
            state.Writer = null;
        }

        if (state.WriterWaitingMarker is { } marker && _IsExpired(marker.Expiration, now))
        {
            state.WriterWaitingMarker = null;
        }
    }

    private static bool _IsExpired(DateTimeOffset? expiration, DateTimeOffset now)
    {
        return expiration is { } exp && exp <= now;
    }

    private static void _ValidateLockId(string leaseId)
    {
        Argument.IsNotNullOrEmpty(leaseId);
        Ensure.False(
            leaseId.Contains(':', StringComparison.Ordinal),
            "Reader-writer lock ids cannot contain ':' because it conflicts with the writer-waiting suffix delimiter."
        );
    }

    private sealed class ResourceState
    {
        public Dictionary<string, LeaseEntry> Readers { get; } = new(StringComparer.Ordinal);

        public WriterEntry? Writer { get; set; }

        public WriterEntry? WriterWaitingMarker { get; set; }
    }

    private sealed record LeaseEntry(DateTimeOffset? Expiration);

    private sealed record WriterEntry(string LeaseId, DateTimeOffset? Expiration);
}
