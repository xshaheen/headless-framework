// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

/// <summary>Stores a guarded coordination heartbeat using Redis server time.</summary>
internal sealed class RedisMembershipHeartbeatScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipHeartbeatScriptDefinition Instance { get; } = new();

    private RedisMembershipHeartbeatScriptDefinition()
        : base(
            """
            local current = redis.call('get', @genKey)
            if current == false or tonumber(current) ~= tonumber(@incarnation) then
              return 0
            end

            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            if tonumber(@allowCreate) ~= 1 then
              local hardExpiryMs = redis.call('zscore', @liveKey, @member)
              local payloadExists = redis.call('hexists', @knownKey, @member)
              if payloadExists ~= 1 or hardExpiryMs == false or tonumber(hardExpiryMs) <= nowMs then
                return 0
              end
            end

            local hardExpiryMs = nowMs + tonumber(@hardMs)
            local payload = cjson.encode({
              last_beat_ms = nowMs,
              role = @role,
              metadata = @metadata
            })

            redis.call('zadd', @liveKey, hardExpiryMs, @member)
            -- Mirror value is a bare decimal (not JSON); read/cleanup classification depends on it not starting with '{'.
            redis.call('hset', @knownKey, @generationField, tostring(@incarnation))
            redis.call('hset', @knownKey, @member, payload)

            return 1
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipHeartbeatScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HeartbeatParams(
    RedisKey liveKey,
    RedisKey knownKey,
    RedisKey genKey,
    string generationField,
    string member,
    long incarnation,
    long hardMs,
    int allowCreate,
    string role,
    string metadata
);
#pragma warning restore IDE1006
