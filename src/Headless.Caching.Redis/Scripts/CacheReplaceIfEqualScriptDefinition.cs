// Copyright (c) Mahmoud Shaheen. All rights reserved.

// This CAS script is intentionally forked from Headless.Redis ReplaceIfEqualScriptDefinition.
// Do NOT merge it back into the shared definition: DistributedLocks.Redis depends on the unmodified
// shared script (whole-value comparison), whereas this slices the framed cache-entry value segment
// (skipping the envelope header) before comparing against the expected value. See project memory note
// "Shared Redis CAS scripts".

using Headless.Redis;

namespace Headless.Caching.Scripts;

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

            if hasMagic and string.byte(currentVal, 2) ~= 3 then
              return redis.error_reply('ERR unsupported cache frame version')
            end

            local isFramed = hasMagic and string.byte(currentVal, 2) == 3

            if isFramed then
              local flags = string.byte(currentVal, 3)
              local currentIsNull = flags % 2 == 1
              local len = string.len(currentVal)

              -- Skip the optional v3 sections in frame layout order (sliding 0x08, eager-refresh 0x10,
              -- last-modified 0x40 fixed 8B each, then etag 0x20 and tags 0x80 as u16le-length-prefixed
              -- UTF-8) to find where the value segment starts.
              local valueStart = headerLen

              if math.floor(flags / 8) % 2 == 1 then valueStart = valueStart + 8 end
              if math.floor(flags / 16) % 2 == 1 then valueStart = valueStart + 8 end
              if math.floor(flags / 64) % 2 == 1 then valueStart = valueStart + 8 end

              if math.floor(flags / 32) % 2 == 1 then
                if len < valueStart + 2 then
                  return redis.error_reply('ERR malformed cache frame')
                end
                valueStart = valueStart + 2
                  + string.byte(currentVal, valueStart + 1)
                  + string.byte(currentVal, valueStart + 2) * 256
              end

              if math.floor(flags / 128) % 2 == 1 then
                if len < valueStart + 2 then
                  return redis.error_reply('ERR malformed cache frame')
                end
                local tagCount = string.byte(currentVal, valueStart + 1)
                  + string.byte(currentVal, valueStart + 2) * 256
                valueStart = valueStart + 2
                for _ = 1, tagCount do
                  if len < valueStart + 2 then
                    return redis.error_reply('ERR malformed cache frame')
                  end
                  valueStart = valueStart + 2
                    + string.byte(currentVal, valueStart + 1)
                    + string.byte(currentVal, valueStart + 2) * 256
                end
              end

              if len < valueStart then
                return redis.error_reply('ERR malformed cache frame')
              end

              if expectedIsNull == 1 then
                if currentIsNull then
                  matches = 1
                end
              elseif not currentIsNull and string.sub(currentVal, valueStart + 1) == @expected then
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
