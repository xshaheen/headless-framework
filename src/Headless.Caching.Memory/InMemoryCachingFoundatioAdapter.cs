// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Foundatio.Caching;
using Headless.Checks;

namespace Headless.Caching;

public sealed class InMemoryCachingFoundatioAdapter(TimeProvider timeProvider, InMemoryCacheOptions options)
    : IInMemoryCache,
        IDisposable
{
    private readonly InMemoryCacheClient _cacheClient = _CacheClient(timeProvider, options);

    #region Update

    public Task<bool> UpsertAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
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
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.AddAsync(key, value, expiration);
    }

    public Task<bool> TryReplaceAsync<T>(
        string key,
        T? value,
        TimeSpan? expiration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(key);
        Argument.IsPositive(expiration);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.ReplaceAsync(key, value, expiration);
    }

    public Task<bool> TryReplaceIfEqualAsync<T>(
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

    public async Task<Caching.CacheValue<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        var result = await _cacheClient.GetAsync<T>(key);

        return _Map(result);
    }

    public async Task<IDictionary<string, Caching.CacheValue<T>>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    )
    {
        var results = await _cacheClient.GetAllAsync<T>(cacheKeys);

        return results.ToDictionary(x => x.Key, x => _Map(x.Value), StringComparer.Ordinal);
    }

    public Task<IDictionary<string, Caching.CacheValue<T>>> GetByPrefixAsync<T>(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(prefix);

        return GetAllAsync<T>(keys, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(_GetKeys(prefix));
    }

    private IReadOnlyList<string> _GetKeys(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return _cacheClient.Keys.AsIReadOnlyList();
        }

        prefix = _GetKey(prefix);
        return _cacheClient.Keys.Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.ExistsAsync(key);
    }

    public Task<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(prefix))
        {
            return Task.FromResult(_cacheClient.Keys.Count);
        }

        prefix = _GetKey(prefix);
        var count = _cacheClient.Keys.Count(x => x.StartsWith(prefix, StringComparison.Ordinal));

        return Task.FromResult(count);
    }

    public Task<TimeSpan?> GetExpirationAsync(string key, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        key = _GetKey(key);

        return _cacheClient.GetExpirationAsync(key);
    }

    public async Task<Caching.CacheValue<ICollection<T>>> GetSetAsync<T>(
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

    public Task<bool> RemoveIfEqualAsync<T>(string key, T? expected, CancellationToken cancellationToken = default)
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
        TimeSpan? expiration,
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

    public void Dispose()
    {
        _cacheClient.Dispose();
    }

    private string _GetKey(string key)
    {
        return string.IsNullOrEmpty(options.KeyPrefix) ? key : options.KeyPrefix + key;
    }

    private static InMemoryCacheClient _CacheClient(TimeProvider timeProvider, InMemoryCacheOptions options)
    {
        return new(
            new InMemoryCacheClientOptions
            {
                TimeProvider = timeProvider,
                MaxItems = options.MaxItems,
                CloneValues = options.CloneValues,
                ShouldThrowOnSerializationError = true,
            }
        );
    }

    private static Caching.CacheValue<T> _Map<T>(Foundatio.Caching.CacheValue<T> x)
    {
        return new(x.Value, x.HasValue);
    }

    #endregion
}
