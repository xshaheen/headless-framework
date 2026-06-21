// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.Redis;
using Headless.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Headless.Caching;

/// <summary>Redis cache implementation with atomic operations and cluster support.</summary>
/// <remarks>
/// <para>
/// <b>Cancellation Token Behavior:</b> Cancellation is checked at the start of each operation.
/// Once a batch operation begins (e.g., <see cref="UpsertAllAsync{T}"/>, <see cref="RemoveAllAsync"/>),
/// it will complete atomically without further cancellation checks. This ensures consumers don't
/// observe partial results from batch operations.
/// </para>
/// <para>
/// Exception: <see cref="RemoveByPrefixAsync"/> and <see cref="GetAllKeysByPrefixAsync"/> use streaming
/// SCAN and support cancellation during iteration since they may process unbounded key sets.
/// </para>
/// </remarks>
public sealed class RedisCache(
    ISerializer serializer,
    TimeProvider timeProvider,
    RedisCacheOptions options,
    [FromKeyedServices(RedisCacheServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader,
    ILogger<RedisCache>? logger = null,
    ICacheFactoryLockProvider? factoryLockProvider = null
) : IRemoteCache, IFactoryCacheStore, ISeedableTagMarkerCache, IBufferCache, IDisposable
{
    /// <summary>Legacy null sentinel retained only for raw pre-envelope payloads and collection entries.</summary>
    private static readonly RedisValue _NullValue = "@@NULL";
    private const int _BatchSize = 250;

    /// <summary>
    /// Reserved namespace segment for Family-2 per-tag invalidation markers: each tag's last-invalidation
    /// timestamp (Unix-ms string) lives at <c>{KeyPrefix}__tag:{tag}</c>. One key per tag, so tag invalidation
    /// works on Redis Cluster. Cache entries must not be stored under keys starting with this segment.
    /// </summary>
    private const string _TagMarkerNamespace = "__tag:";

    /// <summary>
    /// Reserved key for the Family-2 global clear-generation marker (Unix-ms string). Bumped by
    /// <c>ClearAsync</c>; compared on every read. Cannot collide with the tag-marker namespace or user keys.
    /// </summary>
    private const string _ClearMarkerSuffix = "__clear";

    /// <summary>
    /// Reserved key for the Family-2 global remove-generation marker (Unix-ms string). Bumped by the logical
    /// <c>FlushAsync</c>; compared on every read. Entries born before it read as a hard miss with no fail-safe
    /// reserve (distinct from the clear marker, which preserves reserves). Cannot collide with user keys.
    /// </summary>
    private const string _RemoveMarkerSuffix = "__remove";

    private readonly ILogger _logger = logger ?? NullLogger<RedisCache>.Instance;
    private readonly string _keyPrefix = options.KeyPrefix ?? "";

    // Process-local marker cache for Family-2 logical tag-version invalidation. Each entry is the resolved
    // marker (Unix-ms; long.MinValue = "absent in Redis") plus the monotonic tick at which it was fetched, so a
    // read can reuse it within options.TagMarkerRefreshWindow instead of hitting Redis. The cache is refreshed
    // lazily on the read path (pipelined MGET for stale/missing tag markers).
    private readonly ConcurrentDictionary<string, (long MarkerMs, long FetchedTicks)> _markerCache = new(
        StringComparer.Ordinal
    );

    // Cached clear-generation marker: MarkerMs (long.MinValue = absent) and the fetch tick. Read/refreshed on
    // every read (tagged or not) within the refresh window. Guarded by Volatile reads/writes of the tuple via a
    // lock-free swap is unnecessary — a torn read at worst forces an extra refresh, but to keep it simple and
    // correct we store the two longs behind a single reference cell.
    private long _clearMarkerMs = long.MinValue;
    private long _clearMarkerFetchedTicks = long.MinValue;

    // Cached remove-generation marker (logical FlushAsync). Same shape and refresh-window semantics as the clear
    // marker; an entry older than this reads as a hard miss with no fail-safe reserve.
    private long _removeMarkerMs = long.MinValue;
    private long _removeMarkerFetchedTicks = long.MinValue;
    private const long _MarkerAbsent = long.MinValue;

    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions { get; } = options.DefaultEntryOptions;

    private readonly FactoryCacheCoordinator _coordinator = new(timeProvider, logger, factoryLockProvider);

    private volatile bool _supportsMsetEx;
    private volatile bool _supportsMsetExChecked;
    private readonly Lazy<bool> _isClusterLazy = new(() => _CheckIsCluster(options.ConnectionMultiplexer));
    private readonly IDatabase _database = options.ConnectionMultiplexer.GetDatabase();

    private bool IsCluster => _isClusterLazy.Value;

    private static bool _CheckIsCluster(IConnectionMultiplexer connectionMultiplexer)
    {
        foreach (var endpoint in connectionMultiplexer.GetEndPoints())
        {
            var server = connectionMultiplexer.GetServer(endpoint);

            if (server is { IsConnected: true, ServerType: ServerType.Cluster })
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
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
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);

        cancellationToken.ThrowIfCancellationRequested();

        return await _coordinator.GetOrAddAsync(this, key, factory, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = _GetKey(key);

        try
        {
            // Read only the fixed header plus the sliding field (its first optional section) — never the value
            // payload. For a large session entry this re-arms the TTL without transferring the whole value.
            var headerBytes = await _database
                .StringGetRangeAsync(
                    redisKey,
                    0,
                    RedisCacheEntryFrame.HeaderLength + sizeof(long) - 1,
                    options.ReadMode
                )
                .ConfigureAwait(false);

            if (
                !RedisCacheEntryFrame.TryDecodeHeader(((byte[]?)headerBytes).AsSpan(), out var header)
                || header.SlidingExpiration is not { } slidingExpiration
                || header.PhysicalExpiresAt is not { } physicalExpiresAt
            )
            {
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            if (_IsExpired(physicalExpiresAt, now))
            {
                return;
            }

            if (header.HasTags)
            {
                // A tagged entry needs its full tag list to resolve per-tag invalidation markers, which the
                // header prefix does not carry — fall back to the full-value read for correctness.
                await _RefreshSlidingFromFullFrameAsync(redisKey, now).ConfigureAwait(false);

                return;
            }

            // Untagged: only the global clear/remove markers (resolved in-process) can invalidate the entry.
            var newestMarker = await _ResolveNewestMarkerAsync(tags: null).ConfigureAwait(false);

            if (CacheTagInvalidation.IsInvalidated(header.CreatedAt, newestMarker))
            {
                return;
            }

            await _RearmSlidingTtlAsync(redisKey, slidingExpiration, physicalExpiresAt, now, header.LogicalExpiresAt)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!FactoryCacheCoordinator.IsCallerCancellation(exception, cancellationToken))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogSlidingExpirationRefreshFailed(exception, redisKey.ToString());
            }
        }
    }

    // Fallback for the rare tagged sliding entry: the header-only read cannot recover the tag list, so read the
    // full frame and resolve per-tag invalidation markers before re-arming.
    private async ValueTask _RefreshSlidingFromFullFrameAsync(RedisKey redisKey, DateTime now)
    {
        var redisValue = await _database.StringGetAsync(redisKey, options.ReadMode).ConfigureAwait(false);

        if (!redisValue.HasValue)
        {
            return;
        }

        var frame = RedisCacheEntryFrame.Decode(redisValue);

        if (!frame.IsFramed || frame.SlidingExpiration is null || _IsExpired(frame.PhysicalExpiresAt, now))
        {
            return;
        }

        var newestMarker = await _ResolveNewestMarkerAsync(frame.Tags).ConfigureAwait(false);

        if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker))
        {
            return;
        }

        await _TryRearmSlidingEntryAsync(redisKey, frame, now).ConfigureAwait(false);
    }

    #region Update

    public async ValueTask<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        return await _SetInternalAsync(_GetKey(key), value, expiration).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> UpsertEntryAsync<T>(
        string key,
        T? value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        await (this).UpsertEntryAsync(key, value, options, timeProvider, cancellationToken).ConfigureAwait(false);

        return true;
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
            await RemoveAllAsync(value.Keys, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        expiration = _NormalizeExpiration(expiration);

        // Validate keys and serialize values upfront
        var pairs = new KeyValuePair<RedisKey, RedisValue>[value.Count];
        var index = 0;
        var now = timeProvider.GetUtcNow();

        foreach (var kvp in value)
        {
            Argument.IsNotNullOrEmpty(kvp.Key);
            pairs[index++] = new KeyValuePair<RedisKey, RedisValue>(
                _GetKey(kvp.Key),
                _ToFramedRedisValue(kvp.Value, expiration, now)
            );
        }

        if (IsCluster)
        {
            var successCount = 0;
            var slotBuckets = _GroupBySlot(pairs, static pair => pair.Key);

            foreach (var bucket in slotBuckets.Values)
            {
                var count = await _SetAllInternalAsync([.. bucket], expiration).ConfigureAwait(false);
                successCount += count;
            }

            return successCount;
        }

        return await _SetAllInternalAsync(pairs, expiration).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryInsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        return await _SetInternalAsync(_GetKey(key), value, expiration, When.NotExists).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        return await _SetInternalAsync(_GetKey(key), value, expiration, When.Exists).ConfigureAwait(false);
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

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var normalizedExpiration = _NormalizeExpiration(expiration);
        var redisValue = _ToFramedRedisValue(value, normalizedExpiration, now);
        var expectedValue = _ToRedisValue(expected);

        var expiresMs = _GetExpirationMilliseconds(expiration, now);
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var redisResult = await scriptsLoader
            .EvaluateAsync(
                _database,
                CacheReplaceIfEqualScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value = redisValue,
                    expected = expectedValue,
                    expectedIsNull = expected is null ? 1 : 0,
                    expires = expiresArg,
                    headerLen = RedisCacheEntryFrame.HeaderLength,
                    nullValue = _NullValue,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)redisResult > 0;
    }

    public async ValueTask<double> IncrementAsync(
        string key,
        double amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var expiresMs = _GetExpirationMilliseconds(expiration, timeProvider.GetUtcNow());
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await scriptsLoader
            .EvaluateAsync(
                _database,
                IncrementWithExpireScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value = amount,
                    expires = expiresArg,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return double.Parse(result.ToString(), CultureInfo.InvariantCulture);
    }

    public async ValueTask<long> IncrementAsync(
        string key,
        long amount,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var expiresMs = _GetExpirationMilliseconds(expiration, timeProvider.GetUtcNow());
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await scriptsLoader
            .EvaluateAsync(
                _database,
                IncrementWithExpireScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value = amount,
                    expires = expiresArg,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return long.Parse(result.ToString(), CultureInfo.InvariantCulture);
    }

    public async ValueTask<double> SetIfHigherAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var expiresMs = _GetExpirationMilliseconds(expiration, timeProvider.GetUtcNow());
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await scriptsLoader
            .EvaluateAsync(
                _database,
                SetIfHigherScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value,
                    expires = expiresArg,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return double.Parse(result.ToString(), CultureInfo.InvariantCulture);
    }

    public async ValueTask<long> SetIfHigherAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var expiresMs = _GetExpirationMilliseconds(expiration, timeProvider.GetUtcNow());
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await scriptsLoader
            .EvaluateAsync(
                _database,
                SetIfHigherScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value,
                    expires = expiresArg,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return long.Parse(result.ToString(), CultureInfo.InvariantCulture);
    }

    public async ValueTask<double> SetIfLowerAsync(
        string key,
        double value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var expiresMs = _GetExpirationMilliseconds(expiration, timeProvider.GetUtcNow());
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await scriptsLoader
            .EvaluateAsync(
                _database,
                SetIfLowerScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value,
                    expires = expiresArg,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return double.Parse(result.ToString(), CultureInfo.InvariantCulture);
    }

    public async ValueTask<long> SetIfLowerAsync(
        string key,
        long value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var expiresMs = _GetExpirationMilliseconds(expiration, timeProvider.GetUtcNow());
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await scriptsLoader
            .EvaluateAsync(
                _database,
                SetIfLowerScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value,
                    expires = expiresArg,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return long.Parse(result.ToString(), CultureInfo.InvariantCulture);
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
            await SetRemoveAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var expiresAt = expiration.HasValue
            ? timeProvider.GetUtcNow().UtcDateTime.SafeAdd(expiration.Value)
            : DateTime.MaxValue;

        var redisValues = new List<SortedSetEntry>();
        var expiresAtMilliseconds = expiresAt.ToUnixTimeMilliseconds();

        if (value is string stringValue)
        {
            redisValues.Add(new SortedSetEntry(_ToRedisValue(stringValue), expiresAtMilliseconds));
        }
        else
        {
            redisValues.AddRange(
                value.Where(v => v is not null).Select(v => new SortedSetEntry(_ToRedisValue(v), expiresAtMilliseconds))
            );
        }

        await _RemoveExpiredListValuesAsync(key).ConfigureAwait(false);

        if (redisValues.Count is 0)
        {
            return 0;
        }

        var added = await _database.SortedSetAddAsync(key, [.. redisValues]).ConfigureAwait(false);

        if (added > 0)
        {
            await _SetListExpirationAsync(key).ConfigureAwait(false);
        }

        return added;
    }

    #endregion

    #region Get

    public async ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var redisValue = await _database.StringGetAsync(_GetKey(key), options.ReadMode).ConfigureAwait(false);
        return await _RedisValueToCacheValueAsync<T>(_GetKey(key), redisValue).ConfigureAwait(false);
    }

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var originalKeys = new List<string>();
        var redisKeys = new List<RedisKey>();

        foreach (var key in cacheKeys.Distinct(StringComparer.Ordinal))
        {
            Argument.IsNotNullOrEmpty(key);
            originalKeys.Add(key);
            redisKeys.Add(_GetKey(key));
        }

        if (redisKeys.Count is 0)
        {
            return ReadOnlyDictionary<string, CacheValue<T>>.Empty;
        }

        if (IsCluster)
        {
            var result = new Dictionary<string, CacheValue<T>>(redisKeys.Count, StringComparer.Ordinal);

            var pairs = new (string Original, RedisKey Redis)[originalKeys.Count];

            for (var i = 0; i < originalKeys.Count; i++)
            {
                pairs[i] = (originalKeys[i], redisKeys[i]);
            }

            var slotBuckets = _GroupBySlot(pairs, static pair => pair.Redis);

            foreach (var bucket in slotBuckets.Values)
            {
                var slotKeys = new RedisKey[bucket.Count];

                for (var i = 0; i < bucket.Count; i++)
                {
                    slotKeys[i] = bucket[i].Redis;
                }

                var values = await _database.StringGetAsync(slotKeys, options.ReadMode).ConfigureAwait(false);

                for (var i = 0; i < bucket.Count; i++)
                {
                    result[bucket[i].Original] = await _RedisValueToCacheValueAsync<T>(
                            bucket[i].Redis,
                            values[i],
                            rearm: false
                        )
                        .ConfigureAwait(false);
                }
            }

            return result.AsReadOnly();
        }
        else
        {
            var result = new Dictionary<string, CacheValue<T>>(redisKeys.Count, StringComparer.Ordinal);
            var values = await _database.StringGetAsync([.. redisKeys], options.ReadMode).ConfigureAwait(false);

            for (var i = 0; i < originalKeys.Count; i++)
            {
                result[originalKeys[i]] = await _RedisValueToCacheValueAsync<T>(redisKeys[i], values[i], rearm: false)
                    .ConfigureAwait(false);
            }

            return result.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IDictionary<string, CacheValueWithExpiration<T>>> GetAllWithExpirationAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var originalKeys = new List<string>();
        var redisKeys = new List<RedisKey>();

        foreach (var key in cacheKeys.Distinct(StringComparer.Ordinal))
        {
            Argument.IsNotNullOrEmpty(key);
            originalKeys.Add(key);
            redisKeys.Add(_GetKey(key));
        }

        if (redisKeys.Count is 0)
        {
            return ReadOnlyDictionary<string, CacheValueWithExpiration<T>>.Empty;
        }

        // One round-trip: fetch all raw values. For framed entries the logical expiration is embedded in the
        // frame and decoded below without any additional network call. Sliding entries need the live Redis TTL
        // (the frame's logical is just a lower bound); those are collected and fetched in a single pipelined
        // batch — still O(1) network round-trips total.
        RedisValue[] rawValues;

        if (IsCluster)
        {
            // Cluster: batch by hash slot so each StringGetAsync covers one slot group.
            // Manual Dictionary grouping avoids LINQ Lookup allocation + per-group materialization.
            rawValues = new RedisValue[redisKeys.Count];

            var indexedKeys = new (RedisKey Redis, int Index)[redisKeys.Count];

            for (var i = 0; i < redisKeys.Count; i++)
            {
                indexedKeys[i] = (redisKeys[i], i);
            }

            var slotBuckets = _GroupBySlot(indexedKeys, static entry => entry.Redis);

            foreach (var bucket in slotBuckets.Values)
            {
                var slotKeys = new RedisKey[bucket.Count];

                for (var i = 0; i < bucket.Count; i++)
                {
                    slotKeys[i] = bucket[i].Redis;
                }

                var slotValues = await _database.StringGetAsync(slotKeys, options.ReadMode).ConfigureAwait(false);

                for (var i = 0; i < bucket.Count; i++)
                {
                    rawValues[bucket[i].Index] = slotValues[i];
                }
            }
        }
        else
        {
            rawValues = await _database.StringGetAsync([.. redisKeys], options.ReadMode).ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var result = new Dictionary<string, CacheValueWithExpiration<T>>(redisKeys.Count, StringComparer.Ordinal);

        // First pass: decode value + expiration from the frame. Collect keys whose expiration requires a
        // live TTL probe (sliding entries or non-framed legacy payloads).
        List<(int Index, RedisKey RedisKey, T? Value)>? slidingOrLegacyHits = null;

        for (var i = 0; i < originalKeys.Count; i++)
        {
            var rawValue = rawValues[i];

            if (!rawValue.HasValue)
            {
                continue;
            }

            try
            {
                var frame = RedisCacheEntryFrame.Decode(rawValue);

                if (frame.IsFramed)
                {
                    if (_IsExpired(frame.PhysicalExpiresAt, now))
                    {
                        continue;
                    }

                    // Family-2: a tag/clear-invalidated entry is a miss for direct mirror reads.
                    var newestMarker = await _ResolveNewestMarkerAsync(frame.Tags).ConfigureAwait(false);

                    if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker))
                    {
                        continue;
                    }

                    if (frame.SlidingExpiration.HasValue)
                    {
                        // Sliding: logical expiry is maintained by TTL re-arms; the frame's LogicalExpiresAt is a
                        // lower bound, not the live value. Collect for a single pipelined TTL batch below.
                        if (!frame.IsNull)
                        {
                            var slidingValue = _DeserializeValueSegment<T>(frame.ValueSegment);
                            (slidingOrLegacyHits ??= []).Add((i, redisKeys[i], slidingValue));
                        }
                        else
                        {
                            // Null-sentinel sliding entry: still needs live TTL.
                            (slidingOrLegacyHits ??= []).Add((i, redisKeys[i], default));
                        }

                        continue;
                    }

                    if (_IsExpired(frame.LogicalExpiresAt, now))
                    {
                        continue;
                    }

                    TimeSpan? logicalRemaining = frame.LogicalExpiresAt?.Subtract(now);

                    CacheValue<T> cacheValue;

                    if (frame.IsNull)
                    {
                        cacheValue = CacheValue<T>.Null;
                    }
                    else
                    {
                        var framedValue = _DeserializeValueSegment<T>(frame.ValueSegment);
                        cacheValue = new CacheValue<T>(framedValue, true);
                    }

                    result[originalKeys[i]] = new CacheValueWithExpiration<T>(cacheValue, logicalRemaining);
                }
                else
                {
                    // Non-framed (legacy) entry: no embedded expiration metadata — needs live TTL probe.
                    T? legacyValue;

                    if (rawValue == _NullValue)
                    {
                        legacyValue = default;
                    }
                    else
                    {
                        legacyValue = _FromRedisValue<T>(rawValue);
                    }

                    (slidingOrLegacyHits ??= []).Add((i, redisKeys[i], legacyValue));
                }
            }
            catch (Exception e)
            {
                _logger.LogDeserializationFailed(e, rawValue.Length(), typeof(T).FullName);
                throw;
            }
        }

        // Second pass (optional): pipeline all TTL probes in one batch execution — one network round-trip for
        // all sliding/legacy entries regardless of count.
        if (slidingOrLegacyHits is { Count: > 0 })
        {
            var batch = _database.CreateBatch();
            var ttlTasks = new Task<TimeSpan?>[slidingOrLegacyHits.Count];

            for (var j = 0; j < slidingOrLegacyHits.Count; j++)
            {
                ttlTasks[j] = batch.KeyTimeToLiveAsync(slidingOrLegacyHits[j].RedisKey, options.ReadMode);
            }

            batch.Execute();
            var ttlResults = await Task.WhenAll(ttlTasks).ConfigureAwait(false);

            for (var j = 0; j < slidingOrLegacyHits.Count; j++)
            {
                var (idx, _, val) = slidingOrLegacyHits[j];
                var ttl = ttlResults[j];

                // ttl == null  → Redis reports no expiry on the key (persistent / legacy-unframed entry that was
                //                 written without a TTL). The key was confirmed present in pass 1, so null here
                //                 means "no expiry", NOT "key missing". Emit as a hit with Expiration = null,
                //                 matching GetExpirationAsync / GetAsync / InMemoryRemoteCacheAdapter contract.
                //
                // ttl.Value <= Zero → The key has a TTL but it has already elapsed (eviction race between pass 1
                //                     and this probe). Treat as a miss and skip.
                if (ttl is { Ticks: <= 0 })
                {
                    continue;
                }

                var cacheVal = val is null ? CacheValue<T>.Null : new CacheValue<T>(val, true);

                // ttl is null  → persistent key, no expiry → Expiration = null (valid hit)
                // ttl.Value > 0 → remaining TTL → Expiration = ttl
                result[originalKeys[idx]] = new CacheValueWithExpiration<T>(cacheVal, ttl);
            }
        }

        return result.AsReadOnly();
    }

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var keys = await GetAllKeysByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);
        return await GetAllAsync<T>(keys, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        const int chunkSize = 2500;
        var prefixedPrefix = _GetKey(prefix);
        var pattern = $"{prefixedPrefix}*";
        var stripLength = _keyPrefix.Length;
        var keys = new List<string>();

        var endpoints = options.ConnectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            var server = options.ConnectionMultiplexer.GetServer(endpoint);

            if (server.IsReplica)
            {
                continue;
            }

            await foreach (
                var key in server.KeysAsync(pattern: pattern, pageSize: chunkSize).WithCancellation(cancellationToken)
            )
            {
                keys.Add(stripLength == 0 ? key! : ((string)key!)[stripLength..]);
            }
        }

        return keys;
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Fetch the value rather than KeyExists: a fail-safe reserve keeps its Redis TTL aligned to PHYSICAL
        // expiration, so the key can still exist after its LOGICAL expiration. A key-existence check would
        // report such a logically-expired reserve as present; _RedisValueIsLogicallyPresent applies the same
        // logical-expiry rule the read methods use.
        var redisValue = await _database.StringGetAsync(_GetKey(key), options.ReadMode).ConfigureAwait(false);
        return await _RedisValueIsLogicallyPresentAsync(redisValue).ConfigureAwait(false);
    }

    public async ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var endpoints = options.ConnectionMultiplexer.GetEndPoints();

        if (endpoints.Length is 0)
        {
            return 0;
        }

        var usePrefix = !string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(_keyPrefix);
        var tasks = new List<Task<long>>(endpoints.Length);

        if (usePrefix)
        {
            const int chunkSize = 2500;
            var pattern = $"{_GetKey(prefix)}*";

            foreach (var endpoint in endpoints)
            {
                var server = options.ConnectionMultiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                tasks.Add(_CountKeysByPatternAsync(server, pattern, chunkSize, cancellationToken));
            }
        }
        else
        {
            foreach (var endpoint in endpoints)
            {
                var server = options.ConnectionMultiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                tasks.Add(_SafeDatabaseSizeAsync(server));
            }
        }

        if (tasks.Count is 0)
        {
            return 0;
        }

        var counts = await Task.WhenAll(tasks).ConfigureAwait(false);
        return counts.Sum();
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var redisValue = await _database.StringGetAsync(_GetKey(key), options.ReadMode).ConfigureAwait(false);

        if (!redisValue.HasValue)
        {
            return null;
        }

        var frame = RedisCacheEntryFrame.Decode(redisValue);

        if (!frame.IsFramed)
        {
            // Non-framed (legacy/raw) keys carry no logical metadata, so fall back to the server TTL. Only
            // legacy keys pay this second round trip; framed keys return below from the decoded frame.
            return await _database.KeyTimeToLiveAsync(_GetKey(key)).ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (_IsExpired(frame.PhysicalExpiresAt, now))
        {
            return null;
        }

        // Family-2: a tag/clear-invalidated entry has no remaining logical expiration for direct reads.
        var newestMarker = await _ResolveNewestMarkerAsync(frame.Tags).ConfigureAwait(false);

        if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker))
        {
            return null;
        }

        if (frame.SlidingExpiration.HasValue)
        {
            return await _database.KeyTimeToLiveAsync(_GetKey(key)).ConfigureAwait(false);
        }

        if (_IsExpired(frame.LogicalExpiresAt, now))
        {
            return null;
        }

        return frame.LogicalExpiresAt?.Subtract(now);
    }

    /// <inheritdoc/>
    public async ValueTask<CacheValueWithExpiration<T>> GetWithExpirationAsync<T>(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = _GetKey(key);
        var rawValue = await _database.StringGetAsync(redisKey, options.ReadMode).ConfigureAwait(false);

        if (!rawValue.HasValue)
        {
            return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
        }

        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var frame = RedisCacheEntryFrame.Decode(rawValue);

            if (frame.IsFramed)
            {
                if (_IsExpired(frame.PhysicalExpiresAt, now))
                {
                    return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
                }

                // Family-2: a tag/clear-invalidated entry is a miss for direct mirror reads.
                var newestMarker = await _ResolveNewestMarkerAsync(frame.Tags).ConfigureAwait(false);

                if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker))
                {
                    return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
                }

                if (frame.SlidingExpiration.HasValue)
                {
                    // Sliding: logical expiry is maintained by TTL re-arms; the frame's LogicalExpiresAt is a
                    // lower bound. Re-arm and fetch the live TTL with a single extra round-trip (unavoidable for
                    // sliding — the live TTL IS the expiration). Rearm is best-effort (fires-and-ignores errors).
                    await _TryRearmSlidingEntryAsync(redisKey, frame, now).ConfigureAwait(false);

                    var ttl = await _database.KeyTimeToLiveAsync(redisKey).ConfigureAwait(false);

                    if (ttl is { Ticks: <= 0 })
                    {
                        return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
                    }

                    CacheValue<T> slidingValue = frame.IsNull
                        ? CacheValue<T>.Null
                        : new CacheValue<T>(_DeserializeValueSegment<T>(frame.ValueSegment), true);

                    return new CacheValueWithExpiration<T>(slidingValue, ttl);
                }

                if (_IsExpired(frame.LogicalExpiresAt, now))
                {
                    return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
                }

                TimeSpan? logicalRemaining = frame.LogicalExpiresAt?.Subtract(now);

                CacheValue<T> cacheValue = frame.IsNull
                    ? CacheValue<T>.Null
                    : new CacheValue<T>(_DeserializeValueSegment<T>(frame.ValueSegment), true);

                return new CacheValueWithExpiration<T>(cacheValue, logicalRemaining);
            }

            // Non-framed (legacy/raw) entry: value is present but carries no logical expiry metadata.
            // Fall back to the live server TTL for the expiration component.
            CacheValue<T> legacyValue;

            if (rawValue == _NullValue)
            {
                legacyValue = CacheValue<T>.Null;
            }
            else
            {
                legacyValue = new CacheValue<T>(_FromRedisValue<T>(rawValue), true);
            }

            var legacyTtl = await _database.KeyTimeToLiveAsync(redisKey).ConfigureAwait(false);

            if (legacyTtl is { Ticks: <= 0 })
            {
                return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, null);
            }

            return new CacheValueWithExpiration<T>(legacyValue, legacyTtl);
        }
        catch (Exception e)
        {
            _logger.LogDeserializationFailed(e, rawValue.Length(), typeof(T).FullName);
            throw;
        }
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

        if (!pageIndex.HasValue)
        {
            var set = await _database
                .SortedSetRangeByScoreAsync(
                    key,
                    timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                    double.PositiveInfinity,
                    Exclude.Start,
                    flags: options.ReadMode
                )
                .ConfigureAwait(false);

            return _RedisValuesToCacheValue<T>(set);
        }
        else
        {
            var skip = (pageIndex.Value - 1) * pageSize;
            var set = await _database
                .SortedSetRangeByScoreAsync(
                    key,
                    timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                    double.PositiveInfinity,
                    Exclude.Start,
                    skip: skip,
                    take: pageSize,
                    flags: options.ReadMode
                )
                .ConfigureAwait(false);

            return _RedisValuesToCacheValue<T>(set);
        }
    }

    #endregion

    #region Remove

    public async ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return await _database.KeyDeleteAsync(_GetKey(key)).ConfigureAwait(false);
    }

    public async ValueTask<bool> ExpireAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = _GetKey(key);
        var redisValue = await _database.StringGetAsync(redisKey, options.ReadMode).ConfigureAwait(false);

        if (!redisValue.HasValue)
        {
            return false;
        }

        var frame = RedisCacheEntryFrame.Decode(redisValue);

        // A non-framed (legacy/raw) key carries no logical metadata, so there is no fail-safe reserve to preserve:
        // logical expiration collapses to removal, the same fork the framed no-reserve branch takes below.
        if (!frame.IsFramed)
        {
            return await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (_IsExpired(frame.PhysicalExpiresAt, now))
        {
            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return false;
        }

        // Physical > Logical is overloaded (fail-safe extension vs sliding's absolute cap); only the former, on a
        // non-sliding entry, is a parachute worth keeping — mirror InMemoryCache.ExpireAsync exactly.
        var hasFailSafeReserve =
            frame.SlidingExpiration is null
            && frame is { LogicalExpiresAt: { } logical, PhysicalExpiresAt: { } physical }
            && physical > logical;

        if (!hasFailSafeReserve)
        {
            return await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
        }

        // Re-stamp the logical expiry to now while preserving the physical reserve and the key's server TTL
        // (KeepTtl). Reads now miss; a later failing fail-safe factory can still serve the stale value. The eager
        // stamp is cleared — a logically-expired entry must route the next caller through the factory, not eager
        // refresh. Last-writer-wins under a concurrent fresh write, consistent with the sliding re-arm RMW.
        var reStamped = RedisCacheEntryFrame.Encode(
            frame.ValueSegment.ToArray(),
            frame.IsNull,
            logicalExpiresAt: now,
            physicalExpiresAt: frame.PhysicalExpiresAt,
            slidingExpiration: null,
            eagerRefreshAt: null,
            etag: frame.ETag,
            lastModifiedAt: frame.LastModifiedAt,
            tags: frame.Tags,
            // Logical-expire-in-place is a re-stamp, not a new value: preserve the entry's original birth time.
            createdAt: frame.CreatedAt
        );

        // Honor the contract ("true when an entry was found and expired"): When.Exists returns false if the key
        // was evicted between the GET above and this SET (its physical TTL elapsed, or a concurrent RemoveAsync),
        // so surface the actual outcome rather than an unconditional true.
        return await _database
            .StringSetAsync(redisKey, reStamped, expiry: null, keepTtl: true, when: When.Exists)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var expectedValue = _ToRedisValue(expected);
        var redisResult = await scriptsLoader
            .EvaluateAsync(
                _database,
                CacheRemoveIfEqualScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    expected = expectedValue,
                    expectedIsNull = expected is null ? 1 : 0,
                    headerLen = RedisCacheEntryFrame.HeaderLength,
                    nullValue = _NullValue,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)redisResult > 0;
    }

    public async ValueTask<int> RemoveAllAsync(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKeys = cacheKeys is ICollection<string> collection ? new List<RedisKey>(collection.Count) : [];

        foreach (var key in cacheKeys.Distinct(StringComparer.Ordinal))
        {
            Argument.IsNotNullOrEmpty(key);
            redisKeys.Add(_GetKey(key));
        }

        if (redisKeys.Count is 0)
        {
            return 0;
        }

        long deleted = 0;

        if (IsCluster)
        {
            foreach (var batch in redisKeys.Chunk(_BatchSize))
            {
                var slotBuckets = _GroupBySlot(batch, static key => key);

                foreach (var (slot, bucket) in slotBuckets)
                {
                    var hashSlotKeys = bucket.ToArray();

                    try
                    {
                        var count = await _database.KeyDeleteAsync(hashSlotKeys).ConfigureAwait(false);
                        deleted += count;
                    }
                    catch (Exception e)
                    {
                        _logger.LogUnableToDeleteHashSlotKeys(e, slot, hashSlotKeys);
                    }
                }
            }
        }
        else
        {
            foreach (var batch in redisKeys.Chunk(_BatchSize))
            {
                try
                {
                    var count = await _database.KeyDeleteAsync(batch).ConfigureAwait(false);
                    deleted += count;
                }
                catch (Exception e)
                {
                    _logger.LogUnableToDeleteKeys(e, batch);
                }
            }
        }

        return (int)deleted;
    }

    public async ValueTask<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        return (int)await _RemoveByPatternAsync($"{_GetKey(prefix)}*", cancellationToken).ConfigureAwait(false);
    }

    // Scans every primary node for keys matching the pattern and UNLINKs them in batches. Shared by
    // RemoveByPrefixAsync (the instance prefix plus the caller's prefix) and FlushAsync (the instance prefix only).
    private async ValueTask<long> _RemoveByPatternAsync(string pattern, CancellationToken cancellationToken)
    {
        var endpoints = options.ConnectionMultiplexer.GetEndPoints();

        if (endpoints.Length is 0)
        {
            return 0;
        }

        var isCluster = IsCluster;
        long deleted = 0;

        foreach (var endpoint in endpoints)
        {
            var server = options.ConnectionMultiplexer.GetServer(endpoint);

            if (server.IsReplica)
            {
                continue;
            }

            var keys = new List<RedisKey>();

            await foreach (
                var key in server.KeysAsync(pattern: pattern, pageSize: _BatchSize).WithCancellation(cancellationToken)
            )
            {
                keys.Add(key);

                if (keys.Count >= _BatchSize)
                {
                    deleted += await _DeleteKeysAsync([.. keys], isCluster).ConfigureAwait(false);
                    keys.Clear();
                }
            }

            if (keys.Count > 0)
            {
                deleted += await _DeleteKeysAsync([.. keys], isCluster).ConfigureAwait(false);
            }
        }

        return deleted;
    }

    /// <inheritdoc />
    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(tag);
        cancellationToken.ThrowIfCancellationRequested();

        // O(1) Family-2 logical invalidation: raise-only durable write of the per-tag timestamp marker (one key per
        // tag, so this works on Redis Cluster) plus the local stamp. Reads compare a tagged entry's CreatedAt
        // against it. Routed through WriteTagMarkerAsync so the live path and auto-recovery replay share one
        // raise-only write (on the live path `now` is monotonic, so behavior is unchanged).
        return WriteTagMarkerAsync(tag, timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // O(1) Family-2 logical clear: raise-only durable write of the single reserved clear-generation marker.
        // Entries born before it read as misses (direct reads) or demote to fail-safe reserves (coordinator);
        // physical reserves survive (unlike FlushAsync). Compared on every read, tagged or not.
        return WriteClearMarkerAsync(timeProvider.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public void SeedTagMarker(string tag, DateTimeOffset invalidatedAt)
    {
        Argument.IsNotNullOrEmpty(tag);

        // A backplane peer learned this tag was invalidated at invalidatedAt; stamp our local copy fresh so the
        // next read observes it without waiting for the refresh window (mirrors RemoveByTagAsync's own-read stamp).
        // Raise-only: never lower a marker we already know to be newer.
        var ms = RedisCacheEntryFrame.ToUnixTimeMilliseconds(invalidatedAt.UtcDateTime);
        var ticks = _StopwatchTicks();

        _markerCache.AddOrUpdate(
            tag,
            static (_, state) => (state.ms, state.ticks),
            static (_, existing, state) =>
                existing.MarkerMs >= state.ms ? (existing.MarkerMs, state.ticks) : (state.ms, state.ticks),
            (ms, ticks)
        );
    }

    /// <inheritdoc />
    public void SeedClearMarker(DateTimeOffset invalidatedAt)
    {
        var ms = RedisCacheEntryFrame.ToUnixTimeMilliseconds(invalidatedAt.UtcDateTime);

        // Raise-only CAS: a stale push must not lower a newer clear generation this node already observed.
        long current;
        while ((current = Interlocked.Read(ref _clearMarkerMs)) < ms)
        {
            if (Interlocked.CompareExchange(ref _clearMarkerMs, ms, current) == current)
            {
                break;
            }
        }

        Interlocked.Exchange(ref _clearMarkerFetchedTicks, _StopwatchTicks());
    }

    /// <inheritdoc />
    public void SeedRemoveMarker(DateTimeOffset invalidatedAt)
    {
        var ms = RedisCacheEntryFrame.ToUnixTimeMilliseconds(invalidatedAt.UtcDateTime);

        // Raise-only CAS: a stale push must not lower a newer remove generation this node already observed.
        long current;
        while ((current = Interlocked.Read(ref _removeMarkerMs)) < ms)
        {
            if (Interlocked.CompareExchange(ref _removeMarkerMs, ms, current) == current)
            {
                break;
            }
        }

        Interlocked.Exchange(ref _removeMarkerFetchedTicks, _StopwatchTicks());
    }

    /// <inheritdoc />
    public async ValueTask WriteTagMarkerAsync(
        string tag,
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(tag);
        cancellationToken.ThrowIfCancellationRequested();

        var ms = RedisCacheEntryFrame.ToUnixTimeMilliseconds(invalidatedAt.UtcDateTime);
        await _RaiseDurableMarkerAsync((RedisKey)_GetTagMarkerKey(tag), ms, cancellationToken).ConfigureAwait(false);
        SeedTagMarker(tag, invalidatedAt);
    }

    /// <inheritdoc />
    public async ValueTask WriteClearMarkerAsync(
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ms = RedisCacheEntryFrame.ToUnixTimeMilliseconds(invalidatedAt.UtcDateTime);
        await _RaiseDurableMarkerAsync((RedisKey)_GetClearMarkerKey(), ms, cancellationToken).ConfigureAwait(false);
        SeedClearMarker(invalidatedAt);
    }

    /// <inheritdoc />
    public async ValueTask WriteRemoveMarkerAsync(
        DateTimeOffset invalidatedAt,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ms = RedisCacheEntryFrame.ToUnixTimeMilliseconds(invalidatedAt.UtcDateTime);
        await _RaiseDurableMarkerAsync((RedisKey)_GetRemoveMarkerKey(), ms, cancellationToken).ConfigureAwait(false);
        SeedRemoveMarker(invalidatedAt);
    }

    /// <summary>
    /// Raise-only durable marker write (set-if-higher): a recovery replay may carry an older timestamp than a bump
    /// that already landed, so it must never lower the stored marker. Reuses the shared set-if-higher Lua script
    /// (markers carry no expiry, so the optional pexpire is skipped via an empty <c>expires</c> arg).
    /// </summary>
    private async ValueTask _RaiseDurableMarkerAsync(RedisKey markerKey, long ms, CancellationToken cancellationToken)
    {
        await scriptsLoader
            .EvaluateAsync(
                _database,
                SetIfHigherScriptDefinition.Instance,
                new
                {
                    key = markerKey,
                    value = (RedisValue)ms.ToString(CultureInfo.InvariantCulture),
                    expires = RedisValue.EmptyString,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<long> SetRemoveAsync<T>(
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
        var redisValues = new List<RedisValue>();

        if (value is string stringValue)
        {
            redisValues.Add(_ToRedisValue(stringValue));
        }
        else
        {
            redisValues.AddRange(value.Where(v => v is not null).Select(_ToRedisValue));
        }

        await _RemoveExpiredListValuesAsync(key).ConfigureAwait(false);

        if (redisValues.Count is 0)
        {
            return 0;
        }

        var removed = await _database.SortedSetRemoveAsync(key, [.. redisValues]).ConfigureAwait(false);

        if (removed > 0)
        {
            await _SetListExpirationAsync(key).ConfigureAwait(false);
        }

        return removed;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Logical flush (FusionCache Clear(false) parity): a physical wipe of a distributed Redis is unsafe —
        // FLUSHDB only affects the addressed node on a Redis Cluster (and destroys co-tenant data on a shared
        // instance), and an O(N) prefix SCAN+UNLINK does not span shards atomically. Instead bump the reserved
        // remove-generation marker (raise-only durable write): every entry born before it reads as a hard miss with
        // NO fail-safe reserve (distinct from ClearAsync, which preserves reserves). One marker key — cluster-safe;
        // physical memory is reclaimed by each entry's TTL, so GetCountAsync may still count logically-removed
        // entries until they age out.
        return WriteRemoveMarkerAsync(timeProvider.GetUtcNow(), cancellationToken);
    }

    #endregion

    #region Helpers

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : string.Concat(_keyPrefix, key);
    }

    private string _GetTagMarkerKey(string tag) => string.Concat(_keyPrefix, _TagMarkerNamespace, tag);

    private string _GetClearMarkerKey() => string.Concat(_keyPrefix, _ClearMarkerSuffix);

    private string _GetRemoveMarkerKey() => string.Concat(_keyPrefix, _RemoveMarkerSuffix);

    private static long _StopwatchTicks() => Stopwatch.GetTimestamp();

    private bool _MarkerIsFresh(long fetchedTicks)
    {
        if (fetchedTicks == long.MinValue)
        {
            return false;
        }

        var elapsed = Stopwatch.GetElapsedTime(fetchedTicks);
        return elapsed < options.TagMarkerRefreshWindow;
    }

    /// <summary>
    /// Resolves the newest invalidation marker applicable to an entry — the max of the global clear-generation
    /// marker and every per-tag marker the entry carries — using the process-local marker cache and refreshing
    /// stale/missing markers from Redis in a single pipelined MGET. Untagged entries resolve only the clear
    /// marker. Returns <see langword="null"/> when no marker applies (entry is never invalidated).
    /// </summary>
    private async ValueTask<DateTime?> _ResolveNewestMarkerAsync(IReadOnlyCollection<string>? tags)
    {
        // Direct reads: the remove-generation marker (logical FlushAsync) also makes an entry a miss, alongside the
        // clear- and per-tag markers (all compared on every read, tagged or not).
        var newestMs = await _ResolveClearAndTagMarkerMsAsync(tags).ConfigureAwait(false);

        var removeMs = await _ResolveRemoveMarkerAsync().ConfigureAwait(false);

        if (removeMs > newestMs)
        {
            newestMs = removeMs;
        }

        return newestMs == _MarkerAbsent ? null : RedisCacheEntryFrame.FromUnixTimeMilliseconds(newestMs);
    }

    /// <summary>
    /// Resolves the newest CLEAR-generation + per-tag marker, EXCLUDING the remove-generation marker. The
    /// coordinator demote path uses this: a clear/tag-invalidated entry demotes to a still-servable fail-safe
    /// reserve, whereas the remove marker (resolved separately by the caller) is a hard miss with no reserve — so
    /// the coordinator resolves the remove marker once for its hard-miss check and this for the demote check,
    /// instead of resolving the remove marker twice.
    /// </summary>
    private async ValueTask<DateTime?> _ResolveClearAndTagMarkerAsync(IReadOnlyCollection<string>? tags)
    {
        var newestMs = await _ResolveClearAndTagMarkerMsAsync(tags).ConfigureAwait(false);
        return newestMs == _MarkerAbsent ? null : RedisCacheEntryFrame.FromUnixTimeMilliseconds(newestMs);
    }

    private async ValueTask<long> _ResolveClearAndTagMarkerMsAsync(IReadOnlyCollection<string>? tags)
    {
        var newestMs = await _ResolveClearMarkerAsync().ConfigureAwait(false);

        if (tags is { Count: > 0 })
        {
            var tagMs = await _ResolveTagMarkersAsync(tags).ConfigureAwait(false);

            if (tagMs > newestMs)
            {
                newestMs = tagMs;
            }
        }

        return newestMs;
    }

    private async ValueTask<long> _ResolveClearMarkerAsync()
    {
        var fetchedTicks = Interlocked.Read(ref _clearMarkerFetchedTicks);

        if (_MarkerIsFresh(fetchedTicks))
        {
            return Interlocked.Read(ref _clearMarkerMs);
        }

        var value = await _database
            .StringGetAsync((RedisKey)_GetClearMarkerKey(), options.ReadMode)
            .ConfigureAwait(false);
        var ms = _ParseMarkerMs(value);
        Interlocked.Exchange(ref _clearMarkerMs, ms);
        Interlocked.Exchange(ref _clearMarkerFetchedTicks, _StopwatchTicks());
        return ms;
    }

    private async ValueTask<long> _ResolveRemoveMarkerAsync()
    {
        var fetchedTicks = Interlocked.Read(ref _removeMarkerFetchedTicks);

        if (_MarkerIsFresh(fetchedTicks))
        {
            return Interlocked.Read(ref _removeMarkerMs);
        }

        var value = await _database
            .StringGetAsync((RedisKey)_GetRemoveMarkerKey(), options.ReadMode)
            .ConfigureAwait(false);
        var ms = _ParseMarkerMs(value);
        Interlocked.Exchange(ref _removeMarkerMs, ms);
        Interlocked.Exchange(ref _removeMarkerFetchedTicks, _StopwatchTicks());
        return ms;
    }

    private async ValueTask<long> _ResolveTagMarkersAsync(IReadOnlyCollection<string> tags)
    {
        // Collect tags whose cached marker is stale/missing; batch-fetch them in one MGET.
        List<string>? stale = null;
        var newestMs = _MarkerAbsent;

        foreach (var tag in tags)
        {
            if (_markerCache.TryGetValue(tag, out var cached) && _MarkerIsFresh(cached.FetchedTicks))
            {
                if (cached.MarkerMs > newestMs)
                {
                    newestMs = cached.MarkerMs;
                }
            }
            else
            {
                (stale ??= []).Add(tag);
            }
        }

        if (stale is not null)
        {
            var markerKeys = new RedisKey[stale.Count];

            for (var i = 0; i < stale.Count; i++)
            {
                markerKeys[i] = _GetTagMarkerKey(stale[i]);
            }

            var values = await _database.StringGetAsync(markerKeys, options.ReadMode).ConfigureAwait(false);
            var fetchedTicks = _StopwatchTicks();

            for (var i = 0; i < stale.Count; i++)
            {
                var ms = _ParseMarkerMs(values[i]);
                _markerCache[stale[i]] = (ms, fetchedTicks);

                if (ms > newestMs)
                {
                    newestMs = ms;
                }
            }
        }

        return newestMs;
    }

    private static long _ParseMarkerMs(RedisValue value) =>
        RedisCacheEntryFrame.TryParseMarkerMs(value) ?? _MarkerAbsent;

    /// <summary>Encodes a value to its bare wire bytes (the value codec) without any envelope framing.</summary>
    /// <remarks>
    /// This output must stay frame-free: the CAS scripts (<see cref="CacheRemoveIfEqualScriptDefinition"/>,
    /// <see cref="CacheReplaceIfEqualScriptDefinition"/>) pass it as the bare <c>expected</c> operand and compare
    /// it against the framed value's sliced value segment. Adding a header here would break that comparison.
    /// </remarks>
    private RedisValue _ToRedisValue<T>(T? value)
    {
        if (value is null)
        {
            return _NullValue;
        }

        if (value is string s)
        {
            return s;
        }

        // byte[] is the cache's native wire format — it IS the serialized form, so store it verbatim and never
        // route it through the ISerializer (a JSON serializer would base64-bloat it, and it would diverge from the
        // IBufferCache raw path). This sits alongside the string fast path; both feed the frame value segment and
        // the CAS `expected` operand from this single choke point, so encode/decode/compare stay consistent.
        if (value is byte[] bytes)
        {
            return bytes;
        }

        return serializer.SerializeToBytes(value);
    }

    private T? _FromRedisValue<T>(RedisValue redisValue, bool treatNullSentinel = true)
    {
        if (!redisValue.HasValue)
        {
            return default;
        }

        if (treatNullSentinel && redisValue == _NullValue)
        {
            return default;
        }

        if (typeof(T) == typeof(string))
        {
            return (T?)(object?)redisValue.ToString();
        }

        // Native byte[]: return the wire bytes verbatim (mirror of the string fast path in _ToRedisValue).
        if (typeof(T) == typeof(byte[]))
        {
            return (T?)(object?)(byte[]?)redisValue;
        }

        return serializer.Deserialize<T>((byte[])redisValue!);
    }

    private async ValueTask<CacheValue<T>> _RedisValueToCacheValueAsync<T>(
        RedisKey redisKey,
        RedisValue redisValue,
        bool rearm = true
    )
    {
        if (!redisValue.HasValue)
        {
            return CacheValue<T>.NoValue;
        }

        try
        {
            var frame = RedisCacheEntryFrame.Decode(redisValue);

            if (frame.IsFramed)
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;

                if (
                    _IsExpired(frame.PhysicalExpiresAt, now)
                    || (!frame.SlidingExpiration.HasValue && _IsExpired(frame.LogicalExpiresAt, now))
                )
                {
                    return CacheValue<T>.NoValue;
                }

                // Family-2 logical tag/clear invalidation: a direct read of an invalidated entry is a miss. The
                // physically present reserve is left in place so the coordinator can still serve it stale.
                var newestMarker = await _ResolveNewestMarkerAsync(frame.Tags).ConfigureAwait(false);

                if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker))
                {
                    return CacheValue<T>.NoValue;
                }

                // Bulk reads (GetAllAsync) skip re-arm: re-arming here issues one KeyTimeToLive + KeyExpire per
                // sliding key, turning a single batched StringGet into N sequential round trips. Single-key reads
                // still re-arm so idle-window semantics hold on the hot path.
                if (rearm)
                {
                    await _TryRearmSlidingEntryAsync(redisKey, frame, now).ConfigureAwait(false);
                }

                if (frame.IsNull)
                {
                    return CacheValue<T>.Null;
                }

                var framedValue = _DeserializeValueSegment<T>(frame.ValueSegment);
                return new CacheValue<T>(framedValue, true);
            }

            if (redisValue == _NullValue)
            {
                return CacheValue<T>.Null;
            }

            var value = _FromRedisValue<T>(redisValue);
            return new CacheValue<T>(value, true);
        }
        catch (Exception e)
        {
            _logger.LogDeserializationFailed(e, redisValue.Length(), typeof(T).FullName);
            throw;
        }
    }

    /// <summary>
    /// Zero-intermediate-copy buffer read. Mirrors <see cref="_RedisValueToCacheValueAsync{T}"/> exactly (expiry,
    /// Family-2 tag/clear invalidation, single-key sliding re-arm), but writes the validated payload straight into
    /// <paramref name="destination"/> instead of deserializing it — so the generic path's intermediate
    /// <c>byte[]</c> materialization is skipped and only one copy (frame slice -> caller buffer) is paid.
    /// </summary>
    public async ValueTask<bool> TryGetToAsync(
        string key,
        IBufferWriter<byte> destination,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = _GetKey(key);
        var redisValue = await _database.StringGetAsync(redisKey, options.ReadMode).ConfigureAwait(false);

        if (!redisValue.HasValue)
        {
            return false;
        }

        try
        {
            var frame = RedisCacheEntryFrame.Decode(redisValue);

            if (frame.IsFramed)
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;

                if (
                    _IsExpired(frame.PhysicalExpiresAt, now)
                    || (!frame.SlidingExpiration.HasValue && _IsExpired(frame.LogicalExpiresAt, now))
                )
                {
                    return false;
                }

                // Family-2 logical tag/clear invalidation: a direct read of an invalidated entry is a miss.
                var newestMarker = await _ResolveNewestMarkerAsync(frame.Tags).ConfigureAwait(false);

                if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker))
                {
                    return false;
                }

                // Single-key reads re-arm the sliding idle window on the hot path.
                await _TryRearmSlidingEntryAsync(redisKey, frame, now).ConfigureAwait(false);

                // Parity with the byte[] fallback: a null-sentinel hit reads as a miss for the buffer path.
                if (frame.IsNull)
                {
                    return false;
                }

                // The single copy: framed value slice -> caller-provided buffer.
                destination.Write(frame.ValueSegment.Span);
                return true;
            }

            // Legacy non-framed entry: the whole stored value is the raw payload.
            if (redisValue == _NullValue)
            {
                return false;
            }

            destination.Write((byte[])redisValue!);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogDeserializationFailed(e, redisValue.Length(), typeof(byte[]).FullName);
            throw;
        }
    }

    /// <summary>
    /// Zero-intermediate-copy buffer write. Mirrors <see cref="_SetEntryCoreAsync{T}"/> + the stamping in
    /// <c>FactoryCacheStoreExtensions.UpsertEntryAsync</c>: validates options, computes the fresh-write stamps once
    /// via <see cref="CacheEntryStamps.Compute"/>, then frames the <see cref="ReadOnlySequence{T}"/> payload
    /// directly into the wire buffer — skipping the generic path's intermediate <c>byte[]</c> materialization.
    /// Behavior (expiry, sliding, CreatedAt stamping, tag invalidation) is identical to the generic upsert.
    /// </summary>
    public async ValueTask UpsertRawAsync(
        string key,
        ReadOnlySequence<byte> value,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // Validate then stamp, matching UpsertEntryAsync exactly: Compute does the stamp math but does NOT
        // validate, so ValidateOptions (which also validates Tags) runs first at this single choke point.
        CacheEntryStamps.ValidateOptions(options);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stamps = CacheEntryStamps.Compute(options, now);
        var redisKey = _GetKey(key);

        var expiresIn = (
            options.SlidingExpiration is null ? stamps.PhysicalExpiresAt : stamps.LogicalExpiresAt
        ).Subtract(now);

        if (expiresIn <= TimeSpan.Zero)
        {
            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return;
        }

        // Frame the sequence synchronously (Encode consumes it before the StringSet await), so the
        // payload is fully materialized into the wire buffer before any yield — no await precedes Encode.
        var redisValue = RedisCacheEntryFrame.Encode(
            value,
            isNull: false,
            stamps.LogicalExpiresAt,
            stamps.PhysicalExpiresAt,
            options.SlidingExpiration,
            stamps.EagerRefreshAt,
            etag: null,
            lastModifiedAt: null,
            options.Tags,
            createdAt: stamps.CreatedAt
        );

        await _database.StringSetAsync(redisKey, redisValue, expiresIn).ConfigureAwait(false);
    }

    private CacheValue<ICollection<T>> _RedisValuesToCacheValue<T>(RedisValue[] redisValues)
    {
        var result = new List<T>();

        foreach (var redisValue in redisValues)
        {
            if (!redisValue.HasValue || redisValue == _NullValue)
            {
                continue;
            }

            try
            {
                var value = _FromRedisValue<T>(redisValue);

                if (value is not null)
                {
                    result.Add(value);
                }
            }
            catch (Exception e)
            {
                _logger.LogDeserializationFailed(e, redisValue.Length(), typeof(T).FullName);
                throw;
            }
        }

        return new CacheValue<ICollection<T>>(result, result.Count > 0);
    }

    private async Task<bool> _SetInternalAsync<T>(
        string key,
        T? value,
        TimeSpan? expiresIn = null,
        When when = When.Always
    )
    {
        if (expiresIn is { Ticks: <= 0 })
        {
            await _database.KeyDeleteAsync(key).ConfigureAwait(false);
            return false;
        }

        expiresIn = _NormalizeExpiration(expiresIn);
        var redisValue = _ToFramedRedisValue(value, expiresIn, timeProvider.GetUtcNow());

        return await _database.StringSetAsync(key, redisValue, expiresIn, when).ConfigureAwait(false);
    }

    private RedisValue _ToFramedRedisValue<T>(T? value, TimeSpan? expiresIn, DateTimeOffset now)
    {
        var expiresAt = _GetExpirationDateTime(expiresIn, now);
        var valueSegment = value is null ? RedisValue.EmptyString : _ToRedisValue(value);

        return RedisCacheEntryFrame.Encode(
            valueSegment,
            isNull: value is null,
            logicalExpiresAt: expiresAt,
            physicalExpiresAt: expiresAt,
            slidingExpiration: null,
            // Direct upsert path stamps the birth time so a prior tag/clear marker does not invalidate the new
            // value (Family-2 version-pinning compares CreatedAt against the newest applicable marker).
            createdAt: now.UtcDateTime
        );
    }

    ValueTask<CacheStoreEntry<T>> IFactoryCacheStore.TryGetEntryAsync<T>(
        string key,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return _TryGetEntryAsync<T>(key);
    }

    // Non-async forwarder: `in` parameters are not allowed on async methods, so copy the descriptor by value.
    ValueTask<bool> IFactoryCacheStore.SetEntryAsync<T>(
        string key,
        in CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken
    )
        where T : default
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return _SetEntryCoreAsync(key, entry);
    }

    private async ValueTask<bool> _SetEntryCoreAsync<T>(string key, CacheStoreEntryWrite<T> entry)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresIn = (entry.SlidingExpiration is null ? entry.PhysicalExpiresAt : entry.LogicalExpiresAt).Subtract(
            now
        );
        var redisKey = _GetKey(key);

        if (expiresIn <= TimeSpan.Zero)
        {
            if (entry.ExpectedConcurrencyStamp is { } expiredExpectedStamp)
            {
                if (!_TryDecodeConcurrencyStamp(expiredExpectedStamp, out var expiredExpectedValue))
                {
                    return false;
                }

                var current = await _database.StringGetAsync(redisKey).ConfigureAwait(false);

                if (current != expiredExpectedValue)
                {
                    return false;
                }
            }

            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return true;
        }

        var valueSegment = entry.IsNull ? RedisValue.EmptyString : _ToRedisValue(entry.Value);
        var redisValue = RedisCacheEntryFrame.Encode(
            valueSegment,
            entry.IsNull,
            entry.LogicalExpiresAt,
            entry.PhysicalExpiresAt,
            entry.SlidingExpiration,
            entry.EagerRefreshAt,
            entry.ETag,
            entry.LastModifiedAt,
            entry.Tags,
            createdAt: entry.CreatedAt
        );

        var hasExpectedStamp = entry.ExpectedConcurrencyStamp is not null;
        var expectedValue = RedisValue.EmptyString;

        if (hasExpectedStamp && !_TryDecodeConcurrencyStamp(entry.ExpectedConcurrencyStamp!, out expectedValue))
        {
            return false;
        }

        // Tags + CreatedAt now ride inside the frame; the write is a plain SET (Family-2 invalidation reads the
        // markers, not a reverse index — so there is no per-tag index to reconcile and no cluster restriction).
        if (!hasExpectedStamp)
        {
            await _database.StringSetAsync(redisKey, redisValue, expiresIn).ConfigureAwait(false);
            return true;
        }

        // CAS write: one atomic script verifies ExpectedConcurrencyStamp matches the live value before the SET,
        // so a late factory cannot clobber a concurrent writer or resurrect a removed entry.
        var keyTtlMs = _ToPositiveMilliseconds(expiresIn);

        var result = await scriptsLoader
            .EvaluateAsync(
                _database,
                CacheTaggedSetScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)redisKey,
                    value = (RedisValue)redisValue,
                    expectedValue,
                    keyTtlMs,
                },
                CancellationToken.None
            )
            .ConfigureAwait(false);

        return (int)result == 1;
    }

    private static string _ToConcurrencyStamp(RedisValue value) =>
        string.Concat("b64:", Convert.ToBase64String((byte[])value!));

    private static bool _TryDecodeConcurrencyStamp(string stamp, out RedisValue value)
    {
        const string prefix = "b64:";

        if (!stamp.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = RedisValue.Null;
            return false;
        }

        try
        {
            value = Convert.FromBase64String(stamp[prefix.Length..]);
            return true;
        }
        catch (FormatException)
        {
            value = RedisValue.Null;
            return false;
        }
    }

    private static long _ToPositiveMilliseconds(TimeSpan duration)
    {
        var milliseconds = (long)Math.Ceiling(duration.TotalMilliseconds);
        return milliseconds < 1 ? 1 : milliseconds;
    }

    async ValueTask IFactoryCacheStore.TryRearmSlidingAsync(
        string key,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        // No embedded logical lower bound on this path (the coordinator drops it for sliding entries, since it
        // goes stale after a metadata-only re-arm), so the throttle reads the live key TTL.
        await _RearmSlidingTtlAsync(
                _GetKey(key),
                slidingExpiration,
                physicalExpiresAt,
                now,
                embeddedLogicalExpiresAt: null
            )
            .ConfigureAwait(false);
    }

    private async ValueTask<CacheStoreEntry<T>> _TryGetEntryAsync<T>(string key)
    {
        var redisValue = await _database.StringGetAsync(_GetKey(key), options.ReadMode).ConfigureAwait(false);

        if (!redisValue.HasValue)
        {
            return CacheStoreEntry<T>.NotFound;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var concurrencyStamp = _ToConcurrencyStamp(redisValue);
        var frame = RedisCacheEntryFrame.Decode(redisValue);

        if (frame.IsFramed)
        {
            if (_IsExpired(frame.PhysicalExpiresAt, now))
            {
                return CacheStoreEntry<T>.NotFound;
            }

            var value = frame.IsNull ? default : _DeserializeValueSegment<T>(frame.ValueSegment);

            // For sliding entries the embedded logical timestamp goes stale after a metadata-only re-arm
            // (KeyExpire bumps the key TTL but does not rewrite the frame), so reporting it would make the
            // coordinator treat a live, recently-touched key as logically expired and spuriously re-run the
            // factory. Drop it and let freshness rely on key existence + the physical cap, mirroring the
            // other sliding read paths (_RedisValueToCacheValueAsync, _RedisValueIsLogicallyPresentAsync).
            var logicalExpiresAt = frame.SlidingExpiration.HasValue ? null : frame.LogicalExpiresAt;
            var slidingExpiration = frame.SlidingExpiration;

            // Remove-generation (logical FlushAsync): a hard miss with NO fail-safe reserve — distinct from the
            // clear/tag markers below, which demote to a still-servable reserve. Checked first so a removed entry
            // never surfaces as a stale candidate the coordinator could serve.
            var removeMs = await _ResolveRemoveMarkerAsync().ConfigureAwait(false);
            var removeMarker =
                removeMs == _MarkerAbsent ? (DateTime?)null : RedisCacheEntryFrame.FromUnixTimeMilliseconds(removeMs);

            if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, removeMarker))
            {
                return CacheStoreEntry<T>.NotFound;
            }

            // Family-2 logical tag/clear invalidation: demote the entry to logically-expired-but-physically-
            // present so the coordinator re-runs the factory but can still serve this reserve stale on failure.
            // Uses the clear+tag resolver (NOT _ResolveNewestMarkerAsync) so the remove marker — already resolved
            // for the hard-miss check above — is not resolved a second time on this path.
            var newestMarker = await _ResolveClearAndTagMarkerAsync(frame.Tags).ConfigureAwait(false);

            if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker))
            {
                logicalExpiresAt = logicalExpiresAt.HasValue && logicalExpiresAt.Value < now ? logicalExpiresAt : now;
                slidingExpiration = null;
            }

            return new CacheStoreEntry<T>(
                Found: true,
                IsNull: frame.IsNull,
                Value: value,
                LogicalExpiresAt: logicalExpiresAt,
                PhysicalExpiresAt: frame.PhysicalExpiresAt,
                SlidingExpiration: slidingExpiration
            )
            {
                EagerRefreshAt = frame.EagerRefreshAt,
                ETag = frame.ETag,
                LastModifiedAt = frame.LastModifiedAt,
                CreatedAt = frame.CreatedAt,
                Tags = frame.Tags,
                ConcurrencyStamp = concurrencyStamp,
            };
        }

        if (redisValue == _NullValue)
        {
            return new CacheStoreEntry<T>(
                Found: true,
                IsNull: true,
                Value: default,
                LogicalExpiresAt: null,
                PhysicalExpiresAt: null,
                SlidingExpiration: null
            )
            {
                ConcurrencyStamp = concurrencyStamp,
            };
        }

        return new CacheStoreEntry<T>(
            Found: true,
            IsNull: false,
            Value: _FromRedisValue<T>(redisValue),
            LogicalExpiresAt: null,
            PhysicalExpiresAt: null,
            SlidingExpiration: null
        )
        {
            ConcurrencyStamp = concurrencyStamp,
        };
    }

    private async ValueTask<bool> _RedisValueIsLogicallyPresentAsync(RedisValue redisValue)
    {
        if (!redisValue.HasValue)
        {
            return false;
        }

        var frame = RedisCacheEntryFrame.Decode(redisValue);

        if (!frame.IsFramed)
        {
            return true;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (
            _IsExpired(frame.PhysicalExpiresAt, now)
            || (!frame.SlidingExpiration.HasValue && _IsExpired(frame.LogicalExpiresAt, now))
        )
        {
            return false;
        }

        // Family-2: a tag/clear-invalidated entry is logically absent for direct reads (Exists), even though its
        // physical reserve survives for fail-safe serving.
        var newestMarker = await _ResolveNewestMarkerAsync(frame.Tags).ConfigureAwait(false);
        return !CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker);
    }

    private ValueTask _TryRearmSlidingEntryAsync(
        RedisKey redisKey,
        RedisCacheEntryFrame.DecodedFrame frame,
        DateTime now
    )
    {
        if (
            frame.SlidingExpiration is not { } slidingExpiration
            || frame.PhysicalExpiresAt is not { } physicalExpiresAt
        )
        {
            return ValueTask.CompletedTask;
        }

        // The frame's embedded logical is a lower bound on the live key TTL (a metadata-only re-arm only ever
        // pushes the TTL out, never the frame), so the shared helper can use it to skip the KeyTimeToLive probe
        // when at least half the window still remains.
        return _RearmSlidingTtlAsync(redisKey, slidingExpiration, physicalExpiresAt, now, frame.LogicalExpiresAt);
    }

    private async ValueTask _RearmSlidingTtlAsync(
        RedisKey redisKey,
        TimeSpan slidingExpiration,
        DateTime physicalExpiresAt,
        DateTime now,
        DateTime? embeddedLogicalExpiresAt
    )
    {
        var remainingToCap = physicalExpiresAt - now;

        if (remainingToCap <= TimeSpan.Zero)
        {
            return;
        }

        // Re-arm once roughly half the idle window has elapsed. Exact integer halving (no lossy double cast).
        var rearmThreshold = TimeSpan.FromTicks(slidingExpiration.Ticks / 2);

        // Fast path: when the (lower-bound) embedded logical already proves more than half the window remains,
        // the live TTL — which can only be larger — does too, so skip both the re-arm and its round trip.
        if (embeddedLogicalExpiresAt is { } embeddedLogical && embeddedLogical - now > rearmThreshold)
        {
            return;
        }

        try
        {
            // The TTL probe is part of the best-effort re-arm: keep it inside the catch so a transient Redis
            // error during the probe degrades to "skip the re-arm" instead of failing a value read that the
            // caller has already satisfied.
            var ttl = await _database.KeyTimeToLiveAsync(redisKey).ConfigureAwait(false);

            if (ttl.HasValue && ttl.Value > rearmThreshold)
            {
                return;
            }

            var expiresIn = _Min(slidingExpiration, remainingToCap);
            await _database.KeyExpireAsync(redisKey, expiresIn).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogSlidingExpirationRearmFailed(exception, redisKey.ToString());
            }
        }
    }

    private static bool _IsExpired(DateTime? expiresAt, DateTime now) => expiresAt.HasValue && expiresAt.Value <= now;

    private static TimeSpan _Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;

    private T? _DeserializeValueSegment<T>(ReadOnlyMemory<byte> segment)
    {
        // Deserialize the framed value segment off the raw bytes; an empty non-null segment yields the
        // empty value (e.g. "") rather than default, since the frame already proved the value is present.
        if (typeof(T) == typeof(string))
        {
            return (T?)(object)Encoding.UTF8.GetString(segment.Span);
        }

        // Native byte[]: the framed value segment IS the payload — copy it out verbatim, no serializer. (The
        // IBufferCache.TryGetToAsync path avoids even this copy by writing the segment straight to the caller.)
        if (typeof(T) == typeof(byte[]))
        {
            return (T?)(object)segment.ToArray();
        }

        // Avoid copying segment into a new byte[] via ToArray(). If the backing store is a managed array
        // (the common path — RedisValue is decoded into byte[] by StackExchange.Redis), wrap it directly
        // in a non-writable MemoryStream at the right offset and length. Fall back to the copy path for
        // exotic memory types where TryGetArray returns false (e.g. NativeMemoryManager).
        if (MemoryMarshal.TryGetArray(segment, out var arraySegment))
        {
            var stream = new MemoryStream(
                arraySegment.Array!,
                arraySegment.Offset,
                arraySegment.Count,
                writable: false
            );
            return serializer.Deserialize<T>(stream);
        }

        return serializer.Deserialize<T>(segment.ToArray());
    }

    private async Task<int> _SetAllInternalAsync(KeyValuePair<RedisKey, RedisValue>[] pairs, TimeSpan? expiresIn)
    {
        if (expiresIn.HasValue)
        {
            if (_SupportsMsetexCommand())
            {
                var success = await _database
                    .StringSetAsync(pairs, When.Always, new Expiration(expiresIn.Value))
                    .ConfigureAwait(false);
                return success ? pairs.Length : 0;
            }

            // Fallback for Redis < 8.4: pipelined individual SET commands with expiration
            var tasks = new List<Task<bool>>(pairs.Length);

            foreach (var pair in pairs)
            {
                tasks.Add(_database.StringSetAsync(pair.Key, pair.Value, expiresIn, When.Always));
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.Count(r => r);
        }

        var msetSuccess = await _database.StringSetAsync(pairs).ConfigureAwait(false);
        return msetSuccess ? pairs.Length : 0;
    }

    private bool _SupportsMsetexCommand()
    {
        if (_supportsMsetExChecked)
        {
            return _supportsMsetEx;
        }

        // Redis 8.4 RC1 is internally versioned as 8.3.224
        var minVersion = new Version(8, 3, 224);
        var endpoints = options.ConnectionMultiplexer.GetEndPoints();

        if (endpoints.Length is 0)
        {
            return false;
        }

        var foundConnectedPrimary = false;

        foreach (var endpoint in endpoints)
        {
            var server = options.ConnectionMultiplexer.GetServer(endpoint);

            if (server is { IsConnected: true, IsReplica: false })
            {
                foundConnectedPrimary = true;

                if (server.Version < minVersion)
                {
                    _supportsMsetEx = false;
                    _supportsMsetExChecked = true;
                    return false;
                }
            }
        }

        if (foundConnectedPrimary)
        {
            _supportsMsetEx = true;
            _supportsMsetExChecked = true;
            return true;
        }

        return false;
    }

    private TimeSpan? _NormalizeExpiration(TimeSpan? expiresIn)
    {
        if (!expiresIn.HasValue || expiresIn.Value == TimeSpan.MaxValue)
        {
            return null;
        }

        var expiresAt = timeProvider.GetUtcNow().UtcDateTime.SafeAdd(expiresIn.Value);
        return expiresAt == DateTime.MaxValue ? null : expiresIn;
    }

    private static long? _GetExpirationMilliseconds(TimeSpan? expiresIn, DateTimeOffset now)
    {
        if (!expiresIn.HasValue || expiresIn.Value == TimeSpan.MaxValue)
        {
            return null;
        }

        var expiresAt = now.UtcDateTime.SafeAdd(expiresIn.Value);

        if (expiresAt == DateTime.MaxValue)
        {
            return null;
        }

        return (long)expiresIn.Value.TotalMilliseconds;
    }

    private static DateTime? _GetExpirationDateTime(TimeSpan? expiresIn, DateTimeOffset now)
    {
        if (!expiresIn.HasValue || expiresIn.Value == TimeSpan.MaxValue)
        {
            return null;
        }

        var expiresAt = now.UtcDateTime.SafeAdd(expiresIn.Value);
        return expiresAt == DateTime.MaxValue ? null : expiresAt;
    }

    private async Task _SetListExpirationAsync(string key)
    {
        var items = await _database
            .SortedSetRangeByRankWithScoresAsync(key, 0, 0, order: Order.Descending)
            .ConfigureAwait(false);

        if (items.Length is 0)
        {
            return;
        }

        var highestExpirationInMs = (long)items.Single().Score;

        if (highestExpirationInMs >= RedisCacheEntryFrame.MaxUnixEpochMilliseconds)
        {
            await _database.KeyPersistAsync(key).ConfigureAwait(false);
            return;
        }

        var furthestExpirationUtc = DateTimeOffset.FromUnixTimeMilliseconds(highestExpirationInMs);
        var expiresIn = furthestExpirationUtc - timeProvider.GetUtcNow();

        await _database.KeyExpireAsync(key, expiresIn).ConfigureAwait(false);
    }

    private async Task _RemoveExpiredListValuesAsync(string key)
    {
        var expiredValues = await _database
            .SortedSetRemoveRangeByScoreAsync(key, 0, timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
            .ConfigureAwait(false);

        if (expiredValues > 0)
        {
            _logger.LogExpiredValuesRemoved(expiredValues, key);
        }
    }

    private async Task<long> _DeleteKeysAsync(RedisKey[] keys, bool isCluster)
    {
        long deleted = 0;

        if (isCluster)
        {
            var slotBuckets = _GroupBySlot(keys, static key => key);

            foreach (var bucket in slotBuckets.Values)
            {
                var count = await _database.KeyDeleteAsync([.. bucket]).ConfigureAwait(false);
                deleted += count;
            }
        }
        else
        {
            deleted = await _database.KeyDeleteAsync(keys).ConfigureAwait(false);
        }

        return deleted;
    }

    // Groups items into per-Redis-cluster-hash-slot buckets, preserving input order within each bucket. Used by
    // the cluster paths of the batch operations to split a multi-key command into one command per slot (a single
    // MGET/MSET/DEL/UNLINK cannot span slots on Redis Cluster). Manual Dictionary grouping is used over LINQ
    // GroupBy/ToLookup to avoid the Lookup allocation and per-group materialization on these hot paths.
    private Dictionary<int, List<T>> _GroupBySlot<T>(IReadOnlyList<T> items, Func<T, RedisKey> keySelector)
    {
        var slotBuckets = new Dictionary<int, List<T>>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var slot = options.ConnectionMultiplexer.HashSlot(keySelector(item));

            if (!slotBuckets.TryGetValue(slot, out var bucket))
            {
                bucket = [];
                slotBuckets[slot] = bucket;
            }

            bucket.Add(item);
        }

        return slotBuckets;
    }

    private async Task<long> _SafeDatabaseSizeAsync(IServer server)
    {
        try
        {
            return await server.DatabaseSizeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogUnableToReadDatabaseSize(e, server.EndPoint);
            return 0;
        }
    }

    private static async Task<long> _CountKeysByPatternAsync(
        IServer server,
        string pattern,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        long count = 0;

        await foreach (
            var _ in server.KeysAsync(pattern: pattern, pageSize: pageSize).WithCancellation(cancellationToken)
        )
        {
            count++;
        }

        return count;
    }

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        _coordinator.Dispose();
    }
}

