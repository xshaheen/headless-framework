// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Framework.ResourceLocks.Redis;

public sealed class RedisThrottlingResourceLockStorage(IConnectionMultiplexer multiplexer)
    : IThrottlingResourceLockStorage
{
    private bool _scriptsLoaded;
    private LoadedLuaScript? _incrementWithExpire;
    private readonly AsyncLock _loadScriptsLock = new();

    private const string _IncrementWithExpireScript = """
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

        using (await _loadScriptsLock.LockAsync())
        {
            if (_scriptsLoaded)
            {
                return;
            }

            var incrementWithExpire = LuaScript.Prepare(_IncrementWithExpireScript);

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);

                if (server.IsReplica)
                {
                    continue;
                }

                _incrementWithExpire = await incrementWithExpire.LoadAsync(server).AnyContext();
            }

            _scriptsLoaded = true;
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
            _incrementWithExpire!,
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
