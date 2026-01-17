using Microsoft.Extensions.Caching.Distributed;

namespace Framework.Ticker.Utilities.Interfaces;

internal interface ITickerQRedisContext
{
    IDistributedCache DistributedCache { get; }

    bool HasRedisConnection { get; }
    Task<TResult[]?> GetOrSetArrayAsync<TResult>(
        string cacheKey,
        Func<CancellationToken, Task<TResult[]?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
        where TResult : class;

    Task<string[]> GetDeadNodesAsync();

    Task NotifyNodeAliveAsync();
}
