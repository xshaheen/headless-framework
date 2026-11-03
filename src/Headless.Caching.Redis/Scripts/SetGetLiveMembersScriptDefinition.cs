// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Caching.Scripts;

/// <summary>
/// Reads the live members of an expiring set: those whose expiry score is still after Redis's own clock.
/// </summary>
/// <remarks>
/// <see cref="SetAddWithExpireScriptDefinition"/> scores members and prunes them by <c>redis.call('TIME')</c>, so the
/// read compares against the same clock. A cutoff computed from the application clock would disagree with the write
/// side by the app/Redis skew: members hidden early when the app runs ahead, expired ones shown when it runs behind.
/// The script is read-only, but EVAL always runs on a primary, so a set read does not follow
/// <see cref="RedisCacheOptions.ReadMode"/> to a replica.
/// </remarks>
internal static class SetGetLiveMembersScriptDefinition
{
    private const string _Source = """
        local key = KEYS[1]
        local skip = ARGV[1]
        local take = tonumber(ARGV[2])

        -- Same clock and the same millisecond derivation as the set-add script, which prunes scores <= now.
        local serverTime = redis.call('TIME')
        local nowMs = (tonumber(serverTime[1]) * 1000) + math.floor(tonumber(serverTime[2]) / 1000)
        local minScore = string.format('(%d', nowMs)

        if take < 0 then
          return redis.call('zrangebyscore', key, minScore, '+inf')
        end

        return redis.call('zrangebyscore', key, minScore, '+inf', 'LIMIT', skip, take)
        """;

    /// <summary>Reads every live member, or one page of them when <paramref name="take"/> is not negative.</summary>
    public static async Task<RedisValue[]> EvaluateAsync(IDatabaseAsync database, RedisKey key, long skip, long take)
    {
        var result = await database.ScriptEvaluateAsync(_Source, [key], [skip, take]).ConfigureAwait(false);

        return (RedisValue[]?)result ?? [];
    }
}
