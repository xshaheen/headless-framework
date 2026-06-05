// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;

namespace Headless.DistributedLocks.InMemory;

/// <summary>Process-local semaphore storage for tests, local development, and single-instance apps.</summary>
/// <remarks>This storage is in-process only. It does not coordinate across application instances.</remarks>
[PublicAPI]
public sealed class InMemoryDistributedSemaphoreStorage(TimeProvider timeProvider) : IDistributedSemaphoreStorage
{
    private readonly ConcurrentDictionary<string, ResourceState> _resources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _fencingTokens = new(StringComparer.Ordinal);

    public ValueTask<DistributedLockAcquireResult> TryAcquireAsync(
        string resource,
        string leaseId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);
        Argument.IsGreaterThan(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var state = _resources.GetOrAdd(resource, static _ => new ResourceState());

        lock (state)
        {
            _PruneExpired(state);

            if (state.Holders.Count >= maxCount || state.Holders.ContainsKey(leaseId))
            {
                return ValueTask.FromResult(DistributedLockAcquireResult.Failed);
            }

            state.Holders[leaseId] = new HolderEntry(timeProvider.GetUtcNow().Add(ttl));
            var fencingToken = _fencingTokens.AddOrUpdate(resource, static _ => 1, static (_, current) => current + 1);

            return ValueTask.FromResult(new DistributedLockAcquireResult(Acquired: true, fencingToken));
        }
    }

    public ValueTask<bool> TryExtendAsync(
        string resource,
        string leaseId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        Argument.IsGreaterThan(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            if (!state.Holders.TryGetValue(leaseId, out var existing))
            {
                return ValueTask.FromResult(false);
            }

            var newExpiry = timeProvider.GetUtcNow().Add(ttl);

            if (newExpiry > existing.Expires)
            {
                state.Holders[leaseId] = new HolderEntry(newExpiry);
            }

            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> ValidateAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Holders.ContainsKey(leaseId));
        }
    }

    public ValueTask<bool> ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsNotNullOrEmpty(leaseId);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_resources.TryGetValue(resource, out var state))
        {
            return ValueTask.FromResult(false);
        }

        lock (state)
        {
            _PruneExpired(state);

            return ValueTask.FromResult(state.Holders.Remove(leaseId));
        }
    }

    public ValueTask<long> GetCountAsync(string resource, CancellationToken cancellationToken = default)
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

            return ValueTask.FromResult((long)state.Holders.Count);
        }
    }

    private void _PruneExpired(ResourceState state)
    {
        var now = timeProvider.GetUtcNow();

        List<string>? expired = null;

        foreach (var (leaseId, entry) in state.Holders)
        {
            if (entry.Expires <= now)
            {
                expired ??= [];
                expired.Add(leaseId);
            }
        }

        if (expired is not null)
        {
            foreach (var leaseId in expired)
            {
                state.Holders.Remove(leaseId);
            }
        }
    }

    private sealed class ResourceState
    {
        public Dictionary<string, HolderEntry> Holders { get; } = new(StringComparer.Ordinal);
    }

    private sealed record HolderEntry(DateTimeOffset Expires);
}
