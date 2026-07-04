// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Headless.Caching.Scripts;
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
    RedisCacheOptions cacheOptions,
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
    /// timestamp (Unix-ms string) lives at <c>{KeyPrefix}\0__tag:{tag}</c>. One key per tag, so tag invalidation
    /// works on Redis Cluster. The leading NUL (<c>\0</c>) keeps markers in a namespace a normal consumer key
    /// cannot reach: consumer keys flow through <see cref="_GetKey"/> (KeyPrefix + caller key) and ordinary cache
    /// keys do not embed a NUL byte, so an ordinary consumer key such as <c>__tag:x</c> does not collide with a
    /// marker. Consumer keys are NOT sanitized for NUL bytes (key-content validation is delegated to callers), so
    /// this guards against accidental collisions, not a deliberately NUL-prefixed key (see issue #555).
    /// </summary>
    private const string _TagMarkerNamespace = "\0__tag:";

    /// <summary>
    /// Reserved key for the Family-2 global clear-generation marker (Unix-ms string) at <c>{KeyPrefix}\0__clear</c>.
    /// Bumped by <c>ClearAsync</c>; compared on every read. The leading NUL (<c>\0</c>) keeps it in the same
    /// unreachable namespace as the tag markers, so an ordinary consumer key such as <c>__clear</c> does not
    /// collide with it (accidental collisions only — a deliberately NUL-prefixed key is not guarded; see #555).
    /// </summary>
    private const string _ClearMarkerSuffix = "\0__clear";

    /// <summary>
    /// Reserved key for the Family-2 global remove-generation marker (Unix-ms string) at <c>{KeyPrefix}\0__remove</c>.
    /// Bumped by the logical <c>FlushAsync</c>; compared on every read. Entries born before it read as a hard miss
    /// with no fail-safe reserve (distinct from the clear marker, which preserves reserves). The leading NUL
    /// (<c>\0</c>) keeps it in the same unreachable namespace, so an ordinary consumer key such as <c>__remove</c>
    /// does not collide with it (accidental collisions only; see #555).
    /// </summary>
    private const string _RemoveMarkerSuffix = "\0__remove";

    private readonly ILogger _logger = logger ?? NullLogger<RedisCache>.Instance;
    private readonly string _keyPrefix = cacheOptions.KeyPrefix ?? "";

    // Process-local marker cache for Family-2 logical tag-version invalidation. Each entry is the resolved
    // marker (Unix-ms; long.MinValue = "absent in Redis") plus the monotonic tick at which it was fetched, so a
    // read can reuse it within options.TagMarkerRefreshWindow instead of hitting Redis. The cache is refreshed
    // lazily on the read path (pipelined MGET for stale/missing tag markers).
    private readonly ConcurrentDictionary<string, (long MarkerMs, long FetchedTicks)> _markerCache = new(
        StringComparer.Ordinal
    );

    // Bound _markerCache (#547). Every entry is fully re-derivable from Redis, so evicting one is behavior-preserving:
    // a later read re-fetches an identical marker. An entry whose freshness stamp is older than
    // _MarkerCacheStaleMultiplier refresh windows is already past its window (the next read re-fetches it regardless),
    // so dropping it changes no read result. The O(cache) scan is throttled to at most once per refresh window and
    // single-flighted via _markerCachePruneRunning so it never piles up on the hot read path.
    private const int _MarkerCacheStaleMultiplier = 8;
    private const int _MarkerCacheMaxEntries = 100_000;
    private long _lastMarkerCachePruneTicks = long.MinValue;
    private int _markerCachePruneRunning;

    // Cached clear-generation marker: MarkerMs (long.MinValue = absent) and the fetch tick, stored as two separate
    // long fields read/written individually (not a single reference cell). A torn read across the two at worst
    // forces an extra marker refresh, which is harmless, so no lock or atomic swap is used.
    private long _clearMarkerMs = long.MinValue;
    private long _clearMarkerFetchedTicks = long.MinValue;

    // Cached remove-generation marker (logical FlushAsync). Same shape and refresh-window semantics as the clear
    // marker; an entry older than this reads as a hard miss with no fail-safe reserve.
    private long _removeMarkerMs = long.MinValue;
    private long _removeMarkerFetchedTicks = long.MinValue;
    private const long _MarkerAbsent = long.MinValue;

    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions { get; } = cacheOptions.DefaultEntryOptions;

    private readonly FactoryCacheCoordinator _coordinator = new(timeProvider, logger, factoryLockProvider);

    // #37: collapsed two volatile bool fields (_supportsMsetEx + _supportsMsetExChecked) into a single
    // Lazy<bool> to make the check-and-set atomic, matching the _isClusterLazy pattern.
    private readonly Lazy<bool> _supportsMsetExLazy = new(() =>
        _DetectMsetexSupport(cacheOptions.ConnectionMultiplexer)
    );
    private readonly Lazy<bool> _isClusterLazy = new(() => _CheckIsCluster(cacheOptions.ConnectionMultiplexer));
    private readonly IDatabase _database = cacheOptions.ConnectionMultiplexer.GetDatabase();

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
                    cacheOptions.ReadMode
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
                _logger.LogSlidingExpirationRefreshFailed(exception, redisKey);
            }
        }
    }

    // Fallback for the rare tagged sliding entry: the header-only read cannot recover the tag list, so read the
    // full frame and resolve per-tag invalidation markers before re-arming.
    private async ValueTask _RefreshSlidingFromFullFrameAsync(RedisKey redisKey, DateTime now)
    {
        var redisValue = await _database.StringGetAsync(redisKey, cacheOptions.ReadMode).ConfigureAwait(false);

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

        await this.UpsertEntryAsync(key, value, options, timeProvider, cancellationToken).ConfigureAwait(false);

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

        // #18: thin delegation to shared numeric-script helper
        var raw = await _RunNumericScriptAsync(
                IncrementWithExpireScriptDefinition.Instance,
                key,
                amount,
                expiration,
                cancellationToken
            )
            .ConfigureAwait(false);

        return double.Parse(raw.ToString(), CultureInfo.InvariantCulture);
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

        // #18: thin delegation to shared numeric-script helper
        var raw = await _RunNumericScriptAsync(
                IncrementWithExpireScriptDefinition.Instance,
                key,
                amount,
                expiration,
                cancellationToken
            )
            .ConfigureAwait(false);

        return _ParseLongReply(raw);
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

        // #18: thin delegation to shared numeric-script helper
        var raw = await _RunNumericScriptAsync(
                SetIfHigherScriptDefinition.Instance,
                key,
                value,
                expiration,
                cancellationToken
            )
            .ConfigureAwait(false);

        return double.Parse(raw.ToString(), CultureInfo.InvariantCulture);
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

        // #18: thin delegation to shared numeric-script helper
        var raw = await _RunNumericScriptAsync(
                SetIfHigherScriptDefinition.Instance,
                key,
                value,
                expiration,
                cancellationToken
            )
            .ConfigureAwait(false);

        return _ParseLongReply(raw);
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

        // #18: thin delegation to shared numeric-script helper
        var raw = await _RunNumericScriptAsync(
                SetIfLowerScriptDefinition.Instance,
                key,
                value,
                expiration,
                cancellationToken
            )
            .ConfigureAwait(false);

        return double.Parse(raw.ToString(), CultureInfo.InvariantCulture);
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

        // #18: thin delegation to shared numeric-script helper
        var raw = await _RunNumericScriptAsync(
                SetIfLowerScriptDefinition.Instance,
                key,
                value,
                expiration,
                cancellationToken
            )
            .ConfigureAwait(false);

        return _ParseLongReply(raw);
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
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        if (expiration is { Ticks: <= 0 })
        {
            await SetRemoveAsync(key, value, expiration, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = expiration.HasValue ? now.UtcDateTime.SafeAdd(expiration.Value) : DateTime.MaxValue;

        var redisValues = new List<RedisValue>();
        var expiresAtMilliseconds = expiration.HasValue
            ? expiresAt.ToUnixTimeMilliseconds()
            : RedisCacheEntryFrame.MaxUnixEpochMilliseconds;

        if (value is string stringValue)
        {
            redisValues.Add(_ToRedisValue(stringValue));
        }
        else
        {
            redisValues.AddRange(value.Where(v => v is not null).Select(_ToRedisValue));
        }

        if (redisValues.Count is 0)
        {
            return 0;
        }

        var redisKey = _GetKey(key);

        return await _RunSetMutationScriptAsync(
                redisKey,
                redisKey,
                [.. redisValues],
                operation: "add",
                expiresAtMilliseconds,
                now.ToUnixTimeMilliseconds(),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Get

    public async ValueTask<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = _GetKey(key);
        var redisValue = await _database.StringGetAsync(redisKey, cacheOptions.ReadMode).ConfigureAwait(false);
        return await _RedisValueToCacheValueAsync<T>(redisKey, redisValue).ConfigureAwait(false);
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

        // One round-trip (or one per hash slot on a cluster) to fetch all raw values, gathered into a single
        // index-aligned array so the tag-marker prefetch and processing loop can run once over everything.
        RedisValue[] rawValues;

        if (IsCluster)
        {
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

                var slotValues = await _database.StringGetAsync(slotKeys, cacheOptions.ReadMode).ConfigureAwait(false);

                for (var i = 0; i < bucket.Count; i++)
                {
                    rawValues[bucket[i].Index] = slotValues[i];
                }
            }
        }
        else
        {
            rawValues = await _database.StringGetAsync([.. redisKeys], cacheOptions.ReadMode).ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // #12: decode all frames up-front so every entry's stale tag markers are fetched in ONE MGET (via
        // _PrefetchTagMarkersAsync) before the processing loop — previously the per-entry marker resolution
        // issued one MGET per tagged entry whose markers were stale, i.e. N sequential round-trips.
        var decodedFrames = new RedisCacheEntryFrame.DecodedFrame?[rawValues.Length];

        for (var i = 0; i < rawValues.Length; i++)
        {
            if (rawValues[i].HasValue)
            {
                try
                {
                    decodedFrames[i] = RedisCacheEntryFrame.Decode(rawValues[i]);
                }
                catch (Exception e)
                {
                    _logger.LogDeserializationFailed(e, rawValues[i].Length(), typeof(T).FullName);
                    throw;
                }
            }
        }

        // Warm _markerCache for every stale tag in one MGET so the per-entry _ResolveNewestMarkerAsync calls
        // below all hit the local cache without additional network I/O.
        await _PrefetchTagMarkersAsync(decodedFrames, now).ConfigureAwait(false);

        var result = new Dictionary<string, CacheValue<T>>(redisKeys.Count, StringComparer.Ordinal);

        for (var i = 0; i < originalKeys.Count; i++)
        {
            var rawValue = rawValues[i];

            // Mirror the previous _RedisValueToCacheValueAsync contract: a missing key surfaces as NoValue so
            // every requested key is present in the result.
            result[originalKeys[i]] = rawValue.HasValue
                ? await _DecodedToCacheValueAsync<T>(decodedFrames[i]!.Value, rawValue, now).ConfigureAwait(false)
                : CacheValue<T>.NoValue;
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Bulk-read conversion from an already-decoded frame: mirrors <see cref="_RedisValueToCacheValueAsync{T}"/>
    /// with <c>rearm: false</c> (no sliding re-arm on bulk reads) but skips the redundant re-decode. Marker
    /// resolution hits the warm <see cref="_markerCache"/> populated by the batched
    /// <see cref="_PrefetchTagMarkersAsync"/>, so no extra Redis round-trip is paid per entry. (#12)
    /// </summary>
    private async ValueTask<CacheValue<T>> _DecodedToCacheValueAsync<T>(
        RedisCacheEntryFrame.DecodedFrame frame,
        RedisValue redisValue,
        DateTime now
    )
    {
        try
        {
            if (frame.IsFramed)
            {
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

                var slotValues = await _database.StringGetAsync(slotKeys, cacheOptions.ReadMode).ConfigureAwait(false);

                for (var i = 0; i < bucket.Count; i++)
                {
                    rawValues[bucket[i].Index] = slotValues[i];
                }
            }
        }
        else
        {
            rawValues = await _database.StringGetAsync([.. redisKeys], cacheOptions.ReadMode).ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var result = new Dictionary<string, CacheValueWithExpiration<T>>(redisKeys.Count, StringComparer.Ordinal);

        // #10: Decode all frames up-front so we can batch-prefetch stale tag markers for all entries in ONE
        // MGET before the processing loop — previously _ResolveNewestMarkerAsync issued one MGET per entry
        // whose tags were stale, resulting in N sequential round-trips for N tagged entries.
        var decodedFrames = new RedisCacheEntryFrame.DecodedFrame?[rawValues.Length];

        for (var i = 0; i < rawValues.Length; i++)
        {
            if (rawValues[i].HasValue)
            {
                try
                {
                    decodedFrames[i] = RedisCacheEntryFrame.Decode(rawValues[i]);
                }
                catch (Exception e)
                {
                    _logger.LogDeserializationFailed(e, rawValues[i].Length(), typeof(T).FullName);
                    throw;
                }
            }
        }

        // Collect all unique tags from non-expired framed entries and prefetch their markers in one MGET.
        // After this call, _markerCache will contain fresh entries for every stale tag, so per-entry calls
        // to _ResolveNewestMarkerAsync below will all hit the local cache without additional network I/O.
        await _PrefetchTagMarkersAsync(decodedFrames, now).ConfigureAwait(false);

        // First pass: resolve marker checks + collect keys requiring live TTL probes.
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
                var frame = decodedFrames[i]!.Value;

                if (frame.IsFramed)
                {
                    if (_IsExpired(frame.PhysicalExpiresAt, now))
                    {
                        continue;
                    }

                    // Family-2: a tag/clear-invalidated entry is a miss for direct mirror reads.
                    // Tag markers are now warm in _markerCache; this call incurs no extra RTT.
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
                        cacheValue = new CacheValue<T>(framedValue, hasValue: true);
                    }

                    result[originalKeys[i]] = new CacheValueWithExpiration<T>(cacheValue, logicalRemaining);
                }
                else
                {
                    // Non-framed (legacy) entry: no embedded expiration metadata — needs live TTL probe.
                    var legacyValue = rawValue == _NullValue ? default : _FromRedisValue<T>(rawValue);

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
                ttlTasks[j] = batch.KeyTimeToLiveAsync(slidingOrLegacyHits[j].RedisKey, cacheOptions.ReadMode);
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

                var cacheVal = val is null ? CacheValue<T>.Null : new CacheValue<T>(val, hasValue: true);

                // ttl is null  → persistent key, no expiry → Expiration = null (valid hit)
                // ttl.Value > 0 → remaining TTL → Expiration = ttl
                result[originalKeys[idx]] = new CacheValueWithExpiration<T>(cacheVal, expiration: ttl);
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

        var endpoints = cacheOptions.ConnectionMultiplexer.GetEndPoints();

        foreach (var endpoint in endpoints)
        {
            var server = cacheOptions.ConnectionMultiplexer.GetServer(endpoint);

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
        var redisValue = await _database.StringGetAsync(_GetKey(key), cacheOptions.ReadMode).ConfigureAwait(false);
        return await _RedisValueIsLogicallyPresentAsync(redisValue).ConfigureAwait(false);
    }

    public async ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var endpoints = cacheOptions.ConnectionMultiplexer.GetEndPoints();

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
                var server = cacheOptions.ConnectionMultiplexer.GetServer(endpoint);

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
                var server = cacheOptions.ConnectionMultiplexer.GetServer(endpoint);

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

        // #11: cache prefixed key to avoid repeated _GetKey allocations (called up to 3x previously)
        var prefixedKey = _GetKey(key);
        var redisValue = await _database.StringGetAsync(prefixedKey, cacheOptions.ReadMode).ConfigureAwait(false);

        if (!redisValue.HasValue)
        {
            return null;
        }

        var frame = RedisCacheEntryFrame.Decode(redisValue);

        if (!frame.IsFramed)
        {
            // Non-framed (legacy/raw) keys carry no logical metadata, so fall back to the server TTL. Only
            // legacy keys pay this second round trip; framed keys return below from the decoded frame.
            return await _database.KeyTimeToLiveAsync(prefixedKey).ConfigureAwait(false);
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
            return await _database.KeyTimeToLiveAsync(prefixedKey).ConfigureAwait(false);
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
        var rawValue = await _database.StringGetAsync(redisKey, cacheOptions.ReadMode).ConfigureAwait(false);

        if (!rawValue.HasValue)
        {
            return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, expiration: null);
        }

        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var frame = RedisCacheEntryFrame.Decode(rawValue);

            if (frame.IsFramed)
            {
                if (_IsExpired(frame.PhysicalExpiresAt, now))
                {
                    return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, expiration: null);
                }

                // Family-2: a tag/clear-invalidated entry is a miss for direct mirror reads.
                var newestMarker = await _ResolveNewestMarkerAsync(frame.Tags).ConfigureAwait(false);

                if (CacheTagInvalidation.IsInvalidated(frame.CreatedAt, newestMarker))
                {
                    return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, expiration: null);
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
                        return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, expiration: null);
                    }

                    CacheValue<T> slidingValue = frame.IsNull
                        ? CacheValue<T>.Null
                        : new CacheValue<T>(_DeserializeValueSegment<T>(frame.ValueSegment), hasValue: true);

                    return new CacheValueWithExpiration<T>(slidingValue, expiration: ttl);
                }

                if (_IsExpired(frame.LogicalExpiresAt, now))
                {
                    return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, expiration: null);
                }

                TimeSpan? logicalRemaining = frame.LogicalExpiresAt?.Subtract(now);

                CacheValue<T> cacheValue = frame.IsNull
                    ? CacheValue<T>.Null
                    : new CacheValue<T>(_DeserializeValueSegment<T>(frame.ValueSegment), hasValue: true);

                return new CacheValueWithExpiration<T>(cacheValue, logicalRemaining);
            }

            // Non-framed (legacy/raw) entry: value is present but carries no logical expiry metadata.
            // Fall back to the live server TTL for the expiration component.
            var legacyValue =
                rawValue == _NullValue
                    ? CacheValue<T>.Null
                    : new CacheValue<T>(_FromRedisValue<T>(rawValue), hasValue: true);

            var legacyTtl = await _database.KeyTimeToLiveAsync(redisKey).ConfigureAwait(false);

            if (legacyTtl is { Ticks: <= 0 })
            {
                return new CacheValueWithExpiration<T>(CacheValue<T>.NoValue, expiration: null);
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
                    flags: cacheOptions.ReadMode
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
                    flags: cacheOptions.ReadMode
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
        var redisValue = await _database.StringGetAsync(redisKey, cacheOptions.ReadMode).ConfigureAwait(false);

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
            // #36: group by hash slot ONCE before chunking to avoid re-allocating a Dictionary per chunk.
            // Previously _GroupBySlot was called inside the Chunk loop: N/250 allocations for N keys.
            var slotBuckets = _GroupBySlot(redisKeys, static key => key);

            foreach (var (slot, bucket) in slotBuckets)
            {
                foreach (var batch in bucket.Chunk(_BatchSize))
                {
                    var hashSlotKeys = batch;

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
        var endpoints = cacheOptions.ConnectionMultiplexer.GetEndPoints();

        if (endpoints.Length is 0)
        {
            return 0;
        }

        var isCluster = IsCluster;
        long deleted = 0;

        foreach (var endpoint in endpoints)
        {
            var server = cacheOptions.ConnectionMultiplexer.GetServer(endpoint);

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

        _RaiseTagMarker(tag, ms, ticks);
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
        // Zero is allowed (not just positive) so SetAddAsync's expire-immediately branch can delegate here; the
        // expiration is not applied by removal.
        Argument.IsPositiveOrZero(expiration);
        cancellationToken.ThrowIfCancellationRequested();

        var redisValues = new List<RedisValue>();

        if (value is string stringValue)
        {
            redisValues.Add(_ToRedisValue(stringValue));
        }
        else
        {
            redisValues.AddRange(value.Where(v => v is not null).Select(_ToRedisValue));
        }

        if (redisValues.Count is 0)
        {
            return 0;
        }

        var redisKey = _GetKey(key);

        return await _RunSetMutationScriptAsync(
                redisKey,
                redisKey,
                [.. redisValues],
                operation: "remove",
                scoreMilliseconds: 0,
                nowMilliseconds: timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                cancellationToken
            )
            .ConfigureAwait(false);
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
        // #555: reject NUL bytes in consumer keys. Redis keys are binary-safe, so the leading-NUL namespace that
        // isolates the Family-2 markers ({KeyPrefix}\0__tag:/\0__clear/\0__remove) is only a real trust boundary if
        // consumer keys are guaranteed NUL-free; without this a crafted key such as "\0__clear" could forge or
        // suppress an invalidation generation. Internal marker keys are built separately and never flow through here.
        Argument.IsFalse(
            key.Contains('\0', StringComparison.Ordinal),
            "Cache keys must not contain NUL ('\\0') bytes."
        );

        return string.IsNullOrEmpty(_keyPrefix) ? key : string.Concat(_keyPrefix, key);
    }

    // #590: one Lua script serves the long and double SetIfHigher/SetIfLower overloads and returns the difference as
    // a string (integer via %d, fractional via tostring). When a key mixes integer and fractional writes, the
    // long-overload difference can be fractional (e.g. "4.5"); parse the exact integer when the reply is integral,
    // otherwise coerce a fractional reply by truncating toward zero (the (long) cast) instead of throwing
    // FormatException. Values past 2^53 already lose precision in the Lua double arithmetic (documented on the
    // script definitions), so the fallback adds no precision loss the pure-long path did not already have.
    private static long _ParseLongReply(RedisResult raw)
    {
        var text = raw.ToString();

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exact)
            ? exact
            : (long)double.Parse(text, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Shared body for the six numeric-script operations (Increment×2, SetIfHigher×2, SetIfLower×2).
    /// Each public method handles its own argument validation and zero-expiration early-exit, then delegates
    /// the common script-invocation pattern here. Pure C# extraction — no Redis semantic change. (#18)
    /// </summary>
    private Task<RedisResult> _RunNumericScriptAsync(
        RedisScriptDefinition script,
        string key,
        RedisValue value,
        TimeSpan? expiration,
        CancellationToken cancellationToken
    )
    {
        var expiresMs = _GetExpirationMilliseconds(expiration, timeProvider.GetUtcNow());
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        return scriptsLoader.EvaluateAsync(
            _database,
            script,
            new
            {
                key = (RedisKey)_GetKey(key),
                value,
                expires = expiresArg,
            },
            cancellationToken
        );
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
        return elapsed < cacheOptions.TagMarkerRefreshWindow;
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
            .StringGetAsync((RedisKey)_GetClearMarkerKey(), cacheOptions.ReadMode)
            .ConfigureAwait(false);
        var ms = _ParseMarkerMs(value);

        // Raise-only CAS (mirrors SeedClearMarker): a stale durable read — e.g. a lagging replica — must not
        // lower a newer clear generation a backplane push already seeded. Surface the raised max so this read
        // does not under-invalidate either.
        long current;
        while ((current = Interlocked.Read(ref _clearMarkerMs)) < ms)
        {
            if (Interlocked.CompareExchange(ref _clearMarkerMs, ms, current) == current)
            {
                break;
            }
        }

        Interlocked.Exchange(ref _clearMarkerFetchedTicks, _StopwatchTicks());
        return Interlocked.Read(ref _clearMarkerMs);
    }

    private async ValueTask<long> _ResolveRemoveMarkerAsync()
    {
        var fetchedTicks = Interlocked.Read(ref _removeMarkerFetchedTicks);

        if (_MarkerIsFresh(fetchedTicks))
        {
            return Interlocked.Read(ref _removeMarkerMs);
        }

        var value = await _database
            .StringGetAsync((RedisKey)_GetRemoveMarkerKey(), cacheOptions.ReadMode)
            .ConfigureAwait(false);
        var ms = _ParseMarkerMs(value);

        // Raise-only CAS (mirrors SeedRemoveMarker): a stale durable read — e.g. a lagging replica — must not
        // lower a newer remove generation a backplane push already seeded. Surface the raised max so this read
        // does not under-invalidate either.
        long current;
        while ((current = Interlocked.Read(ref _removeMarkerMs)) < ms)
        {
            if (Interlocked.CompareExchange(ref _removeMarkerMs, ms, current) == current)
            {
                break;
            }
        }

        Interlocked.Exchange(ref _removeMarkerFetchedTicks, _StopwatchTicks());
        return Interlocked.Read(ref _removeMarkerMs);
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

            var values = await _GetTagMarkerValuesAsync(markerKeys).ConfigureAwait(false);
            var fetchedTicks = _StopwatchTicks();

            for (var i = 0; i < stale.Count; i++)
            {
                var ms = _ParseMarkerMs(values[i]);

                // Raise-only (mirrors SeedTagMarker): a stale durable read must not lower a newer per-tag marker
                // a backplane push already seeded. FetchedTicks is still refreshed so the freshness window holds.
                var resolved = _RaiseTagMarker(stale[i], ms, fetchedTicks);

                if (resolved.MarkerMs > newestMs)
                {
                    newestMs = resolved.MarkerMs;
                }
            }
        }

        _PruneMarkerCacheIfDue();

        return newestMs;
    }

    private static long _ParseMarkerMs(RedisValue value) =>
        RedisCacheEntryFrame.TryParseMarkerMs(value) ?? _MarkerAbsent;

    // Tag-marker keys carry no hash-tag braces, so distinct tags hash to arbitrary cluster slots — a single flat
    // MGET across them fails with CROSSSLOT on Redis Cluster. Split per slot on cluster topologies (mirroring the
    // batch-operation cluster paths); non-cluster keeps the single MGET. Returns values in input-key order.
    private async Task<RedisValue[]> _GetTagMarkerValuesAsync(RedisKey[] markerKeys)
    {
        if (!IsCluster || markerKeys.Length <= 1)
        {
            return await _database.StringGetAsync(markerKeys, cacheOptions.ReadMode).ConfigureAwait(false);
        }

        var indexedKeys = new (RedisKey Redis, int Index)[markerKeys.Length];

        for (var i = 0; i < markerKeys.Length; i++)
        {
            indexedKeys[i] = (markerKeys[i], i);
        }

        var values = new RedisValue[markerKeys.Length];
        var slotBuckets = _GroupBySlot(indexedKeys, static entry => entry.Redis);

        foreach (var bucket in slotBuckets.Values)
        {
            var slotKeys = new RedisKey[bucket.Count];

            for (var i = 0; i < bucket.Count; i++)
            {
                slotKeys[i] = bucket[i].Redis;
            }

            var slotValues = await _database.StringGetAsync(slotKeys, cacheOptions.ReadMode).ConfigureAwait(false);

            for (var i = 0; i < bucket.Count; i++)
            {
                values[bucket[i].Index] = slotValues[i];
            }
        }

        return values;
    }

    /// <summary>
    /// Raises the process-local tag marker for <paramref name="tag"/> to <paramref name="markerMs"/> when it is
    /// newer, always refreshing the freshness stamp to <paramref name="fetchedTicks"/>. Raise-only: a stale durable
    /// read or a backplane push can never lower a newer marker this node already observed; the freshness stamp is
    /// refreshed in both branches so the refresh window holds. Returns the resolved (post-raise) entry. Shared by
    /// <see cref="SeedTagMarker"/>, <see cref="_ResolveTagMarkersAsync"/>, and <see cref="_PrefetchTagMarkersAsync"/>
    /// so the raise-only invariant lives in exactly one place.
    /// </summary>
    private (long MarkerMs, long FetchedTicks) _RaiseTagMarker(string tag, long markerMs, long fetchedTicks)
    {
        return _markerCache.AddOrUpdate(
            tag,
            static (_, state) => (state.markerMs, state.fetchedTicks),
            static (_, existing, state) =>
                existing.MarkerMs >= state.markerMs
                    ? (existing.MarkerMs, state.fetchedTicks)
                    : (state.markerMs, state.fetchedTicks),
            (markerMs, fetchedTicks)
        );
    }

    /// <summary>
    /// Opportunistically bounds <see cref="_markerCache"/> (#547). Throttled to at most once per refresh window and
    /// single-flighted, so the O(cache) scan is amortized off the hot read path. Drops entries whose freshness stamp
    /// is older than <see cref="_MarkerCacheStaleMultiplier"/> refresh windows — they are already stale and would be
    /// re-fetched on next use, so eviction is behavior-preserving — and, as a fallback for a burst of many distinct
    /// still-fresh tags that the age-prune cannot reclaim, evicts the oldest entries by freshness stamp down to
    /// <see cref="_MarkerCacheMaxEntries"/>.
    /// </summary>
    private void _PruneMarkerCacheIfDue()
    {
        if (_markerCache.IsEmpty)
        {
            return;
        }

        var refreshWindow = cacheOptions.TagMarkerRefreshWindow;
        var last = Interlocked.Read(ref _lastMarkerCachePruneTicks);

        if (last != long.MinValue && Stopwatch.GetElapsedTime(last) < refreshWindow)
        {
            return;
        }

        // Single-flight: one thread scans, concurrent readers skip. A skipped run is retried on the next read.
        if (Interlocked.CompareExchange(ref _markerCachePruneRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Interlocked.Exchange(ref _lastMarkerCachePruneTicks, _StopwatchTicks());

            var staleThreshold = refreshWindow * _MarkerCacheStaleMultiplier;

            foreach (var (tag, entry) in _markerCache)
            {
                if (
                    entry.FetchedTicks != long.MinValue
                    && Stopwatch.GetElapsedTime(entry.FetchedTicks) < staleThreshold
                )
                {
                    continue;
                }

                // Conditional remove: drop only the exact stale snapshot so a concurrent _RaiseTagMarker refresh
                // (which rewrites FetchedTicks) is never clobbered.
                ((ICollection<KeyValuePair<string, (long MarkerMs, long FetchedTicks)>>)_markerCache).Remove(
                    new KeyValuePair<string, (long MarkerMs, long FetchedTicks)>(tag, entry)
                );
            }

            _EvictMarkerCacheToCap();
        }
        finally
        {
            Interlocked.Exchange(ref _markerCachePruneRunning, 0);
        }
    }

    // Fallback size cap for a burst of many distinct still-fresh tags the age-prune cannot reclaim. Evicts the oldest
    // entries by freshness stamp down to _MarkerCacheMaxEntries; still behavior-preserving since every evicted marker
    // is re-fetched from Redis on next use.
    private void _EvictMarkerCacheToCap()
    {
        var overflow = _markerCache.Count - _MarkerCacheMaxEntries;

        if (overflow <= 0)
        {
            return;
        }

        var snapshot = _markerCache.Select(static kvp => (kvp.Key, kvp.Value.FetchedTicks)).ToArray();
        Array.Sort(snapshot, static (a, b) => a.FetchedTicks.CompareTo(b.FetchedTicks));

        var toRemove = Math.Min(overflow, snapshot.Length);

        for (var i = 0; i < toRemove; i++)
        {
            _markerCache.TryRemove(snapshot[i].Key, out _);
        }
    }

    /// <summary>
    /// Pre-warms <see cref="_markerCache"/> for all unique stale tag markers referenced by the supplied decoded
    /// frames in a single MGET — O(1) Redis round-trip regardless of how many entries share tags.
    /// Called once before the processing loop in <see cref="GetAllWithExpirationAsync{T}"/> so that subsequent
    /// per-entry <see cref="_ResolveNewestMarkerAsync"/> calls hit the local cache without extra network I/O. (#10)
    /// </summary>
    private async ValueTask _PrefetchTagMarkersAsync(RedisCacheEntryFrame.DecodedFrame?[] decodedFrames, DateTime now)
    {
        // Collect all unique stale tags across all non-expired framed entries.
        HashSet<string>? staleTags = null;

        foreach (var frame in decodedFrames)
        {
            if (frame is not { IsFramed: true } f || _IsExpired(f.PhysicalExpiresAt, now) || f.Tags is null)
            {
                continue;
            }

            foreach (var tag in f.Tags)
            {
                if (!_markerCache.TryGetValue(tag, out var cached) || !_MarkerIsFresh(cached.FetchedTicks))
                {
                    (staleTags ??= new HashSet<string>(StringComparer.Ordinal)).Add(tag);
                }
            }
        }

        if (staleTags is null)
        {
            return;
        }

        // Fetch all stale tag markers in one MGET (one per hash slot on cluster) and populate the local cache.
        var staleList = new List<string>(staleTags);
        var markerKeys = new RedisKey[staleList.Count];

        for (var i = 0; i < staleList.Count; i++)
        {
            markerKeys[i] = _GetTagMarkerKey(staleList[i]);
        }

        var values = await _GetTagMarkerValuesAsync(markerKeys).ConfigureAwait(false);
        var fetchedTicks = _StopwatchTicks();

        for (var i = 0; i < staleList.Count; i++)
        {
            var ms = _ParseMarkerMs(values[i]);

            // Raise-only (mirrors SeedTagMarker): a stale durable read must not lower a newer per-tag marker a
            // backplane push already seeded. FetchedTicks is still refreshed so the freshness window holds.
            _RaiseTagMarker(staleList[i], ms, fetchedTicks);
        }

        _PruneMarkerCacheIfDue();
    }

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

        // SE.Redis 3.0+ exposes the value as a ReadOnlySequence<byte> over its native storage; deserializing off
        // the sequence skips the byte[] materialization that (byte[])redisValue forces for inline (ShortBlob) values.
        return serializer.Deserialize<T>((ReadOnlySequence<byte>)redisValue!);
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
                return new CacheValue<T>(framedValue, hasValue: true);
            }

            if (redisValue == _NullValue)
            {
                return CacheValue<T>.Null;
            }

            var value = _FromRedisValue<T>(redisValue);
            return new CacheValue<T>(value, hasValue: true);
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
        var redisValue = await _database.StringGetAsync(redisKey, cacheOptions.ReadMode).ConfigureAwait(false);

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

            // Write the value straight from its native ReadOnlySequence<byte> storage (SE.Redis 3.0+), avoiding the
            // intermediate byte[] that (byte[])redisValue would materialize — keeping this path's single-copy intent.
            foreach (var segment in (ReadOnlySequence<byte>)redisValue!)
            {
                destination.Write(segment.Span);
            }

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

        // Empty result -> NoValue (Value:null), matching InMemory. An empty ZRANGEBYSCORE reply is indistinguishable
        // from an absent key here (both yield an empty array) without an extra EXISTS round-trip, so GetSetAsync
        // reports the requested page's members, not key presence: an absent key, an empty set, and a page past the
        // last live member all read as a miss (HasValue == false).
        if (result.Count is 0)
        {
            return CacheValue<ICollection<T>>.NoValue;
        }

        return new CacheValue<ICollection<T>>(result, hasValue: true);
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

        return _EncodeFramedValue(
            value,
            isNull: value is null,
            logicalExpiresAt: expiresAt,
            physicalExpiresAt: expiresAt,
            slidingExpiration: null,
            // Direct upsert path stamps the birth time so a prior tag/clear marker does not invalidate the new
            // value (Family-2 version-pinning compares CreatedAt against the newest applicable marker).
            createdAt: now.UtcDateTime
        );
    }

    // Encodes a framed entry, choosing the lowest-allocation value-segment path. string/byte[] (and the null
    // sentinel) keep their verbatim RedisValue fast path through _ToRedisValue, so encode/decode and the CAS
    // `expected` operand stay byte-consistent. Other types serialize straight into a pooled buffer that Encode
    // copies once into the frame — eliminating the intermediate exact-size byte[] that SerializeToBytes allocates.
    private byte[] _EncodeFramedValue<T>(
        T? value,
        bool isNull,
        DateTime? logicalExpiresAt,
        DateTime? physicalExpiresAt,
        TimeSpan? slidingExpiration,
        DateTime? eagerRefreshAt = null,
        string? etag = null,
        DateTime? lastModifiedAt = null,
        IReadOnlyCollection<string>? tags = null,
        DateTime? createdAt = null
    )
    {
        // isNull and the string/byte[]/null fast paths both encode through the RedisValue overload, so resolve the
        // segment first and keep the 9-arg Encode tail at a single call site. isNull selects the empty sentinel;
        // otherwise _ToRedisValue preserves the legacy null-sentinel encoding and takes string/byte[] verbatim.
        if (isNull || value is null or string or byte[])
        {
            return RedisCacheEntryFrame.Encode(
                isNull ? RedisValue.EmptyString : _ToRedisValue(value),
                isNull,
                logicalExpiresAt,
                physicalExpiresAt,
                slidingExpiration,
                eagerRefreshAt,
                etag,
                lastModifiedAt,
                tags,
                createdAt
            );
        }

        using var buffer = new PooledByteBufferWriter();
        serializer.Serialize(value, buffer);

        return RedisCacheEntryFrame.Encode(
            new ReadOnlySequence<byte>(buffer.WrittenMemory),
            isNull: false,
            logicalExpiresAt,
            physicalExpiresAt,
            slidingExpiration,
            eagerRefreshAt,
            etag,
            lastModifiedAt,
            tags,
            createdAt
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

        return _SetEntryCoreAsync(key, entry, cancellationToken);
    }

    private async ValueTask<bool> _SetEntryCoreAsync<T>(
        string key,
        CacheStoreEntryWrite<T> entry,
        CancellationToken cancellationToken = default // #7: thread caller token through CAS path
    )
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
                var current = await _database.StringGetAsync(redisKey).ConfigureAwait(false);

                // #13: compare via the header-only stamp so this CAS-delete stays consistent with the slice the
                // live path captures. Recomputing the stamp from the current value reuses the single stamp codec
                // instead of re-deriving the header offset here.
                if (
                    !current.HasValue
                    || !string.Equals(_ToConcurrencyStamp(current), expiredExpectedStamp, StringComparison.Ordinal)
                )
                {
                    return false;
                }
            }

            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return true;
        }

        var redisValue = _EncodeFramedValue(
            entry.Value,
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
                    headerLen = RedisCacheEntryFrame.HeaderLength,
                },
                cancellationToken // #7: was CancellationToken.None — now respects caller's token
            )
            .ConfigureAwait(false);

        return (int)result == 1;
    }

    // #13: the concurrency stamp captures only the fixed frame header (the first HeaderLength bytes) rather than
    // base64-encoding the whole payload on every read. The header carries the per-write CreatedAt + expiry stamps
    // that uniquely identify an entry version, so it is sufficient for CAS. Both CAS comparison sites
    // (CacheTaggedSetScriptDefinition and the zero-expiration delete branch) compare the SAME header slice, so the
    // stamp stays provably consistent. A shorter-than-header value (e.g. a legacy raw value) encodes in full.
    private static string _ToConcurrencyStamp(RedisValue value)
    {
        var bytes = (byte[])value!;
        var length = Math.Min(bytes.Length, RedisCacheEntryFrame.HeaderLength);

        return string.Concat("b64:", Convert.ToBase64String(bytes, 0, length));
    }

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
        var redisValue = await _database.StringGetAsync(_GetKey(key), cacheOptions.ReadMode).ConfigureAwait(false);

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
                logicalExpiresAt = logicalExpiresAt < now ? logicalExpiresAt : now;
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
            // #9: single-RTT atomic TTL-check-and-conditional-PEXPIRE via the loaded SlidingRearm script (EVALSHA
            // with NOSCRIPT recovery). Previously issued KeyTimeToLiveAsync then (conditionally) KeyExpireAsync.
            var expiresIn = _Min(slidingExpiration, remainingToCap);
            var rearmThresholdMs = (long)rearmThreshold.TotalMilliseconds;
            var newTtlMs = (long)expiresIn.TotalMilliseconds;

            await scriptsLoader
                .EvaluateAsync(
                    _database,
                    SlidingRearmScriptDefinition.Instance,
                    new
                    {
                        key = redisKey,
                        rearmThresholdMs = (RedisValue)rearmThresholdMs,
                        newTtlMs = (RedisValue)newTtlMs,
                    }
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogSlidingExpirationRearmFailed(exception, redisKey.ToString());
            }
        }
    }

    private static bool _IsExpired(DateTime? expiresAt, DateTime now) => expiresAt <= now;

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

        // The buffer-first serializer reads the framed value segment in place — no MemoryStream wrapper and no
        // intermediate byte[] copy. (RedisValue is decoded into a managed array by StackExchange.Redis, so the
        // ReadOnlyMemory<byte> here is already contiguous.)
        return serializer.Deserialize<T>(segment);
    }

    private async Task<int> _SetAllInternalAsync(KeyValuePair<RedisKey, RedisValue>[] pairs, TimeSpan? expiresIn)
    {
        if (expiresIn.HasValue)
        {
            if (_supportsMsetExLazy.Value) // #37: atomic Lazy<bool> replaces non-atomic two-volatile check
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

    // #37: extracted into a static factory so _supportsMsetExLazy captures no instance state.
    // Lazy<bool> gives us thread-safe once-only initialization matching the _isClusterLazy pattern.
    private static bool _DetectMsetexSupport(IConnectionMultiplexer connectionMultiplexer)
    {
        // Redis 8.4 RC1 is internally versioned as 8.3.224
        var minVersion = new Version(8, 3, 224);
        var endpoints = connectionMultiplexer.GetEndPoints();

        if (endpoints.Length is 0)
        {
            return false;
        }

        var foundConnectedPrimary = false;

        foreach (var endpoint in endpoints)
        {
            var server = connectionMultiplexer.GetServer(endpoint);

            if (server is { IsConnected: true, IsReplica: false })
            {
                foundConnectedPrimary = true;

                if (server.Version < minVersion)
                {
                    return false;
                }
            }
        }

        return foundConnectedPrimary;
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

    private async ValueTask<long> _RunSetMutationScriptAsync(
        RedisKey key,
        string logKey,
        RedisValue[] values,
        string operation,
        long scoreMilliseconds,
        long nowMilliseconds,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await SetAddWithExpireScriptDefinition
            .EvaluateAsync(
                _database,
                key,
                _GetSetMutationScriptValues(operation, scoreMilliseconds, nowMilliseconds, values)
            )
            .ConfigureAwait(false);

        var valuesResult = (RedisResult[]?)result;

        if (valuesResult is null || valuesResult.Length < 2)
        {
            throw new RedisServerException("Unexpected set mutation script result.");
        }

        var changed = (long)valuesResult[0];
        var pruned = (long)valuesResult[1];

        if (pruned > 0)
        {
            _logger.LogExpiredValuesRemoved(pruned, logKey);
        }

        return changed;
    }

    private static RedisValue[] _GetSetMutationScriptValues(
        string operation,
        long scoreMilliseconds,
        long nowMilliseconds,
        RedisValue[] values
    )
    {
        var scriptValues = new RedisValue[values.Length + 4];
        scriptValues[0] = operation;
        scriptValues[1] = scoreMilliseconds;
        scriptValues[2] = nowMilliseconds;
        scriptValues[3] = RedisCacheEntryFrame.MaxUnixEpochMilliseconds;
        values.CopyTo(scriptValues, 4);

        return scriptValues;
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

        foreach (var item in items)
        {
            var slot = cacheOptions.ConnectionMultiplexer.HashSlot(keySelector(item));

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
