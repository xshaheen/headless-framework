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
            // Both skip outcomes (ttl <= threshold, and persistent/missing via ttl < 0) return the live PTTL
            // verbatim, so every reply is the key's resulting TTL in ms and callers need no follow-up probe.
            """
            local ttl = redis.call('PTTL', @key)
            if ttl < 0 or ttl > tonumber(@rearmThresholdMs) then return ttl end
            redis.call('PEXPIRE', @key, tonumber(@newTtlMs))
            return tonumber(@newTtlMs)
            """
        ) { }
}
