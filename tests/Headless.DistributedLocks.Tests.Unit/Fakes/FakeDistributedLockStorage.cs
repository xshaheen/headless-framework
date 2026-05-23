// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;

namespace Tests.Fakes;

internal sealed class FakeDistributedLockStorage : IDistributedLockStorage
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public FakeDistributedLockStorage(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DateTime _UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    public ValueTask<bool> InsertAsync(
        string key,
        string lockId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _RemoveIfExpired(key);
        var entry = new LockEntry(lockId, ttl.HasValue ? _UtcNow().Add(ttl.Value) : null);
        var added = _locks.TryAdd(key, entry);
        return ValueTask.FromResult(added);
    }

    public ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_TryGetLiveEntry(key, out var existing) || existing.LockId != expectedId)
        {
            return ValueTask.FromResult(false);
        }

        var newEntry = new LockEntry(newId, newTtl.HasValue ? _UtcNow().Add(newTtl.Value) : null);
        var replaced = _locks.TryUpdate(key, newEntry, existing);
        return ValueTask.FromResult(replaced);
    }

    public ValueTask<bool> RemoveIfEqualAsync(
        string key,
        string expectedId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_TryGetLiveEntry(key, out var existing) || existing.LockId != expectedId)
        {
            return ValueTask.FromResult(false);
        }

        var removed = _locks.TryRemove(key, out _);
        return ValueTask.FromResult(removed);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_TryGetLiveEntry(key, out var entry) || entry.Expiration is null)
        {
            return ValueTask.FromResult<TimeSpan?>(null);
        }

        var remaining = entry.Expiration.Value - _UtcNow();
        return ValueTask.FromResult<TimeSpan?>(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_TryGetLiveEntry(key, out _));
    }

    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_TryGetLiveEntry(key, out var entry) ? entry.LockId : null);
    }

    public ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _PruneExpired();
        var result = _locks
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value.LockId, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    public ValueTask<IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>> GetAllWithExpirationByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _PruneExpired();
        var result = _locks
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    var remaining = kv.Value.Expiration.HasValue
                        ? kv.Value.Expiration.Value - _UtcNow()
                        : (TimeSpan?)null;
                    var ttl = remaining > TimeSpan.Zero ? remaining : (remaining.HasValue ? TimeSpan.Zero : null);
                    return (kv.Value.LockId, ttl);
                },
                StringComparer.Ordinal
            );

        return ValueTask.FromResult<IReadOnlyDictionary<string, (string LockId, TimeSpan? Ttl)>>(result);
    }

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _PruneExpired();
        var count = string.IsNullOrEmpty(prefix)
            ? (long)_locks.Count
            : _locks.Count(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal));

        return ValueTask.FromResult(count);
    }

    // Test helpers
    public void Clear() => _locks.Clear();

    public void SetLock(string key, string lockId, TimeSpan? ttl = null)
    {
        _locks[key] = new LockEntry(lockId, ttl.HasValue ? _UtcNow().Add(ttl.Value) : null);
    }

    public void RemoveLock(string key)
    {
        _locks.TryRemove(key, out _);
    }

    private sealed record LockEntry(string LockId, DateTime? Expiration);

    private bool _TryGetLiveEntry(string key, out LockEntry entry)
    {
        if (_locks.TryGetValue(key, out entry!) && !_IsExpired(entry))
        {
            return true;
        }

        _RemoveIfExpired(key);
        entry = null!;

        return false;
    }

    private void _PruneExpired()
    {
        foreach (var key in _locks.Keys)
        {
            _RemoveIfExpired(key);
        }
    }

    private void _RemoveIfExpired(string key)
    {
        if (_locks.TryGetValue(key, out var entry) && _IsExpired(entry))
        {
            _locks.TryRemove(key, out _);
        }
    }

    private bool _IsExpired(LockEntry entry)
    {
        return entry.Expiration is { } expiration && expiration <= _UtcNow();
    }
}
