// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Redis;
using StackExchange.Redis;

namespace Framework.ResourceLocks.Redis;

public sealed class RedisThrottlingResourceLockStorage(
    IConnectionMultiplexer multiplexer,
    FrameworkRedisScriptsLoader scriptsLoader
) : IThrottlingResourceLockStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async Task<long> GetHitCountsAsync(string resource)
    {
        var redisValue = await Db.StringGetAsync(resource);

        return redisValue.HasValue ? (long)redisValue : 0;
    }

    public async Task<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsPositive(ttl);

        await scriptsLoader.LoadScriptsAsync();

        var result = await Db.ScriptEvaluateAsync(
            scriptsLoader.IncrementWithExpireScript!,
            new
            {
                key = (RedisKey)resource,
                value = 1,
                expires = (int)ttl.TotalMilliseconds,
            }
        );

        return (long)result;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
