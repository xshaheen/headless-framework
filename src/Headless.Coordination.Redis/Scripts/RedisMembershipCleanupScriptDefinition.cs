// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis;

/// <summary>Prunes expired coordination liveness entries without deleting generation counters.</summary>
internal sealed class RedisMembershipCleanupScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipCleanupScriptDefinition Instance { get; } = new();

    private RedisMembershipCleanupScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            local entries = redis.call('hgetall', @knownKey)
            local removed = 0

            redis.call('zremrangebyscore', @liveKey, '-inf', nowMs)

            for i = 1, #entries, 2 do
              local member = entries[i]
              local payload = cjson.decode(entries[i + 1])
              local ageMs = nowMs - tonumber(payload['last_beat_ms'])
              if ageMs >= tonumber(@pruneMs) then
                redis.call('hdel', @knownKey, member)
                redis.call('zrem', @liveKey, member)
                removed = removed + 1
              end
            end

            return removed
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipCleanupScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CleanupParams(RedisKey knownKey, RedisKey liveKey, long pruneMs);
#pragma warning restore IDE1006