internal static partial class RedisCacheLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "UnableToReadDatabaseSize",
        Level = LogLevel.Error,
        Message = "Unable to read database size from {Endpoint}"
    )]
    public static partial void LogUnableToReadDatabaseSize(
        this ILogger logger,
        Exception exception,
        System.Net.EndPoint? endpoint
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "UnableToDeleteHashSlotKeys",
        Level = LogLevel.Error,
        Message = "Unable to delete {HashSlot} keys ({Keys})"
    )]
    public static partial void LogUnableToDeleteHashSlotKeys(
        this ILogger logger,
        Exception exception,
        int hashSlot,
        RedisKey[] keys
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "UnableToDeleteKeys",
        Level = LogLevel.Error,
        Message = "Unable to delete keys ({Keys})"
    )]
    public static partial void LogUnableToDeleteKeys(this ILogger logger, Exception exception, RedisKey[] keys);

    [LoggerMessage(
        EventId = 4,
        EventName = "DeserializationFailed",
        Level = LogLevel.Error,
        Message = "Unable to deserialize cached value of {ValueLength} bytes to type {Type}"
    )]
    public static partial void LogDeserializationFailed(
        this ILogger logger,
        Exception exception,
        long valueLength,
        string? type
    );

    [LoggerMessage(
        EventId = 5,
        EventName = "ExpiredValuesRemoved",
        Level = LogLevel.Trace,
        Message = "Removed {ExpiredValues} expired values for key: {Key}"
    )]
    public static partial void LogExpiredValuesRemoved(this ILogger logger, long expiredValues, string key);

    [LoggerMessage(
        EventId = 6,
        EventName = "SlidingExpirationRearmFailed",
        Level = LogLevel.Debug,
        Message = "Unable to re-arm sliding expiration for cache key {Key}; the value will still be returned."
    )]
    public static partial void LogSlidingExpirationRearmFailed(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 7,
        EventName = "SlidingExpirationRefreshFailed",
        Level = LogLevel.Warning,
        Message = "Unable to refresh sliding expiration for cache key {Key}."
    )]
    public static partial void LogSlidingExpirationRefreshFailed(this ILogger logger, Exception exception, string key);
}
