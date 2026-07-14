// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Caching.Scripts;

/// <summary>
/// Atomically prunes expired set members, adds/removes members, and re-arms the sorted-set key expiration.
/// </summary>
/// <remarks>
/// The expiry clock is REDIS'S, not the caller's. The script takes a RELATIVE ttl and derives both the member
/// score and the prune cutoff from <c>redis.call('TIME')</c> inside the atomic body.
/// <para>
/// Sending an absolute, app-computed epoch instead (the previous shape) put the writing process's wall clock
/// into the expiry decision: a fast client's members outlive their ttl, a slow client's are pruned early, and
/// two app instances with drifting clocks disagree about which members are still live in the SAME key. Every
/// other script in this package already sends a relative duration; this one is now consistent with them.
/// </para>
/// </remarks>
internal static class SetAddWithExpireScriptDefinition
{
    private const string _Source = """
        local key = KEYS[1]
        local operation = ARGV[1]
        local ttlMs = tonumber(ARGV[2])
        local maxExpirationMs = tonumber(ARGV[3])

        -- Server clock. TIME returns { unixSeconds, microseconds } and is the only clock in this script.
        local serverTime = redis.call('TIME')
        local nowMs = (tonumber(serverTime[1]) * 1000) + math.floor(tonumber(serverTime[2]) / 1000)

        local scoreMs
        if ttlMs < 0 then
          scoreMs = maxExpirationMs
        else
          scoreMs = nowMs + ttlMs
          if scoreMs > maxExpirationMs then
            scoreMs = maxExpirationMs
          end
        end

        local pruned = redis.call('zremrangebyscore', key, 0, nowMs)
        local changed = 0

        if operation == 'add' then
          for i = 4, #ARGV do
            changed = changed + redis.call('zadd', key, scoreMs, ARGV[i])
          end
        elseif operation == 'remove' then
          for i = 4, #ARGV do
            changed = changed + redis.call('zrem', key, ARGV[i])
          end
        else
          return redis.error_reply('ERR unsupported set mutation operation')
        end

        if pruned > 0 or changed > 0 or operation == 'add' then
          local highest = redis.call('zrevrange', key, 0, 0, 'WITHSCORES')

          if #highest == 0 then
            redis.call('del', key)
          else
            local highestExpirationMs = tonumber(highest[2])

            if highestExpirationMs >= maxExpirationMs then
              redis.call('persist', key)
            else
              redis.call('pexpireat', key, math.ceil(highestExpirationMs))
            end
          end
        end

        return { changed, pruned }
        """;

    public static Task<RedisResult> EvaluateAsync(IDatabaseAsync database, RedisKey key, RedisValue[] values)
    {
        return database.ScriptEvaluateAsync(_Source, [key], values);
    }
}
