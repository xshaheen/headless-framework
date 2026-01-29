// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Headless.Checks;
using Nito.AsyncEx;

namespace Headless.Caching;

/// <summary>In-memory cache implementation with LRU eviction, expiration, and list/set operations.</summary>
public sealed class InMemoryCache(TimeProvider timeProvider, InMemoryCacheOptions options) : IInMemoryCache, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new();
    private readonly AsyncLock _lock = new();
    private readonly CancellationTokenSource _disposedCts = new();
    private readonly string _keyPrefix = options.KeyPrefix ?? "";
    private readonly int? _maxItems = options.MaxItems;
    private readonly bool _shouldClone = options.CloneValues;
    private DateTime _lastMaintenance;
    private bool _isDisposed;

    #region Update

    public Task<bool> UpsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return Task.FromResult(false);
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var entry = new CacheEntry(value, expiresAt, timeProvider, _shouldClone);

        return _SetInternalAsync(key, entry);
    }

    public async Task<int> UpsertAllAsync<T>(IDictionary<string, T> value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        if (value.Count is 0)
        {
            return 0;
        }

        if (expiration is { Ticks: <= 0 })
        {
            foreach (var k in value.Keys)
            {
                _RemoveExpiredKey(_GetKey(k));
            }

            return 0;
        }

        var count = 0;

        foreach (var (k, v) in value)
        {
            if (await UpsertAsync(k, v, expiration, cancellationToken).AnyContext())
            {
                count++;
            }
        }

        return count;
    }

    public Task<bool> TryInsertAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return Task.FromResult(false);
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var entry = new CacheEntry(value, expiresAt, timeProvider, _shouldClone);

        return _SetInternalAsync(key, entry, addOnly: true);
    }

    public Task<bool> TryReplaceAsync<T>(string key, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.ContainsKey(key))
        {
            return Task.FromResult(false);
        }

        return UpsertAsync(key, value, expiration, cancellationToken);
    }

    public async Task<bool> TryReplaceIfEqualAsync<T>(string key, T? expected, T? value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return false;
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var wasExpectedValue = false;

        _memory.TryUpdate(key, (_, existingEntry) =>
        {
            var currentValue = existingEntry.GetValue<T>();

            if (Equals(currentValue, expected))
            {
                existingEntry.Value = value;
                existingEntry.ExpiresAt = expiresAt;
                wasExpectedValue = true;
            }

            return existingEntry;
        });

        await _StartMaintenanceAsync().AnyContext();

        return wasExpectedValue;
    }

    public async Task<double> IncrementAsync(string key, double amount, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return 0;
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var newEntry = new CacheEntry(amount, expiresAt, timeProvider, _shouldClone);

        var result = _memory.AddOrUpdate(
            key,
            _ => newEntry,
            (_, existingEntry) =>
            {
                double? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<double?>();
                }
                catch
                {
                    // ignore
                }

                existingEntry.Value = currentValue.HasValue ? currentValue.Value + amount : amount;
                existingEntry.ExpiresAt = expiresAt;

                return existingEntry;
            }
        );

        await _StartMaintenanceAsync().AnyContext();

        return result.GetValue<double>();
    }

    public async Task<long> IncrementAsync(string key, long amount, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return 0;
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var newEntry = new CacheEntry(amount, expiresAt, timeProvider, _shouldClone);

        var result = _memory.AddOrUpdate(
            key,
            _ => newEntry,
            (_, existingEntry) =>
            {
                long? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<long?>();
                }
                catch
                {
                    // ignore
                }

                existingEntry.Value = currentValue.HasValue ? currentValue.Value + amount : amount;
                existingEntry.ExpiresAt = expiresAt;

                return existingEntry;
            }
        );

        await _StartMaintenanceAsync().AnyContext();

        return result.GetValue<long>();
    }

    public async Task<double> SetIfHigherAsync(string key, double value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return 0;
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var newEntry = new CacheEntry(value, expiresAt, timeProvider, _shouldClone);
        var difference = value;

        _memory.AddOrUpdate(
            key,
            _ => newEntry,
            (_, existingEntry) =>
            {
                double? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<double?>();
                }
                catch
                {
                    // ignore
                }

                if (currentValue.HasValue && currentValue.Value < value)
                {
                    difference = value - currentValue.Value;
                    existingEntry.Value = value;
                }
                else
                {
                    difference = 0;
                }

                existingEntry.ExpiresAt = expiresAt;

                return existingEntry;
            }
        );

        await _StartMaintenanceAsync().AnyContext();

        return difference;
    }

    public async Task<long> SetIfHigherAsync(string key, long value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return 0;
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var newEntry = new CacheEntry(value, expiresAt, timeProvider, _shouldClone);
        var difference = value;

        _memory.AddOrUpdate(
            key,
            _ => newEntry,
            (_, existingEntry) =>
            {
                long? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<long?>();
                }
                catch
                {
                    // ignore
                }

                if (currentValue.HasValue && currentValue.Value < value)
                {
                    difference = value - currentValue.Value;
                    existingEntry.Value = value;
                }
                else
                {
                    difference = 0;
                }

                existingEntry.ExpiresAt = expiresAt;

                return existingEntry;
            }
        );

        await _StartMaintenanceAsync().AnyContext();

        return difference;
    }

    public async Task<double> SetIfLowerAsync(string key, double value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return 0;
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var newEntry = new CacheEntry(value, expiresAt, timeProvider, _shouldClone);
        var difference = value;

        _memory.AddOrUpdate(
            key,
            _ => newEntry,
            (_, existingEntry) =>
            {
                double? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<double?>();
                }
                catch
                {
                    // ignore
                }

                if (currentValue.HasValue && currentValue.Value > value)
                {
                    difference = currentValue.Value - value;
                    existingEntry.Value = value;
                }
                else
                {
                    difference = 0;
                }

                existingEntry.ExpiresAt = expiresAt;

                return existingEntry;
            }
        );

        await _StartMaintenanceAsync().AnyContext();

        return difference;
    }

    public async Task<long> SetIfLowerAsync(string key, long value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return 0;
        }

        var expiresAt = expiration.HasValue ? timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value) : (DateTime?)null;
        var newEntry = new CacheEntry(value, expiresAt, timeProvider, _shouldClone);
        var difference = value;

        _memory.AddOrUpdate(
            key,
            _ => newEntry,
            (_, existingEntry) =>
            {
                long? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<long?>();
                }
                catch
                {
                    // ignore
                }

                if (currentValue.HasValue && currentValue.Value > value)
                {
                    difference = currentValue.Value - value;
                    existingEntry.Value = value;
                }
                else
                {
                    difference = 0;
                }

                existingEntry.ExpiresAt = expiresAt;

                return existingEntry;
            }
        );

        await _StartMaintenanceAsync().AnyContext();

        return difference;
    }

    public async Task<long> SetAddAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            await SetRemoveAsync(key, value, expiration, cancellationToken).AnyContext();
            return 0;
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = expiration.HasValue ? utcNow.Add(expiration.Value) : (DateTime?)null;

        if (value is string stringValue)
        {
            var items = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase) { { stringValue, expiresAt } };
            var entry = new CacheEntry(items, expiresAt, timeProvider, _shouldClone);

            _memory.AddOrUpdate(
                key,
                _ => entry,
                (existingKey, existingEntry) =>
                {
                    if (existingEntry.Value is not IDictionary<string, DateTime?> dictionary)
                    {
                        throw new InvalidOperationException($"Unable to add value for key: {existingKey}. Cache value does not contain a dictionary");
                    }

                    _ExpireListValues(dictionary);
                    dictionary[stringValue] = expiresAt;
                    existingEntry.Value = dictionary;
                    existingEntry.ExpiresAt = dictionary.Values.Contains(null) ? null : dictionary.Values.Max();

                    return existingEntry;
                }
            );

            await _StartMaintenanceAsync().AnyContext();

            return items.Count;
        }
        else
        {
            var items = new Dictionary<object, DateTime?>();

            foreach (var v in value.Where(v => v is not null))
            {
                items[v!] = expiresAt;
            }

            if (items.Count is 0)
            {
                return 0;
            }

            var entry = new CacheEntry(items, expiresAt, timeProvider, _shouldClone);

            _memory.AddOrUpdate(
                key,
                _ => entry,
                (existingKey, existingEntry) =>
                {
                    if (existingEntry.Value is not IDictionary<object, DateTime?> dictionary)
                    {
                        throw new InvalidOperationException($"Unable to add value for key: {existingKey}. Cache value does not contain a set");
                    }

                    _ExpireListValuesObject(dictionary);

                    foreach (var kvp in items)
                    {
                        dictionary[kvp.Key] = kvp.Value;
                    }

                    existingEntry.Value = dictionary;
                    existingEntry.ExpiresAt = dictionary.Values.Contains(null) ? null : dictionary.Values.Max();

                    return existingEntry;
                }
            );

            await _StartMaintenanceAsync().AnyContext();

            return items.Count;
        }
    }

    #endregion

    #region Get

    public Task<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return Task.FromResult(CacheValue<T>.NoValue);
        }

        if (existingEntry.IsExpired)
        {
            return Task.FromResult(CacheValue<T>.NoValue);
        }

        try
        {
            var value = existingEntry.GetValue<T>();
            return Task.FromResult(new CacheValue<T>(value, true));
        }
        catch
        {
            return Task.FromResult(CacheValue<T>.NoValue);
        }
    }

    public async Task<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var map = new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal);

        foreach (var key in cacheKeys)
        {
            map[key] = await GetAsync<T>(key, cancellationToken).AnyContext();
        }

        return map;
    }

    public Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(string prefix, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(prefix);
        return GetAllAsync<T>(keys, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetAllKeysByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_GetKeys(prefix));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(!existingEntry.IsExpired);
    }

    public Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(prefix))
        {
            return Task.FromResult(_memory.Count(i => !i.Value.IsExpired));
        }

        prefix = _GetKey(prefix);
        var count = _memory.Count(x => x.Key.StartsWith(prefix, StringComparison.Ordinal) && !x.Value.IsExpired);

        return Task.FromResult(count);
    }

    public Task<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return Task.FromResult<TimeSpan?>(null);
        }

        if (existingEntry.IsExpired)
        {
            return Task.FromResult<TimeSpan?>(null);
        }

        if (!existingEntry.ExpiresAt.HasValue || existingEntry.ExpiresAt.Value == DateTime.MaxValue)
        {
            return Task.FromResult<TimeSpan?>(null);
        }

        return Task.FromResult<TimeSpan?>(existingEntry.ExpiresAt.Value.Subtract(timeProvider.GetUtcNow().UtcDateTime));
    }

    public async Task<CacheValue<ICollection<T>>> GetSetAsync<T>(string key, int? pageIndex = null, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(pageSize);
        Argument.IsPositive(pageIndex);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        var dictionaryCacheValue = await GetAsync<IDictionary<T, DateTime?>>(key, cancellationToken).AnyContext();

        if (!dictionaryCacheValue.HasValue)
        {
            return new CacheValue<ICollection<T>>([], false);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var nonExpiredKeys = dictionaryCacheValue.Value!
            .Where(kvp => kvp.Value is null || kvp.Value >= utcNow)
            .Select(kvp => kvp.Key)
            .ToArray();

        if (nonExpiredKeys.Length is 0)
        {
            return new CacheValue<ICollection<T>>([], false);
        }

        if (!pageIndex.HasValue)
        {
            return new CacheValue<ICollection<T>>(nonExpiredKeys, true);
        }

        var skip = (pageIndex.Value - 1) * pageSize;
        var pagedItems = nonExpiredKeys.Skip(skip).Take(pageSize).ToArray();

        return new CacheValue<ICollection<T>>(pagedItems, true);
    }

    #endregion

    #region Remove

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryRemove(key, out var entry))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(!entry.IsExpired);
    }

    public async Task<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);
        var wasExpectedValue = false;

        _memory.TryUpdate(key, (_, existingEntry) =>
        {
            var currentValue = existingEntry.GetValue<T>();

            if (Equals(currentValue, expected))
            {
                existingEntry.ExpiresAt = DateTime.MinValue;
                wasExpectedValue = true;
            }

            return existingEntry;
        });

        var success = wasExpectedValue;
        await _StartMaintenanceAsync().AnyContext();

        return success;
    }

    public Task<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = 0;

        foreach (var key in cacheKeys.Distinct())
        {
            Argument.IsNotNullOrEmpty(key);

            if (_memory.TryRemove(_GetKey(key), out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(prefix))
        {
            var count = _memory.Count;
            _memory.Clear();
            return Task.FromResult(count);
        }

        prefix = _GetKey(prefix);
        var keys = _memory.Keys.ToList();
        var keysToRemove = new List<string>(keys.Count);

        var regex = new Regex(string.Concat("^", Regex.Escape(prefix), ".*?$"), RegexOptions.Singleline);

        foreach (var key in keys)
        {
            if (regex.IsMatch(key))
            {
                keysToRemove.Add(key);
            }
        }

        var removed = 0;

        foreach (var key in keysToRemove)
        {
            if (_memory.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public Task<long> SetRemoveAsync<T>(string key, IEnumerable<T> value, TimeSpan? expiration, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);
        long removed = 0;

        if (value is string stringValue)
        {
            var items = new HashSet<string>([stringValue]);

            _memory.TryUpdate(key, (_, existingEntry) =>
            {
                if (existingEntry.Value is IDictionary<string, DateTime?> { Count: > 0 } dictionary)
                {
                    _ExpireListValues(dictionary);

                    foreach (var v in items)
                    {
                        if (dictionary.Remove(v))
                        {
                            Interlocked.Increment(ref removed);
                        }
                    }

                    existingEntry.Value = dictionary;

                    if (dictionary.Count > 0)
                    {
                        existingEntry.ExpiresAt = dictionary.Values.Contains(null) ? null : dictionary.Values.Max();
                    }
                    else
                    {
                        existingEntry.ExpiresAt = DateTime.MinValue;
                    }
                }

                return existingEntry;
            });

            return Task.FromResult(removed);
        }
        else
        {
            var items = new HashSet<object>();

            foreach (var v in value.Where(v => v is not null))
            {
                items.Add(v!);
            }

            if (items.Count is 0)
            {
                return Task.FromResult(0L);
            }

            _memory.TryUpdate(key, (_, existingEntry) =>
            {
                if (existingEntry.Value is IDictionary<object, DateTime?> { Count: > 0 } dictionary)
                {
                    _ExpireListValuesObject(dictionary);

                    foreach (var v in items)
                    {
                        if (dictionary.Remove(v))
                        {
                            Interlocked.Increment(ref removed);
                        }
                    }

                    existingEntry.Value = dictionary;

                    if (dictionary.Count > 0)
                    {
                        existingEntry.ExpiresAt = dictionary.Values.Contains(null) ? null : dictionary.Values.Max();
                    }
                    else
                    {
                        existingEntry.ExpiresAt = DateTime.MinValue;
                    }
                }

                return existingEntry;
            });

            return Task.FromResult(removed);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _memory.Clear();
        return Task.CompletedTask;
    }

    #endregion

    #region Helpers

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _memory.Clear();
        _disposedCts.Cancel();
        _disposedCts.Dispose();
    }

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : _keyPrefix + key;
    }

    private IReadOnlyList<string> _GetKeys(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return _memory.Where(kvp => !kvp.Value.IsExpired)
                .OrderBy(kvp => kvp.Value.LastAccessTicks)
                .ThenBy(kvp => kvp.Value.InstanceNumber)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        prefix = _GetKey(prefix);

        return _memory.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal) && !x.Value.IsExpired)
            .OrderBy(kvp => kvp.Value.LastAccessTicks)
            .ThenBy(kvp => kvp.Value.InstanceNumber)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    private void _RemoveExpiredKey(string key)
    {
        _memory.TryRemove(key, out _);
    }

    private async Task<bool> _SetInternalAsync(string key, CacheEntry entry, bool addOnly = false)
    {
        if (entry.IsExpired)
        {
            _RemoveExpiredKey(key);
            return false;
        }

        var wasUpdated = true;

        if (addOnly)
        {
            _memory.AddOrUpdate(key, entry, (_, existingEntry) =>
            {
                wasUpdated = false;

                if (existingEntry.IsExpired)
                {
                    wasUpdated = true;
                    return entry;
                }

                return existingEntry;
            });
        }
        else
        {
            _memory.AddOrUpdate(key, entry, (_, _) => entry);
        }

        await _StartMaintenanceAsync(_ShouldCompact).AnyContext();

        return wasUpdated;
    }

    private bool _ShouldCompact => !_disposedCts.IsCancellationRequested && _maxItems.HasValue && _memory.Count > _maxItems;

    private async Task _StartMaintenanceAsync(bool compactImmediately = false)
    {
        if (_disposedCts.IsCancellationRequested)
        {
            return;
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (compactImmediately)
        {
            await _CompactAsync().AnyContext();
        }

        if (TimeSpan.FromMilliseconds(250) < utcNow - _lastMaintenance)
        {
            _lastMaintenance = utcNow;
            _ = Task.Run(_DoMaintenanceAsync, _disposedCts.Token);
        }
    }

    private async Task _CompactAsync()
    {
        if (!_ShouldCompact)
        {
            return;
        }

        using (await _lock.LockAsync(_disposedCts.Token).AnyContext())
        {
            var removalCount = 0;
            const int maxRemovals = 10;

            while (_ShouldCompact && removalCount < maxRemovals)
            {
                var keyToRemove = _FindLeastRecentlyUsed();

                if (keyToRemove is null)
                {
                    break;
                }

                if (_memory.TryRemove(keyToRemove, out _))
                {
                    removalCount++;
                }
                else
                {
                    break;
                }
            }
        }
    }

    private string? _FindLeastRecentlyUsed()
    {
        (string? Key, long LastAccessTicks, long InstanceNumber) oldest = (null, long.MaxValue, 0);

        foreach (var kvp in _memory)
        {
            var isExpired = kvp.Value.IsExpired;

            if (isExpired ||
                kvp.Value.LastAccessTicks < oldest.LastAccessTicks ||
                (kvp.Value.LastAccessTicks == oldest.LastAccessTicks && kvp.Value.InstanceNumber < oldest.InstanceNumber))
            {
                oldest = (kvp.Key, kvp.Value.LastAccessTicks, kvp.Value.InstanceNumber);
            }

            if (isExpired)
            {
                break;
            }
        }

        return oldest.Key;
    }

    private async Task _DoMaintenanceAsync()
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(50);
        var lastAccessMaximumTicks = utcNow.AddMilliseconds(-300).Ticks;

        try
        {
            foreach (var kvp in _memory.ToArray())
            {
                var lastAccessTimeIsInfrequent = kvp.Value.LastAccessTicks < lastAccessMaximumTicks;

                if (!lastAccessTimeIsInfrequent)
                {
                    continue;
                }

                var expiresAt = kvp.Value.ExpiresAt;

                if (!expiresAt.HasValue)
                {
                    continue;
                }

                if (expiresAt < DateTime.MaxValue && expiresAt <= utcNow)
                {
                    _memory.TryRemove(kvp.Key, out _);
                }
            }
        }
        catch
        {
            // ignore
        }

        if (_ShouldCompact)
        {
            await _CompactAsync().AnyContext();
        }
    }

    private void _ExpireListValues<T>(IDictionary<T, DateTime?> dictionary)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var expiredValueKeys = dictionary.Where(kvp => kvp.Value < utcNow).Select(kvp => kvp.Key).ToArray();

        foreach (var expiredKey in expiredValueKeys)
        {
            dictionary.Remove(expiredKey);
        }
    }

    private void _ExpireListValuesObject(IDictionary<object, DateTime?> dictionary)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var expiredValueKeys = dictionary.Where(kvp => kvp.Value < utcNow).Select(kvp => kvp.Key).ToArray();

        foreach (var expiredKey in expiredValueKeys)
        {
            dictionary.Remove(expiredKey);
        }
    }

    #endregion

    #region CacheEntry

    private sealed class CacheEntry
    {
        private static long _InstanceCount;
        private readonly bool _shouldClone;
        private readonly TimeProvider _timeProvider;
        private object? _cacheValue;

        public CacheEntry(object? value, DateTime? expiresAt, TimeProvider timeProvider, bool shouldClone)
        {
            _timeProvider = timeProvider;
            _shouldClone = shouldClone && _TypeRequiresCloning(value?.GetType());
            _cacheValue = _shouldClone ? _DeepClone(value) : value;

            var utcNow = _timeProvider.GetUtcNow();
            LastAccessTicks = utcNow.Ticks;
            LastModifiedTicks = utcNow.Ticks;
            ExpiresAt = expiresAt;
            InstanceNumber = Interlocked.Increment(ref _InstanceCount);
        }

        internal long InstanceNumber { get; }

        internal DateTime? ExpiresAt { get; set; }

        internal bool IsExpired => ExpiresAt.HasValue && ExpiresAt < _timeProvider.GetUtcNow().UtcDateTime;

        internal long LastAccessTicks { get; private set; }

        internal long LastModifiedTicks { get; private set; }

        internal object? Value
        {
            get
            {
                LastAccessTicks = _timeProvider.GetUtcNow().Ticks;
                return _shouldClone ? _DeepClone(_cacheValue) : _cacheValue;
            }
            set
            {
                _cacheValue = _shouldClone ? _DeepClone(value) : value;

                var utcNow = _timeProvider.GetUtcNow();
                LastAccessTicks = utcNow.Ticks;
                LastModifiedTicks = utcNow.Ticks;
            }
        }

        public T? GetValue<T>()
        {
            var val = Value;
            var t = typeof(T);

            if (t == typeof(bool) || t == typeof(string) || t == typeof(char) || t == typeof(DateTime) || t == typeof(object) || _IsNumeric(t))
            {
                return (T?)Convert.ChangeType(val, t);
            }

            if (t == typeof(bool?) || t == typeof(char?) || t == typeof(DateTime?) || _IsNullableNumeric(t))
            {
                return val is null ? default : (T?)Convert.ChangeType(val, Nullable.GetUnderlyingType(t)!);
            }

            return (T?)val;
        }

        private static bool _TypeRequiresCloning(Type? t)
        {
            if (t is null)
            {
                return true;
            }

            if (t == typeof(bool) || t == typeof(bool?) ||
                t == typeof(string) ||
                t == typeof(char) || t == typeof(char?) ||
                _IsNumeric(t) || _IsNullableNumeric(t))
            {
                return false;
            }

            return !t.GetTypeInfo().IsValueType;
        }

        private static bool _IsNumeric(Type t)
        {
            return t == typeof(byte) || t == typeof(sbyte) ||
                   t == typeof(short) || t == typeof(ushort) ||
                   t == typeof(int) || t == typeof(uint) ||
                   t == typeof(long) || t == typeof(ulong) ||
                   t == typeof(float) || t == typeof(double) ||
                   t == typeof(decimal);
        }

        private static bool _IsNullableNumeric(Type t)
        {
            var underlying = Nullable.GetUnderlyingType(t);
            return underlying is not null && _IsNumeric(underlying);
        }

        private static object? _DeepClone(object? value)
        {
            if (value is null)
            {
                return null;
            }

            // Use System.Text.Json for deep cloning
            var json = System.Text.Json.JsonSerializer.Serialize(value, value.GetType());
            return System.Text.Json.JsonSerializer.Deserialize(json, value.GetType());
        }
    }

    #endregion
}

file static class ConcurrentDictionaryExtensions
{
    public static bool TryUpdate<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TKey, TValue, TValue> updateValueFactory
    )
        where TKey : notnull
    {
        while (dictionary.TryGetValue(key, out var existingValue))
        {
            var newValue = updateValueFactory(key, existingValue);

            if (dictionary.TryUpdate(key, newValue, existingValue))
            {
                return true;
            }
        }

        return false;
    }
}
