// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching.Scripts;

/// <summary>
/// Compare-and-set write: verifies the live value equals the caller's expected concurrency stamp before writing
/// the framed entry with its key TTL. Used only when a write carries an <c>ExpectedConcurrencyStamp</c> (a
/// factory write derived from an existing physical entry); plain writes use a direct SET. Family-2 tag/clear
/// invalidation reads timestamp markers, so this script no longer maintains any reverse tag index.
/// </summary>
internal sealed class CacheTaggedSetScriptDefinition : RedisScriptDefinition
{
    public static CacheTaggedSetScriptDefinition Instance { get; } = new();

    private CacheTaggedSetScriptDefinition()
        : base(
            """
            local keyTtlMs = tonumber(@keyTtlMs)

            if @expectedValue ~= nil and string.len(@expectedValue) > 0 then
              local current = redis.call('get', @key)
              if current ~= @expectedValue then
                return 0
              end
            end

            redis.call('set', @key, @value, 'PX', keyTtlMs)

            return 1
            """
        ) { }
}
