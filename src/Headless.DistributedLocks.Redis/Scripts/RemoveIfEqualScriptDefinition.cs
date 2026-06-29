// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically removes a key only if its value matches the expected value.</summary>
internal sealed class RemoveIfEqualScriptDefinition : RedisScriptDefinition
{
    public static RemoveIfEqualScriptDefinition Instance { get; } = new();

    private RemoveIfEqualScriptDefinition()
        : base(
            """
            if redis.call('get', @key) == @expected then
              return redis.call('del', @key)
            else
              return 0
            end
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RemoveIfEqualScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct RemoveIfEqualParams(RedisKey key, string? expected);
#pragma warning restore IDE1006
