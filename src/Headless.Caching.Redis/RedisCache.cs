// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.ObjectModel;
using System.Globalization;
using Headless.Checks;
using Headless.Redis;
using Headless.Serializer;
using Headless.Threading;
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
/// <para>
/// <b>Limitation:</b> The literal string "@@NULL" cannot be stored as a cache value since it is used
/// internally as a sentinel to distinguish null from missing keys.
/// </para>
/// </remarks>
public sealed class RedisCache(
    ISerializer serializer,
    TimeProvider timeProvider,
    RedisCacheOptions options,
    HeadlessRedisScriptsLoader scriptsLoader,
    ILogger<RedisCache>? logger = null
) : IDistributedCache, IDisposable
{
    /// <summary>
    /// Sentinel value used to distinguish null from missing keys in Redis.
    /// WARNING: The literal string "@@NULL" cannot be stored as a cache value.
    /// </summary>
    private static readonly RedisValue _NullValue = "@@NULL";
    private const int _BatchSize = 250;

    private readonly ILogger _logger = logger ?? NullLogger<RedisCache>.Instance;
    private readonly string _keyPrefix = options.KeyPrefix ?? "";
    private readonly KeyedAsyncLock _keyedLock = new();

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
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
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

        return await _SetInternalAsync(_GetKey(key), value, expiration);
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

        foreach (var kvp in value)
        {
            Argument.IsNotNullOrEmpty(kvp.Key);
            pairs[index++] = new KeyValuePair<RedisKey, RedisValue>(_GetKey(kvp.Key), _ToRedisValue(kvp.Value));
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

        return await _SetInternalAsync(_GetKey(key), value, expiration, When.NotExists);
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

        return await _SetInternalAsync(_GetKey(key), value, expiration, When.Exists);
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

        await scriptsLoader.LoadScriptsAsync().ConfigureAwait(false);

        var redisValue = _ToRedisValue(value);
        var expectedValue = _ToRedisValue(expected);

        var expiresMs = _GetExpirationMilliseconds(expiration);
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var redisResult = await _database
            .ScriptEvaluateAsync(
                scriptsLoader.ReplaceIfEqualScript!,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value = redisValue,
                    expected = expectedValue,
                    expires = expiresArg,
                }
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

        await scriptsLoader.LoadScriptsAsync().ConfigureAwait(false);

        var expiresMs = _GetExpirationMilliseconds(expiration);
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await _database
            .ScriptEvaluateAsync(
                scriptsLoader.IncrementWithExpireScript!,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value = amount,
                    expires = expiresArg,
                }
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

        await scriptsLoader.LoadScriptsAsync().ConfigureAwait(false);

        var expiresMs = _GetExpirationMilliseconds(expiration);
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await _database
            .ScriptEvaluateAsync(
                scriptsLoader.IncrementWithExpireScript!,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value = amount,
                    expires = expiresArg,
                }
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

        await scriptsLoader.LoadScriptsAsync().ConfigureAwait(false);

        var expiresMs = _GetExpirationMilliseconds(expiration);
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await _database
            .ScriptEvaluateAsync(
                scriptsLoader.SetIfHigherScript!,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value,
                    expires = expiresArg,
                }
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

        await scriptsLoader.LoadScriptsAsync().ConfigureAwait(false);

        var expiresMs = _GetExpirationMilliseconds(expiration);
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await _database
            .ScriptEvaluateAsync(
                scriptsLoader.SetIfHigherScript!,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value,
                    expires = expiresArg,
                }
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

        await scriptsLoader.LoadScriptsAsync().ConfigureAwait(false);

        var expiresMs = _GetExpirationMilliseconds(expiration);
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await _database
            .ScriptEvaluateAsync(
                scriptsLoader.SetIfLowerScript!,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value,
                    expires = expiresArg,
                }
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

        await scriptsLoader.LoadScriptsAsync().ConfigureAwait(false);

        var expiresMs = _GetExpirationMilliseconds(expiration);
        var expiresArg = expiresMs ?? RedisValue.EmptyString;

        var result = await _database
            .ScriptEvaluateAsync(
                scriptsLoader.SetIfLowerScript!,
                new
                {
                    key = (RedisKey)_GetKey(key),
                    value,
                    expires = expiresArg,
                }
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
        return _RedisValueToCacheValue<T>(redisValue);
    }

    public async ValueTask<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
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
            return ReadOnlyDictionary<string, CacheValue<T>>.Empty;
        }

        if (IsCluster)
        {
            var result = new Dictionary<string, CacheValue<T>>(redisKeys.Count, StringComparer.Ordinal);

            foreach (var hashSlotGroup in redisKeys.GroupBy(k => options.ConnectionMultiplexer.HashSlot(k)))
            {
                var hashSlotKeys = hashSlotGroup.ToArray();
                var values = await _database.StringGetAsync(hashSlotKeys, options.ReadMode).ConfigureAwait(false);

                for (var i = 0; i < hashSlotKeys.Length; i++)
                {
                    result[hashSlotKeys[i]!] = _RedisValueToCacheValue<T>(values[i]);
                }
            }

            return result.AsReadOnly();
        }
        else
        {
            var result = new Dictionary<string, CacheValue<T>>(redisKeys.Count, StringComparer.Ordinal);
            var values = await _database.StringGetAsync([.. redisKeys], options.ReadMode).ConfigureAwait(false);

            for (var i = 0; i < redisKeys.Count; i++)
            {
                result[redisKeys[i]!] = _RedisValueToCacheValue<T>(values[i]);
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
        var pattern = $"{_GetKey(prefix)}*";
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
                keys.Add(key!);
            }
        }

        return keys;
    }

    public async ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return await _database.KeyExistsAsync(_GetKey(key));
    }

    public async ValueTask<long> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        var count = await GetLongCountAsync(prefix, cancellationToken).ConfigureAwait(false);
        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    public async ValueTask<long> GetLongCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var endpoints = options.ConnectionMultiplexer.GetEndPoints();

        if (endpoints.Length is 0)
        {
            return 0;
        }

        if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(_keyPrefix))
        {
            long total = 0;

            foreach (var endpoint in endpoints)
            {
                var server = options.ConnectionMultiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                try
                {
                    total += await server.DatabaseSizeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to read database size from {Endpoint}", server.EndPoint);
                }
            }

            return total;
        }

        const int chunkSize = 2500;
        var pattern = $"{_GetKey(prefix)}*";
        long count = 0;

        foreach (var endpoint in endpoints)
        {
            var server = options.ConnectionMultiplexer.GetServer(endpoint);

            if (server.IsReplica)
            {
                continue;
            }

            await foreach (
                var _ in server.KeysAsync(pattern: pattern, pageSize: chunkSize).WithCancellation(cancellationToken)
            )
            {
                count++;
            }
        }

        return count;
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        return await _database.KeyTimeToLiveAsync(_GetKey(key));
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

        return await _database.KeyDeleteAsync(_GetKey(key));
    }

    public async ValueTask<bool> RemoveIfEqualAsync<T>(
        string key,
        T? expected,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();

        await scriptsLoader.LoadScriptsAsync().ConfigureAwait(false);

        var expectedValue = _ToRedisValue(expected);
        var redisResult = await _database
            .ScriptEvaluateAsync(
                scriptsLoader.RemoveIfEqualScript!,
                new { key = (RedisKey)_GetKey(key), expected = expectedValue }
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
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Unable to delete {HashSlot} keys ({Keys})",
                            hashSlotGroup.Key,
                            hashSlotKeys
                        );
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to delete keys ({Keys})", batch);
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

        await options.ConnectionMultiplexer.FlushAllAsync();
    }

    #endregion

    #region Helpers

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : $"{_keyPrefix}{key}";
    }

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

    private T? _FromRedisValue<T>(RedisValue redisValue)
    {
        if (!redisValue.HasValue)
        {
            return default;
        }

        if (redisValue == _NullValue)
        {
            return default;
        }

        if (typeof(T) == typeof(string))
        {
            return (T?)(object?)redisValue.ToString();
        }

        return serializer.Deserialize<T>((byte[])redisValue!);
    }

    private CacheValue<T> _RedisValueToCacheValue<T>(RedisValue redisValue)
    {
        if (!redisValue.HasValue)
        {
            return CacheValue<T>.NoValue;
        }

        if (redisValue == _NullValue)
        {
            return CacheValue<T>.Null;
        }

        try
        {
            var value = _FromRedisValue<T>(redisValue);
            return new CacheValue<T>(value, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to deserialize value {Value} to type {Type}", redisValue, typeof(T).FullName);
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
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to deserialize value {Value} to type {Type}",
                    redisValue,
                    typeof(T).FullName
                );
                throw;
            }
        }

        return new CacheValue<ICollection<T>>(result, result.Count > 0);
    }

    private async Task<bool> _SetInternalAsync<T>(
        string key,
        T value,
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
        var redisValue = _ToRedisValue(value);

        return await _database.StringSetAsync(key, redisValue, expiresIn, when).ConfigureAwait(false);
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

    private long? _GetExpirationMilliseconds(TimeSpan? expiresIn)
    {
        if (!expiresIn.HasValue || expiresIn.Value == TimeSpan.MaxValue)
        {
            return null;
        }

        var expiresAt = timeProvider.GetUtcNow().UtcDateTime.SafeAdd(expiresIn.Value);

        if (expiresAt == DateTime.MaxValue)
        {
            return null;
        }

        return (long)expiresIn.Value.TotalMilliseconds;
    }

    private const long _MaxUnixEpochMilliseconds = 253_402_300_799_999L; // 9999-12-31T23:59:59.999Z

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

        if (highestExpirationInMs >= _MaxUnixEpochMilliseconds)
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
            _logger.LogTrace("Removed {ExpiredValues} expired values for key: {Key}", expiredValues, key);
        }
    }

    private async Task<int> _FlushAllAsync()
    {
        var endpoints = options.ConnectionMultiplexer.GetEndPoints();

        if (endpoints.Length is 0)
        {
            return 0;
        }

        long deleted = 0;

        foreach (var endpoint in endpoints)
        {
            var server = options.ConnectionMultiplexer.GetServer(endpoint);

            if (server.IsReplica)
            {
                continue;
            }

            try
            {
                var dbSize = await server.DatabaseSizeAsync().ConfigureAwait(false);
                await server.FlushDatabaseAsync().ConfigureAwait(false);
                deleted += dbSize;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to flush database on {Endpoint}", server.EndPoint);
            }
        }

        return (int)deleted;
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

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        _keyedLock.Dispose();
    }
}
