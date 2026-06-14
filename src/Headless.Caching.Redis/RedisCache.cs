// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using Headless.Checks;
using Headless.Redis;
using Headless.Serializer;
using Headless.Threading;
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
) : IRemoteCache, IFactoryCacheStore, IDisposable
{
    /// <summary>Legacy null sentinel retained only for raw pre-envelope payloads and collection entries.</summary>
    private static readonly RedisValue _NullValue = "@@NULL";
    private const int _BatchSize = 250;

    /// <summary>
    /// Reserved namespace segment for the reverse tag index: tag hashes live at
    /// <c>{KeyPrefix}__cache_tag__:{tag}</c>. Cache entries must not be stored under keys starting with this
    /// segment. See <see cref="CacheTaggedSetScriptDefinition"/> for the index contract.
    /// </summary>
    private const string _TagNamespace = "__cache_tag__:";

    private readonly ILogger _logger = logger ?? NullLogger<RedisCache>.Instance;
    private readonly string _keyPrefix = options.KeyPrefix ?? "";

    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions { get; } = options.DefaultEntryOptions;

    private readonly FactoryCacheCoordinator _coordinator = new(timeProvider, logger, factoryLockProvider);

    private volatile bool _supportsMsetEx;
    private volatile bool _supportsMsetExChecked;
    private readonly Lazy<bool> _isClusterLazy = new(() => _CheckIsCluster(options.ConnectionMultiplexer));
    private readonly IDatabase _database = options.ConnectionMultiplexer.GetDatabase();

    private bool IsCluster => _isClusterLazy.Value;

    // Tag operations touch the tag reverse-index hash and the tagged entry keys together; on Redis Cluster those
    // span hash slots, which a single Lua script cannot do (CROSSSLOT). Fail fast with a clear message instead of
    // surfacing a raw RedisServerException. A cluster-safe design is tracked in issue #438.
    private void _EnsureTaggingClusterSupported()
    {
        if (IsCluster)
        {
            throw new NotSupportedException(
                "Redis cache tag operations (tagged writes and RemoveByTagAsync) are not supported on Redis "
                    + "Cluster: the tag reverse-index and tagged entries span hash slots, which a single Lua "
                    + "script cannot touch. Use a standalone or replicated Redis deployment for tagging, or omit "
                    + "tags when connected to a cluster."
            );
        }
    }

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

        await ((IFactoryCacheStore)this)
            .UpsertEntryAsync(key, value, options, timeProvider, cancellationToken)
            .ConfigureAwait(false);

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
            var slotBuckets = new Dictionary<int, List<KeyValuePair<RedisKey, RedisValue>>>();

            foreach (var pair in pairs)
            {
                var slot = options.ConnectionMultiplexer.HashSlot(pair.Key);

                if (!slotBuckets.TryGetValue(slot, out var bucket))
                {
                    bucket = [];
                    slotBuckets[slot] = bucket;
                }

                bucket.Add(pair);
            }

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

        return double.Parse(result.ToString()!, CultureInfo.InvariantCulture);
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

        return long.Parse(result.ToString()!, CultureInfo.InvariantCulture);
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

            // Group by hash slot without LINQ: avoids Lookup allocation + per-group materialization.
            var slotBuckets = new Dictionary<int, List<(string Original, RedisKey Redis)>>();

            for (var i = 0; i < originalKeys.Count; i++)
            {
                var slot = options.ConnectionMultiplexer.HashSlot(redisKeys[i]);

                if (!slotBuckets.TryGetValue(slot, out var bucket))
                {
                    bucket = [];
                    slotBuckets[slot] = bucket;
                }

                bucket.Add((originalKeys[i], redisKeys[i]));
            }

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
            var slotBuckets = new Dictionary<int, List<(RedisKey Redis, int Index)>>();

            for (var i = 0; i < redisKeys.Count; i++)
            {
                var slot = options.ConnectionMultiplexer.HashSlot(redisKeys[i]);

                if (!slotBuckets.TryGetValue(slot, out var bucket))
                {
                    bucket = [];
                    slotBuckets[slot] = bucket;
                }

                bucket.Add((redisKeys[i], i));
            }

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
                _logger.LogDeserializationFailed(e, rawValue, typeof(T).FullName);
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
        return _RedisValueIsLogicallyPresent(redisValue);
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
            _logger.LogDeserializationFailed(e, rawValue, typeof(T).FullName);
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
            (byte[])frame.ValueSegment.ToArray(),
            frame.IsNull,
            logicalExpiresAt: now,
            physicalExpiresAt: frame.PhysicalExpiresAt,
            slidingExpiration: null,
            eagerRefreshAt: null,
            etag: frame.ETag,
            lastModifiedAt: frame.LastModifiedAt,
            tags: frame.Tags
        );

        await _database
            .StringSetAsync(redisKey, reStamped, expiry: null, keepTtl: true, when: When.Exists)
            .ConfigureAwait(false);

        return true;
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
                // Manual slot grouping avoids LINQ Lookup allocation + per-group materialization.
                var slotBuckets = new Dictionary<int, List<RedisKey>>();

                foreach (var key in batch)
                {
                    var slot = options.ConnectionMultiplexer.HashSlot(key);

                    if (!slotBuckets.TryGetValue(slot, out var bucket))
                    {
                        bucket = [];
                        slotBuckets[slot] = bucket;
                    }

                    bucket.Add(key);
                }

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
    /// <remarks>
    /// Not supported on Redis Cluster: the tag hash and the tagged keys span hash slots.
    /// <para>
    /// Processes tag members in batches of up to <see cref="RedisCacheOptions.MaxMembersPerTagRemoval"/>
    /// per Lua call and loops in C# until the tag hash is fully drained, so removal is always complete
    /// regardless of tag cardinality. The returned count is the total across all batches.
    /// </para>
    /// <para>
    /// This operation is <em>point-in-time best-effort</em>: a key re-added to the tag during the sweep
    /// may survive if the concurrent write races ahead of the final batch. The version-pinning check in
    /// the Lua script prevents incorrectly deleting freshly re-created keys; it does not prevent new
    /// additions from being missed.
    /// </para>
    /// </remarks>
    public async ValueTask<int> RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(tag);
        cancellationToken.ThrowIfCancellationRequested();
        _EnsureTaggingClusterSupported();

        var tagHashKey = (RedisKey)_GetTagHashKey(tag);
        var scriptArgs = new
        {
            tagHash = tagHashKey,
            headerLen = RedisCacheEntryFrame.HeaderLength,
            maxMembers = options.MaxMembersPerTagRemoval,
        };

        var totalRemoved = 0;

        // Derive an iteration budget from the tag's current cardinality plus generous slack (×4) to
        // absorb concurrent writes. This bounds the loop under sustained concurrent tag writes —
        // without a cap, a hot tag could spin issuing unbounded EVALSHA round-trips indefinitely.
        var initialSize = await _database.HashLengthAsync(tagHashKey).ConfigureAwait(false);
        var batchSize = options.MaxMembersPerTagRemoval;
        var iterationBudget = (int)Math.Ceiling((double)(initialSize * 4 + batchSize) / batchSize);
        var iterations = 0;

        // Loop until the Lua script reports no remaining members. Each invocation processes a bounded
        // batch so no single script call blocks Redis for an unbounded duration (large tag sets).
        while (iterations < iterationBudget)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iterations++;

            var result = await scriptsLoader
                .EvaluateAsync(_database, CacheRemoveByTagScriptDefinition.Instance, scriptArgs, cancellationToken)
                .ConfigureAwait(false);

            var resultArray = (RedisResult[])result!;
            totalRemoved += (int)resultArray[0];
            var hasRemaining = (int)resultArray[1] == 1;

            if (!hasRemaining)
            {
                break;
            }
        }

        if (iterations >= iterationBudget)
        {
            _logger.LogRemoveByTagBudgetExceeded(tag, iterationBudget, totalRemoved);
        }

        return totalRemoved;
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

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Clear only this cache instance's keyspace (its KeyPrefix), not the whole Redis database: a shared Redis
        // may host other caches and application data. When no KeyPrefix is configured the pattern is "*", which
        // clears the database — unavoidable for an unprefixed cache, and documented as such.
        await _RemoveByPatternAsync($"{_keyPrefix}*", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Helpers

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : string.Concat(_keyPrefix, key);
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
            _logger.LogDeserializationFailed(e, redisValue, typeof(T).FullName);
            throw;
        }
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
                _logger.LogDeserializationFailed(e, redisValue, typeof(T).FullName);
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
            slidingExpiration: null
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
            entry.Tags
        );

        var hasTags = entry.Tags is { Count: > 0 };
        var hasRemovedTags = entry.RemovedTags is { Count: > 0 };
        var hasExpectedStamp = entry.ExpectedConcurrencyStamp is not null;
        var expectedValue = RedisValue.EmptyString;

        if (hasExpectedStamp && !_TryDecodeConcurrencyStamp(entry.ExpectedConcurrencyStamp!, out expectedValue))
        {
            return false;
        }

        // Untagged writes with no prior tags keep the plain SET path: zero hot-path regression.
        if (!hasTags && !hasRemovedTags && !hasExpectedStamp)
        {
            await _database.StringSetAsync(redisKey, redisValue, expiresIn).ConfigureAwait(false);
            return true;
        }

        _EnsureTaggingClusterSupported();

        // Tagged write: one atomic script does the SET plus the reverse-tag-index reconciliation (HSET the
        // current tags with the physical-expiry version, GT-extend the tag hash TTLs, HDEL dropped tags).
        var keyTtlMs = _ToPositiveMilliseconds(expiresIn);
        var tagTtlMs = Math.Max(_ToPositiveMilliseconds(entry.PhysicalExpiresAt.Subtract(now)), keyTtlMs);
        var physicalMs = RedisCacheEntryFrame.ToUnixTimeMilliseconds(entry.PhysicalExpiresAt);

        var result = await scriptsLoader
            .EvaluateAsync(
                _database,
                CacheTaggedSetScriptDefinition.Instance,
                new
                {
                    key = (RedisKey)redisKey,
                    value = (RedisValue)redisValue,
                    expectedValue = hasExpectedStamp ? expectedValue : RedisValue.EmptyString,
                    keyTtlMs,
                    tagTtlMs,
                    physicalMs = (RedisValue)physicalMs.ToString(CultureInfo.InvariantCulture),
                    tags = hasTags ? (RedisValue)RedisCacheEntryFrame.EncodeTags(entry.Tags!) : RedisValue.EmptyString,
                    removedTags = hasRemovedTags
                        ? (RedisValue)RedisCacheEntryFrame.EncodeTags(entry.RemovedTags!)
                        : RedisValue.EmptyString,
                    tagPrefix = (RedisValue)string.Concat(_keyPrefix, _TagNamespace),
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
        const string Prefix = "b64:";

        if (!stamp.StartsWith(Prefix, StringComparison.Ordinal))
        {
            value = RedisValue.Null;
            return false;
        }

        try
        {
            value = Convert.FromBase64String(stamp[Prefix.Length..]);
            return true;
        }
        catch (FormatException)
        {
            value = RedisValue.Null;
            return false;
        }
    }

    private string _GetTagHashKey(string tag) => string.Concat(_keyPrefix, _TagNamespace, tag);

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

            return new CacheStoreEntry<T>(
                Found: true,
                IsNull: frame.IsNull,
                Value: value,
                // For sliding entries the embedded logical timestamp goes stale after a metadata-only re-arm
                // (KeyExpire bumps the key TTL but does not rewrite the frame), so reporting it would make the
                // coordinator treat a live, recently-touched key as logically expired and spuriously re-run the
                // factory. Drop it and let freshness rely on key existence + the physical cap, mirroring the
                // other sliding read paths (_RedisValueToCacheValueAsync, _RedisValueIsLogicallyPresent).
                LogicalExpiresAt: frame.SlidingExpiration.HasValue ? null : frame.LogicalExpiresAt,
                PhysicalExpiresAt: frame.PhysicalExpiresAt,
                SlidingExpiration: frame.SlidingExpiration
            )
            {
                EagerRefreshAt = frame.EagerRefreshAt,
                ETag = frame.ETag,
                LastModifiedAt = frame.LastModifiedAt,
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

    private bool _RedisValueIsLogicallyPresent(RedisValue redisValue)
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
        return !_IsExpired(frame.PhysicalExpiresAt, now)
            && (frame.SlidingExpiration.HasValue || !_IsExpired(frame.LogicalExpiresAt, now));
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
            // Manual slot grouping avoids LINQ Lookup allocation + per-group materialization.
            var slotBuckets = new Dictionary<int, List<RedisKey>>();

            foreach (var key in keys)
            {
                var slot = options.ConnectionMultiplexer.HashSlot(key);

                if (!slotBuckets.TryGetValue(slot, out var bucket))
                {
                    bucket = [];
                    slotBuckets[slot] = bucket;
                }

                bucket.Add(key);
            }

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
        Message = "Unable to deserialize value {Value} to type {Type}"
    )]
    public static partial void LogDeserializationFailed(
        this ILogger logger,
        Exception exception,
        RedisValue value,
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
        EventName = "RemoveByTagBudgetExceeded",
        Level = LogLevel.Warning,
        Message = "RemoveByTagAsync for tag '{Tag}' hit the iteration budget ({Budget} batches) and stopped early after removing {TotalRemoved} entries; keys re-added during the sweep may not have been removed."
    )]
    public static partial void LogRemoveByTagBudgetExceeded(
        this ILogger logger,
        string tag,
        int budget,
        int totalRemoved
    );
}
