// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>
/// Read-only live holder count. Does NOT prune expired slots — it counts members whose expiry
/// score is still in the future via ZCOUNT, so a stale (expired-but-unpruned) slot is excluded from
/// the result without a write. Expired-slot reclamation is the acquire script's job.
/// </summary>
internal sealed class GetSemaphoreCountScriptDefinition : RedisScriptDefinition
{
    public static GetSemaphoreCountScriptDefinition Instance { get; } = new();

    private GetSemaphoreCountScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            return redis.call('zcount', @holdersKey, '(' .. nowMs, '+inf')
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="GetSemaphoreCountScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SemaphoreCountParams(RedisKey HoldersKey);
#pragma warning restore IDE1006
