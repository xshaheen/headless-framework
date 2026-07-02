// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>
/// Read-only check of whether a holder is still live (its slot's expiry score has not passed).
/// Does NOT prune expired slots — validation must not mutate state on a hot per-iteration path.
/// </summary>
internal sealed class ValidateSemaphoreScriptDefinition : RedisScriptDefinition
{
    public static ValidateSemaphoreScriptDefinition Instance { get; } = new();

    private ValidateSemaphoreScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            local score = redis.call('zscore', @holdersKey, @leaseId)
            if score ~= false and tonumber(score) > nowMs then
              return 1
            end
            return 0
            """
        ) { }
}
