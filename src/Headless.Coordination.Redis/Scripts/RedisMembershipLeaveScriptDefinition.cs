// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

/// <summary>Marks a coordination member as left using Redis server time.</summary>
internal sealed class RedisMembershipLeaveScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipLeaveScriptDefinition Instance { get; } = new();

    private RedisMembershipLeaveScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            local lastBeatMs = nowMs - tonumber(@hardMs)
            local role = @role
            local metadata = @metadata

            local existing = redis.call('hget', @knownKey, @member)
            if existing ~= false then
              local payload = cjson.decode(existing)
              role = payload['role'] or role
              metadata = payload['metadata'] or metadata
            end

            redis.call('hset', @knownKey, @member, cjson.encode({
              last_beat_ms = lastBeatMs,
              role = role,
              metadata = metadata
            }))
            redis.call('zrem', @liveKey, @member)
            return 1
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipLeaveScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LeaveParams(
    RedisKey KnownKey,
    RedisKey LiveKey,
    string Member,
    long HardMs,
    string Role,
    string Metadata
);
#pragma warning restore IDE1006
