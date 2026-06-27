// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically acquires a mutex lock and issues a fencing token only when the grant succeeds.</summary>
internal sealed class TryAcquireLockWithFenceScriptDefinition : RedisScriptDefinition
{
    public static TryAcquireLockWithFenceScriptDefinition Instance { get; } = new();

    private TryAcquireLockWithFenceScriptDefinition()
        : base(
            """
            local result
            if (@expires ~= nil and @expires ~= '') then
              result = redis.call('set', @key, @leaseId, 'NX', 'PX', @expires)
            else
              result = redis.call('set', @key, @leaseId, 'NX')
            end

            if result then
              return {1, redis.call('incr', @fenceKey)}
            end

            return {0}
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="TryAcquireLockWithFenceScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AcquireLockParams(RedisKey key, RedisKey fenceKey, string leaseId, RedisValue expires);
#pragma warning restore IDE1006
