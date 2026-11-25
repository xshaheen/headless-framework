// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching.Scripts;

internal static class NativeGenerationScript
{
    // At the current marker timestamp, retain only the highest flush sequence from each Hybrid instance.
    // A timestamp advance discards earlier sequences because those events can no longer advance the generation.
    public const string Flush = """
        local timestamp = tonumber(ARGV[1])
        local previousTimestamp = tonumber(redis.call('get', KEYS[1]))
        local previousGeneration = tonumber(redis.call('get', KEYS[2]))
        local newerTimestamp = not previousTimestamp or timestamp > previousTimestamp
        if newerTimestamp then
          redis.call('set', KEYS[1], ARGV[1])
          redis.call('del', KEYS[3])
        end

        local newSequence = false
        local origin = ARGV[3]
        if origin ~= '' and (newerTimestamp or timestamp == previousTimestamp) then
          local sequence = tonumber(ARGV[4])
          local previousSequence = tonumber(redis.call('hget', KEYS[3], origin))
          if not previousSequence or sequence > previousSequence then
            redis.call('hset', KEYS[3], origin, ARGV[4])
            newSequence = true
          end
        end

        if newerTimestamp or ARGV[2] == '1' or newSequence then
          local generation = math.max(timestamp, (previousGeneration or previousTimestamp or timestamp - 1) + 1)
          local stamp = string.format('%.0f', generation)
          redis.call('set', KEYS[2], stamp)
          return stamp
        end

        return redis.call('get', KEYS[2]) or redis.call('get', KEYS[1])
        """;

    public const string Mutating = """
        local function prepareNativeMutation(key, generationKey, generationArg)
          if key == generationKey then
            return nil
          end

          local generation = tonumber(generationArg)
          local existing = tonumber(redis.call('get', generationKey))
          if not generation then
            return existing
          end

          if existing and existing >= generation then
            return existing
          end

          -- Framed values use their embedded birth time and can replace a native value at the same key.
          local kind = redis.call('type', key).ok
          if kind == 'zset' or (kind == 'string' and redis.call('getrange', key, 0, 0) ~= string.char(255)) then
            redis.call('del', key, generationKey)
          end

          return generation
        end

        local function recordNativeGeneration(key, generationKey, generation)
          if not generation then
            return
          end

          local ttl = redis.call('pttl', key)
          local stamp = string.format('%.0f', generation)
          if ttl == -2 then
            redis.call('del', generationKey)
          elseif ttl == -1 then
            redis.call('set', generationKey, stamp)
          else
            redis.call('set', generationKey, stamp, 'PX', math.max(1, ttl))
          end
        end

        """;

    // KEYS lists data keys first, followed by their same-slot metadata keys. Count only data keys.
    public const string Delete = """
        local count = #KEYS / 2
        local removed = 0
        for i = 1, count do
          removed = removed + redis.call('del', KEYS[i])
          redis.call('del', KEYS[count + i])
        end
        return removed
        """;

    // Use MSET or MSETEX where available. Keep the write and metadata cleanup in one same-slot command
    // so a concurrent native write cannot lose its generation. Chunk Lua arrays below unpack's limit.
    public const string SetFrames = """
        local count = #KEYS / 2
        local ttl = ARGV[1]
        local supportsMsetex = ARGV[2] == '1'
        for first = 1, count, 250 do
          local last = math.min(first + 249, count)
          if ttl == '' or supportsMsetex then
            local args = {}
            if ttl ~= '' then
              args[1] = last - first + 1
            end
            for i = first, last do
              args[#args + 1] = KEYS[i]
              args[#args + 1] = ARGV[i + 2]
            end
            if ttl == '' then
              redis.call('mset', unpack(args))
            else
              args[#args + 1] = 'PX'
              args[#args + 1] = ttl
              redis.call('msetex', unpack(args))
            end
          else
            for i = first, last do
              redis.call('set', KEYS[i], ARGV[i + 2], 'PX', ttl)
            end
          end

          local metadata = {}
          for i = first, last do
            metadata[#metadata + 1] = KEYS[count + i]
          end
          redis.call('del', unpack(metadata))
        end
        return count
        """;
}
