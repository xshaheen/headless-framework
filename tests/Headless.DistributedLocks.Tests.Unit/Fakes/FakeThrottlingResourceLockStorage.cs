// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;

namespace Tests.Fakes;

internal sealed class FakeThrottlingResourceLockStorage : IThrottlingResourceLockStorage
{
    private readonly ConcurrentDictionary<string, ThrottleEntry> _counters = new(StringComparer.Ordinal);
    private bool _disposed;

    public Task<long> GetHitCountsAsync(string resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_counters.TryGetValue(resource, out var entry))
        {
            return Task.FromResult(0L);
        }

        // Check if expired
        if (entry.Expiration <= DateTime.UtcNow)
        {
            _counters.TryRemove(resource, out _);
            return Task.FromResult(0L);
        }

        return Task.FromResult(entry.Count);
    }

    public Task<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var expiration = DateTime.UtcNow.Add(ttl);

        var newEntry = _counters.AddOrUpdate(
            resource,
            _ => new ThrottleEntry(1, expiration),
            (_, existing) =>
            {
                // If expired, start fresh
                if (existing.Expiration <= DateTime.UtcNow)
                {
                    return new ThrottleEntry(1, expiration);
                }

                // Otherwise increment, keep original expiration
                return existing with { Count = existing.Count + 1 };
            }
        );

        return Task.FromResult(newEntry.Count);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _counters.Clear();
        return ValueTask.CompletedTask;
    }

    // Test helpers
    public void Clear() => _counters.Clear();

    public void SetCount(string resource, long count, TimeSpan ttl) =>
        _counters[resource] = new ThrottleEntry(count, DateTime.UtcNow.Add(ttl));

    private sealed record ThrottleEntry(long Count, DateTime Expiration);
}
