// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;

namespace Tests.Fakes;

/// <summary>
/// Process-local fake of <see cref="IDistributedLockStorage"/> for unit tests.
/// Implements the CAS primitives via a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by resource.
/// Not distributed — for in-process test scenarios only.
/// </summary>
internal sealed class InMemoryDistributedLockStorage(TimeProvider timeProvider) : IDistributedLockStorage
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public ValueTask<bool> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        var entry = _CreateEntry(lockId, ttl);

        // Evict expired holder first so a stale key does not block re-insert.
        if (_entries.TryGetValue(key, out var existing) && _IsExpired(existing))
        {
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, existing));
        }

        return ValueTask.FromResult(_entries.TryAdd(key, entry));
    }

    public ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!_entries.TryGetValue(key, out var existing))
        {
            return ValueTask.FromResult(false);
        }

        if (!string.Equals(existing.LockId, expectedId, StringComparison.Ordinal) || _IsExpired(existing))
        {
            return ValueTask.FromResult(false);
        }

        var updated = _CreateEntry(newId, newTtl);

        return ValueTask.FromResult(_entries.TryUpdate(key, updated, existing));
    }

    public ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    )
    {
        if (!_entries.TryGetValue(key, out var existing))
        {
            return ValueTask.FromResult(false);
        }

        if (!string.Equals(existing.LockId, expectedId, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(false);
        }

        var removed = ((ICollection<KeyValuePair<string, Entry>>)_entries).Remove(
            new KeyValuePair<string, Entry>(key, existing)
        );

        return ValueTask.FromResult(removed);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(key, out var entry) || _IsExpired(entry))
        {
            return ValueTask.FromResult<TimeSpan?>(null);
        }

        if (entry.Expiry is null)
        {
            return ValueTask.FromResult<TimeSpan?>(Timeout.InfiniteTimeSpan);
        }

        var remaining = entry.Expiry.Value - timeProvider.GetUtcNow();

        return ValueTask.FromResult<TimeSpan?>(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining);
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_entries.TryGetValue(key, out var entry) && !_IsExpired(entry));
    }

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(key, out var entry) && !_IsExpired(entry))
        {
            return ValueTask.FromResult<string?>(entry.LockId);
        }

        return ValueTask.FromResult<string?>(null);
    }

    public ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, entry) in _entries)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (_IsExpired(entry))
            {
                continue;
            }

            result[key] = entry.LockId;
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    public ValueTask<IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>> GetAllWithExpirationByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var result = new Dictionary<string, (string, TimeSpan?)>(StringComparer.Ordinal);
        var now = timeProvider.GetUtcNow();

        foreach (var (key, entry) in _entries)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (_IsExpired(entry))
            {
                continue;
            }

            TimeSpan? ttl;

            if (entry.Expiry is null)
            {
                ttl = Timeout.InfiniteTimeSpan;
            }
            else
            {
                var remaining = entry.Expiry.Value - now;
                ttl = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }

            result[key] = (entry.LockId, ttl);
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>>(result);
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        var count = 0L;

        foreach (var (key, entry) in _entries)
        {
            if (prefix.Length > 0 && !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (_IsExpired(entry))
            {
                continue;
            }

            count++;
        }

        return ValueTask.FromResult(count);
    }

    private Entry _CreateEntry(string lockId, TimeSpan? ttl)
    {
        DateTimeOffset? expiry = ttl is null ? null : timeProvider.GetUtcNow().Add(ttl.Value);

        return new Entry(lockId, expiry);
    }

    private bool _IsExpired(Entry entry)
    {
        return entry.Expiry is { } expiry && expiry <= timeProvider.GetUtcNow();
    }

    private sealed record Entry(string LockId, DateTimeOffset? Expiry);
}
