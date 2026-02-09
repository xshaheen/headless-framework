// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Checks;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nito.AsyncEx;

namespace Headless.Caching;

/// <summary>In-memory cache implementation with LRU eviction, expiration, and list/set operations.</summary>
public sealed class InMemoryCache : IInMemoryCache, IDisposable
{
    /// <summary>Minimum interval between background maintenance runs.</summary>
    private const int _MaintenanceIntervalMs = 250;

    /// <summary>Maximum entries to evict per compaction cycle.</summary>
    private const int _MaxEvictionsPerCompaction = 10;

    /// <summary>Number of random entries to sample when finding eviction candidates (Redis-style).</summary>
    private const int _EvictionSampleSize = 5;

    /// <summary>Entries accessed within this window are skipped during maintenance (considered hot).</summary>
    private const int _HotAccessWindowMs = 300;

    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new(StringComparer.Ordinal);
    private readonly AsyncLock _lock = new();
    private readonly KeyedAsyncLock _keyedLock = new();
    private readonly CancellationTokenSource _disposedCts = new();
    private readonly ILogger _logger;
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
    private long _lastMaintenanceTicks;
    private int _isDisposed;

    /// <summary>Gets the current memory size in bytes used by the cache.</summary>
    public long CurrentMemorySize => Interlocked.Read(ref _currentMemorySize);

    public InMemoryCache(TimeProvider timeProvider, InMemoryCacheOptions options, ILogger<InMemoryCache>? logger = null)
    {
        _logger = logger ?? NullLogger<InMemoryCache>.Instance;
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

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheValue = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);

        if (cacheValue.HasValue)
        {
            return cacheValue;
        }

        using (await _keyedLock.LockAsync(key, cancellationToken).ConfigureAwait(false))
        {
            // Double-check after acquiring lock
            cacheValue = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
            if (cacheValue.HasValue)
            {
                return cacheValue;
            }

            var value = await factory(cancellationToken).ConfigureAwait(false);
            await UpsertAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);

            return new(value, hasValue: true);
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
        _ThrowIfDisposed();
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

            if (await UpsertAsync(k, v, expiration, cancellationToken).ConfigureAwait(false))
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

