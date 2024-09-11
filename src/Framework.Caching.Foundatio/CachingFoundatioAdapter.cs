// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Foundatio.Caching;
using Framework.Kernel.Checks;

namespace Framework.Caching;

public sealed class CachingFoundatioAdapter(ICacheClient cacheClient) : ICache
{
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

        return cacheClient.SetAsync(key, value, expiration);
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

        return cacheClient.SetAllAsync(value, expiration);
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

        return cacheClient.AddAsync(key, value, expiration);
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

        return cacheClient.ReplaceAsync(key, value, expiration);
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

        return cacheClient.ReplaceIfEqualAsync(key, value, expected, expiration);
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

        return cacheClient.IncrementAsync(key, amount, expiration);
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

        return cacheClient.IncrementAsync(key, amount, expiration);
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

        return cacheClient.SetIfHigherAsync(key, value, expiration);
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

        return cacheClient.SetIfHigherAsync(key, value, expiration);
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

        return cacheClient.SetIfLowerAsync(key, value, expiration);
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

        return cacheClient.SetIfLowerAsync(key, value, expiration);
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

        return cacheClient.ListAddAsync(key, value, expiration);
    }

    #endregion

    public async Task<IDictionary<string, CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        var results = await cacheClient.GetAllAsync<T>(cacheKeys);

        return results.ToDictionary(x => x.Key, x => _Map(x.Value), StringComparer.Ordinal);
    }

    public Task<IDictionary<string, CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public async Task<CacheValue<T>> GetAsync<T>(string cacheKey, CancellationToken cancellationToken = default)
    {
        var result = await cacheClient.GetAsync<T>(cacheKey);

        return _Map(result);
    }

    public Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cacheClient.ExistsAsync(cacheKey);
    }

    public Task<TimeSpan?> GetExpirationAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cacheClient.GetExpirationAsync(cacheKey);
    }

    public async Task<CacheValue<ICollection<T>>> GetSetAsync<T>(string key, int? pageIndex = null, int pageSize = 100)
    {
        var result = await cacheClient.GetListAsync<T>(key, pageIndex, pageSize);

        return new CacheValue<ICollection<T>>(result.Value, result.HasValue);
    }

    public Task<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        return cacheClient.RemoveAsync(cacheKey);
    }

    public Task<bool> RemoveIfEqualAsync<T>(string cacheKey, T expected)
    {
        return cacheClient.RemoveIfEqualAsync(cacheKey, expected);
    }

    public Task<int> RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default)
    {
        return cacheClient.RemoveAllAsync(cacheKeys);
    }

    public Task<int> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        return cacheClient.RemoveByPrefixAsync(prefix);
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

        return cacheClient.ListRemoveAsync(key, value, expiration);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return cacheClient.RemoveAllAsync();
    }

    private static CacheValue<T> _Map<T>(Foundatio.Caching.CacheValue<T> x)
    {
        return new CacheValue<T>(x.Value, x.HasValue);
    }
}
