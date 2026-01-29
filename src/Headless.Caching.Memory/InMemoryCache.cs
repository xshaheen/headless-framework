// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Headless.Checks;
using Nito.AsyncEx;

namespace Headless.Caching;

/// <summary>In-memory cache implementation with LRU eviction, expiration, and list/set operations.</summary>
public sealed class InMemoryCache : IInMemoryCache, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new();
    private readonly AsyncLock _lock = new();
    private readonly CancellationTokenSource _disposedCts = new();
    private readonly TimeProvider _timeProvider;
    private readonly string _keyPrefix;
    private readonly int? _maxItems;
    private readonly bool _shouldClone;
    private readonly long? _maxMemorySize;
    private readonly long? _maxEntrySize;
    private readonly Func<object?, long>? _sizeCalculator;
    private readonly bool _shouldThrowOnMaxEntrySizeExceeded;
    private readonly bool _shouldThrowOnSerializationError;
    private long _currentMemorySize;
    private DateTime _lastMaintenance;
    private bool _isDisposed;

    /// <summary>Gets the current memory size in bytes used by the cache.</summary>
    public long CurrentMemorySize => Interlocked.Read(ref _currentMemorySize);

    public InMemoryCache(TimeProvider timeProvider, InMemoryCacheOptions options)
    {
        _timeProvider = timeProvider;
        _keyPrefix = options.KeyPrefix ?? "";
        _maxItems = options.MaxItems;
        _shouldClone = options.CloneValues;
        _maxMemorySize = options.MaxMemorySize;
        _maxEntrySize = options.MaxEntrySize;
        _sizeCalculator = options.SizeCalculator;
        _shouldThrowOnMaxEntrySizeExceeded = options.ShouldThrowOnMaxEntrySizeExceeded;
        _shouldThrowOnSerializationError = options.ShouldThrowOnSerializationError;

        if ((_maxMemorySize.HasValue || _maxEntrySize.HasValue) && _sizeCalculator is null)
        {
            throw new ArgumentException(
                "SizeCalculator is required when MaxMemorySize or MaxEntrySize is set.",
                nameof(options)
            );
        }
    }

    #region Update

    public ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return new ValueTask<bool>(false);
        }

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(value);

        if (!_ValidateEntrySize(entrySize))
        {
            return new ValueTask<bool>(false);
        }

        var entry = new CacheEntry(
            value,
            expiresAt,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize
        );

        return _SetInternalAsync(key, entry);
    }

    public async ValueTask<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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
            cancellationToken.ThrowIfCancellationRequested();

            if (await UpsertAsync(k, v, expiration, cancellationToken).AnyContext())
            {
                count++;
            }
        }

        return count;
    }

    public ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(key);
            return new ValueTask<bool>(false);
        }

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(value);

        if (!_ValidateEntrySize(entrySize))
        {
            return new ValueTask<bool>(false);
        }

        var entry = new CacheEntry(
            value,
            expiresAt,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize
        );

        return _SetInternalAsync(key, entry, addOnly: true);
    }

    public ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        var prefixedKey = _GetKey(key);

        if (!_memory.ContainsKey(prefixedKey))
        {
            return new ValueTask<bool>(false);
        }

        return UpsertAsync(key, value, expiration, cancellationToken);
    }

    public async ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var newSize = _CalculateEntrySize(value);

        if (!_ValidateEntrySize(newSize))
        {
            return false;
        }

        var wasExpectedValue = false;
        long sizeDelta = 0;

        _memory.TryUpdate(
            key,
            (_, existingEntry) =>
            {
                var currentValue = existingEntry.GetValue<T>();

                if (Equals(currentValue, expected))
                {
                    var oldSize = existingEntry.Size;
                    existingEntry.Value = value;
                    existingEntry.ExpiresAt = expiresAt;
                    existingEntry.Size = newSize;
                    sizeDelta = newSize - oldSize;
                    wasExpectedValue = true;
                }

                return existingEntry;
            }
        );

        if (wasExpectedValue && sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        await _StartMaintenanceAsync().AnyContext();

        return wasExpectedValue;
    }

    public async ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(amount);
        var newEntry = new CacheEntry(
            amount,
            expiresAt,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize
        );

        var result = _memory.AddOrUpdate(
            key,
            _ =>
            {
                Interlocked.Add(ref _currentMemorySize, entrySize);
                return newEntry;
            },
            (_, existingEntry) =>
            {
                double? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<double?>();
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    // Type conversion failed - treat as new value
                }

                existingEntry.Value = currentValue.HasValue ? currentValue.Value + amount : amount;
                existingEntry.ExpiresAt = expiresAt;

                return existingEntry;
            }
        );

        await _StartMaintenanceAsync().AnyContext();

        return result.GetValue<double>();
    }

    public async ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(amount);
        var newEntry = new CacheEntry(
            amount,
            expiresAt,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize
        );

        var result = _memory.AddOrUpdate(
            key,
            _ =>
            {
                Interlocked.Add(ref _currentMemorySize, entrySize);
                return newEntry;
            },
            (_, existingEntry) =>
            {
                long? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<long?>();
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    // Type conversion failed - treat as new value
                }

                existingEntry.Value = currentValue.HasValue ? currentValue.Value + amount : amount;
                existingEntry.ExpiresAt = expiresAt;

                return existingEntry;
            }
        );

        await _StartMaintenanceAsync().AnyContext();

        return result.GetValue<long>();
    }

    public async ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(value);
        var newEntry = new CacheEntry(
            value,
            expiresAt,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize
        );
        var difference = value;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                Interlocked.Add(ref _currentMemorySize, entrySize);
                return newEntry;
            },
            (_, existingEntry) =>
            {
                double? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<double?>();
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    // Type conversion failed - treat as if no current value
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

    public async ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(value);
        var newEntry = new CacheEntry(
            value,
            expiresAt,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize
        );
        var difference = value;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                Interlocked.Add(ref _currentMemorySize, entrySize);
                return newEntry;
            },
            (_, existingEntry) =>
            {
                long? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<long?>();
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    // Type conversion failed - treat as if no current value
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

    public async ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(value);
        var newEntry = new CacheEntry(
            value,
            expiresAt,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize
        );
        var difference = value;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                Interlocked.Add(ref _currentMemorySize, entrySize);
                return newEntry;
            },
            (_, existingEntry) =>
            {
                double? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<double?>();
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    // Type conversion failed - treat as if no current value
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

    public async ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(value);
        var newEntry = new CacheEntry(
            value,
            expiresAt,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize
        );
        var difference = value;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                Interlocked.Add(ref _currentMemorySize, entrySize);
                return newEntry;
            },
            (_, existingEntry) =>
            {
                long? currentValue = null;

                try
                {
                    currentValue = existingEntry.GetValue<long?>();
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    // Type conversion failed - treat as if no current value
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

    public async ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = expiration.HasValue ? utcNow.Add(expiration.Value) : (DateTime?)null;

        if (value is string stringValue)
        {
            var items = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase)
            {
                { stringValue, expiresAt },
            };
            var entrySize = _CalculateEntrySize(items);
            var entry = new CacheEntry(
                items,
                expiresAt,
                _timeProvider,
                _shouldClone,
                _shouldThrowOnSerializationError,
                entrySize
            );

            _memory.AddOrUpdate(
                key,
                _ =>
                {
                    Interlocked.Add(ref _currentMemorySize, entrySize);
                    return entry;
                },
                (existingKey, existingEntry) =>
                {
                    if (existingEntry.Value is not IDictionary<string, DateTime?> dictionary)
                    {
                        throw new InvalidOperationException(
                            $"Unable to add value for key: {existingKey}. Cache value does not contain a dictionary"
                        );
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

            var entrySize = _CalculateEntrySize(items);
            var entry = new CacheEntry(
                items,
                expiresAt,
                _timeProvider,
                _shouldClone,
                _shouldThrowOnSerializationError,
                entrySize
            );

            _memory.AddOrUpdate(
                key,
                _ =>
                {
                    Interlocked.Add(ref _currentMemorySize, entrySize);
                    return entry;
                },
                (existingKey, existingEntry) =>
                {
                    if (existingEntry.Value is not IDictionary<object, DateTime?> dictionary)
                    {
                        throw new InvalidOperationException(
                            $"Unable to add value for key: {existingKey}. Cache value does not contain a set"
                        );
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

    public ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return new ValueTask<CacheValue<T>>(CacheValue<T>.NoValue);
        }

        if (existingEntry.IsExpired)
        {
            return new ValueTask<CacheValue<T>>(CacheValue<T>.NoValue);
        }

        try
        {
            var value = existingEntry.GetValue<T>();
            return new ValueTask<CacheValue<T>>(new CacheValue<T>(value, true));
        }
        catch (Exception) when (!_shouldThrowOnSerializationError)
        {
            return new ValueTask<CacheValue<T>>(CacheValue<T>.NoValue);
        }
    }

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
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

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(prefix);
        return await GetAllAsync<T>(keys, cancellationToken);
    }

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<string>>(_GetKeys(prefix));
    }

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return new ValueTask<bool>(false);
        }

        return new ValueTask<bool>(!existingEntry.IsExpired);
    }

    public ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(prefix))
        {
            return new ValueTask<int>(_memory.Count(i => !i.Value.IsExpired));
        }

        prefix = _GetKey(prefix);
        var count = _memory.Count(x => x.Key.StartsWith(prefix, StringComparison.Ordinal) && !x.Value.IsExpired);

        return new ValueTask<int>(count);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        }

        if (existingEntry.IsExpired)
        {
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        }

        if (!existingEntry.ExpiresAt.HasValue || existingEntry.ExpiresAt.Value == DateTime.MaxValue)
        {
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        }

        return new ValueTask<TimeSpan?>(existingEntry.ExpiresAt.Value.Subtract(_timeProvider.GetUtcNow().UtcDateTime));
    }

    public async ValueTask<CacheValue<ICollection<T>>> GetSetAsync<T>(
        string key,
        int? pageIndex = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default
    )
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

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var nonExpiredKeys = dictionaryCacheValue
            .Value!.Where(kvp => kvp.Value is null || kvp.Value >= utcNow)
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

    public ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryRemove(key, out var entry))
        {
            return new ValueTask<bool>(false);
        }

        Interlocked.Add(ref _currentMemorySize, -entry.Size);
        return new ValueTask<bool>(!entry.IsExpired);
    }

    public async ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);
        var wasExpectedValue = false;

        _memory.TryUpdate(
            key,
            (_, existingEntry) =>
            {
                var currentValue = existingEntry.GetValue<T>();

                if (Equals(currentValue, expected))
                {
                    existingEntry.ExpiresAt = DateTime.MinValue;
                    wasExpectedValue = true;
                }

                return existingEntry;
            }
        );

        var success = wasExpectedValue;
        await _StartMaintenanceAsync().AnyContext();

        return success;
    }

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = 0;

        foreach (var key in cacheKeys.Distinct())
        {
            Argument.IsNotNullOrEmpty(key);

            if (_memory.TryRemove(_GetKey(key), out var entry))
            {
                Interlocked.Add(ref _currentMemorySize, -entry.Size);
                removed++;
            }
        }

        return new ValueTask<int>(removed);
    }

    public ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(prefix))
        {
            var count = _memory.Count;
            _memory.Clear();
            Interlocked.Exchange(ref _currentMemorySize, 0);
            return new ValueTask<int>(count);
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
            if (_memory.TryRemove(key, out var entry))
            {
                Interlocked.Add(ref _currentMemorySize, -entry.Size);
                removed++;
            }
        }

        return new ValueTask<int>(removed);
    }

    public ValueTask<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
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

            _memory.TryUpdate(
                key,
                (_, existingEntry) =>
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
                }
            );

            return new ValueTask<long>(removed);
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
                return new ValueTask<long>(0L);
            }

            _memory.TryUpdate(
                key,
                (_, existingEntry) =>
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
                }
            );

            return new ValueTask<long>(removed);
        }
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _memory.Clear();
        Interlocked.Exchange(ref _currentMemorySize, 0);
        return ValueTask.CompletedTask;
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
        Interlocked.Exchange(ref _currentMemorySize, 0);
        _disposedCts.Cancel();
        _disposedCts.Dispose();
    }

    private void _ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : _keyPrefix + key;
    }

    private IReadOnlyList<string> _GetKeys(string prefix)
    {
        var prefixedPrefix = string.IsNullOrEmpty(prefix) ? null : _GetKey(prefix);

        return _memory
            .Where(kvp =>
                !kvp.Value.IsExpired
                && (prefixedPrefix is null || kvp.Key.StartsWith(prefixedPrefix, StringComparison.Ordinal))
            )
            .OrderBy(kvp => kvp.Value.LastAccessTicks)
            .ThenBy(kvp => kvp.Value.InstanceNumber)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    private void _RemoveExpiredKey(string key)
    {
        _memory.TryRemove(key, out _);
    }

    private async ValueTask<bool> _SetInternalAsync(string key, CacheEntry entry, bool addOnly = false)
    {
        if (entry.IsExpired)
        {
            _RemoveExpiredKey(key);
            return false;
        }

        var wasUpdated = true;
        long sizeDelta = 0;

        if (addOnly)
        {
            _memory.AddOrUpdate(
                key,
                _ =>
                {
                    sizeDelta = entry.Size;
                    return entry;
                },
                (_, existingEntry) =>
                {
                    wasUpdated = false;

                    if (existingEntry.IsExpired)
                    {
                        sizeDelta = entry.Size - existingEntry.Size;
                        wasUpdated = true;
                        return entry;
                    }

                    return existingEntry;
                }
            );
        }
        else
        {
            _memory.AddOrUpdate(
                key,
                _ =>
                {
                    sizeDelta = entry.Size;
                    return entry;
                },
                (_, existingEntry) =>
                {
                    sizeDelta = entry.Size - existingEntry.Size;
                    return entry;
                }
            );
        }

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        await _StartMaintenanceAsync(_ShouldCompact).AnyContext();

        return wasUpdated;
    }

    private bool _ShouldCompact =>
        !_disposedCts.IsCancellationRequested
        && (
            (_maxItems.HasValue && _memory.Count > _maxItems)
            || (_maxMemorySize.HasValue && Interlocked.Read(ref _currentMemorySize) > _maxMemorySize)
        );

    private async Task _StartMaintenanceAsync(bool compactImmediately = false)
    {
        if (_disposedCts.IsCancellationRequested)
        {
            return;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

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
                var keyToRemove = _FindLeastRecentlyUsedOrLargest();

                if (keyToRemove is null)
                {
                    break;
                }

                if (_memory.TryRemove(keyToRemove, out var removedEntry))
                {
                    Interlocked.Add(ref _currentMemorySize, -removedEntry.Size);
                    removalCount++;
                }
                else
                {
                    break;
                }
            }
        }
    }

    private string? _FindLeastRecentlyUsedOrLargest()
    {
        // Redis-style random sampling: sample a few entries and evict the best candidate.
        // This is O(k) where k is sample size, instead of O(n) scanning entire dictionary.
        const int sampleSize = 5;
        var keysSnapshot = _memory.Keys.ToArray();
        var keysCount = keysSnapshot.Length;

        if (keysCount is 0)
        {
            return null;
        }

        // When memory-constrained, prefer evicting larger items that are also less frequently used
        // When item-count constrained, prefer evicting least recently used (LRU)
        var isMemoryConstrained = _maxMemorySize.HasValue && Interlocked.Read(ref _currentMemorySize) > _maxMemorySize;
        (string? Key, long LastAccessTicks, long InstanceNumber, long Size) best = (null, long.MaxValue, 0, 0);

        // For small caches, scan all entries. Otherwise sample randomly.
        var indicesToSample =
            keysCount <= sampleSize ? Enumerable.Range(0, keysCount) : _GenerateRandomIndices(sampleSize, keysCount);

        foreach (var index in indicesToSample)
        {
            var key = keysSnapshot[index];

            if (!_memory.TryGetValue(key, out var entry))
            {
                continue;
            }

            if (entry.IsExpired)
            {
                // Always prefer expired items first
                return key;
            }

            var isBetter = false;

            if (isMemoryConstrained)
            {
                // When memory constrained: prefer larger items, breaking ties by LRU
                if (
                    entry.Size > best.Size
                    || (entry.Size == best.Size && entry.LastAccessTicks < best.LastAccessTicks)
                    || (
                        entry.Size == best.Size
                        && entry.LastAccessTicks == best.LastAccessTicks
                        && entry.InstanceNumber < best.InstanceNumber
                    )
                )
                {
                    isBetter = true;
                }
            }
            else
            {
                // Standard LRU
                if (
                    entry.LastAccessTicks < best.LastAccessTicks
                    || (entry.LastAccessTicks == best.LastAccessTicks && entry.InstanceNumber < best.InstanceNumber)
                )
                {
                    isBetter = true;
                }
            }

            if (isBetter)
            {
                best = (key, entry.LastAccessTicks, entry.InstanceNumber, entry.Size);
            }
        }

        return best.Key;
    }

    private static IEnumerable<int> _GenerateRandomIndices(int count, int maxExclusive)
    {
        // Use HashSet to avoid duplicates
        var indices = new HashSet<int>(count);

        while (indices.Count < count)
        {
            indices.Add(Random.Shared.Next(maxExclusive));
        }

        return indices;
    }

    private async Task _DoMaintenanceAsync()
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(50);
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
                    if (_memory.TryRemove(kvp.Key, out var removedEntry))
                    {
                        Interlocked.Add(ref _currentMemorySize, -removedEntry.Size);
                    }
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
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var expiredValueKeys = dictionary.Where(kvp => kvp.Value < utcNow).Select(kvp => kvp.Key).ToArray();

        foreach (var expiredKey in expiredValueKeys)
        {
            dictionary.Remove(expiredKey);
        }
    }

    private void _ExpireListValuesObject(IDictionary<object, DateTime?> dictionary)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var expiredValueKeys = dictionary.Where(kvp => kvp.Value < utcNow).Select(kvp => kvp.Key).ToArray();

        foreach (var expiredKey in expiredValueKeys)
        {
            dictionary.Remove(expiredKey);
        }
    }

    private long _CalculateEntrySize(object? value)
    {
        if (_sizeCalculator is null)
        {
            return 0;
        }

        var size = _sizeCalculator(value);
        return size < 0 ? 0 : size;
    }

    private bool _ValidateEntrySize(long entrySize)
    {
        if (!_maxEntrySize.HasValue || entrySize <= _maxEntrySize.Value)
        {
            return true;
        }

        if (_shouldThrowOnMaxEntrySizeExceeded)
        {
            throw new MaxEntrySizeExceededException(
                $"Entry size {entrySize} exceeds maximum allowed size of {_maxEntrySize.Value} bytes."
            );
        }

        // Skip entry - it's too large
        return false;
    }

    #endregion

    #region CacheEntry

    private sealed class CacheEntry
    {
        private static long _InstanceCount;
        private readonly bool _shouldClone;
        private readonly bool _shouldThrowOnSerializationError;
        private readonly TimeProvider _timeProvider;
        private object? _cacheValue;

        public CacheEntry(
            object? value,
            DateTime? expiresAt,
            TimeProvider timeProvider,
            bool shouldClone,
            bool shouldThrowOnSerializationError = true,
            long size = 0
        )
        {
            _timeProvider = timeProvider;
            _shouldClone = shouldClone && _TypeRequiresCloning(value?.GetType());
            _shouldThrowOnSerializationError = shouldThrowOnSerializationError;
            _cacheValue = _shouldClone ? _DeepClone(value) : value;

            var utcNow = _timeProvider.GetUtcNow();
            LastAccessTicks = utcNow.Ticks;
            LastModifiedTicks = utcNow.Ticks;
            ExpiresAt = expiresAt;
            Size = size;
            InstanceNumber = Interlocked.Increment(ref _InstanceCount);
        }

        internal long InstanceNumber { get; }

        internal DateTime? ExpiresAt { get; set; }

        internal bool IsExpired => ExpiresAt.HasValue && ExpiresAt < _timeProvider.GetUtcNow().UtcDateTime;

        internal long LastAccessTicks { get; private set; }

        internal long LastModifiedTicks { get; private set; }

        internal long Size { get; set; }

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

            if (
                t == typeof(bool)
                || t == typeof(string)
                || t == typeof(char)
                || t == typeof(DateTime)
                || t == typeof(object)
                || _IsNumeric(t)
            )
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

            if (
                t == typeof(bool)
                || t == typeof(bool?)
                || t == typeof(string)
                || t == typeof(char)
                || t == typeof(char?)
                || _IsNumeric(t)
                || _IsNullableNumeric(t)
            )
            {
                return false;
            }

            return !t.GetTypeInfo().IsValueType;
        }

        private static bool _IsNumeric(Type t)
        {
            return t == typeof(byte)
                || t == typeof(sbyte)
                || t == typeof(short)
                || t == typeof(ushort)
                || t == typeof(int)
                || t == typeof(uint)
                || t == typeof(long)
                || t == typeof(ulong)
                || t == typeof(float)
                || t == typeof(double)
                || t == typeof(decimal);
        }

        private static bool _IsNullableNumeric(Type t)
        {
            var underlying = Nullable.GetUnderlyingType(t);
            return underlying is not null && _IsNumeric(underlying);
        }

        private object? _DeepClone(object? value)
        {
            if (value is null)
            {
                return null;
            }

            try
            {
                // Use System.Text.Json for deep cloning
                var json = System.Text.Json.JsonSerializer.Serialize(value, value.GetType());
                return System.Text.Json.JsonSerializer.Deserialize(json, value.GetType());
            }
            catch (Exception) when (!_shouldThrowOnSerializationError)
            {
                return value;
            }
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
