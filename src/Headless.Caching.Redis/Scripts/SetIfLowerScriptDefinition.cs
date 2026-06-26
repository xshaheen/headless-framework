// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching;

/// <summary>
/// Sets a value only if it's lower than the current value. Creates the key if it doesn't exist. Returns the
/// difference <c>(old - new)</c> when an existing value was lowered, the newly stored value when the key was
/// absent, or <c>0</c> when the store was left unchanged — matching the <c>ICache.SetIfLowerAsync</c> contract.
/// </summary>
internal sealed class SetIfLowerScriptDefinition : RedisScriptDefinition
{
    public static SetIfLowerScriptDefinition Instance { get; } = new();

    private SetIfLowerScriptDefinition()
        : base(
            // One script serves both the long and double overloads, so the difference is returned as a string:
            // integer-valued differences use %d (avoids %.14g scientific notation that long.Parse would reject for
            // large values), and fractional differences use tostring so the double overload keeps its precision.
            // A Lua-number reply is never returned directly because Redis would truncate it to an integer.
            """
            local c = tonumber(redis.call('get', @key))
            local v = tonumber(@value)
            if c then
              if v < c then
                redis.call('set', @key, @value)
                if (@expires ~= nil and @expires ~= '') then
                  redis.call('pexpire', @key, math.ceil(@expires))
                end
                local d = c - v
                if d == math.floor(d) then
                  return string.format('%d', d)
                end
                return tostring(d)
              end
              return 0
            else
              redis.call('set', @key, @value)
              if (@expires ~= nil and @expires ~= '') then
                redis.call('pexpire', @key, math.ceil(@expires))
              end
              return redis.call('get', @key)
            end
            """
        ) { }
}
