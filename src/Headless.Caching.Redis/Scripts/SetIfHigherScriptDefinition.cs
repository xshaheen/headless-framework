// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching.Scripts;

/// <summary>Sets a value only if it's higher than the current value. Creates the key if it doesn't exist.</summary>
internal sealed class SetIfHigherScriptDefinition : RedisScriptDefinition
{
    public static SetIfHigherScriptDefinition Instance { get; } = new();

    private SetIfHigherScriptDefinition()
        : base(
            """
            local c = tonumber(redis.call('get', @key))
            local v = tonumber(@value)
            if c then
              if v > c then
                redis.call('set', @key, @value)
                if (@expires ~= nil and @expires ~= '') then
                  redis.call('pexpire', @key, math.ceil(@expires))
                end
              end
            else
              redis.call('set', @key, @value)
              if (@expires ~= nil and @expires ~= '') then
                redis.call('pexpire', @key, math.ceil(@expires))
              end
            end
            return redis.call('get', @key)
            """
        ) { }
}
