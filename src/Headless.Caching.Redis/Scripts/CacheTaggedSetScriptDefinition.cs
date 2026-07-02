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
