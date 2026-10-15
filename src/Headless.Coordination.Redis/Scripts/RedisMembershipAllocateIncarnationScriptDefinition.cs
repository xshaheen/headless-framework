// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

/// <summary>
/// Allocates a node incarnation that rises across a store reset and mirrors it into the known-node projection.
/// </summary>
internal sealed class RedisMembershipAllocateIncarnationScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipAllocateIncarnationScriptDefinition Instance { get; } = new();

    private RedisMembershipAllocateIncarnationScriptDefinition()
        : base(
            """
            -- Seed from the server clock (millisecond resolution in microsecond units, ~1.8e15 today, below 2^53) so an incarnation
            -- issued after a flush or a lossy failover still exceeds every incarnation issued before it; a plain
            -- INCR would restart at 1 and hand out identities that earlier rows were stamped with.
            local current = tonumber(redis.call('get', @genKey) or '0')
            local now = redis.call('TIME')
            local seed = ((tonumber(now[1]) * 1000) + math.floor(tonumber(now[2]) / 1000)) * 1000
            local incarnation = math.max(current + 1, seed)
            -- Lua 5.1 tostring() renders 16-digit numbers in exponent form, so format the integer explicitly.
            local text = string.format('%.0f', incarnation)
            redis.call('set', @genKey, text)
            -- Mirror value is a bare decimal (not JSON); read/cleanup classification depends on it not starting with '{'.
            redis.call('hset', @knownKey, @generationField, text)
            return text
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipAllocateIncarnationScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AllocateIncarnationParams(RedisKey genKey, RedisKey knownKey, string generationField);
#pragma warning restore IDE1006
