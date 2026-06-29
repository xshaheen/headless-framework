// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

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
            local generationFieldPrefixLength = string.len(@generationFieldPrefix)
            local removed = 0
            local liveNodeIds = {}

            -- isGenerationField: true for a reserved __gen:<node-id> mirror field, false for a member payload.
            -- Mirror values are bare decimals written by tostring(incarnation) in the allocate/heartbeat
            -- scripts; member payloads are cjson objects that always begin with '{'. BOTH checks are required
            -- so a node-id that itself starts with __gen: is still classified as a member. Keep this predicate
            -- byte-for-byte identical to its twin in RedisMembershipReadScriptDefinition.
            local function isGenerationField(member, payloadText)
              return string.sub(member, 1, generationFieldPrefixLength) == @generationFieldPrefix
                and string.sub(payloadText, 1, 1) ~= '{'
            end

            redis.call('zremrangebyscore', @liveKey, '-inf', nowMs)

            -- Pass 1: prune expired member payloads; remember node-ids that still have a surviving member.
            for i = 1, #entries, 2 do
              local member = entries[i]
              local payloadText = entries[i + 1]
              if not isGenerationField(member, payloadText) then
                local payload = cjson.decode(payloadText)
                local ageMs = nowMs - tonumber(payload['last_beat_ms'])
                if ageMs >= tonumber(@pruneMs) then
                  redis.call('hdel', @knownKey, member)
                  redis.call('zrem', @liveKey, member)
                  removed = removed + 1
                else
                  local nodeId = string.match(member, '^(.+)@([0-9]+)$')
                  if nodeId ~= nil then
                    liveNodeIds[nodeId] = true
                  end
                end
              end
            end

            -- Pass 2: sweep orphaned generation mirrors so :known cannot grow with every node-id ever seen.
            -- A mirror is retained only while its node still has a surviving member payload. The durable
            -- :gen: counter is never touched, so a restarting node re-mirrors on its next allocate/heartbeat.
            for i = 1, #entries, 2 do
              local member = entries[i]
              local payloadText = entries[i + 1]
              if isGenerationField(member, payloadText) then
                local nodeId = string.sub(member, generationFieldPrefixLength + 1)
                if not liveNodeIds[nodeId] then
                  redis.call('hdel', @knownKey, member)
                end
              end
            end

            return removed
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipCleanupScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CleanupParams(
    RedisKey knownKey,
    RedisKey liveKey,
    string generationFieldPrefix,
    long pruneMs
);
#pragma warning restore IDE1006
