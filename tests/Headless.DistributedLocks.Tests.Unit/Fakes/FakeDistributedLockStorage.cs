// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;

namespace Tests.Fakes;

internal sealed class FakeDistributedLockStorage : IDistributedLockStorage
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);

    public ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        var entry = new LockEntry(lockId, ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null);
        var added = _locks.TryAdd(key, entry);
        return ValueTask.FromResult(added);
    }

    public ValueTask<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        if (!_locks.TryGetValue(key, out var existing) || existing.LockId != expectedId)
        {
            return ValueTask.FromResult(false);
        }

        var newEntry = new LockEntry(newId, newTtl.HasValue ? DateTime.UtcNow.Add(newTtl.Value) : null);
        var replaced = _locks.TryUpdate(key, newEntry, existing);
        return ValueTask.FromResult(replaced);
    }

    public ValueTask<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        if (!_locks.TryGetValue(key, out var existing) || existing.LockId != expectedId)
        {
            return ValueTask.FromResult(false);
        }

        var removed = _locks.TryRemove(key, out _);
        return ValueTask.FromResult(removed);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key)
    {
        if (!_locks.TryGetValue(key, out var entry) || entry.Expiration is null)
        {
            return ValueTask.FromResult<TimeSpan?>(null);
        }

        var remaining = entry.Expiration.Value - DateTime.UtcNow;
        return ValueTask.FromResult<TimeSpan?>(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    public ValueTask<bool> ExistsAsync(string key) => ValueTask.FromResult(_locks.ContainsKey(key));

    public ValueTask<string?> GetAsync(string key) =>
        ValueTask.FromResult(_locks.TryGetValue(key, out var entry) ? entry.LockId : null);

    public ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string prefix)
    {
        var result = _locks
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value.LockId, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    public ValueTask<long> GetCountAsync(string prefix = "")
    {
        var count = string.IsNullOrEmpty(prefix)
            ? _locks.Count
            : _locks.Count(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal));

        return ValueTask.FromResult<long>(count);
    }

    // Test helpers
    public void Clear() => _locks.Clear();

    public void SimulateExpiration(string key)
    {
        if (_locks.TryGetValue(key, out var entry))
        {
            _locks[key] = entry with { Expiration = DateTime.UtcNow.AddSeconds(-1) };
        }
    }

    private sealed record LockEntry(string LockId, DateTime? Expiration);
}
