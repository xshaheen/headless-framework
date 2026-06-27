// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

/// <summary>
/// Classifies a single coordination member with Redis server time — the targeted, read-only counterpart to
/// <see cref="RedisMembershipReadScriptDefinition"/>. Returns the state string, or nil when the member is
/// absent, not current-generation, or retention-expired. Performs no writes (no prune, no mirror backfill).
/// </summary>
internal sealed class RedisMembershipReadNodeLivenessScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipReadNodeLivenessScriptDefinition Instance { get; } = new();

    private RedisMembershipReadNodeLivenessScriptDefinition()
        : base(
            """
            local payloadText = redis.call('hget', @knownKey, @member)
            if payloadText == false then
              return nil
            end

            -- Resolve the node's current generation mirror-first (matching the read script), gen: key fallback.
            -- The mirror is a bare decimal written by tostring(incarnation); reading it avoids a second key when present.
            local current = redis.call('hget', @knownKey, @generationField)
            if current == false then
              current = redis.call('get', @genKey)
            end

            if current == false or tonumber(current) ~= tonumber(@incarnation) then
              return nil
            end

            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            local payload = cjson.decode(payloadText)
            local ageMs = nowMs - tonumber(payload['last_beat_ms'])

            -- Retention boundary as a read-only cutoff: the read script deletes-and-omits such members, so the
            -- targeted path reports absence (nil) rather than Dead to stay parity-equal without writing.
            if ageMs >= tonumber(@pruneMs) then
              return nil
            end

            if ageMs >= tonumber(@hardMs) then
              return @deadState
            elseif ageMs >= tonumber(@softMs) then
              return @suspectedState
            end

            return @aliveState
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipReadNodeLivenessScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReadNodeLivenessParams(
    RedisKey knownKey,
    RedisKey genKey,
    string generationField,
    string member,
    long incarnation,
    long softMs,
    long hardMs,
    long pruneMs,
    string aliveState,
    string suspectedState,
    string deadState
);
#pragma warning restore IDE1006
