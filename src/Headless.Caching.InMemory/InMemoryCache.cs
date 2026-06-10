// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using Headless.Checks;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nito.AsyncEx;

namespace Headless.Caching;

/// <summary>In-memory cache implementation with LRU eviction, expiration, and list/set operations.</summary>
public sealed class InMemoryCache : IInMemoryCache, IFactoryCacheStore, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new(StringComparer.Ordinal);

    // Reverse tag index: tag -> set of prefixed keys whose entry carried the tag when written. Memberships may
    // be momentarily stale (an untagged overwrite races the index update), so RemoveByTagAsync always verifies
    // against the LIVE entry's tags before removing. Empty per-tag sets are intentionally not pruned: removing
    // a set instance races a concurrent writer adding to that same instance and would lose its membership; the
    // residue is bounded by the process-lifetime distinct-tag cardinality.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new(
        StringComparer.Ordinal
    );

    private readonly PriorityQueue<string, long> _expirationQueue = new();
    private readonly Lock _expirationLock = new();
    private readonly ConcurrentQueue<string> _lruQueue = new();
    private readonly AsyncLock _lock = new();
    private readonly FactoryCacheCoordinator _coordinator;
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
    private readonly long _maintenanceIntervalTicks;
    private readonly int _maxEvictionsPerCompaction;
    private readonly int _evictionSampleSize;
    private long _currentMemorySize;
    private long _lastMaintenanceTicks;
    private int _maintenanceRunning;
    private int _isDisposed;

    /// <summary>Gets the current memory size in bytes used by the cache.</summary>
    public long CurrentMemorySize => Interlocked.Read(ref _currentMemorySize);

    public InMemoryCache(TimeProvider timeProvider, InMemoryCacheOptions options, ILogger<InMemoryCache>? logger = null)
    {
        _logger = logger ?? NullLogger<InMemoryCache>.Instance;
        _coordinator = new FactoryCacheCoordinator(timeProvider, _logger);
        _timeProvider = timeProvider;
        _keyPrefix = options.KeyPrefix ?? "";
        _maxItems = options.MaxItems;
        _shouldClone = options.CloneValues;
        _maxMemorySize = options.MaxMemorySize;
        _maxEntrySize = options.MaxEntrySize;
        _sizeCalculator = options.SizeCalculator;
        _shouldThrowOnMaxEntrySizeExceeded = options.ShouldThrowOnMaxEntrySizeExceeded;
        _shouldThrowOnSerializationError = options.ShouldThrowOnSerializationError;
        _maintenanceIntervalTicks = options.MaintenanceInterval.Ticks;
        _maxEvictionsPerCompaction = options.MaxEvictionsPerCompaction;
        _evictionSampleSize = options.EvictionSampleSize;

        if ((_maxMemorySize.HasValue || _maxEntrySize.HasValue) && _sizeCalculator is null)
        {
            throw new ArgumentException(
                @"SizeCalculator is required when MaxMemorySize or MaxEntrySize is set.",
                nameof(options)
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        cancellationToken.ThrowIfCancellationRequested();

        return await _coordinator.GetOrAddAsync(this, key, factory, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        cancellationToken.ThrowIfCancellationRequested();

        return await _coordinator.GetOrAddAsync(this, key, factory, options, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        await ((IFactoryCacheStore)this)
            .UpsertEntryAsync(key, value, options, _timeProvider, cancellationToken)
            .ConfigureAwait(false);

        return true;
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
        IReadOnlySet<string>? previousTags = null;

        // Use atomic TryUpdate to avoid TOCTOU race condition
        _memory.TryUpdate(
            prefixedKey,
            (_, existingEntry) =>
            {
                wasReplaced = false;
                sizeDelta = 0;
                previousTags = null;

                if (existingEntry.IsExpired)
                {
                    return existingEntry;
                }

                sizeDelta = entrySize - existingEntry.Size;
                wasReplaced = true;
                previousTags = existingEntry.Tags;

                return new CacheEntry(
                    value,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    entrySize
                );
            }
        );

        if (wasReplaced)
        {
            if (sizeDelta != 0)
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            _TrackUpdate(prefixedKey, expiresAt);
            _UpdateTagIndex(prefixedKey, previousTags, currentTags: null);
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
        IReadOnlySet<string>? previousTags = null;

        _memory.TryUpdate(
            key,
            (_, existingEntry) =>
            {
                wasExpectedValue = false;
                sizeDelta = 0;
                previousTags = null;

                if (existingEntry.IsExpired)
                {
                    return existingEntry;
                }

                var currentValue = existingEntry.GetValue<T>();

                if (!Equals(currentValue, expected))
                {
                    return existingEntry;
                }

                sizeDelta = newSize - existingEntry.Size;
                wasExpectedValue = true;
                previousTags = existingEntry.Tags;

                return new CacheEntry(
                    value,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    newSize
                );
            }
        );

        if (wasExpectedValue)
        {
            if (sizeDelta != 0)
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            _TrackUpdate(key, expiresAt);
            _UpdateTagIndex(key, previousTags, currentTags: null);
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

        long sizeDelta = 0;
        double resultValue = 0;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                var size = _CalculateEntrySize(amount);
                sizeDelta = size;
                resultValue = amount;

                return new CacheEntry(
                    amount,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    size
                );
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

                var computedValue = currentValue.HasValue ? currentValue.Value + amount : amount;
                var computedSize = _CalculateEntrySize(computedValue);
                sizeDelta = computedSize - existingEntry.Size;
                resultValue = computedValue;

                return new CacheEntry(
                    computedValue,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    computedSize
                );
            }
        );

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        _TrackUpdate(key, expiresAt);

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return resultValue;
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

        long sizeDelta = 0;
        long resultValue = 0;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                var size = _CalculateEntrySize(amount);
                sizeDelta = size;
                resultValue = amount;

                return new CacheEntry(
                    amount,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    size
                );
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

                var computedValue = currentValue.HasValue ? currentValue.Value + amount : amount;
                var computedSize = _CalculateEntrySize(computedValue);
                sizeDelta = computedSize - existingEntry.Size;
                resultValue = computedValue;

                return new CacheEntry(
                    computedValue,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    computedSize
                );
            }
        );

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        _TrackUpdate(key, expiresAt);

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return resultValue;
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

        long sizeDelta = 0;
        double difference = 0;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                var size = _CalculateEntrySize(value);
                sizeDelta = size;
                difference = value;

                return new CacheEntry(
                    value,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    size
                );
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
                    var computedSize = _CalculateEntrySize(value);
                    sizeDelta = computedSize - existingEntry.Size;

                    return new CacheEntry(
                        value,
                        expiresAt,
                        _timeProvider,
                        _shouldClone,
                        _shouldThrowOnSerializationError,
                        computedSize
                    );
                }

                difference = 0;
                sizeDelta = 0;

                return existingEntry.WithExpiration(expiresAt);
            }
        );

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        _TrackUpdate(key, expiresAt);

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

        long sizeDelta = 0;
        long difference = 0;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                var size = _CalculateEntrySize(value);
                sizeDelta = size;
                difference = value;

                return new CacheEntry(
                    value,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    size
                );
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
                    var computedSize = _CalculateEntrySize(value);
                    sizeDelta = computedSize - existingEntry.Size;

                    return new CacheEntry(
                        value,
                        expiresAt,
                        _timeProvider,
                        _shouldClone,
                        _shouldThrowOnSerializationError,
                        computedSize
                    );
                }

                difference = 0;
                sizeDelta = 0;

                return existingEntry.WithExpiration(expiresAt);
            }
        );

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        _TrackUpdate(key, expiresAt);

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

        long sizeDelta = 0;
        double difference = 0;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                var size = _CalculateEntrySize(value);
                sizeDelta = size;
                difference = value;

                return new CacheEntry(
                    value,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    size
                );
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
                    var computedSize = _CalculateEntrySize(value);
                    sizeDelta = computedSize - existingEntry.Size;

                    return new CacheEntry(
                        value,
                        expiresAt,
                        _timeProvider,
                        _shouldClone,
                        _shouldThrowOnSerializationError,
                        computedSize
                    );
                }

                difference = 0;
                sizeDelta = 0;

                return existingEntry.WithExpiration(expiresAt);
            }
        );

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        _TrackUpdate(key, expiresAt);

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

        long sizeDelta = 0;
        long difference = 0;

        _memory.AddOrUpdate(
            key,
            _ =>
            {
                var size = _CalculateEntrySize(value);
                sizeDelta = size;
                difference = value;

                return new CacheEntry(
                    value,
                    expiresAt,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    size
                );
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
                    var computedSize = _CalculateEntrySize(value);
                    sizeDelta = computedSize - existingEntry.Size;

                    return new CacheEntry(
                        value,
                        expiresAt,
                        _timeProvider,
                        _shouldClone,
                        _shouldThrowOnSerializationError,
                        computedSize
                    );
                }

                difference = 0;
                sizeDelta = 0;

                return existingEntry.WithExpiration(expiresAt);
            }
        );

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        _TrackUpdate(key, expiresAt);

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

        if (expiration is { Ticks: <= 0 })
        {
            await SetRemoveAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        key = _GetKey(key);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = expiration.HasValue ? utcNow.Add(expiration.Value) : (DateTime?)null;

        if (typeof(T) == typeof(string))
        {
            var newItems = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in value)
            {
                if (v is not null)
                {
                    newItems[(string)(object)v] = expiresAt;
                }
            }

            if (newItems.Count is 0)
            {
                return 0;
            }

            var entrySize = _CalculateEntrySize(newItems);
            var entry = new CacheEntry(
                newItems,
                expiresAt,
                _timeProvider,
                _shouldClone,
                _shouldThrowOnSerializationError,
                entrySize
            );
            long sizeDelta = 0;

            var committed = _memory.AddOrUpdate(
                key,
                _ =>
                {
                    sizeDelta = entrySize;
                    return entry;
                },
                (existingKey, existingEntry) =>
                {
                    if (existingEntry.PeekValue() is not IDictionary<string, DateTime?> dictionary)
                    {
                        throw new InvalidOperationException(
                            $"Unable to add value for key: {existingKey}. Cache value does not contain a set"
                        );
                    }

                    var updatedDict = new Dictionary<string, DateTime?>(dictionary, StringComparer.OrdinalIgnoreCase);
                    var currentMax = _ExpireAndGetMaxExpiration(updatedDict);

                    foreach (var kvp in newItems)
                    {
                        updatedDict[kvp.Key] = kvp.Value;
                    }

                    var newExpiresAt =
                        (expiresAt is null || currentMax is null) ? (DateTime?)null
                        : expiresAt.Value > currentMax.Value ? expiresAt.Value
                        : currentMax.Value;
                    var newSize = _CalculateEntrySize(updatedDict);
                    sizeDelta = newSize - existingEntry.Size;

                    return new CacheEntry(
                        updatedDict,
                        newExpiresAt,
                        _timeProvider,
                        _shouldClone,
                        _shouldThrowOnSerializationError,
                        newSize
                    );
                }
            );

            if (sizeDelta != 0)
            {
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            }

            _TrackUpdate(key, committed.PhysicalExpiresAt);

            await _StartMaintenanceAsync().ConfigureAwait(false);

            return newItems.Count;
        }
        else
        {
            var newItems = new Dictionary<object, DateTime?>();

            foreach (var v in value)
            {
                if (v is not null)
                {
                    newItems[v] = expiresAt;
                }
            }

            if (newItems.Count is 0)
            {
                return 0;
            }

            var entrySize = _CalculateEntrySize(newItems);
            var entry = new CacheEntry(
                newItems,
                expiresAt,
                _timeProvider,
                _shouldClone,
                _shouldThrowOnSerializationError,
                entrySize
            );
            long sizeDelta = 0;

            var committed = _memory.AddOrUpdate(
                key,
                _ =>
                {
                    sizeDelta = entrySize;
                    return entry;
                },
                (existingKey, existingEntry) =>
                {
                    if (existingEntry.PeekValue() is not IDictionary<object, DateTime?> dictionary)
                    {
                        throw new InvalidOperationException(
                            $"Unable to add value for key: {existingKey}. Cache value does not contain a set"
                        );
                    }

                    var updatedDict = new Dictionary<object, DateTime?>(dictionary);
                    var currentMax = _ExpireAndGetMaxExpiration(updatedDict);

                    foreach (var kvp in newItems)
                    {
                        updatedDict[kvp.Key] = kvp.Value;
                    }

                    var newExpiresAt =
                        (expiresAt is null || currentMax is null) ? (DateTime?)null
                        : expiresAt.Value > currentMax.Value ? expiresAt.Value
                        : currentMax.Value;
                    var newSize = _CalculateEntrySize(updatedDict);
                    sizeDelta = newSize - existingEntry.Size;

                    return new CacheEntry(
                        updatedDict,
                        newExpiresAt,
                        _timeProvider,
                        _shouldClone,
                        _shouldThrowOnSerializationError,
                        newSize
                    );
                }
            );

            if (sizeDelta != 0)
            {
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            }

            _TrackUpdate(key, committed.PhysicalExpiresAt);

            await _StartMaintenanceAsync().ConfigureAwait(false);

            return newItems.Count;
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
            _TryRemoveExpiredEntry(key, existingEntry);
            return new ValueTask<CacheValue<T>>(CacheValue<T>.NoValue);
        }

        if (existingEntry.IsLogicallyExpired)
        {
            if (existingEntry.SlidingExpiration.HasValue)
            {
                _TryRemoveExpiredEntry(key, existingEntry);
            }

            return new ValueTask<CacheValue<T>>(CacheValue<T>.NoValue);
        }

        try
        {
            var value = existingEntry.GetValue<T>();
            _TryRearmSlidingEntry(key, existingEntry, _timeProvider.GetUtcNow().UtcDateTime);

            return new ValueTask<CacheValue<T>>(new CacheValue<T>(value, hasValue: true));
        }
        catch (Exception ex) when (!_shouldThrowOnSerializationError)
        {
            _logger.LogDeserializationError(ex, string.GetHashCode(key, StringComparison.Ordinal));
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

        return new ValueTask<bool>(!existingEntry.IsExpired && !existingEntry.IsLogicallyExpired);
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

        if (existingEntry.IsExpired || existingEntry.IsLogicallyExpired)
        {
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        }

        if (!existingEntry.LogicalExpiresAt.HasValue || existingEntry.LogicalExpiresAt.Value == DateTime.MaxValue)
        {
            return new ValueTask<TimeSpan?>((TimeSpan?)null);
        }

        return new ValueTask<TimeSpan?>(
            existingEntry.LogicalExpiresAt.Value.Subtract(_timeProvider.GetUtcNow().UtcDateTime)
        );
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

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (typeof(T) == typeof(string))
        {
            var dictionaryCacheValue = await GetAsync<IDictionary<string, DateTime?>>(key, cancellationToken)
                .ConfigureAwait(false);

            if (!dictionaryCacheValue.HasValue)
            {
                return new CacheValue<ICollection<T>>([], false);
            }

            var nonExpiredKeys = dictionaryCacheValue
                .Value!.Where(kvp => kvp.Value is null || kvp.Value >= utcNow)
                .Select(kvp => (T)(object)kvp.Key)
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
            return new CacheValue<ICollection<T>>(nonExpiredKeys.Skip(skip).Take(pageSize).ToArray(), true);
        }
        else
        {
            var dictionaryCacheValue = await GetAsync<IDictionary<object, DateTime?>>(key, cancellationToken)
                .ConfigureAwait(false);

            if (!dictionaryCacheValue.HasValue)
            {
                return new CacheValue<ICollection<T>>([], false);
            }

            var nonExpiredKeys = dictionaryCacheValue
                .Value!.Where(kvp => kvp.Value is null || kvp.Value >= utcNow)
                .Select(kvp => (T)kvp.Key)
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
            return new CacheValue<ICollection<T>>(nonExpiredKeys.Skip(skip).Take(pageSize).ToArray(), true);
        }
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
        _UntagEntry(key, entry);
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
        var wasRemoved = false;

        while (_memory.TryGetValue(key, out var existingEntry))
        {
            if (!Equals(existingEntry.GetValue<T>(), expected))
            {
                break;
            }

            if (_memory.TryRemove(new KeyValuePair<string, CacheEntry>(key, existingEntry)))
            {
                Interlocked.Add(ref _currentMemorySize, -existingEntry.Size);
                _UntagEntry(key, existingEntry);
                wasRemoved = true;
                break;
            }
        }

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return wasRemoved;
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

            var prefixedKey = _GetKey(key);

            if (_memory.TryRemove(prefixedKey, out var entry))
            {
                Interlocked.Add(ref _currentMemorySize, -entry.Size);
                _UntagEntry(prefixedKey, entry);
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

        prefix = _GetKey(prefix);
        var removed = 0;

        foreach (var (key, entry) in _memory)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (_memory.TryRemove(key, out var removedEntry))
                {
                    Interlocked.Add(ref _currentMemorySize, -removedEntry.Size);
                    _UntagEntry(key, removedEntry);
                    removed++;
                }
            }
        }

        return new ValueTask<int>(removed);
    }

    /// <inheritdoc />
    public ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(tag);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_tagIndex.TryGetValue(tag, out var taggedKeys))
        {
            return new ValueTask<int>(0);
        }

        var removed = 0;

        foreach (var key in taggedKeys.Keys)
        {
            if (!_memory.TryGetValue(key, out var entry))
            {
                taggedKeys.TryRemove(key, out _);
                continue;
            }

            // Version-pinned guard: only remove when the LIVE entry still carries the tag. A key overwritten
            // by an untagged write (or re-created after expiry) keeps a stale index membership which is
            // cleaned up here instead of removing the new entry.
            if (entry.Tags is null || !entry.Tags.Contains(tag))
            {
                taggedKeys.TryRemove(key, out _);
                continue;
            }

            if (_memory.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry)))
            {
                Interlocked.Add(ref _currentMemorySize, -entry.Size);
                _UntagEntry(key, entry);

                if (!entry.IsExpired)
                {
                    removed++;
                }
            }

            // A failed CAS removal means a concurrent write replaced the entry; the replacement may or may
            // not carry the tag, so keep the membership for the live-entry check on the next invalidation.
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

        if (typeof(T) == typeof(string))
        {
            var stringsToRemove = value.Where(v => v is not null).Select(v => (string)(object)v!).ToList();

            if (stringsToRemove.Count is 0)
            {
                return new ValueTask<long>(0L);
            }

            long removed = 0;
            long sizeDelta = 0;

            _memory.TryUpdate(
                key,
                (_, existingEntry) =>
                {
                    if (existingEntry.PeekValue() is not IDictionary<string, DateTime?> { Count: > 0 } dictionary)
                    {
                        sizeDelta = 0;
                        return existingEntry;
                    }

                    var updatedDict = new Dictionary<string, DateTime?>(dictionary, StringComparer.OrdinalIgnoreCase);
                    long localRemoved = 0;

                    foreach (var v in stringsToRemove)
                    {
                        if (updatedDict.Remove(v))
                        {
                            localRemoved++;
                        }
                    }

                    removed = localRemoved;

                    var newExpiresAt = _ExpireAndGetMaxExpiration(updatedDict);
                    var newSize = _CalculateEntrySize(updatedDict);
                    sizeDelta = newSize - existingEntry.Size;

                    return new CacheEntry(
                        updatedDict,
                        newExpiresAt,
                        _timeProvider,
                        _shouldClone,
                        _shouldThrowOnSerializationError,
                        newSize
                    );
                }
            );

            if (sizeDelta != 0)
            {
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            }

            return new ValueTask<long>(removed);
        }
        else
        {
            var valuesToRemove = value.Where(v => v is not null).Select(v => (object)v!).ToList();

            if (valuesToRemove.Count is 0)
            {
                return new ValueTask<long>(0L);
            }

            long removed = 0;
            long sizeDelta = 0;

            _memory.TryUpdate(
                key,
                (_, existingEntry) =>
                {
                    if (existingEntry.PeekValue() is not IDictionary<object, DateTime?> { Count: > 0 } dictionary)
                    {
                        sizeDelta = 0;
                        return existingEntry;
                    }

                    var updatedDict = new Dictionary<object, DateTime?>(dictionary);
                    long localRemoved = 0;

                    foreach (var v in valuesToRemove)
                    {
                        if (updatedDict.Remove(v))
                        {
                            localRemoved++;
                        }
                    }

                    removed = localRemoved;

                    var newExpiresAt = _ExpireAndGetMaxExpiration(updatedDict);
                    var newSize = _CalculateEntrySize(updatedDict);
                    sizeDelta = newSize - existingEntry.Size;

                    return new CacheEntry(
                        updatedDict,
                        newExpiresAt,
                        _timeProvider,
                        _shouldClone,
                        _shouldThrowOnSerializationError,
                        newSize
                    );
                }
            );

            if (sizeDelta != 0)
            {
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            }

            return new ValueTask<long>(removed);
        }
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _memory.Clear();
        _tagIndex.Clear();
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
        _tagIndex.Clear();

        lock (_expirationLock)
        {
            _expirationQueue.Clear();
        }

        _lruQueue.Clear();
        Interlocked.Exchange(ref _currentMemorySize, 0);
        _coordinator.Dispose();
        _disposedCts.Cancel();
        _disposedCts.Dispose();
    }

    ValueTask<CacheStoreEntry<T>> IFactoryCacheStore.TryGetEntryAsync<T>(
        string key,
        CancellationToken cancellationToken
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return new ValueTask<CacheStoreEntry<T>>(CacheStoreEntry<T>.NotFound);
        }

        if (existingEntry.IsExpired)
        {
            _RemoveExpiredKey(key);
            return new ValueTask<CacheStoreEntry<T>>(CacheStoreEntry<T>.NotFound);
        }

        T? value;

        try
        {
            value = existingEntry.GetValue<T>();
        }
        catch (Exception ex) when (!_shouldThrowOnSerializationError)
        {
            _logger.LogDeserializationError(ex, string.GetHashCode(key, StringComparison.Ordinal));
            return new ValueTask<CacheStoreEntry<T>>(CacheStoreEntry<T>.NotFound);
        }

        return new ValueTask<CacheStoreEntry<T>>(
            new CacheStoreEntry<T>(
                Found: true,
                IsNull: value is null,
                Value: value,
                LogicalExpiresAt: existingEntry.LogicalExpiresAt,
                PhysicalExpiresAt: existingEntry.PhysicalExpiresAt,
                SlidingExpiration: existingEntry.SlidingExpiration
            )
            {
                EagerRefreshAt = existingEntry.EagerRefreshAt,
                ETag = existingEntry.ETag,
                LastModifiedAt = existingEntry.LastModifiedAt,
                Tags = existingEntry.Tags,
            }
        );
    }

    // Non-async forwarder: `in` parameters are not allowed on async methods, so copy the descriptor by value.
    ValueTask IFactoryCacheStore.SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
        where T : default
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return _SetEntryCoreAsync(key, entry);
    }

    private async ValueTask _SetEntryCoreAsync<T>(string key, CacheStoreEntryWrite<T> entry)
    {
        key = _GetKey(key);
        var entrySize = _CalculateEntrySize(entry.Value);

        if (!_ValidateEntrySize(entrySize))
        {
            return;
        }

        var cacheEntry = new CacheEntry(
            entry.IsNull ? default : entry.Value,
            entry.LogicalExpiresAt,
            entry.PhysicalExpiresAt,
            entry.SlidingExpiration,
            _timeProvider,
            _shouldClone,
            _shouldThrowOnSerializationError,
            entrySize,
            tags: entry.Tags,
            eagerRefreshAt: entry.EagerRefreshAt,
            etag: entry.ETag,
            lastModifiedAt: entry.LastModifiedAt
        );

        await _SetInternalAsync(key, cacheEntry).ConfigureAwait(false);
    }

    ValueTask IFactoryCacheStore.TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        // The live entry carries the authoritative sliding/physical/logical metadata, so re-arm against it
        // directly. _TryRearmSlidingEntry applies the same throttle + value-equality TryUpdate the direct
        // GetAsync path uses; the slidingExpiration/physicalExpiresAt parameters are needed only by stores
        // (Redis) whose metadata is not co-located with the value.
        if (_memory.TryGetValue(key, out var existingEntry))
        {
            _TryRearmSlidingEntry(key, existingEntry, now);
        }

        return ValueTask.CompletedTask;
    }

    private void _ThrowIfDisposed()
    {
        Ensure.NotDisposed(Volatile.Read(ref _isDisposed) != 0, this);
    }

    private void _TrackUpdate(string key, DateTime? expiresAt)
    {
        if (expiresAt.HasValue)
        {
            lock (_expirationLock)
            {
                _expirationQueue.Enqueue(key, expiresAt.Value.Ticks);
            }
        }

        _lruQueue.Enqueue(key);
    }

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : string.Concat(_keyPrefix, key);
    }

    private List<string> _GetKeys(string prefix)
    {
        var prefixedPrefix = string.IsNullOrEmpty(prefix) ? null : _GetKey(prefix);
        var stripLength = _keyPrefix.Length;

        return _memory
            .Where(kvp =>
                !kvp.Value.IsExpired
                && (prefixedPrefix is null || kvp.Key.StartsWith(prefixedPrefix, StringComparison.Ordinal))
            )
            .Select(kvp => stripLength == 0 ? kvp.Key : kvp.Key[stripLength..])
            .ToList();
    }

    private void _RemoveExpiredKey(string key)
    {
        if (_memory.TryRemove(key, out var removedEntry))
        {
            Interlocked.Add(ref _currentMemorySize, -removedEntry.Size);
            _UntagEntry(key, removedEntry);
        }
    }

    private void _TryRemoveExpiredEntry(string key, CacheEntry entry)
    {
        if (_memory.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry)))
        {
            Interlocked.Add(ref _currentMemorySize, -entry.Size);
            _UntagEntry(key, entry);
        }
    }

    // Registers the committed entry's tags and drops the replaced entry's tags that no longer apply. The index
    // may be momentarily stale under write races; RemoveByTagAsync's live-entry check absorbs that.
    private void _UpdateTagIndex(string key, IReadOnlySet<string>? previousTags, IReadOnlySet<string>? currentTags)
    {
        if (previousTags is null && currentTags is null)
        {
            return;
        }

        if (currentTags is not null)
        {
            foreach (var tag in currentTags)
            {
                var taggedKeys = _tagIndex.GetOrAdd(
                    tag,
                    static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal)
                );

                taggedKeys[key] = 0;
            }
        }

        if (previousTags is not null)
        {
            foreach (var tag in previousTags)
            {
                if (currentTags is not null && currentTags.Contains(tag))
                {
                    continue;
                }

                if (_tagIndex.TryGetValue(tag, out var taggedKeys))
                {
                    taggedKeys.TryRemove(key, out _);
                }
            }
        }
    }

    private void _UntagEntry(string key, CacheEntry entry)
    {
        if (entry.Tags is null)
        {
            return;
        }

        foreach (var tag in entry.Tags)
        {
            if (_tagIndex.TryGetValue(tag, out var taggedKeys))
            {
                taggedKeys.TryRemove(key, out _);
            }
        }
    }

    private void _TryRearmSlidingEntry(string key, CacheEntry entry, DateTime now)
    {
        if (
            entry.SlidingExpiration is not { } slidingExpiration
            || entry.LogicalExpiresAt is not { } logicalExpiresAt
            || entry.PhysicalExpiresAt is not { } physicalExpiresAt
        )
        {
            return;
        }

        if (physicalExpiresAt <= now)
        {
            return;
        }

        var remaining = logicalExpiresAt - now;
        // Re-arm once roughly half the idle window has elapsed. Exact integer halving (no lossy double cast).
        var rearmThreshold = TimeSpan.FromTicks(slidingExpiration.Ticks / 2);

        if (remaining > rearmThreshold)
        {
            return;
        }

        var rearmedLogicalExpiresAt = _Min(now.Add(slidingExpiration), physicalExpiresAt);

        if (rearmedLogicalExpiresAt <= logicalExpiresAt)
        {
            return;
        }

        var rearmedEntry = entry.WithLogicalExpiration(rearmedLogicalExpiresAt);

        if (_memory.TryUpdate(key, rearmedEntry, entry))
        {
            _TrackUpdate(key, rearmedEntry.TrackedExpiresAt);
            _ = _StartMaintenanceAsync();
        }
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
        CacheEntry? previousEntry = null;

        if (addOnly)
        {
            _memory.AddOrUpdate(
                key,
                _ =>
                {
                    sizeDelta = entry.Size;
                    previousEntry = null;
                    return entry;
                },
                (_, existingEntry) =>
                {
                    wasUpdated = false;
                    previousEntry = null;

                    if (existingEntry.IsExpired)
                    {
                        sizeDelta = entry.Size - existingEntry.Size;
                        wasUpdated = true;
                        previousEntry = existingEntry;
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
                    previousEntry = null;
                    return entry;
                },
                (_, existingEntry) =>
                {
                    sizeDelta = entry.Size - existingEntry.Size;
                    previousEntry = existingEntry;
                    return entry;
                }
            );
        }

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        if (wasUpdated)
        {
            _TrackUpdate(key, entry.TrackedExpiresAt);
            _UpdateTagIndex(key, previousEntry?.Tags, entry.Tags);
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

        if (Volatile.Read(ref _maintenanceRunning) != 0)
        {
            return;
        }

        // Use Interlocked.CompareExchange to ensure only one thread spawns maintenance
        var utcNowTicks = utcNow.Ticks;
        var lastTicks = Volatile.Read(ref _lastMaintenanceTicks);
        var thresholdTicks = _maintenanceIntervalTicks;

        if (utcNowTicks - lastTicks > thresholdTicks)
        {
            // Atomically try to claim the maintenance slot
            if (Interlocked.CompareExchange(ref _lastMaintenanceTicks, utcNowTicks, lastTicks) == lastTicks)
            {
                if (Interlocked.CompareExchange(ref _maintenanceRunning, 1, 0) == 0)
                {
                    _ = Task.Run(_DoMaintenanceAsync, _disposedCts.Token);
                }
            }
        }
    }

    private async Task _CompactAsync()
    {
        if (!ShouldCompact)
        {
            return;
        }

        try
        {
            using (await _lock.LockAsync(_disposedCts.Token).ConfigureAwait(false))
            {
                var removalCount = 0;

                while (ShouldCompact && removalCount < _maxEvictionsPerCompaction)
                {
                    var keyToRemove = _FindLeastRecentlyUsedOrLargest();

                    if (keyToRemove is null)
                    {
                        break;
                    }

                    if (_memory.TryRemove(keyToRemove, out var removedEntry))
                    {
                        Interlocked.Add(ref _currentMemorySize, -removedEntry.Size);
                        _UntagEntry(keyToRemove, removedEntry);
                        removalCount++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal triggered cancellation; safe to ignore
        }
    }

    private string? _FindLeastRecentlyUsedOrLargest()
    {
        // Sample-based selection from the head of the FIFO queue.
        // This is O(K) where K is sample size, much better than O(N).

        var isMemoryConstrained = _maxMemorySize.HasValue && Interlocked.Read(ref _currentMemorySize) > _maxMemorySize;
        (string? Key, long LastAccessTicks, long InstanceNumber, long Size) best = (null, long.MaxValue, 0, 0);

        var sampledKeys = new List<string>(_evictionSampleSize);

        // Dequeue up to K candidates
        for (var i = 0; i < _evictionSampleSize; i++)
        {
            if (_lruQueue.TryDequeue(out var key))
            {
                if (_memory.TryGetValue(key, out var entry))
                {
                    sampledKeys.Add(key);

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
                            || (
                                entry.LastAccessTicks == best.LastAccessTicks
                                && entry.InstanceNumber < best.InstanceNumber
                            )
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
            }
            else
            {
                break;
            }
        }

        // Put back the ones we didn't pick
        foreach (var key in sampledKeys)
        {
            if (key != best.Key)
            {
                _lruQueue.Enqueue(key);
            }
        }

        return best.Key;
    }

    private async Task _DoMaintenanceAsync()
    {
        try
        {
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var nowTicks = utcNow.Ticks;

            try
            {
                // Prune some stale items from the LRU queue to prevent memory growth
                for (var i = 0; i < 100; i++)
                {
                    if (_lruQueue.TryDequeue(out var key))
                    {
                        if (_memory.ContainsKey(key))
                        {
                            _lruQueue.Enqueue(key);
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                while (true)
                {
                    string? key;
                    long expiresAtTicks;

                    lock (_expirationLock)
                    {
                        if (!_expirationQueue.TryPeek(out key, out expiresAtTicks) || expiresAtTicks > nowTicks)
                        {
                            break;
                        }

                        _expirationQueue.Dequeue();
                    }

                    if (key is not null && _memory.TryGetValue(key, out var entry))
                    {
                        if (entry.ShouldRemoveAt(expiresAtTicks))
                        {
                            if (_memory.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry)))
                            {
                                Interlocked.Add(ref _currentMemorySize, -entry.Size);
                                _UntagEntry(key, entry);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogMaintenanceTaskFailed(e);
            }

            if (ShouldCompact)
            {
                await _CompactAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _maintenanceRunning, 0);
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

    /// <summary>
    /// Immutable snapshot of a cached value. Every field except <c>_lastAccessTicks</c> (LRU bookkeeping)
    /// is assigned at construction and never mutated afterwards.
    /// </summary>
    /// <remarks>
    /// <para>Why immutable replacement is preferred over in-place mutation:</para>
    /// <list type="number">
    /// <item>
    /// <b>Consistency for concurrent readers.</b> A reader that loads a <see cref="CacheEntry"/> reference
    /// from <see cref="_memory"/> sees a stable snapshot of <see cref="PeekValue"/>, expiration metadata,
    /// and <see cref="Size"/>. With mutable setters a reader could observe a new <c>Value</c> together with
    /// the old expiration metadata (or vice versa) — a torn read even under <c>Volatile</c>/<c>Interlocked</c>.
    /// </item>
    /// <item>
    /// <b>Safe composition with <see cref="ConcurrentDictionary{TKey, TValue}.AddOrUpdate(TKey, Func{TKey, TValue}, Func{TKey, TValue, TValue})"/>.</b>
    /// The add- and update-factories may be invoked multiple times under contention (by design — only
    /// the CAS-winning result is committed). If factories mutate the existing entry they corrupt state
    /// visible to other threads before the CAS resolves. Constructing a new entry is side-effect-free,
    /// so repeated factory calls are harmless: only the winning instance is published.
    /// </item>
    /// <item>
    /// <b>No structural size drift.</b> <see cref="Size"/> is fixed at construction, so every write path
    /// must compute it exactly once against the final value. This eliminates a whole class of bugs where
    /// <c>_currentMemorySize</c> diverges from the sum of live entry sizes.
    /// </item>
    /// <item>
    /// <b>Single mutation surface.</b> The only field that still mutates is <c>_lastAccessTicks</c>, which
    /// is advisory LRU data — benign to race and already guarded by <see cref="Interlocked"/>.
    /// </item>
    /// </list>
    /// <para>Callers update the dictionary via replace-on-write: construct a new <see cref="CacheEntry"/>
    /// (or call <see cref="WithExpiration"/> when only the TTL changes) and hand it to
    /// <c>AddOrUpdate</c>/<c>TryUpdate</c>.</para>
    /// </remarks>
    private sealed class CacheEntry
    {
        private static long _instanceCount;
        private readonly bool _shouldClone;
        private readonly bool _shouldThrowOnSerializationError;
        private readonly TimeProvider _timeProvider;
        private readonly object? _cacheValue;
        private long _lastAccessTicks;

        public CacheEntry(
            object? value,
            DateTime? expiresAt,
            TimeProvider timeProvider,
            bool shouldClone,
            bool shouldThrowOnSerializationError = true,
            long size = 0
        )
            : this(
                value,
                logicalExpiresAt: expiresAt,
                physicalExpiresAt: expiresAt,
                slidingExpiration: null,
                timeProvider,
                shouldClone,
                shouldThrowOnSerializationError,
                size
            ) { }

        internal CacheEntry(
            object? value,
            DateTime? logicalExpiresAt,
            DateTime? physicalExpiresAt,
            TimeSpan? slidingExpiration,
            TimeProvider timeProvider,
            bool shouldClone,
            bool shouldThrowOnSerializationError = true,
            long size = 0,
            LastFactoryError? lastFactoryError = null,
            IReadOnlyCollection<string>? tags = null,
            DateTime? eagerRefreshAt = null,
            string? etag = null,
            DateTime? lastModifiedAt = null
        )
        {
            _timeProvider = timeProvider;
            _shouldClone = shouldClone && _TypeRequiresCloning(value?.GetType());
            _shouldThrowOnSerializationError = shouldThrowOnSerializationError;
            _cacheValue = _shouldClone ? _DeepClone(value) : value;

            var utcNow = _timeProvider.GetUtcNow();
            _lastAccessTicks = utcNow.Ticks;
            LogicalExpiresAt = logicalExpiresAt;
            PhysicalExpiresAt = physicalExpiresAt;
            SlidingExpiration = slidingExpiration;
            LastFactoryError = lastFactoryError;
            Tags = tags is { Count: > 0 } ? tags.ToFrozenSet(StringComparer.Ordinal) : null;
            EagerRefreshAt = eagerRefreshAt;
            ETag = etag;
            LastModifiedAt = lastModifiedAt;
            Size = size;
            InstanceNumber = Interlocked.Increment(ref _instanceCount);
        }

        /// <summary>Private constructor used by <see cref="WithExpiration"/> to share the already-cloned
        /// value without re-cloning.</summary>
        private CacheEntry(
            CacheEntry prototype,
            DateTime? logicalExpiresAt,
            DateTime? physicalExpiresAt,
            TimeSpan? slidingExpiration
        )
        {
            _timeProvider = prototype._timeProvider;
            _shouldClone = prototype._shouldClone;
            _shouldThrowOnSerializationError = prototype._shouldThrowOnSerializationError;
            _cacheValue = prototype._cacheValue;
            _lastAccessTicks = _timeProvider.GetUtcNow().Ticks;
            LogicalExpiresAt = logicalExpiresAt;
            PhysicalExpiresAt = physicalExpiresAt;
            SlidingExpiration = slidingExpiration;
            LastFactoryError = prototype.LastFactoryError;
            Tags = prototype.Tags;
            EagerRefreshAt = prototype.EagerRefreshAt;
            ETag = prototype.ETag;
            LastModifiedAt = prototype.LastModifiedAt;
            Size = prototype.Size;
            InstanceNumber = Interlocked.Increment(ref _instanceCount);
        }

        internal long InstanceNumber { get; }

        internal DateTime? LogicalExpiresAt { get; }

        internal DateTime? PhysicalExpiresAt { get; }

        internal TimeSpan? SlidingExpiration { get; }

        internal DateTime? TrackedExpiresAt => SlidingExpiration.HasValue ? LogicalExpiresAt : PhysicalExpiresAt;

        internal LastFactoryError? LastFactoryError { get; }

        internal IReadOnlySet<string>? Tags { get; }

        internal DateTime? EagerRefreshAt { get; }

        internal string? ETag { get; }

        internal DateTime? LastModifiedAt { get; }

        // Expired at the exact tick (expiresAt <= now): align with the Core (IsFresh/IsPhysicallyPresent),
        // Redis (_IsExpired), and the eviction maintenance loop conventions so every provider and the
        // coordinator agree on the boundary instant.
        internal bool IsExpired =>
            PhysicalExpiresAt.HasValue && PhysicalExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime;

        internal bool IsLogicallyExpired =>
            LogicalExpiresAt.HasValue && LogicalExpiresAt <= _timeProvider.GetUtcNow().UtcDateTime;

        internal bool ShouldRemoveAt(long expiresAtTicks)
        {
            if (PhysicalExpiresAt.HasValue && PhysicalExpiresAt.Value.Ticks <= expiresAtTicks)
            {
                return true;
            }

            return SlidingExpiration.HasValue
                && LogicalExpiresAt.HasValue
                && LogicalExpiresAt.Value.Ticks <= expiresAtTicks;
        }

        internal long LastAccessTicks => Interlocked.Read(ref _lastAccessTicks);

        internal long Size { get; }

        /// <summary>Returns the stored value, touching LRU and cloning when required.</summary>
        internal object? ReadValue()
        {
            Interlocked.Exchange(ref _lastAccessTicks, _timeProvider.GetUtcNow().Ticks);
            return _shouldClone ? _DeepClone(_cacheValue) : _cacheValue;
        }

        /// <summary>Returns the raw stored reference without touching LRU or cloning. Intended for
        /// use inside <c>AddOrUpdate</c>/<c>TryUpdate</c> factories where the caller is inspecting
        /// the existing entry to decide the next entry — a context in which LRU touches and clones
        /// are both incorrect and wasteful.</summary>
        internal object? PeekValue() => _cacheValue;

        /// <summary>Converts the raw stored value to <typeparamref name="T"/> without touching LRU or
        /// cloning. Used for metadata reads where a clone/LRU touch would be incorrect or wasteful.</summary>
        internal T? PeekValue<T>() => _ConvertValue<T>(_cacheValue);

        /// <summary>Returns a new entry that shares this entry's value but has a different
        /// expiration. Used by writers that only need to refresh the TTL.</summary>
        internal CacheEntry WithExpiration(DateTime? expiresAt) =>
            new(this, logicalExpiresAt: expiresAt, physicalExpiresAt: expiresAt, slidingExpiration: null);

        /// <summary>Returns a new entry that shares this entry's value but only moves logical expiration.</summary>
        internal CacheEntry WithLogicalExpiration(DateTime logicalExpiresAt) =>
            new(this, logicalExpiresAt, PhysicalExpiresAt, SlidingExpiration);

        public T? GetValue<T>() => _ConvertValue<T>(ReadValue());

        private static T? _ConvertValue<T>(object? val)
        {
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

    private sealed record LastFactoryError(Exception Error, DateTime DateCreated);

    private DateTime? _ExpireAndGetMaxExpiration<T>(IDictionary<T, DateTime?> dictionary)
        where T : notnull
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var max = DateTime.MinValue;
        var hasInfinite = false;

        // Collect keys to remove to avoid "Collection was modified" exception during iteration
        List<T>? keysToRemove = null;

        foreach (var kvp in dictionary)
        {
            if (kvp.Value.HasValue)
            {
                if (kvp.Value.Value < utcNow)
                {
                    keysToRemove ??= [];
                    keysToRemove.Add(kvp.Key);
                }
                else if (kvp.Value.Value > max)
                {
                    max = kvp.Value.Value;
                }
            }
            else
            {
                hasInfinite = true;
            }
        }

        if (keysToRemove != null)
        {
            foreach (var key in keysToRemove)
            {
                dictionary.Remove(key);
            }
        }

        if (dictionary.Count == 0)
        {
            return DateTime.MinValue;
        }

        return hasInfinite ? null : max;
    }

    private static DateTime _Min(DateTime left, DateTime right) => left <= right ? left : right;

    #endregion
}

internal static partial class InMemoryCacheLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "DeserializationError",
        Level = LogLevel.Warning,
        Message = "Deserialization error for cache key (hash: {KeyHash})"
    )]
    public static partial void LogDeserializationError(this ILogger logger, Exception exception, int keyHash);

    [LoggerMessage(
        EventId = 2,
        EventName = "MaintenanceTaskFailed",
        Level = LogLevel.Warning,
        Message = "Cache maintenance task failed"
    )]
    public static partial void LogMaintenanceTaskFailed(this ILogger logger, Exception exception);
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
