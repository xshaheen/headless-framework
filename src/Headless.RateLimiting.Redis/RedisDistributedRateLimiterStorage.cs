// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.RateLimiting;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.RateLimiting.Redis;

public sealed class RedisDistributedRateLimiterStorage(
    IConnectionMultiplexer multiplexer,
    HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedRateLimiterStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async Task<long> GetHitCountsAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var redisValue = await Db.StringGetAsync(resource).ConfigureAwait(false);

        return redisValue.HasValue ? (long)redisValue : 0;
    }

    public Task<long> IncrementAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsPositive(ttl);

        return scriptsLoader.IncrementAsync(Db, resource, 1, ttl, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
