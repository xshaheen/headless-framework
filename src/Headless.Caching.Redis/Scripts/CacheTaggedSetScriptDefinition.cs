// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching.Scripts;

/// <summary>
/// Compare-and-set write: verifies the live value matches the caller's expected concurrency stamp before writing
/// the framed entry with its key TTL. Used only when a write carries an <c>ExpectedConcurrencyStamp</c> (a
/// factory write derived from an existing physical entry); plain writes use a direct SET. Family-2 tag/clear
/// invalidation reads timestamp markers, so this script no longer maintains any reverse tag index.
/// </summary>
/// <remarks>
/// The concurrency stamp captures only the fixed frame header (the first <c>@headerLen</c> bytes), so the CAS
/// compares that same prefix of the live value rather than the whole payload (#13). <c>string.sub</c> clamps to
/// the value's length, so a shorter legacy value compares in full and stays consistent with the C# stamp codec.
/// <para>
/// Because the header omits the payload, two writes to the same key with identical options — same logical,
/// physical, and created-at millisecond timestamps and the same optional-field flags — produce byte-identical
/// stamps. Within that same-key, same-options, same-millisecond window the CAS cannot distinguish a stale factory
/// write from a newer concurrent one, so the "cannot clobber a concurrent writer" guarantee does not strictly hold
/// there (#583). This is a deliberate perf-over-correctness choice: the header-only compare avoids hashing the
/// whole frame on every write, and <see cref="CacheEntryOptions.JitterMaxDuration"/> <c>&gt; 0</c> spreads the
/// header timestamps sub-millisecond and defeats the collision. Callers needing strict cross-writer CAS on a hot
/// key should enable jitter rather than rely on a same-millisecond guarantee.
/// </para>
/// </remarks>
internal sealed class CacheTaggedSetScriptDefinition : RedisScriptDefinition
{
    public static CacheTaggedSetScriptDefinition Instance { get; } = new();

    private CacheTaggedSetScriptDefinition()
        : base(
            """
            local keyTtlMs = tonumber(@keyTtlMs)

            if @expectedValue ~= nil and string.len(@expectedValue) > 0 then
              local current = redis.call('get', @key)
              if current == false then
                return 0
              end
              local headerLen = tonumber(@headerLen)
              if string.sub(current, 1, headerLen) ~= @expectedValue then
                return 0
              end
            end

            redis.call('set', @key, @value, 'PX', keyTtlMs)

            return 1
            """
        ) { }
}
