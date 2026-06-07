// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching;

/// <summary>Atomically increments a value and sets expiration in a single operation.</summary>
internal sealed class IncrementWithExpireScriptDefinition : RedisScriptDefinition
{
    public static IncrementWithExpireScriptDefinition Instance { get; } = new();

    private IncrementWithExpireScriptDefinition()
        : base(
            """
            if math.modf(@value) == 0 then
              redis.call('incrby', @key, @value)
            else
              redis.call('incrbyfloat', @key, @value)
            end
            if (@expires ~= nil and @expires ~= '') then
              redis.call('pexpire', @key, math.ceil(@expires))
            end
            return redis.call('get', @key)
            """
        ) { }
}
