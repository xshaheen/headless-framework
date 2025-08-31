// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Redis;
using StackExchange.Redis;

namespace Framework.ResourceLocks.Redis;

public sealed class RedisThrottlingResourceLockStorage(
    IConnectionMultiplexer multiplexer,
    HeadlessRedisScriptsLoader scriptsLoader
) : IThrottlingResourceLockStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async Task<long> GetHitCountsAsync(string resource)
    {
        var redisValue = await Db.StringGetAsync(resource);

        return redisValue.HasValue ? (long)redisValue : 0;
    }

    public Task<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsPositive(ttl);

        return scriptsLoader.IncrementAsync(Db, resource, 1, ttl);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
