// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Redis;
using Framework.ResourceLocks.RegularLocks;
using StackExchange.Redis;

namespace Framework.ResourceLocks.Redis;

public sealed class RedisResourceLockStorage(
    IConnectionMultiplexer multiplexer,
    FrameworkRedisScriptsLoader scriptsLoader
) : IResourceLockStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        return await Db.StringSetAsync(key, lockId, ttl, When.NotExists, CommandFlags.None);
    }

    public async Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        await scriptsLoader.LoadScriptsAsync();

        var redisResult = await Db.ScriptEvaluateAsync(
            scriptsLoader.ReplaceIfEqualScript!,
            _GetReplaceIfEqualParameters(key, newId, expectedId, newTtl)
        );

        var result = (int)redisResult;

        return result > 0;
    }

    public async Task<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        Argument.IsNotNullOrEmpty(key);

        await scriptsLoader.LoadScriptsAsync();

        var redisResult = await Db.ScriptEvaluateAsync(
            scriptsLoader.RemoveIfEqualScript!,
            new { key = (RedisKey)key, expected = expectedId }
        );

        var result = (int)redisResult;

        return result > 0;
    }

    public async Task<TimeSpan?> GetExpirationAsync(string key)
    {
        return await Db.KeyTimeToLiveAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await Db.KeyExistsAsync(key);
    }

    #region Helpers

    private static object _GetReplaceIfEqualParameters(RedisKey key, string value, string expected, TimeSpan? expires)
    {
        if (expires.HasValue)
        {
            return new
            {
                key,
                value,
                expected,
                expires = (int)expires.Value.TotalMilliseconds,
            };
        }

        return new
        {
            key,
            value,
            expected,
            expires = "",
        };
    }

    #endregion
}
