// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Redis;
using Framework.ResourceLocks.RegularLocks;
using StackExchange.Redis;

namespace Framework.ResourceLocks.Redis;

public sealed class RedisResourceLockStorage(
    IConnectionMultiplexer multiplexer,
    HeadlessRedisScriptsLoader scriptsLoader
) : IResourceLockStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        return await Db.StringSetAsync(key, lockId, ttl, When.NotExists, CommandFlags.None);
    }

    public Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        return scriptsLoader.ReplaceIfEqualAsync(Db, key, newId, expectedId, newTtl);
    }

    public Task<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        Argument.IsNotNullOrEmpty(key);

        return scriptsLoader.RemoveIfEqualAsync(Db, key, expectedId);
    }

    public async Task<TimeSpan?> GetExpirationAsync(string key)
    {
        return await Db.KeyTimeToLiveAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await Db.KeyExistsAsync(key);
    }
}
