// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching.Scripts;

/// <summary>
/// Atomic sliding-expiration re-arm: reads the live key TTL and conditionally pushes it out in a single Redis
/// round-trip. Returns the key's resulting TTL in milliseconds — the new TTL (<c>newTtlMs</c>) after a re-arm,
/// the current PTTL when the re-arm was skipped, <c>-1</c> for a persistent key, <c>-2</c> when the key is
/// missing — so <c>GetWithExpirationAsync</c> reads the expiration from the same round-trip with no follow-up
/// TTL probe. Replaces the previous two-round-trip PTTL-then-PEXPIRE sequence. (#9)
/// </summary>
internal sealed class SlidingRearmScriptDefinition : RedisScriptDefinition
{
    public static SlidingRearmScriptDefinition Instance { get; } = new();

    private SlidingRearmScriptDefinition()
        : base(
            // Re-arm only when the new TTL moves the live one out by more than the tolerance; a move within it (or
            // a negative one from clock skew, so the script never shortens a TTL) is skipped. Both skip outcomes
            // (within tolerance, and persistent/missing via ttl < 0) return the live PTTL verbatim, so every reply
            // is the key's resulting TTL in ms and callers need no follow-up probe.
            """
            local ttl = redis.call('PTTL', @key)
            local newTtl = tonumber(@newTtlMs)
            if ttl < 0 or newTtl - ttl <= tonumber(@rearmToleranceMs) then return ttl end
            redis.call('PEXPIRE', @key, newTtl)
            return newTtl
            """
        ) { }
}
