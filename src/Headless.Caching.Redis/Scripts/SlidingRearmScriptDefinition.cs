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
            // a negative one from clock skew, so the script never shortens a TTL) is skipped. Guarded reads
            // compare just the metadata prefix so a replacement cannot inherit the previous entry's deadline.
            """
            local ttl = redis.call('PTTL', @key)
            local newTtl = tonumber(@newTtlMs)
            if ttl < 0 or newTtl - ttl <= tonumber(@rearmToleranceMs) then return ttl end
            local prefix = @expectedPrefix
            -- pcall also treats a replacement of a different Redis type as a mismatch.
            if #prefix > 0 and redis.pcall('GETRANGE', @key, 0, #prefix - 1) ~= prefix then return ttl end
            redis.call('PEXPIRE', @key, newTtl)
            return newTtl
            """
        ) { }
}
