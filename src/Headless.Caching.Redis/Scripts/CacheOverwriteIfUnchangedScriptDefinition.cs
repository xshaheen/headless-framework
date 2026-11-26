// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.Caching.Scripts;

/// <summary>
/// Atomically overwrites a cache key only while it still holds the exact bytes the caller read, so a write that
/// judged those bytes logically absent (invalidated by a clear, flush, or tag marker, or past their logical expiry)
/// cannot clobber a live entry another writer stored in between.
/// </summary>
internal sealed class CacheOverwriteIfUnchangedScriptDefinition : RedisScriptDefinition
{
    public static CacheOverwriteIfUnchangedScriptDefinition Instance { get; } = new();

    private CacheOverwriteIfUnchangedScriptDefinition()
        : base(
            """
            if redis.call('get', @key) ~= @current then
              return 0
            end

            local flushGeneration = tonumber(@flushGeneration)
            if flushGeneration then
              local storedGeneration = tonumber(redis.call('get', @generationKey))
              if storedGeneration and storedGeneration >= flushGeneration then
                return 0
              end
            end

            -- RedisValue.EmptyString is the no-expiry sentinel passed for @expires.
            if @expires ~= '' then
              redis.call('set', @key, @value, 'PX', @expires)
            else
              redis.call('set', @key, @value)
            end

            redis.call('del', @generationKey)
            return 1
            """
        ) { }
}
