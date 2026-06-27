// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

/// <summary>Classifies known coordination members with Redis server time.</summary>
internal sealed class RedisMembershipReadScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipReadScriptDefinition Instance { get; } = new();

    private RedisMembershipReadScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            local entries = redis.call('hgetall', @knownKey)
            local generationFieldPrefixLength = string.len(@generationFieldPrefix)
            local currentByNode = {}
            local result = {}

            -- isGenerationField: true for a reserved __gen:<node-id> mirror field, false for a member payload.
            -- Mirror values are bare decimals written by tostring(incarnation) in the allocate/heartbeat
            -- scripts; member payloads are cjson objects that always begin with '{'. BOTH checks are required
            -- so a node-id that itself starts with __gen: is still classified as a member. Keep this predicate
            -- byte-for-byte identical to its twin in RedisMembershipCleanupScriptDefinition.
            local function isGenerationField(member, payloadText)
              return string.sub(member, 1, generationFieldPrefixLength) == @generationFieldPrefix
                and string.sub(payloadText, 1, 1) ~= '{'
            end

            for i = 1, #entries, 2 do
              local member = entries[i]
              local payloadText = entries[i + 1]
              if isGenerationField(member, payloadText) then
                local nodeId = string.sub(member, generationFieldPrefixLength + 1)
                currentByNode[nodeId] = entries[i + 1]
              end
            end

            for i = 1, #entries, 2 do
              local member = entries[i]
              local payloadText = entries[i + 1]
              if not isGenerationField(member, payloadText) then
                local payload = cjson.decode(payloadText)
                local lastBeatMs = tonumber(payload['last_beat_ms'])
                local ageMs = nowMs - lastBeatMs

                if ageMs >= tonumber(@pruneMs) then
                  redis.call('hdel', @knownKey, member)
                  redis.call('zrem', @liveKey, member)
                else
                  local nodeId, incarnation = string.match(member, '^(.+)@([0-9]+)$')
                  if nodeId ~= nil then
                    local current = currentByNode[nodeId]
                    if current == nil then
                      current = redis.call('get', @genKeyPrefix .. nodeId)
                      if current ~= false then
                        currentByNode[nodeId] = current
                        redis.call('hset', @knownKey, @generationFieldPrefix .. nodeId, current)
                      else
                        currentByNode[nodeId] = false
                      end
                    end

                    if current ~= false and tonumber(current) == tonumber(incarnation) then
                      local state = @aliveState
                      if ageMs >= tonumber(@hardMs) then
                        state = @deadState
                      elseif ageMs >= tonumber(@softMs) then
                        state = @suspectedState
                      end

                      table.insert(result, { member, state, payload['role'] or '', payload['metadata'] or '{}' })
                    end
                  end
                end
              end
            end

            table.sort(result, function(left, right) return left[1] < right[1] end)
            return result
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipReadScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReadParams(
    RedisKey knownKey,
    RedisKey liveKey,
    string genKeyPrefix,
    string generationFieldPrefix,
    long softMs,
    long hardMs,
    long pruneMs,
    string aliveState,
    string suspectedState,
    string deadState
);
#pragma warning restore IDE1006
