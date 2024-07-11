namespace Framework.Caching;

public interface ICache
{
    ValueTask SetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> TrySetAsync<T>(
        string cacheKey,
        T cacheValue,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask SetAllAsync<T>(
        IDictionary<string, T> value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    );

    ValueTask RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask RemoveAllAsync(IEnumerable<string> cacheKeys, CancellationToken cancellationToken = default);

    ValueTask RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    ValueTask RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    ValueTask<T> GetAsync<T>(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask<IDictionary<string, T>> GetAllAsync<T>(
        IEnumerable<string> cacheKeys,
        CancellationToken cancellationToken = default
    );

    ValueTask<IDictionary<string, T>> GetByPrefixAsync<T>(string prefix, CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<string>> GetAllKeysByPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default
    );

    ValueTask<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default);

    ValueTask<int> GetCountAsync(string prefix = "", CancellationToken cancellationToken = default);

    ValueTask FlushAsync(CancellationToken cancellationToken = default);
}
