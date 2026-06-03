// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;

namespace Tests.Fakes;

internal sealed class FakeDistributedSemaphoreStorage(TimeProvider? timeProvider = null) : IDistributedSemaphoreStorage
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Entry>> _holders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _fencingTokens = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask<DistributedLockAcquireResult> TryAcquireAsync(
        string resource,
        string lockId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var holders = _GetLiveHolders(resource);

        if (holders.Count >= maxCount)
        {
            return ValueTask.FromResult(DistributedLockAcquireResult.Failed);
        }

        var added = holders.TryAdd(lockId, new Entry(_timeProvider.GetUtcNow().Add(ttl)));
        if (!added)
        {
            return ValueTask.FromResult(DistributedLockAcquireResult.Failed);
        }

        var fencingToken = _fencingTokens.AddOrUpdate(resource, static _ => 1, static (_, current) => current + 1);

        return ValueTask.FromResult(new DistributedLockAcquireResult(Acquired: true, fencingToken));
    }

    public ValueTask<bool> TryExtendAsync(
        string resource,
        string lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var holders = _GetLiveHolders(resource);
        if (!holders.ContainsKey(lockId))
        {
            return ValueTask.FromResult(false);
        }

        holders[lockId] = new Entry(_timeProvider.GetUtcNow().Add(ttl));

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> ValidateAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var holders = _GetLiveHolders(resource);

        return ValueTask.FromResult(holders.ContainsKey(lockId));
    }

    public ValueTask<bool> ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var holders = _GetLiveHolders(resource);

        return ValueTask.FromResult(holders.TryRemove(lockId, out _));
    }

    public ValueTask<long> GetCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult((long)_GetLiveHolders(resource).Count);
    }

    private ConcurrentDictionary<string, Entry> _GetLiveHolders(string resource)
    {
        var holders = _holders.GetOrAdd(resource, static _ => new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal));
        var now = _timeProvider.GetUtcNow();

        foreach (var (lockId, entry) in holders)
        {
            if (entry.Expires <= now)
            {
                holders.TryRemove(lockId, out _);
            }
        }

        return holders;
    }

    private sealed record Entry(DateTimeOffset Expires);
}
