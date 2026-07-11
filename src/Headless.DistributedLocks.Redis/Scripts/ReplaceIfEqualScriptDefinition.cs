// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically replaces a value only if it matches the expected value.</summary>
internal sealed class ReplaceIfEqualScriptDefinition : RedisScriptDefinition
{
    public static ReplaceIfEqualScriptDefinition Instance { get; } = new();

    private ReplaceIfEqualScriptDefinition()
        : base(
            """
            local currentVal = redis.call('get', @key)
            local expected = @expected
            if expected == '' then expected = false end
            if currentVal == expected then
              if (@expires ~= nil and @expires ~= '') then
                return redis.call('set', @key, @value, 'PX', @expires) and 1 or 0
              else
                return redis.call('set', @key, @value, 'KEEPTTL') and 1 or 0
              end
            else
              return -1
            end
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="ReplaceIfEqualScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReplaceIfEqualParams(RedisKey key, string? value, string expected, RedisValue expires);
#pragma warning restore IDE1006
