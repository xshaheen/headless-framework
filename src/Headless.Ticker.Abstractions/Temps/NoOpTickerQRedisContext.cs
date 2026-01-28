using Headless.Ticker.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace Headless.Ticker.Temps;

internal class NoOpTickerQRedisContext : ITickerQRedisContext
{
    public IDistributedCache DistributedCache => null!;
    public bool HasRedisConnection => false;

    public Task<TResult[]?> GetOrSetArrayAsync<TResult>(
        string cacheKey,
        Func<CancellationToken, Task<TResult[]?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
        where TResult : class
    {
        return factory(cancellationToken);
    }

    public Task<string[]> GetDeadNodesAsync()
    {
        return Task.FromResult(Array.Empty<string>());
    }

    public Task NotifyNodeAliveAsync()
    {
        return Task.CompletedTask;
    }
}
