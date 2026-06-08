// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces;
using Microsoft.Extensions.Caching.Distributed;

namespace Headless.Jobs.Temps;

/// <summary>Default no-op caching context: no distributed cache, every lookup falls through to the factory.</summary>
internal sealed class NoOpJobsCacheContext : IJobsCacheContext
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
}
