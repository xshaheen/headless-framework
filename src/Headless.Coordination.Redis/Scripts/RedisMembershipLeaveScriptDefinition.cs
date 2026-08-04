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

            local existing = redis.call('hget', @knownKey, @member)
            if existing == false then
              -- IMembershipStore contract: leave is a no-op for an absent, pruned, or superseded-and-swept
              -- identity — recreating a member payload here would retain a phantom entry for the whole
              -- RedisKnownNodeRetention window and pin the node's generation mirror against cleanup (the
              -- relational providers are UPDATE-only and correctly touch zero rows). zrem stays: it is
              -- idempotent and covers a live entry whose payload was already swept.
              redis.call('zrem', @liveKey, @member)
              return 0
            end

            local payload = cjson.decode(existing)
            local role = payload['role'] or @role
            local metadata = payload['metadata'] or @metadata

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
    RedisKey knownKey,
    RedisKey liveKey,
    string member,
    long hardMs,
    string role,
    string metadata
);
#pragma warning restore IDE1006
