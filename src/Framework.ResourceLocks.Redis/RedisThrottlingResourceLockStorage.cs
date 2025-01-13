// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Checks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Framework.ResourceLocks.Redis;

public sealed class RedisThrottlingResourceLockStorage(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisThrottlingResourceLockStorage> logger
) : IThrottlingResourceLockStorage
{
    private bool _scriptsLoaded;
    private LoadedLuaScript? _incrementWithExpireScript;
    private readonly AsyncLock _loadScriptsLock = new();

    private const string _IncrementWithExpire = """
        if math.modf(@value) == 0 then
          local v = redis.call('incrby', @key, @value)
          if (@expires ~= nil and @expires ~= '') then
            redis.call('pexpire', @key, math.ceil(@expires))
          end
          return tonumber(v)
        else
          local v = redis.call('incrbyfloat', @key, @value)
          if (@expires ~= nil and @expires ~= '') then
            redis.call('pexpire', @key, math.ceil(@expires))
          end
          return tonumber(v)
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

            logger.LogTrace("Preparing Lua script for increment with expire");
            var incrementWithExpire = LuaScript.Prepare(_IncrementWithExpire);

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                logger.LogTrace("Loading Lua scripts to server: {@Endpoint}", endpoint);
                _incrementWithExpireScript = await incrementWithExpire.LoadAsync(server).AnyContext();
            }

            _scriptsLoaded = true;
            logger.LogTrace("Scripts loaded successfully in {Elapsed:g}", Stopwatch.GetElapsedTime(timestamp));
        }
    }

    public async ValueTask<long> GetHitCountsAsync(string resource, long defaultValue = 0)
    {
        var redisValue = await Db.StringGetAsync(resource);

        return redisValue.HasValue ? (long)redisValue : defaultValue;
    }

    public async ValueTask<long> IncrementAsync(string resource, TimeSpan ttl)
    {
        Argument.IsNotNullOrEmpty(resource);
        Argument.IsPositive(ttl);

        await LoadScriptsAsync();

        var result = await Db.ScriptEvaluateAsync(
            _incrementWithExpireScript!,
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