        return _SetInternalAsync(key, entry, addOnly: true);
    }

    public async ValueTask<bool> TryReplaceAsync<T>(
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

        var prefixedKey = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            _RemoveExpiredKey(prefixedKey);
            return false;
        }

        var expiresAt = expiration.HasValue
            ? _timeProvider.GetUtcNow().UtcDateTime.Add(expiration.Value)
            : (DateTime?)null;
        var entrySize = _CalculateEntrySize(value);

        if (!_ValidateEntrySize(entrySize))
        {
            return false;
        }

        var wasReplaced = false;
        long sizeDelta = 0;

        // Use atomic TryUpdate to avoid TOCTOU race condition
        _memory.TryUpdate(
            prefixedKey,
            (_, existingEntry) =>
            {
                if (existingEntry.IsExpired)
                {
                    // Entry exists but is expired - don't replace
                    return existingEntry;
                }

                var oldSize = existingEntry.Size;
                existingEntry.Value = value;
                existingEntry.ExpiresAt = expiresAt;
                existingEntry.Size = entrySize;
                sizeDelta = entrySize - oldSize;
                wasReplaced = true;

                return existingEntry;
            }
        );

        if (wasReplaced && sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return wasReplaced;
    }

    public async ValueTask<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T? expected,
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

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return wasExpectedValue;
    }

    public async ValueTask<double> IncrementAsync(
        string key,
        double amount,
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

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return result.GetValue<double>();
    }

    public async ValueTask<long> IncrementAsync(
        string key,
        long amount,
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

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return result.GetValue<long>();
    }

    public async ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
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

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return difference;
    }

    public async ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
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

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return difference;
    }

    public async ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
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

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return difference;
    }

    public async ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
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

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return difference;
    }

    public async ValueTask<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (expiration is { Ticks: <= 0 })
        {
            await SetRemoveAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
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

            long sizeDelta = 0;

            _memory.AddOrUpdate(
                key,
                _ =>
                {
                    sizeDelta = entrySize;
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

                    var oldSize = existingEntry.Size;
                    _ExpireListValues(dictionary);
                    dictionary[stringValue] = expiresAt;
                    existingEntry.Value = dictionary;
                    existingEntry.ExpiresAt = dictionary.Values.Contains(null) ? null : dictionary.Values.Max();

                    var newSize = _CalculateEntrySize(dictionary);
                    existingEntry.Size = newSize;
                    sizeDelta = newSize - oldSize;

                    return existingEntry;
                }
            );

            if (sizeDelta != 0)
            {
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            }

            await _StartMaintenanceAsync().ConfigureAwait(false);

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

            long sizeDelta = 0;

            _memory.AddOrUpdate(
                key,
                _ =>
                {
                    sizeDelta = entrySize;
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

                    var oldSize = existingEntry.Size;
                    _ExpireListValues(dictionary);

                    foreach (var kvp in items)
                    {
                        dictionary[kvp.Key] = kvp.Value;
                    }

                    existingEntry.Value = dictionary;
                    existingEntry.ExpiresAt = dictionary.Values.Contains(null) ? null : dictionary.Values.Max();

                    var newSize = _CalculateEntrySize(dictionary);
                    existingEntry.Size = newSize;
                    sizeDelta = newSize - oldSize;

                    return existingEntry;
                }
            );

            if (sizeDelta != 0)
            {
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            }

            await _StartMaintenanceAsync().ConfigureAwait(false);

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
        catch (Exception ex) when (!_shouldThrowOnSerializationError)
        {
            _logger.LogWarning(
                ex,
                "Deserialization error for cache key (hash: {KeyHash})",
                string.GetHashCode(key, StringComparison.Ordinal)
            );
            return new ValueTask<CacheValue<T>>(CacheValue<T>.NoValue);
        }
    }

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var map = new Dictionary<string, CacheValue<T>>(StringComparer.Ordinal);

        foreach (var key in cacheKeys)
        {
            map[key] = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }

        return map;
    }

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        var keys = _GetKeys(prefix);
        return await GetAllAsync<T>(keys, cancellationToken);
    }

    public ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
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

    public ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(prefix))
        {
            return new ValueTask<long>(_memory.LongCount(i => !i.Value.IsExpired));
        }

        prefix = _GetKey(prefix);
        var count = _memory.LongCount(x => x.Key.StartsWith(prefix, StringComparison.Ordinal) && !x.Value.IsExpired);

        return new ValueTask<long>(count);
    }

    public ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
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
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(pageSize);
        Argument.IsPositive(pageIndex);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        var dictionaryCacheValue = await GetAsync<IDictionary<T, DateTime?>>(key, cancellationToken)
            .ConfigureAwait(false);

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
        _ThrowIfDisposed();
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
        await _StartMaintenanceAsync().ConfigureAwait(false);

        return success;
    }

    public ValueTask<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var removed = 0;

        foreach (var key in cacheKeys.Distinct(StringComparer.Ordinal))
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
        _ThrowIfDisposed();
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

        foreach (var key in keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
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
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);
        long removed = 0;

        if (value is string stringValue)
        {
            var items = new HashSet<string>([stringValue], StringComparer.Ordinal);

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
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _memory.Clear();
        Interlocked.Exchange(ref _currentMemorySize, 0);
        _keyedLock.Dispose();
        _disposedCts.Cancel();
        _disposedCts.Dispose();
    }

    private void _ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
    }

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : $"{_keyPrefix}:{key}";
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

        await _StartMaintenanceAsync(ShouldCompact).ConfigureAwait(false);

        return wasUpdated;
    }

    private bool ShouldCompact =>
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
            await _CompactAsync().ConfigureAwait(false);
        }

        // Use Interlocked.CompareExchange to ensure only one thread spawns maintenance
        var utcNowTicks = utcNow.Ticks;
        var lastTicks = Volatile.Read(ref _lastMaintenanceTicks);
        var thresholdTicks = TimeSpan.FromMilliseconds(_MaintenanceIntervalMs).Ticks;

        if (utcNowTicks - lastTicks > thresholdTicks)
        {
            // Atomically try to claim the maintenance slot
            if (Interlocked.CompareExchange(ref _lastMaintenanceTicks, utcNowTicks, lastTicks) == lastTicks)
            {
                _ = Task.Run(_DoMaintenanceAsync, _disposedCts.Token);
            }
        }
    }

    private async Task _CompactAsync()
    {
        if (!ShouldCompact)
        {
            return;
        }

        using (await _lock.LockAsync(_disposedCts.Token).ConfigureAwait(false))
        {
            var removalCount = 0;

            while (ShouldCompact && removalCount < _MaxEvictionsPerCompaction)
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
        // Redis-style random sampling using reservoir sampling (Algorithm R).
        // This is O(n) iteration but O(k) memory where k is sample size.
        // Avoids materializing Keys.ToArray() which would allocate O(n) memory.

        // When memory-constrained, prefer evicting larger items that are also less frequently used
        // When item-count constrained, prefer evicting least recently used (LRU)
        var isMemoryConstrained = _maxMemorySize.HasValue && Interlocked.Read(ref _currentMemorySize) > _maxMemorySize;
        (string? Key, long LastAccessTicks, long InstanceNumber, long Size) best = (null, long.MaxValue, 0, 0);

        // Reservoir sampling: fill reservoir with first k items, then probabilistically replace
        var reservoir = new (string Key, CacheEntry Entry)[_EvictionSampleSize];
        var reservoirCount = 0;
        var itemIndex = 0;

        foreach (var kvp in _memory)
        {
            if (kvp.Value.IsExpired)
            {
                // Always prefer expired items first - return immediately
                return kvp.Key;
            }

            if (reservoirCount < _EvictionSampleSize)
            {
                reservoir[reservoirCount++] = (kvp.Key, kvp.Value);
            }
            else
            {
                // Replace element at random index with decreasing probability
                var j = Random.Shared.Next(itemIndex + 1);

                if (j < _EvictionSampleSize)
                {
                    reservoir[j] = (kvp.Key, kvp.Value);
                }
            }

            itemIndex++;
        }

        if (reservoirCount == 0)
        {
            return null;
        }

        // Find best candidate from reservoir
        for (var i = 0; i < reservoirCount; i++)
        {
            var (key, entry) = reservoir[i];
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

    private async Task _DoMaintenanceAsync()
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(50);
        var lastAccessMaximumTicks = utcNow.AddMilliseconds(-_HotAccessWindowMs).Ticks;

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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache maintenance task failed");
        }

        if (ShouldCompact)
        {
            await _CompactAsync().ConfigureAwait(false);
        }
    }

    private void _ExpireListValues<T>(IDictionary<T, DateTime?> dictionary)
        where T : notnull
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
        private static long _instanceCount;
        private readonly bool _shouldClone;
        private readonly bool _shouldThrowOnSerializationError;
        private readonly TimeProvider _timeProvider;
        private object? _cacheValue;
        private long _lastAccessTicks;
        private long _size;

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
            _lastAccessTicks = utcNow.Ticks;
            ExpiresAt = expiresAt;
            _size = size;
            InstanceNumber = Interlocked.Increment(ref _instanceCount);
        }

        internal long InstanceNumber { get; }

        internal DateTime? ExpiresAt { get; set; }

        internal bool IsExpired => ExpiresAt.HasValue && ExpiresAt < _timeProvider.GetUtcNow().UtcDateTime;

        internal long LastAccessTicks => Interlocked.Read(ref _lastAccessTicks);

        internal long Size
        {
            get => Interlocked.Read(ref _size);
            set => Interlocked.Exchange(ref _size, value);
        }

        internal object? Value
        {
            get
            {
                Interlocked.Exchange(ref _lastAccessTicks, _timeProvider.GetUtcNow().Ticks);
                return _shouldClone ? _DeepClone(_cacheValue) : _cacheValue;
            }
            set
            {
                _cacheValue = _shouldClone ? _DeepClone(value) : value;
                Interlocked.Exchange(ref _lastAccessTicks, _timeProvider.GetUtcNow().Ticks);
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
                return (T?)Convert.ChangeType(val, t, CultureInfo.InvariantCulture);
            }

            if (t == typeof(bool?) || t == typeof(char?) || t == typeof(DateTime?) || _IsNullableNumeric(t))
            {
                return val is null
                    ? default
                    : (T?)Convert.ChangeType(val, Nullable.GetUnderlyingType(t)!, CultureInfo.InvariantCulture);
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
                var json = JsonSerializer.Serialize(value, value.GetType());
                return JsonSerializer.Deserialize(json, value.GetType());
            }
            catch (Exception) when (!_shouldThrowOnSerializationError)
            {
                // Cloning failed - return original value (no logging available in CacheEntry).
                // This is less critical than deserialization failure since caller still gets data.
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
