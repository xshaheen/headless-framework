// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Caching.Scripts;

/// <summary>
/// Atomically prunes expired set members, adds/removes members, and re-arms the sorted-set key expiration.
/// </summary>
internal static class SetAddWithExpireScriptDefinition
{
    private const string _Source = """
        local key = KEYS[1]
        local operation = ARGV[1]
        local scoreMs = tonumber(ARGV[2])
        local nowMs = tonumber(ARGV[3])
        local maxExpirationMs = tonumber(ARGV[4])

        local pruned = redis.call('zremrangebyscore', key, 0, nowMs)
        local changed = 0

        if operation == 'add' then
          for i = 5, #ARGV do
            changed = changed + redis.call('zadd', key, scoreMs, ARGV[i])
          end
        elseif operation == 'remove' then
          for i = 5, #ARGV do
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
