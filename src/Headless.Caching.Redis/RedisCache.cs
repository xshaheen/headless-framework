// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
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
    ILogger<RedisCache>? logger = null
) : IRemoteCache, IFactoryCacheStore, IDisposable
{
    /// <summary>Legacy null sentinel retained only for raw pre-envelope payloads and collection entries.</summary>
    private static readonly RedisValue _NullValue = "@@NULL";
    private const int _BatchSize = 250;
    private const double _SlidingRearmThreshold = 0.5d;

    private readonly ILogger _logger = logger ?? NullLogger<RedisCache>.Instance;
    private readonly string _keyPrefix = options.KeyPrefix ?? "";
    private readonly FactoryCacheCoordinator _coordinator = new(timeProvider, logger);

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

            foreach (var slotGroup in pairs.GroupBy(p => options.ConnectionMultiplexer.HashSlot(p.Key)))
            {
                var count = await _SetAllInternalAsync(slotGroup.ToArray(), expiration).ConfigureAwait(false);
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

            foreach (
                var hashSlotGroup in originalKeys
                    .Select((k, i) => (Original: k, Redis: redisKeys[i]))
                    .GroupBy(x => options.ConnectionMultiplexer.HashSlot(x.Redis))
            )
            {
                var pairs = hashSlotGroup.ToArray();
                var hashSlotRedisKeys = pairs.Select(x => x.Redis).ToArray();
                var values = await _database.StringGetAsync(hashSlotRedisKeys, options.ReadMode).ConfigureAwait(false);

                for (var i = 0; i < pairs.Length; i++)
                {
                    result[pairs[i].Original] = await _RedisValueToCacheValueAsync<T>(pairs[i].Redis, values[i])
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
                result[originalKeys[i]] = await _RedisValueToCacheValueAsync<T>(redisKeys[i], values[i])
                    .ConfigureAwait(false);
            }

            return result.AsReadOnly();
        }
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
                foreach (var hashSlotGroup in batch.GroupBy(k => options.ConnectionMultiplexer.HashSlot(k)))
                {
                    var hashSlotKeys = hashSlotGroup.ToArray();

                    try
                    {
                        var count = await _database.KeyDeleteAsync(hashSlotKeys).ConfigureAwait(false);
                        deleted += count;
                    }
                    catch (Exception e)
                    {
                        _logger.LogUnableToDeleteHashSlotKeys(e, hashSlotGroup.Key, hashSlotKeys);
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
                var key in server
                    .KeysAsync(pattern: $"{_GetKey(prefix)}*", pageSize: _BatchSize)
                    .WithCancellation(cancellationToken)
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

        return (int)deleted;
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

        await options.ConnectionMultiplexer.FlushAllAsync().ConfigureAwait(false);
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

    private async ValueTask<CacheValue<T>> _RedisValueToCacheValueAsync<T>(RedisKey redisKey, RedisValue redisValue)
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

                await _TryRearmSlidingEntryAsync(redisKey, frame, now).ConfigureAwait(false);

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

    async ValueTask IFactoryCacheStore.SetEntryAsync<T>(
        string key,
        T? value,
        bool isNull,
        DateTime logicalExpiresAt,
        DateTime physicalExpiresAt,
        TimeSpan? slidingExpiration,
        CancellationToken cancellationToken
    )
        where T : default
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresIn = (slidingExpiration is null ? physicalExpiresAt : logicalExpiresAt).Subtract(now);
        var redisKey = _GetKey(key);

        if (expiresIn <= TimeSpan.Zero)
        {
            await _database.KeyDeleteAsync(redisKey).ConfigureAwait(false);
            return;
        }

        var valueSegment = isNull ? RedisValue.EmptyString : _ToRedisValue(value);
        var redisValue = RedisCacheEntryFrame.Encode(
            valueSegment,
            isNull,
            logicalExpiresAt,
            physicalExpiresAt,
            slidingExpiration
        );

        await _database.StringSetAsync(redisKey, redisValue, expiresIn).ConfigureAwait(false);
    }

    private async ValueTask<CacheStoreEntry<T>> _TryGetEntryAsync<T>(string key)
    {
        var redisValue = await _database.StringGetAsync(_GetKey(key), options.ReadMode).ConfigureAwait(false);

        if (!redisValue.HasValue)
        {
            return CacheStoreEntry<T>.NotFound;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
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
                LogicalExpiresAt: frame.LogicalExpiresAt,
                PhysicalExpiresAt: frame.PhysicalExpiresAt,
                SlidingExpiration: frame.SlidingExpiration
            );
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
            );
        }

        return new CacheStoreEntry<T>(
            Found: true,
            IsNull: false,
            Value: _FromRedisValue<T>(redisValue),
            LogicalExpiresAt: null,
            PhysicalExpiresAt: null,
            SlidingExpiration: null
        );
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

    private async ValueTask _TryRearmSlidingEntryAsync(RedisKey redisKey, RedisCacheEntryFrame.DecodedFrame frame, DateTime now)
    {
        if (frame.SlidingExpiration is not { } slidingExpiration || frame.PhysicalExpiresAt is not { } physicalExpiresAt)
        {
            return;
        }

        var remainingToCap = physicalExpiresAt - now;

        if (remainingToCap <= TimeSpan.Zero)
        {
            return;
        }

        var ttl = await _database.KeyTimeToLiveAsync(redisKey).ConfigureAwait(false);
        var rearmThreshold = TimeSpan.FromTicks((long)(slidingExpiration.Ticks * _SlidingRearmThreshold));

        if (ttl.HasValue && ttl.Value > rearmThreshold)
        {
            return;
        }

        var expiresIn = _Min(slidingExpiration, remainingToCap);

        try
        {
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
            foreach (var slotGroup in keys.GroupBy(k => options.ConnectionMultiplexer.HashSlot(k)))
            {
                var count = await _database.KeyDeleteAsync(slotGroup.ToArray()).ConfigureAwait(false);
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
}
