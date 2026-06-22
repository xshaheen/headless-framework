// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nito.AsyncEx;

namespace Headless.Caching;

/// <summary>
/// Process-local in-memory cache implementing <see cref="IInMemoryCache"/> (the L1 tier), with capacity-capped
/// LRU eviction, background expiry maintenance, Family-2 logical tag/clear-generation invalidation, fail-safe
/// stale serving, and zero-copy buffer reads/writes via <see cref="IBufferCache"/>.
/// </summary>
/// <remarks>
/// Entry lifecycle: <see cref="ICache.ClearAsync"/> bumps a logical clear-generation marker in O(1) while
/// keeping physical bytes resident so a failing factory can still serve the stale value; <see cref="ICache.FlushAsync"/>
/// physically wipes every entry including fail-safe reserves. Background maintenance fires at
/// <see cref="InMemoryCacheOptions.MaintenanceInterval"/> to reap expired entries and enforce
/// <see cref="InMemoryCacheOptions.MaxItems"/> / <see cref="InMemoryCacheOptions.MaxMemorySize"/> via
/// approximate LRU eviction sampling. This class is <see cref="IDisposable"/>; dispose it (or rely on DI
/// disposal) to stop background maintenance.
/// </remarks>
public sealed class InMemoryCache
    : IInMemoryCache,
        IFactoryCacheStore,
        ISeedableTagMarkerCache,
        IBufferCache,
        IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _memory = new(StringComparer.Ordinal);

    // Family-2 logical tag-version invalidation: tag -> last-invalidation UTC marker. RemoveByTagAsync bumps the
    // marker (O(1), no member enumeration); reads treat an entry as invalidated when its CreatedAt predates the
    // newest applicable marker (CacheTagInvalidation.IsInvalidated). Markers are never pruned: the residue is
    // bounded by the process-lifetime distinct-tag cardinality, and a re-created entry (newer CreatedAt) is
    // naturally not invalidated by an older marker.
    private readonly ConcurrentDictionary<string, DateTime> _tagMarkers = new(StringComparer.Ordinal);

    // Global logical clear-generation marker (UTC ticks; 0 = never cleared). ClearAsync bumps it; every read
    // compares the entry's CreatedAt against it. Stored as ticks so it can be read/written atomically via
    // Interlocked without tearing a DateTime.
    private long _clearGenerationTicks;

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

    // Soonest known physical/sliding expiry tick across all live entries — a monotone lower bound, not exact.
    // Writers lower it via CAS on the hot path (lock-free, no allocation); the maintenance sweep recomputes the
    // true minimum of survivors. long.MaxValue means "no expiring entry tracked". This lets _DoMaintenanceAsync
    // skip the O(live-N) scan entirely until something is actually due, so steady-state memory is bounded by
    // live entries (not write volume) and background work is proportional to expiry events.
    private long _nextExpiryTicks = long.MaxValue;
    private long _lastMaintenanceTicks;
    private int _maintenanceRunning;
    private int _isDisposed;

    /// <summary>Gets the current memory size in bytes used by the cache.</summary>
    public long CurrentMemorySize => Interlocked.Read(ref _currentMemorySize);

    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions { get; }

    public InMemoryCache(
        TimeProvider timeProvider,
        InMemoryCacheOptions options,
        ILogger<InMemoryCache>? logger = null,
        ICacheFactoryLockProvider? factoryLockProvider = null
    )
    {
        _logger = logger ?? NullLogger<InMemoryCache>.Instance;
        _coordinator = new FactoryCacheCoordinator(timeProvider, _logger, factoryLockProvider);
        _timeProvider = timeProvider;
        DefaultEntryOptions = options.DefaultEntryOptions;
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
                "SizeCalculator is required when MaxMemorySize or MaxEntrySize is set.",
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

    /// <inheritdoc />
    public ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        // Only re-arm an entry a value-returning read would actually serve: skip entries that are expired,
        // logically expired, or tag/clear-invalidated. Without these guards Refresh would push the TTL out on a
        // miss, diverging from the Redis provider (which gates the same way) and from the read path here.
        if (
            _memory.TryGetValue(key, out var existingEntry)
            && !existingEntry.IsExpired
            && !existingEntry.IsLogicallyExpired
            && !_IsTagInvalidated(existingEntry)
        )
        {
            _TryRearmSlidingEntry(key, existingEntry, _timeProvider.GetUtcNow().UtcDateTime);
        }

        return ValueTask.CompletedTask;
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

        // Single clock read reused for the expiry, the entry's last-access stamp, and maintenance scheduling —
        // the hot write path previously fetched the clock three times.
        var nowTicks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;
        var expiresAt = expiration.HasValue
            ? new DateTime(nowTicks, DateTimeKind.Utc).Add(expiration.Value)
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
            entrySize,
            nowTicksOverride: nowTicks
        );

        return _SetInternalAsync(key, entry, nowTicks: nowTicks);
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

        await (this).UpsertEntryAsync(key, value, options, _timeProvider, cancellationToken).ConfigureAwait(false);

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

        // Batch all inserts: read the clock once, accumulate the total size delta, and call
        // _StartMaintenanceAsync a single time after the loop — instead of N clock reads,
        // N Interlocked.Add calls, and N maintenance checks.
        var nowTicks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;
        var expiresAt = expiration.HasValue
            ? new DateTime(nowTicks, DateTimeKind.Utc).Add(expiration.Value)
            : (DateTime?)null;

        var count = 0;
        long totalSizeDelta = 0;

        foreach (var (k, v) in value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var prefixedKey = _GetKey(k);
            var entrySize = _CalculateEntrySize(v);

            if (!_ValidateEntrySize(entrySize))
            {
                continue;
            }

            var entry = new CacheEntry(
                v,
                expiresAt,
                _timeProvider,
                _shouldClone,
                _shouldThrowOnSerializationError,
                entrySize,
                nowTicksOverride: nowTicks
            );

            if (entry.IsExpired)
            {
                _RemoveExpiredKey(prefixedKey);
                continue;
            }

            long sizeDelta = 0;

            _memory.AddOrUpdate(
                prefixedKey,
                _ =>
                {
                    sizeDelta = entrySize;
                    return entry;
                },
                (_, existingEntry) =>
                {
                    sizeDelta = entrySize - existingEntry.Size;
                    return entry;
                }
            );

            totalSizeDelta += sizeDelta;
            _TrackUpdate(entry.TrackedExpiresAt);
            count++;
        }

        if (totalSizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, totalSizeDelta);
        }

        if (ShouldCompact)
        {
            await _CompactAsync().ConfigureAwait(false);
        }

        _ScheduleMaintenance(nowTicks);

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

        // Single clock read reused for the expiry, the entry's birth/last-access stamp, and maintenance
        // scheduling — mirrors UpsertAsync so the add-only write path fetches the clock once, not three times.
        var nowTicks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;
        var expiresAt = expiration.HasValue
            ? new DateTime(nowTicks, DateTimeKind.Utc).Add(expiration.Value)
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
            entrySize,
            nowTicksOverride: nowTicks
        );

        return _SetInternalAsync(key, entry, addOnly: true, nowTicks: nowTicks);
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
        var nowTicks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;

        // Use atomic TryUpdate to avoid TOCTOU race condition
        _memory.TryUpdate(
            prefixedKey,
            (_, existingEntry) =>
            {
                wasReplaced = false;
                sizeDelta = 0;

                if (existingEntry.IsExpired)
                {
                    return existingEntry;
                }

                sizeDelta = entrySize - existingEntry.Size;
                wasReplaced = true;

                return new CacheEntry(
                    value,
                    logicalExpiresAt: expiresAt,
                    physicalExpiresAt: expiresAt,
                    slidingExpiration: null,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    entrySize,
                    // Direct write stamps a fresh birth time so a prior tag/clear marker does not invalidate it.
                    createdAt: new DateTime(nowTicks, DateTimeKind.Utc),
                    nowTicksOverride: nowTicks
                );
            }
        );

        if (wasReplaced)
        {
            if (sizeDelta != 0)
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            _TrackUpdate(expiresAt);
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
        var nowTicks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;

        _memory.TryUpdate(
            key,
            (_, existingEntry) =>
            {
                wasExpectedValue = false;
                sizeDelta = 0;

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

                return new CacheEntry(
                    value,
                    logicalExpiresAt: expiresAt,
                    physicalExpiresAt: expiresAt,
                    slidingExpiration: null,
                    _timeProvider,
                    _shouldClone,
                    _shouldThrowOnSerializationError,
                    newSize,
                    // Direct write stamps a fresh birth time so a prior tag/clear marker does not invalidate it.
                    createdAt: new DateTime(nowTicks, DateTimeKind.Utc),
                    nowTicksOverride: nowTicks
                );
            }
        );

        if (wasExpectedValue)
        {
            if (sizeDelta != 0)
                Interlocked.Add(ref _currentMemorySize, sizeDelta);
            _TrackUpdate(expiresAt);
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

        _TrackUpdate(expiresAt);

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

        _TrackUpdate(expiresAt);

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

        _TrackUpdate(expiresAt);

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

        _TrackUpdate(expiresAt);

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

        _TrackUpdate(expiresAt);

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

        _TrackUpdate(expiresAt);

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

        long result;

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

            result = _SetAddStringItems(key, newItems, expiresAt);
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

            result = _SetAddObjectItems(key, newItems, expiresAt);
        }

        await _StartMaintenanceAsync().ConfigureAwait(false);

        return result;
    }

    private long _SetAddStringItems(string key, Dictionary<string, DateTime?> newItems, DateTime? expiresAt)
    {
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

        _TrackUpdate(committed.PhysicalExpiresAt);

        return newItems.Count;
    }

    private long _SetAddObjectItems(string key, Dictionary<object, DateTime?> newItems, DateTime? expiresAt)
    {
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

        _TrackUpdate(committed.PhysicalExpiresAt);

        return newItems.Count;
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

        // Logical tag/clear invalidation: a direct read of a tag-invalidated entry is a miss. The physically
        // present reserve is left in place so the coordinator's TryGetEntryAsync can still serve it stale.
        if (_IsTagInvalidated(existingEntry))
        {
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
        return await GetAllAsync<T>(keys, cancellationToken).ConfigureAwait(false);
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

        return new ValueTask<bool>(
            !existingEntry.IsExpired && !existingEntry.IsLogicallyExpired && !_IsTagInvalidated(existingEntry)
        );
    }

    /// <summary>Returns the count of live, logically-valid entries whose keys begin with <paramref name="prefix"/>. Pass an empty string to count all entries.</summary>
    /// <remarks>
    /// When <paramref name="prefix"/> is non-empty this method performs an O(N) full scan of all live entries
    /// (N = current item count). Do not call it on hot request paths; reserve it for admin, monitoring, or
    /// diagnostic use.
    /// </remarks>
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

        if (existingEntry.IsExpired || existingEntry.IsLogicallyExpired || _IsTagInvalidated(existingEntry))
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

            return _GetSetStringItems<T>(dictionaryCacheValue, pageIndex, pageSize, utcNow);
        }
        else
        {
            var dictionaryCacheValue = await GetAsync<IDictionary<object, DateTime?>>(key, cancellationToken)
                .ConfigureAwait(false);

            return _GetSetObjectItems<T>(dictionaryCacheValue, pageIndex, pageSize, utcNow);
        }
    }

    private static CacheValue<ICollection<T>> _GetSetStringItems<T>(
        CacheValue<IDictionary<string, DateTime?>> dictionaryCacheValue,
        int? pageIndex,
        int pageSize,
        DateTime utcNow
    )
    {
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

    private static CacheValue<ICollection<T>> _GetSetObjectItems<T>(
        CacheValue<IDictionary<object, DateTime?>> dictionaryCacheValue,
        int? pageIndex,
        int pageSize,
        DateTime utcNow
    )
    {
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

    /// <summary>
    /// Zero-intermediate-copy buffer read. Mirrors <see cref="GetAsync{T}"/> exactly (expiry, sliding logical
    /// expiry, Family-2 tag/clear invalidation, single-key sliding re-arm), but writes the stored payload bytes
    /// straight into <paramref name="destination"/> instead of deserializing them — so the generic path's
    /// intermediate <c>byte[]</c> conversion is skipped. The output-cache named instance stores values as
    /// <c>byte[]</c> (raw-bytes serializer), so the stored array's bytes are written directly; for a pure
    /// in-memory cache the floor is one copy (stored array -> caller buffer).
    /// </summary>
    public ValueTask<bool> TryGetToAsync(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return new ValueTask<bool>(false);
        }

        if (existingEntry.IsExpired)
        {
            _TryRemoveExpiredEntry(key, existingEntry);
            return new ValueTask<bool>(false);
        }

        if (existingEntry.IsLogicallyExpired)
        {
            if (existingEntry.SlidingExpiration.HasValue)
            {
                _TryRemoveExpiredEntry(key, existingEntry);
            }

            return new ValueTask<bool>(false);
        }

        // Logical tag/clear invalidation: a direct read of a tag-invalidated entry is a miss. The physically
        // present reserve is left in place so the coordinator's TryGetEntryAsync can still serve it stale.
        if (_IsTagInvalidated(existingEntry))
        {
            return new ValueTask<bool>(false);
        }

        try
        {
            var value = existingEntry.GetValue<byte[]>();
            _TryRearmSlidingEntry(key, existingEntry, _timeProvider.GetUtcNow().UtcDateTime);

            // Parity with the byte[] fallback (CacheValue<byte[]>.Value is null -> false): a null-sentinel hit
            // reads as a miss for the buffer path. Nothing is written.
            if (value is null)
            {
                return new ValueTask<bool>(false);
            }

            // The single copy: stored array -> caller-provided buffer.
            destination.Write(value);
            return new ValueTask<bool>(true);
        }
        catch (Exception ex) when (!_shouldThrowOnSerializationError)
        {
            _logger.LogDeserializationError(ex, string.GetHashCode(key, StringComparison.Ordinal));
            return new ValueTask<bool>(false);
        }
    }

    /// <summary>
    /// Zero-intermediate-copy buffer write. Mirrors <see cref="UpsertEntryAsync{T}"/> + the stamping in
    /// <c>FactoryCacheStoreExtensions.UpsertEntryAsync</c>: validates options, computes the fresh-write stamps once
    /// via <see cref="CacheEntryStamps.Compute"/>, then stores the payload bytes with identical stamping
    /// (CreatedAt, expiry, tags, sliding) to the generic upsert so Family-2 tag invalidation and fail-safe still
    /// work. The sequence is materialized into a stable owned <c>byte[]</c> synchronously before any await — the
    /// cache retains it — so callers may hand in pooled buffers valid only for the duration of the call.
    /// </summary>
    public async ValueTask UpsertRawAsync(
        string key,
        ReadOnlySequence<byte> value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Materialize a stable owned copy synchronously, before any stamping/await: the cache retains this array,
        // so it must not alias a caller-pooled buffer that is recycled after the call returns.
        var bytes = value.ToArray();

        // Validate then stamp, matching the UpsertEntryAsync extension exactly: Compute does the stamp math but
        // does NOT validate, so ValidateOptions (which also validates Tags) runs first at this single choke point.
        CacheEntryStamps.ValidateOptions(options);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var stamps = CacheEntryStamps.Compute(options, now);

        var entry = new CacheStoreEntryWrite<byte[]>
        {
            Value = bytes,
            IsNull = false,
            LogicalExpiresAt = stamps.LogicalExpiresAt,
            PhysicalExpiresAt = stamps.PhysicalExpiresAt,
            SlidingExpiration = options.SlidingExpiration,
            EagerRefreshAt = stamps.EagerRefreshAt,
            // Stamp the birth time so a prior tag/clear marker does not logically invalidate this fresh write
            // (Family-2 version-pinning compares CreatedAt against the newest applicable marker).
            CreatedAt = stamps.CreatedAt,
            Tags = options.Tags,
            SkipMemoryCacheWrite = options.SkipMemoryCacheWrite,
            SkipDistributedCacheWrite = options.SkipDistributedCacheWrite,
        };

        await _SetEntryCoreWithResultAsync(key, entry).ConfigureAwait(false);
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

    public ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        key = _GetKey(key);

        if (!_memory.TryGetValue(key, out var existingEntry))
        {
            return new ValueTask<bool>(false);
        }

        if (existingEntry.IsExpired)
        {
            _TryRemoveExpiredEntry(key, existingEntry);
            return new ValueTask<bool>(false);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Reserve-preservation fork. Physical > Logical is overloaded: it is produced both by a fail-safe write
        // (FailSafeMaxDuration extends physical) and by a sliding entry whose absolute Duration cap exceeds its
        // idle window. Only the former is a fail-safe parachute worth keeping — sliding × fail-safe is mutually
        // exclusive, so a sliding entry's surplus physical span is just its cap, not a reserve, and must collapse.
        var hasFailSafeReserve =
            existingEntry.SlidingExpiration is null && existingEntry.PhysicalExpiresAt > existingEntry.LogicalExpiresAt;

        if (hasFailSafeReserve)
        {
            // Logically expire in place: normal reads miss, but the physical reserve survives so a later
            // GetOrAddAsync whose factory fails (fail-safe) can still serve the stale value. Optimistic single
            // swap, matching _TryRearmSlidingEntry's fire-and-forget posture: a lost race means a concurrent
            // writer already produced a newer state, which satisfies the caller's intent to expire what they saw.
            var expiredEntry = existingEntry.WithLogicalExpiration(now);

            if (_memory.TryUpdate(key, expiredEntry, existingEntry))
            {
                _TrackUpdate(expiredEntry.TrackedExpiresAt);
            }

            return new ValueTask<bool>(true);
        }

        // No reserve to preserve: removing avoids manufacturing a phantom reserve that headless's per-call
        // fail-safe model could later resurrect. Mirror RemoveAsync's size bookkeeping.
        if (_memory.TryRemove(new KeyValuePair<string, CacheEntry>(key, existingEntry)))
        {
            Interlocked.Add(ref _currentMemorySize, -existingEntry.Size);
        }

        return new ValueTask<bool>(true);
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

        foreach (var (key, _) in _memory)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                if (_memory.TryRemove(key, out var removedEntry))
                {
                    Interlocked.Add(ref _currentMemorySize, -removedEntry.Size);
                    removed++;
                }
            }
        }

        return new ValueTask<int>(removed);
    }

    /// <inheritdoc />
    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(tag);
        cancellationToken.ThrowIfCancellationRequested();

        // O(1) logical invalidation: stamp the per-tag marker to now. No member enumeration. Reads compare each
        // entry's CreatedAt against this marker; physically present reserves survive for fail-safe serving.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _tagMarkers.AddOrUpdate(tag, now, (_, existing) => now > existing ? now : existing);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // O(1) logical clear: bump the global clear-generation marker. Entries born before it read as misses and
        // demote to fail-safe reserves; physical reserves are preserved (unlike FlushAsync's physical wipe).
        var nowTicks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;

        // Monotone bump only (never move the generation backwards under a racing clock or concurrent caller).
        long current;
        do
        {
            current = Interlocked.Read(ref _clearGenerationTicks);
            if (nowTicks <= current)
            {
                break;
            }
        } while (Interlocked.CompareExchange(ref _clearGenerationTicks, nowTicks, current) != current);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void SeedTagMarker(string tag, DateTimeOffset invalidatedAt)
    {
        Argument.IsNotNullOrEmpty(tag);

        // Raise-only seed from a backplane notification: apply the originator's timestamp, NOT this node's local
        // clock, so a receiver whose clock lags the origin still records a marker newer than the invalidated
        // entries' CreatedAt (the cross-node clock-skew trap). Never lower a marker we already know to be newer.
        var at = invalidatedAt.UtcDateTime;
        _tagMarkers.AddOrUpdate(tag, at, (_, existing) => at > existing ? at : existing);
    }

    /// <inheritdoc />
    public void SeedClearMarker(DateTimeOffset invalidatedAt)
    {
        // Raise-only CAS using the originator's timestamp (see SeedTagMarker). Never move the generation backwards.
        var ticks = invalidatedAt.UtcDateTime.Ticks;

        long current;
        do
        {
            current = Interlocked.Read(ref _clearGenerationTicks);
            if (ticks <= current)
            {
                break;
            }
        } while (Interlocked.CompareExchange(ref _clearGenerationTicks, ticks, current) != current);
    }

    /// <inheritdoc />
    public void SeedRemoveMarker(DateTimeOffset invalidatedAt)
    {
        // No-op by design: the remove-generation marker is a distributed-tier (logical FlushAsync) concept. This
        // in-process cache's FlushAsync wipes physically and a FlushAll backplane broadcast wipes the receiver's L1
        // the same way, so there is no local logical remove marker to seed.
    }

    // The in-process cache has no separate durable store: its marker dictionaries ARE the store, so the durable
    // Write* writes collapse to the raise-only local Seed* bumps. WriteRemoveMarkerAsync stays a no-op (FlushAsync
    // wipes physically). These exist for ISeedableTagMarkerCache compliance; in a hybrid this cache is L1, not L2.

    /// <inheritdoc />
    public ValueTask WriteTagMarkerAsync(
        string tag,
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    )
    {
        SeedTagMarker(tag, invalidatedAt);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask WriteClearMarkerAsync(DateTimeOffset invalidatedAt, CancellationToken cancellationToken = default)
    {
        SeedClearMarker(invalidatedAt);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask WriteRemoveMarkerAsync(DateTimeOffset invalidatedAt, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Computes the newest invalidation marker applicable to <paramref name="entry"/> — the max of the global
    /// clear-generation marker and every per-tag marker the entry carries — or <see langword="null"/> when none
    /// applies. Untagged entries pay only the single clear-generation read.
    /// </summary>
    private DateTime? _NewestMarkerFor(CacheEntry entry)
    {
        DateTime? newest = null;

        var clearTicks = Interlocked.Read(ref _clearGenerationTicks);
        if (clearTicks != 0)
        {
            newest = new DateTime(clearTicks, DateTimeKind.Utc);
        }

        if (entry.Tags is { Count: > 0 } tags)
        {
            foreach (var tag in tags)
            {
                if (_tagMarkers.TryGetValue(tag, out var marker) && (newest is null || marker > newest.Value))
                {
                    newest = marker;
                }
            }
        }

        return newest;
    }

    /// <summary>Returns whether <paramref name="entry"/> is logically invalidated by a tag or clear marker.</summary>
    private bool _IsTagInvalidated(CacheEntry entry) =>
        CacheTagInvalidation.IsInvalidated(entry.CreatedAt, _NewestMarkerFor(entry));

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
            return new ValueTask<long>(_SetRemoveStringItems(key, stringsToRemove));
        }
        else
        {
            var valuesToRemove = value.Where(v => v is not null).Select(v => (object)v!).ToList();
            return new ValueTask<long>(_SetRemoveObjectItems(key, valuesToRemove));
        }
    }

    private long _SetRemoveStringItems(string key, List<string> stringsToRemove)
    {
        if (stringsToRemove.Count is 0)
        {
            return 0L;
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

        return removed;
    }

    private long _SetRemoveObjectItems(string key, List<object> valuesToRemove)
    {
        if (valuesToRemove.Count is 0)
        {
            return 0L;
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

        return removed;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        _ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _memory.Clear();
        // Physical wipe also resets the logical invalidation markers: no entry survives to be invalidated, so the
        // markers and clear generation can drop with the keyspace.
        _tagMarkers.Clear();
        Interlocked.Exchange(ref _clearGenerationTicks, 0);
        Interlocked.Exchange(ref _currentMemorySize, 0);
        Interlocked.Exchange(ref _nextExpiryTicks, long.MaxValue);
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
        _tagMarkers.Clear();
        Interlocked.Exchange(ref _clearGenerationTicks, 0);
        Interlocked.Exchange(ref _currentMemorySize, 0);
        Interlocked.Exchange(ref _nextExpiryTicks, long.MaxValue);
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

        // Logical tag/clear invalidation: demote the entry to logically-expired-but-physically-present so the
        // coordinator re-runs the factory but can still serve this reserve stale when the factory fails. Clamp
        // LogicalExpiresAt to now (and drop sliding so it is not re-armed/treated fresh) while keeping the
        // physical reserve intact.
        var logicalExpiresAt = existingEntry.LogicalExpiresAt;
        var slidingExpiration = existingEntry.SlidingExpiration;

        if (_IsTagInvalidated(existingEntry))
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            logicalExpiresAt = logicalExpiresAt.HasValue && logicalExpiresAt.Value < now ? logicalExpiresAt : now;
            slidingExpiration = null;
        }

        return new ValueTask<CacheStoreEntry<T>>(
            new CacheStoreEntry<T>(
                Found: true,
                IsNull: value is null,
                Value: value,
                LogicalExpiresAt: logicalExpiresAt,
                PhysicalExpiresAt: existingEntry.PhysicalExpiresAt,
                SlidingExpiration: slidingExpiration
            )
            {
                EagerRefreshAt = existingEntry.EagerRefreshAt,
                ETag = existingEntry.ETag,
                LastModifiedAt = existingEntry.LastModifiedAt,
                CreatedAt = existingEntry.CreatedAt,
                Tags = existingEntry.Tags,
                ConcurrencyStamp = existingEntry.ConcurrencyStamp,
            }
        );
    }

    // Non-async forwarder: `in` parameters are not allowed on async methods, so copy the descriptor by value.
    ValueTask<bool> IFactoryCacheStore.SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
        where T : default
    {
        _ThrowIfDisposed();
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return _SetEntryCoreWithResultAsync(key, entry);
    }

    private async ValueTask<bool> _SetEntryCoreWithResultAsync<T>(string key, CacheStoreEntryWrite<T> entry)
    {
        key = _GetKey(key);

        // Already expired on write (e.g. a non-positive Duration from a past BCL absolute expiration): evict
        // immediately and report success, mirroring RedisCache._SetEntryCoreAsync — honoring the CAS guard when
        // an expected stamp is supplied so a stale-stamp write does not delete a newer entry.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresIn = (entry.SlidingExpiration is null ? entry.PhysicalExpiresAt : entry.LogicalExpiresAt) - now;

        if (expiresIn <= TimeSpan.Zero)
        {
            if (
                entry.ExpectedConcurrencyStamp is { } expiredExpectedStamp
                && _memory.TryGetValue(key, out var existingEntry)
                && !string.Equals(existingEntry.ConcurrencyStamp, expiredExpectedStamp, StringComparison.Ordinal)
            )
            {
                return false;
            }

            _RemoveExpiredKey(key);
            return true;
        }

        var entrySize = _CalculateEntrySize(entry.Value);

        if (!_ValidateEntrySize(entrySize))
        {
            return false;
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
            lastModifiedAt: entry.LastModifiedAt,
            createdAt: entry.CreatedAt
        );

        if (entry.ExpectedConcurrencyStamp is { } expectedStamp)
        {
            return await _SetInternalIfStampMatchesAsync(key, cacheEntry, expectedStamp).ConfigureAwait(false);
        }

        return await _SetInternalAsync(key, cacheEntry).ConfigureAwait(false);
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

    // Lower the soonest-expiry hint if this write expires before any currently-tracked entry. Lock-free CAS on
    // the hot write path; no key is recorded because LRU access ticks already live on the entry
    // (CacheEntry.LastAccessTicks) and eviction samples the live dictionary directly.
    private void _TrackUpdate(DateTime? expiresAt)
    {
        if (!expiresAt.HasValue)
        {
            return;
        }

        var ticks = expiresAt.Value.Ticks;
        long current;

        while ((current = Volatile.Read(ref _nextExpiryTicks)) > ticks)
        {
            if (Interlocked.CompareExchange(ref _nextExpiryTicks, ticks, current) == current)
            {
                break;
            }
        }
    }

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : string.Concat(_keyPrefix, key);
    }

    private List<string> _GetKeys(string prefix)
    {
        var prefixedPrefix = string.IsNullOrEmpty(prefix) ? null : _GetKey(prefix);
        var stripLength = _keyPrefix.Length;

        // Manual foreach avoids the LINQ Where+Select iterator-wrapper allocations on every prefix scan.
        var result = new List<string>();

        foreach (var kvp in _memory)
        {
            if (kvp.Value.IsExpired)
            {
                continue;
            }

            if (prefixedPrefix is not null && !kvp.Key.StartsWith(prefixedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(stripLength == 0 ? kvp.Key : kvp.Key[stripLength..]);
        }

        return result;
    }

    private void _RemoveExpiredKey(string key)
    {
        if (_memory.TryRemove(key, out var removedEntry))
        {
            Interlocked.Add(ref _currentMemorySize, -removedEntry.Size);
        }
    }

    private void _TryRemoveExpiredEntry(string key, CacheEntry entry)
    {
        if (_memory.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry)))
        {
            Interlocked.Add(ref _currentMemorySize, -entry.Size);
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
            _TrackUpdate(rearmedEntry.TrackedExpiresAt);
            _ = _StartMaintenanceAsync();
        }
    }

    private async ValueTask<bool> _SetInternalAsync(
        string key,
        CacheEntry entry,
        bool addOnly = false,
        long nowTicks = -1
    )
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

        if (wasUpdated)
        {
            _TrackUpdate(entry.TrackedExpiresAt);
        }

        // Compaction is the only async work; await it only under memory/count pressure. Otherwise dispatch the
        // background sweep synchronously so the common write completes without an async suspension.
        if (ShouldCompact)
        {
            await _CompactAsync().ConfigureAwait(false);
        }

        _ScheduleMaintenance(nowTicks >= 0 ? nowTicks : _timeProvider.GetUtcNow().UtcDateTime.Ticks);

        return wasUpdated;
    }

    private async ValueTask<bool> _SetInternalIfStampMatchesAsync(string key, CacheEntry entry, string expectedStamp)
    {
        if (entry.IsExpired)
        {
            return false;
        }

        if (!_memory.TryGetValue(key, out var existingEntry) || existingEntry.IsExpired)
        {
            if (existingEntry is not null)
            {
                _TryRemoveExpiredEntry(key, existingEntry);
            }

            return false;
        }

        if (!string.Equals(existingEntry.ConcurrencyStamp, expectedStamp, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_memory.TryUpdate(key, entry, existingEntry))
        {
            return false;
        }

        var sizeDelta = entry.Size - existingEntry.Size;

        if (sizeDelta != 0)
        {
            Interlocked.Add(ref _currentMemorySize, sizeDelta);
        }

        _TrackUpdate(entry.TrackedExpiresAt);

        await _StartMaintenanceAsync(ShouldCompact).ConfigureAwait(false);

        return true;
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

        var nowTicks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;

        if (compactImmediately)
        {
            await _CompactAsync().ConfigureAwait(false);
        }

        _ScheduleMaintenance(nowTicks);
    }

    // Synchronous, allocation-free maintenance dispatch: claim the throttle slot via CAS and spawn the
    // background sweep. Takes a pre-fetched timestamp so the hot write path neither re-reads the clock nor
    // awaits an async wrapper when no compaction is needed.
    private void _ScheduleMaintenance(long nowTicks)
    {
        if (_disposedCts.IsCancellationRequested || Volatile.Read(ref _maintenanceRunning) != 0)
        {
            return;
        }

        var lastTicks = Volatile.Read(ref _lastMaintenanceTicks);

        if (nowTicks - lastTicks <= _maintenanceIntervalTicks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastMaintenanceTicks, nowTicks, lastTicks) != lastTicks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _maintenanceRunning, 1, 0) == 0)
        {
            _ = Task.Run(_DoMaintenanceAsync, _disposedCts.Token);
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
        // Sample up to K live entries straight from the source-of-truth dictionary (Redis-style approximate
        // LRU/size eviction). The entry carries its own LastAccessTicks/Size, so no access-ordered side queue is
        // needed; O(K) per call, repeated by the compaction loop until the cache is back under its limit.
        var isMemoryConstrained = _maxMemorySize.HasValue && Interlocked.Read(ref _currentMemorySize) > _maxMemorySize;
        (string? Key, long LastAccessTicks, long InstanceNumber, long Size) best = (null, long.MaxValue, 0, 0);

        var sampled = 0;

        foreach (var (key, entry) in _memory)
        {
            var isBetter = isMemoryConstrained
                // When memory constrained: prefer larger items, breaking ties by LRU then instance age.
                ? entry.Size > best.Size
                    || (entry.Size == best.Size && entry.LastAccessTicks < best.LastAccessTicks)
                    || (
                        entry.Size == best.Size
                        && entry.LastAccessTicks == best.LastAccessTicks
                        && entry.InstanceNumber < best.InstanceNumber
                    )
                // Standard LRU, breaking ties by instance age.
                : entry.LastAccessTicks < best.LastAccessTicks
                    || (entry.LastAccessTicks == best.LastAccessTicks && entry.InstanceNumber < best.InstanceNumber);

            if (isBetter)
            {
                best = (key, entry.LastAccessTicks, entry.InstanceNumber, entry.Size);
            }

            if (++sampled >= _evictionSampleSize)
            {
                break;
            }
        }

        return best.Key;
    }

    private async Task _DoMaintenanceAsync()
    {
        try
        {
            var nowTicks = _timeProvider.GetUtcNow().UtcDateTime.Ticks;

            try
            {
                // Skip the O(live-N) scan unless an entry is actually due. The hint is a lower bound, so a sweep
                // runs only once the soonest tracked expiry has passed — keeping maintenance proportional to
                // expiry events, not to cache size or write volume.
                if (Volatile.Read(ref _nextExpiryTicks) <= nowTicks)
                {
                    _SweepExpiredEntries(nowTicks);
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

    // Single pass over the live entries: evict the physically/sliding-expired and recompute the soonest
    // surviving expiry to re-arm the hint. Gated by _DoMaintenanceAsync so it only runs when something is due.
    // Eviction is capped at _maxEvictionsPerCompaction per sweep so a simultaneous burst of N tagged entries
    // expiring cannot turn one tick into an O(N x tags) untag storm (#15). When the cap is hit the remainder is
    // left for the next maintenance tick, which is forced to run promptly by arming the hint to now (see below).
    private void _SweepExpiredEntries(long nowTicks)
    {
        var soonest = long.MaxValue;
        var removalCount = 0;
        var capReached = false;

        foreach (var (key, entry) in _memory)
        {
            if (entry.ShouldRemoveAt(nowTicks))
            {
                if (removalCount >= _maxEvictionsPerCompaction)
                {
                    // Per-sweep eviction cap reached with expired work still pending. Abort the scan: the
                    // soonest-survivor recomputation is now incomplete (entries past this point are unscanned),
                    // so we must not trust `soonest`. Force an immediate follow-up tick to continue the sweep.
                    capReached = true;
                    break;
                }

                if (_memory.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry)))
                {
                    Interlocked.Add(ref _currentMemorySize, -entry.Size);
                    removalCount++;
                }

                continue;
            }

            if (entry.TrackedExpiresAt is { } tracked && tracked.Ticks < soonest)
            {
                soonest = tracked.Ticks;
            }
        }

        // Re-arm the hint.
        //
        // Capped sweep: the scan aborted early, `soonest` is incomplete, and expired work is known to remain.
        // Arm the target to `nowTicks` so the next maintenance tick's (_nextExpiryTicks <= now) gate passes and
        // resumes the sweep, instead of deferring to a far-future survivor we may not have observed.
        //
        // Full sweep: use the soonest survivor.
        //
        // For either target: a hint still <= now is the stale pre-sweep value of an entry we just removed: raise
        // it (full sweep) or hold it at now (capped) so the next sweep is scheduled correctly. A hint already
        // > now was lowered by a writer to a live entry added during the scan: keep the smaller of the two so that
        // entry is never starved of a sweep. The CAS retries if a writer moves the hint mid-update; writers only
        // ever lower, so convergence is monotone.
        var sweepResult = capReached ? nowTicks : soonest;

        long current;
        long target;

        do
        {
            current = Volatile.Read(ref _nextExpiryTicks);
            target = current <= nowTicks ? sweepResult : Math.Min(current, sweepResult);

            if (target == current)
            {
                break;
            }
        } while (Interlocked.CompareExchange(ref _nextExpiryTicks, target, current) != current);
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
            long size = 0,
            long nowTicksOverride = -1
        )
            : this(
                value,
                logicalExpiresAt: expiresAt,
                physicalExpiresAt: expiresAt,
                slidingExpiration: null,
                timeProvider,
                shouldClone,
                shouldThrowOnSerializationError,
                size,
                // Direct-write paths (Upsert/TryInsert/UpsertAll/Increment/SetAdd/SetIf*) flow through this
                // constructor: stamp a birth time so a prior tag/clear marker does not invalidate the new value.
                createdAt: new DateTime(
                    nowTicksOverride >= 0 ? nowTicksOverride : timeProvider.GetUtcNow().UtcDateTime.Ticks,
                    DateTimeKind.Utc
                ),
                nowTicksOverride: nowTicksOverride
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
            DateTime? lastModifiedAt = null,
            DateTime? createdAt = null,
            long nowTicksOverride = -1
        )
        {
            _timeProvider = timeProvider;
            _shouldClone = shouldClone && _TypeRequiresCloning(value?.GetType());
            _shouldThrowOnSerializationError = shouldThrowOnSerializationError;
            _cacheValue = _shouldClone ? _DeepClone(value) : value;

            // Reuse the caller's already-fetched timestamp when supplied (hot write path), else read the clock.
            _lastAccessTicks = nowTicksOverride >= 0 ? nowTicksOverride : _timeProvider.GetUtcNow().Ticks;
            LogicalExpiresAt = logicalExpiresAt;
            PhysicalExpiresAt = physicalExpiresAt;
            SlidingExpiration = slidingExpiration;
            LastFactoryError = lastFactoryError;
            Tags = tags is { Count: > 0 } ? tags.ToFrozenSet(StringComparer.Ordinal) : null;
            EagerRefreshAt = eagerRefreshAt;
            ETag = etag;
            LastModifiedAt = lastModifiedAt;
            CreatedAt = createdAt;
            Size = size;
            InstanceNumber = Interlocked.Increment(ref _instanceCount);
            // Precompute the immutable concurrency stamp once at construction so warm-hit reads and CAS
            // comparisons reuse the same string instead of allocating on every non-miss read (#23).
            ConcurrencyStamp = InstanceNumber.ToString(CultureInfo.InvariantCulture);
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
            // A WithExpiration/WithLogicalExpiration copy is a re-stamp (TTL/logical move only), so the entry's
            // original birth time is preserved rather than reset.
            CreatedAt = prototype.CreatedAt;
            Size = prototype.Size;
            InstanceNumber = Interlocked.Increment(ref _instanceCount);
            ConcurrencyStamp = InstanceNumber.ToString(CultureInfo.InvariantCulture);
        }

        internal long InstanceNumber { get; }

        /// <summary>Immutable concurrency stamp (<see cref="InstanceNumber"/> formatted invariantly),
        /// computed once at construction. Used for warm-hit reads and CAS comparisons.</summary>
        internal string ConcurrencyStamp { get; }

        internal DateTime? LogicalExpiresAt { get; }

        internal DateTime? PhysicalExpiresAt { get; }

        internal TimeSpan? SlidingExpiration { get; }

        internal DateTime? TrackedExpiresAt => SlidingExpiration.HasValue ? LogicalExpiresAt : PhysicalExpiresAt;

        internal LastFactoryError? LastFactoryError { get; }

        internal IReadOnlySet<string>? Tags { get; }

        internal DateTime? EagerRefreshAt { get; }

        internal string? ETag { get; }

        internal DateTime? LastModifiedAt { get; }

        internal DateTime? CreatedAt { get; }

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
