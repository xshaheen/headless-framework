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
