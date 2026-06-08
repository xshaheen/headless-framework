// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Caching.Distributed;

namespace Headless.Jobs.Interfaces;

/// <summary>
/// Cron-expression caching seam for the durable path. This is the caching half of the former
/// <c>IJobsRedisContext</c>; node membership/liveness now lives in <c>Headless.Coordination</c>.
/// </summary>
internal interface IJobsCacheContext
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
}
