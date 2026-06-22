// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching;

/// <summary>Atomically increments a value and sets expiration in a single operation.</summary>
internal sealed class IncrementWithExpireScriptDefinition : RedisScriptDefinition
{
    public static IncrementWithExpireScriptDefinition Instance { get; } = new();

    private IncrementWithExpireScriptDefinition()
        : base(
            // REVIEW(#1): NEEDS INTEGRATION VERIFICATION (Docker)
            // math.modf returns (intpart, frac); branching on the first return value means the
            // condition was true only when intpart == 0 (i.e. |value| < 1), which is exactly
            // backwards. Fix: capture the fractional part and branch on it.
            """
            local intpart, frac = math.modf(@value)
            if frac == 0 then
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
