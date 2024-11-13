// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Caching;
using Framework.Kernel.Checks;
using Framework.Serializer;
using StackExchange.Redis;

namespace Framework.Caching;

public sealed class RedisCachingFoundatioAdapter(
    ISerializer serializer,
    TimeProvider timeProvider,
    RedisCacheOptions options
) : ICache, IDisposable
{
    private readonly RedisCacheClient _cacheClient = _CacheClient(serializer, timeProvider, options);

    #region Update

    public Task<bool> UpsertAsync<T>(
        string key,
        T value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.SetAsync(key, value, expiration);
    }

    public Task<int> UpsertAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        var keys = value.ToDictionary(x => _GetKey(x.Key), x => x.Value, StringComparer.Ordinal);

        return _cacheClient.SetAllAsync(keys, expiration);
    }

    public Task<bool> TryInsertAsync<T>(
        string key,
        T value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.AddAsync(key, value, expiration);
    }

    public Task<bool> TryReplaceAsync<T>(
        string key,
        T value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.ReplaceAsync(key, value, expiration);
    }

    public Task<bool> TryReplaceIfEqualAsync<T>(
        string key,
        T value,
        T expected,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsNotNull(expected);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.ReplaceIfEqualAsync(key, value, expected, expiration);
    }

    public Task<double> IncrementAsync(
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

        return _cacheClient.IncrementAsync(key, amount, expiration);
    }

    public Task<long> IncrementAsync(
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

        return _cacheClient.IncrementAsync(key, amount, expiration);
    }

    public Task<double> SetIfHigherAsync(
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

        return _cacheClient.SetIfHigherAsync(key, value, expiration);
    }

    public Task<long> SetIfHigherAsync(
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

        return _cacheClient.SetIfHigherAsync(key, value, expiration);
    }

    public Task<double> SetIfLowerAsync(
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

        return _cacheClient.SetIfLowerAsync(key, value, expiration);
    }

    public Task<long> SetIfLowerAsync(
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

        return _cacheClient.SetIfLowerAsync(key, value, expiration);
    }

    public Task<long> SetAddAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.ListAddAsync(key, value, expiration);
    }

    #endregion

    #region Get

    public async Task<CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        var result = await _cacheClient.GetAsync<T>(key);

        return _Map(result);
    }

    public async Task<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        var results = await _cacheClient.GetAllAsync<T>(cacheKeys);

        return results.ToDictionary(x => x.Key, x => _Map(x.Value), StringComparer.Ordinal);
    }

    public async Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var keys = await GetAllKeysByPrefixAsync(prefix, cancellationToken);

        return await GetAllAsync<T>(keys, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        const int chunkSize = 2500;
        var regex = $"{prefix}*";

        var index = 0;
        var (cursor, chunkKeys) = await _ScanKeysAsync(regex, index, chunkSize);

        List<string> keys = [.. chunkKeys];

        while (chunkKeys.Length != 0 || index < chunkSize)
        {
            index += chunkSize;
            (cursor, chunkKeys) = await _ScanKeysAsync(regex, cursor, chunkSize);
        }

        return keys;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.ExistsAsync(key);
    }

    public async Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        var key = await GetAllKeysByPrefixAsync(prefix, cancellationToken);

        return key.Count;
    }

    public Task<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.GetExpirationAsync(key);
    }

    public async Task<CacheValue<ICollection<T>>> GetSetAsync<T>(
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

        var result = await _cacheClient.GetListAsync<T>(key, pageIndex, pageSize);

        return new(result.Value, result.HasValue);
    }

    #endregion

    #region Remove

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.RemoveAsync(key);
    }

    public Task<bool> RemoveIfEqualAsync<T>(string key, T expected, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.RemoveIfEqualAsync(key, expected);
    }

    public Task<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(cacheKeys);
        cancellationToken.ThrowIfCancellationRequested();
        cacheKeys = cacheKeys.Select(_GetKey);

        return _cacheClient.RemoveAllAsync(cacheKeys);
    }

    public Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(prefix);
        cancellationToken.ThrowIfCancellationRequested();
        prefix = _GetKey(prefix);

        return _cacheClient.RemoveByPrefixAsync(prefix);
    }

    public Task<long> SetRemoveAsync<T>(
        string key,
        IEnumerable<T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(value);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.ListRemoveAsync(key, value, expiration);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _cacheClient.RemoveAllAsync();
    }

    #endregion

    #region Helpers

    /// <summary>Scan for keys matching the prefix</summary>
    /// <remarks>SCAN, SSCAN, HSCAN and ZSCAN return a two elements multi-bulk reply, where the first element
    /// is a string representing an unsigned 64 bit number (the cursor), and the second element is a multi-bulk
    /// with an array of elements.</remarks>
    private async Task<(int Curser, string[] Keys)> _ScanKeysAsync(string prefix, int index, int chunkSize)
    {
        var result = await _cacheClient.Database.ExecuteAsync("scan", index, "match", prefix, "count", chunkSize);
        var value = (RedisResult[])result!;
        return ((int)value[0], (string[])value[1]!);
    }

    public void Dispose()
    {
        _cacheClient.Dispose();
    }

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(options.KeyPrefix) ? key : options.KeyPrefix + key;
    }

    private static CacheValue<T> _Map<T>(Foundatio.Caching.CacheValue<T> x)
    {
        return new(x.Value, x.HasValue);
    }

    private static RedisCacheClient _CacheClient(
        ISerializer serializer,
        TimeProvider timeProvider,
        RedisCacheOptions options
    )
    {
        var redisCacheClientOptions = new RedisCacheClientOptions
        {
            TimeProvider = timeProvider,
            Serializer = new FoundationSerializerAdapter(serializer),
            ConnectionMultiplexer = options.ConnectionMultiplexer,
            ShouldThrowOnSerializationError = true,
            ReadMode = options.ReadMode,
        };

        return new(redisCacheClientOptions);
    }

    #endregion
}
