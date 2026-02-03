// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

public sealed class RedisDistributedLockStorage(
    IConnectionMultiplexer multiplexer,
    HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedLockStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        return await Db.StringSetAsync(key, lockId, ttl, When.NotExists, CommandFlags.None);
    }

    public async ValueTask<bool> ReplaceIfEqualAsync(
        string key,
        string expectedId,
        string newId,
        TimeSpan? newTtl = null
    )
    {
        Argument.IsNotNullOrEmpty(key);

        return await scriptsLoader.ReplaceIfEqualAsync(Db, key, expectedId, newId, newTtl);
    }

    public async ValueTask<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        Argument.IsNotNullOrEmpty(key);

        return await scriptsLoader.RemoveIfEqualAsync(Db, key, expectedId);
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key) => await Db.KeyTimeToLiveAsync(key);

    public async ValueTask<bool> ExistsAsync(string key) => await Db.KeyExistsAsync(key);

    public async ValueTask<string?> GetAsync(string key)
    {
        var value = await Db.StringGetAsync(key);

        return value.HasValue ? value.ToString() : null;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> GetAllByPrefixAsync(string prefix)
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

    public async ValueTask<int> GetCountAsync(string prefix = "")
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
