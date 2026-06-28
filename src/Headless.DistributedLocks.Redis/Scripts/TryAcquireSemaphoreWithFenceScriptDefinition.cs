// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically acquires a distributed semaphore slot and issues a fencing token.</summary>
internal sealed class TryAcquireSemaphoreWithFenceScriptDefinition : RedisScriptDefinition
{
    public static TryAcquireSemaphoreWithFenceScriptDefinition Instance { get; } = new();

    private TryAcquireSemaphoreWithFenceScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)

            if redis.call('zcard', @holdersKey) >= tonumber(@maxCount) then
              return {0}
            end

            local expiryMs = nowMs + tonumber(@expires)
            redis.call('zadd', @holdersKey, expiryMs, @leaseId)
            local safetyTtl = tonumber(@expires) * 2
            local currentTtl = redis.call('pttl', @holdersKey)
            if currentTtl < safetyTtl then
              redis.call('pexpire', @holdersKey, safetyTtl)
            end

            -- @fenceKey is intentionally persistent (no TTL/PEXPIRE): the monotonic fence counter
            -- must outlive every holder so tokens never reset. Do not add an expiry here.
            return {1, redis.call('incr', @fenceKey)}
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="TryAcquireSemaphoreWithFenceScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SemaphoreAcquireParams(
    RedisKey HoldersKey,
    RedisKey FenceKey,
    string LeaseId,
    int MaxCount,
    long Expires
);
#pragma warning restore IDE1006
