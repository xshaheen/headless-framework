// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching;

/// <summary>
/// Atomic sliding-expiration re-arm: reads the live key TTL and conditionally pushes it out in a single Redis
/// round-trip. Returns 1 when PEXPIRE was issued, 0 when the key already had enough TTL or is persistent
/// (PTTL = -1). Replaces the previous two-round-trip PTTL-then-PEXPIRE sequence. (#9)
/// </summary>
internal sealed class SlidingRearmScriptDefinition : RedisScriptDefinition
{
    public static SlidingRearmScriptDefinition Instance { get; } = new();

    private SlidingRearmScriptDefinition()
        : base(
            """
            local ttl = redis.call('PTTL', @key)
            if ttl < 0 or ttl > tonumber(@rearmThresholdMs) then return 0 end
            return redis.call('PEXPIRE', @key, tonumber(@newTtlMs))
            """
        ) { }
}
