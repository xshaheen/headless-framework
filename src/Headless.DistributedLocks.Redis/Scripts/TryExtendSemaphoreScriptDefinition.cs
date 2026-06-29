// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically extends a distributed semaphore slot when the holder is still present.</summary>
internal sealed class TryExtendSemaphoreScriptDefinition : RedisScriptDefinition
{
    public static TryExtendSemaphoreScriptDefinition Instance { get; } = new();

    private TryExtendSemaphoreScriptDefinition()
        : base(
            // No ZREMRANGEBYSCORE prune: extend is XX-gated, so it only mutates a member that
            // already exists. Pruning expired members here would be an extra write with no bearing
            // on the extending holder's own slot (whose score is simply updated). Expired-slot
            // reclamation is the acquire script's job, where it is correctness-critical.
            //
            // Soft expiry: XX matches any existing member, including one whose score has already
            // lapsed but has not yet been pruned by a competing acquire. Such a holder reclaims its
            // own slot on extend rather than losing it — standard lease-renewal semantics, and
            // capacity-safe because script atomicity orders this extend against any acquire that
            // would prune-then-take the slot. "Expired" is therefore soft until an acquire prunes.
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            local expiryMs = nowMs + tonumber(@expires)
            -- GT: a shorter extend must never shorten a live lease. An "extend" that moves expiry
            -- earlier is incoherent (it would prematurely surrender capacity), so only grow the score.
            -- This matches the in-memory provider's GREATEST semantics. XX still gates on existence so a
            -- non-holder cannot create a slot; existence is reasserted via zscore because GT suppresses the
            -- CH "changed" signal when the new score is not greater, which we must not read as "missing".
            local existing = redis.call('zscore', @holdersKey, @leaseId)
            if existing == false then
              return 0
            end
            redis.call('zadd', @holdersKey, 'XX', 'GT', expiryMs, @leaseId)

            local safetyTtl = tonumber(@expires) * 2
            local currentTtl = redis.call('pttl', @holdersKey)
            if currentTtl < safetyTtl then
              redis.call('pexpire', @holdersKey, safetyTtl)
            end
            return 1
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>
/// Parameters shared by the semaphore slot scripts (<see cref="TryExtendSemaphoreScriptDefinition"/>,
/// <see cref="ValidateSemaphoreScriptDefinition"/>, <see cref="ReleaseSemaphoreScriptDefinition"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SemaphoreSlotParams(RedisKey holdersKey, string leaseId, RedisValue expires);
#pragma warning restore IDE1006
