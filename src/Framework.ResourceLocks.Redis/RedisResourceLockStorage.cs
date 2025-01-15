// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Checks;
using Framework.ResourceLocks.RegularLocks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Framework.ResourceLocks.Redis;

public sealed class RedisResourceLockStorage(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisResourceLockStorage> logger
) : IResourceLockStorage
{
    private bool _scriptsLoaded;
    private LoadedLuaScript? _removeIfEqualScript;
    private LoadedLuaScript? _replaceIfEqualScript;
    private readonly AsyncLock _loadScriptsLock = new();

    private const string _ReplaceIfEqual = """
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

    private const string _RemoveIfEqual = """
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

        var timestamp = Stopwatch.GetTimestamp();
        using (await _loadScriptsLock.LockAsync())
        {
            if (_scriptsLoaded)
            {
                logger.LogTrace("Scripts already loaded inside lock {Elapsed:g}", Stopwatch.GetElapsedTime(timestamp));

                return;
            }
            logger.LogTrace("Preparing Lua scripts for remove if equal and replace if equal");
            var removeIfEqual = LuaScript.Prepare(_RemoveIfEqual);
            var replaceIfEqual = LuaScript.Prepare(_ReplaceIfEqual);

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                logger.LogInformation("Loading Lua scripts on server: {@EndPoint}", server.EndPoint);
                _removeIfEqualScript = await removeIfEqual.LoadAsync(server).AnyContext();
                _replaceIfEqualScript = await replaceIfEqual.LoadAsync(server).AnyContext();
            }

            _scriptsLoaded = true;
            logger.LogTrace("Scripts loaded successfully in {Elapsed:g}", Stopwatch.GetElapsedTime(timestamp));
        }
    }

    public async Task<bool> InsertAsync(string key, string lockId, TimeSpan? ttl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        return await Db.StringSetAsync(key, lockId, ttl, When.NotExists, CommandFlags.None);
    }

    public async Task<bool> ReplaceIfEqualAsync(string key, string expectedId, string newId, TimeSpan? newTtl = null)
    {
        Argument.IsNotNullOrEmpty(key);

        await LoadScriptsAsync();

        var redisResult = await Db.ScriptEvaluateAsync(
            _replaceIfEqualScript!,
            _GetReplaceIfEqualParameters(key, newId, expectedId, newTtl)
        );

        var result = (int)redisResult;

        return result > 0;
    }

    public async Task<bool> RemoveIfEqualAsync(string key, string expectedId)
    {
        Argument.IsNotNullOrEmpty(key);

        await LoadScriptsAsync();

        var redisResult = await Db.ScriptEvaluateAsync(
            _removeIfEqualScript!,
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
