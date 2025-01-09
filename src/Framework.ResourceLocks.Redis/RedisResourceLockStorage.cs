// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.ResourceLocks.Storage.RegularLocks;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Framework.ResourceLocks.Redis;

public sealed class RedisResourceLockStorage(IConnectionMultiplexer multiplexer) : IResourceLockStorage
{
    private bool _scriptsLoaded;
    private LoadedLuaScript? _removeIfEqual;
    private LoadedLuaScript? _replaceIfEqual;
    private readonly AsyncLock _loadScriptsLock = new();

    private const string _ReplaceIfEqualScript = """
        local currentVal = redis.call('get', @key)
        if (currentVal == false or currentVal == @expected) then
          if (@expires ~= nil and @expires ~= '') then
            return redis.call('set', @key, @value, 'PX', @expires) and 1 or 0
          else
            return redis.call('set', @key, @value) and 1 or 0
          end
        else
          return -1
        end
        """;

    private const string _RemoveIfEqualScript = """
        if redis.call('get', @key) == @expected then
          return redis.call('del', @key)
        else
          return 0
        end
        """;

    private IDatabase Db => multiplexer.GetDatabase();

    public async Task LoadScriptsAsync()
    {
        if (_scriptsLoaded)
        {
            return;
        }

        using (await _loadScriptsLock.LockAsync())
        {
            if (_scriptsLoaded)
            {
                return;
            }

            var removeIfEqual = LuaScript.Prepare(_RemoveIfEqualScript);
            var replaceIfEqual = LuaScript.Prepare(_ReplaceIfEqualScript);

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                _removeIfEqual = await removeIfEqual.LoadAsync(server).AnyContext();
                _replaceIfEqual = await replaceIfEqual.LoadAsync(server).AnyContext();
            }

            _scriptsLoaded = true;
        }
    }

    public async ValueTask<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        return await Db.StringSetAsync(key, lockId, ttl, When.NotExists, CommandFlags.None);
    }

    public async ValueTask<bool> ReplaceIfEqualAsync(string key, string lockId, string expected, TimeSpan? ttl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        await LoadScriptsAsync();

        var redisResult = await Db.ScriptEvaluateAsync(
            _replaceIfEqual!,
            _GetReplaceIfEqualParameters(key, lockId, expected, ttl)
        );

        var result = (int)redisResult;

        return result > 0;
    }

    public async ValueTask<bool> RemoveAsync(string key, string lockId)
    {
        Argument.IsNotNullOrEmpty(key);

        await LoadScriptsAsync();

        var redisResult = await Db.ScriptEvaluateAsync(_removeIfEqual!, new { key = (RedisKey)key, expected = lockId });

        var result = (int)redisResult;

        return result > 0;
    }

    public async ValueTask<TimeSpan?> GetExpirationAsync(string key)
    {
        return await Db.KeyTimeToLiveAsync(key);
    }

    public async ValueTask<bool> ExistsAsync(string key)
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
