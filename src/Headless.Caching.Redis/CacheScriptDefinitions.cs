// Copyright (c) Mahmoud Shaheen. All rights reserved.

// These CAS scripts are intentionally forked from Headless.Redis Remove/ReplaceIfEqualScriptDefinition.
// Do NOT merge them back into the shared definitions: DistributedLocks.Redis depends on the unmodified
// shared scripts (whole-value comparison), whereas these slice the framed cache-entry value segment
// (skipping the envelope header) before comparing against the expected value. See project memory note
// "Shared Redis CAS scripts".

using Headless.Redis;

namespace Headless.Caching;

/// <summary>Atomically removes a cache key only if its framed or raw value matches the expected value.</summary>
internal sealed class CacheRemoveIfEqualScriptDefinition : RedisScriptDefinition
{
    public static CacheRemoveIfEqualScriptDefinition Instance { get; } = new();

    private CacheRemoveIfEqualScriptDefinition()
        : base(
            """
            local currentVal = redis.call('get', @key)
            if currentVal == false then
              return 0
            end

            local expectedIsNull = tonumber(@expectedIsNull)
            local headerLen = tonumber(@headerLen)
            local hasMagic =
              string.len(currentVal) >= headerLen
              and string.byte(currentVal, 1) == 255

            if hasMagic and string.byte(currentVal, 2) ~= 1 then
              return redis.error_reply('ERR unsupported cache frame version')
            end

            local isFramed = hasMagic and string.byte(currentVal, 2) == 1

            if isFramed then
              local flags = string.byte(currentVal, 3)
              local currentIsNull = flags % 2 == 1

              if expectedIsNull == 1 then
                if currentIsNull then
                  return redis.call('del', @key)
                end

                return 0
              end

              if currentIsNull then
                return 0
              end

              if string.sub(currentVal, headerLen + 1) == @expected then
                return redis.call('del', @key)
              end

              return 0
            end

            if expectedIsNull == 1 then
              if currentVal == @nullValue then
                return redis.call('del', @key)
              end

              return 0
            end

            if currentVal == @expected then
              return redis.call('del', @key)
            end

            return 0
            """
        ) { }
}

/// <summary>Atomically replaces a cache key only if its framed or raw value matches the expected value.</summary>
internal sealed class CacheReplaceIfEqualScriptDefinition : RedisScriptDefinition
{
    public static CacheReplaceIfEqualScriptDefinition Instance { get; } = new();

    private CacheReplaceIfEqualScriptDefinition()
        : base(
            """
            local currentVal = redis.call('get', @key)
            if currentVal == false then
              return 0
            end

            local expectedIsNull = tonumber(@expectedIsNull)
            local headerLen = tonumber(@headerLen)
            local matches = 0
            local hasMagic =
              string.len(currentVal) >= headerLen
              and string.byte(currentVal, 1) == 255

            if hasMagic and string.byte(currentVal, 2) ~= 1 then
              return redis.error_reply('ERR unsupported cache frame version')
            end

            local isFramed = hasMagic and string.byte(currentVal, 2) == 1

            if isFramed then
              local flags = string.byte(currentVal, 3)
              local currentIsNull = flags % 2 == 1

              if expectedIsNull == 1 then
                if currentIsNull then
                  matches = 1
                end
              elseif not currentIsNull and string.sub(currentVal, headerLen + 1) == @expected then
                matches = 1
              end
            else
              if expectedIsNull == 1 then
                if currentVal == @nullValue then
                  matches = 1
                end
              elseif currentVal == @expected then
                matches = 1
              end
            end

            if matches == 0 then
              return -1
            end

            -- RedisValue.EmptyString is the no-expiry sentinel passed for @expires.
            if @expires ~= '' then
              return redis.call('set', @key, @value, 'PX', @expires) and 1 or 0
            end

            return redis.call('set', @key, @value) and 1 or 0
            """
        ) { }
}
