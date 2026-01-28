// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

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

    public async Task<string?> GetAsync(string key)
    {
        var value = await Db.StringGetAsync(key);

        return value.HasValue ? value.ToString() : null;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string prefix)
    {
        var server = multiplexer.GetServers()[0];
        var pattern = string.IsNullOrEmpty(prefix) ? "*" : $"{prefix}*";

        var keys = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: pattern, pageSize: 1000))
        {
            keys.Add(key);
        }

        if (keys.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var keyArray = keys.ToArray();
        var values = await Db.StringGetAsync(keyArray);
        var result = new Dictionary<string, string>(keyArray.Length, StringComparer.Ordinal);

        for (var i = 0; i < keyArray.Length; i++)
        {
            if (values[i].HasValue)
            {
                result[keyArray[i].ToString()] = values[i].ToString();
            }
        }

        return result;
    }

    public async Task<int> GetCountAsync(string prefix = "")
    {
        var server = multiplexer.GetServers().First();
        var pattern = string.IsNullOrEmpty(prefix) ? "*" : $"{prefix}*";

        var count = 0;
        await foreach (var _ in server.KeysAsync(pattern: pattern))
        {
            count++;
        }

        return count;
    }
}
