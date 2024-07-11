namespace Framework.Caching;

public static class CacheExtensions
{
    public static async Task<T> GetOrAddAsync<T>(
        this ICache cache,
        string key,
        Func<Task<T>> factory,
        TimeSpan expiration,
        CancellationToken cancellationToken = default
    )
    {
        var value = await cache.GetAsync<T?>(key, cancellationToken);

        if (value is not null)
        {
            return value;
        }

        value = await factory();
        await cache.SetAsync(key, value, expiration, cancellationToken);

        return value;
    }
}
