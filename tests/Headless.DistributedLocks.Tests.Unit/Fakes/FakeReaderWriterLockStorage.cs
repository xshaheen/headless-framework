// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;

namespace Tests.Fakes;

internal sealed class FakeReaderWriterLockStorage : IDistributedReaderWriterLockStorage
{
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);

    public int WriteReleaseCount { get; private set; }

    public ValueTask<bool> TryAcquireReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            if (state.WriterId is not null)
            {
                return ValueTask.FromResult(false);
            }

            state.ReaderIds.Add(lockId);

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> TryExtendReadAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ValidateRead(resource, lockId));
    }

    public ValueTask ReleaseReadAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_resources.TryGetValue(resource, out var state))
        {
            lock (state)
            {
                state.ReaderIds.Remove(lockId);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> TryAcquireWriteAsync(
        string resource,
        string lockId,
        string waitingId,
        TimeSpan? ttl = null,
        TimeSpan? markerTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            if (state.WriterId is not null && state.WriterId != waitingId)
            {
                return ValueTask.FromResult(false);
            }

            if (state.ReaderIds.Count == 0)
            {
                state.WriterId = lockId;

                return ValueTask.FromResult(true);
            }

            state.WriterId = waitingId;

            return ValueTask.FromResult(false);
        }
    }

    public ValueTask<bool> TryExtendWriteAsync(
        string resource,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ValidateWrite(resource, lockId));
    }

    public ValueTask ReleaseWriteAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteReleaseCount++;

        if (_resources.TryGetValue(resource, out var state))
        {
            lock (state)
            {
                if (
                    state.WriterId == lockId
                    || state.WriterId == DistributedLockCoreHelpers.GetWriterWaitingId(lockId)
                )
                {
                    state.WriterId = null;
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> ValidateReadAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ValidateRead(resource, lockId));
    }

    public ValueTask<bool> ValidateWriteAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(ValidateWrite(resource, lockId));
    }

    public ValueTask<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            return ValueTask.FromResult(state.ReaderIds.Count > 0);
        }
    }

    public ValueTask<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            return ValueTask.FromResult(
                state.WriterId is not null
                    && !state.WriterId.EndsWith(
                        DistributedLockCoreHelpers.WriterWaitingSuffix,
                        StringComparison.Ordinal
                    )
            );
        }
    }

    public ValueTask<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(0L);
        }

        lock (state)
        {
            return ValueTask.FromResult((long)state.ReaderIds.Count);
        }
    }

    public void SetRead(string resource, string lockId)
    {
        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            state.ReaderIds.Add(lockId);
        }
    }

    public void SetWrite(string resource, string lockId)
    {
        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            state.WriterId = lockId;
        }
    }

    private bool ValidateRead(string resource, string lockId)
    {
        if (!_resources.TryGetValue(resource, out var state))
        {
            return false;
        }

        lock (state)
        {
            return state.ReaderIds.Contains(lockId);
        }
    }

    private bool ValidateWrite(string resource, string lockId)
    {
        if (!_resources.TryGetValue(resource, out var state))
        {
            return false;
        }

        lock (state)
        {
            return state.WriterId == lockId;
        }
    }

    private sealed class ResourceState
    {
        public HashSet<string> ReaderIds { get; } = new(StringComparer.Ordinal);

        public string? WriterId { get; set; }
    }
}
