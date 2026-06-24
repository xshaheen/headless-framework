// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis;

/// <summary>
/// Returns the current-generation <c>Alive</c> members from the <c>:live</c> sorted set — the targeted,
/// read-only counterpart to <see cref="RedisMembershipReadScriptDefinition"/> for the live-node fast path.
/// Performs no writes (no prune, no mirror backfill).
/// </summary>
internal sealed class RedisMembershipReadLiveNodesScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipReadLiveNodesScriptDefinition Instance { get; } = new();

    private RedisMembershipReadLiveNodesScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            -- The :live score is last_beat + DeadThreshold, so beat age = nowMs - (score - hard). Alive means
            -- age < soft, i.e. score > nowMs + hard - soft. The exclusive '(' min drops the exact boundary
            -- (age == soft), which classifies as Suspected, matching the snapshot read's >= comparison.
            local aliveFloor = nowMs + tonumber(@hardMs) - tonumber(@softMs)
            local candidates = redis.call('zrangebyscore', @liveKey, '(' .. aliveFloor, '+inf')
            local currentByNode = {}
            local result = {}

            for i = 1, #candidates do
              local member = candidates[i]
              local nodeId, incarnation = string.match(member, '^(.+)@([0-9]+)$')
              if nodeId ~= nil then
                local current = currentByNode[nodeId]
                if current == nil then
                  -- Resolve the node's current generation mirror-first (matching the read script), gen: fallback.
                  current = redis.call('hget', @knownKey, @generationFieldPrefix .. nodeId)
                  if current == false then
                    current = redis.call('get', @genKeyPrefix .. nodeId)
                  end
                  currentByNode[nodeId] = current
                end

                -- Drop a superseded incarnation even while it is still alive-by-score: live reads are current
                -- generation only.
                if current ~= false and tonumber(current) == tonumber(incarnation) then
                  table.insert(result, member)
                end
              end
            end

            table.sort(result)
            return result
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipReadLiveNodesScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReadLiveNodesParams(
    RedisKey liveKey,
    RedisKey knownKey,
    string genKeyPrefix,
    string generationFieldPrefix,
    long softMs,
    long hardMs
);
#pragma warning restore IDE1006
