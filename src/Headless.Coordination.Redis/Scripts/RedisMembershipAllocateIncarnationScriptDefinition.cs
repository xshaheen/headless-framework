// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

/// <summary>Allocates a node incarnation and mirrors it into the known-node projection.</summary>
internal sealed class RedisMembershipAllocateIncarnationScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipAllocateIncarnationScriptDefinition Instance { get; } = new();

    private RedisMembershipAllocateIncarnationScriptDefinition()
        : base(
            """
            local incarnation = redis.call('incr', @genKey)
            -- Mirror value is a bare decimal (not JSON); read/cleanup classification depends on it not starting with '{'.
            redis.call('hset', @knownKey, @generationField, tostring(incarnation))
            return incarnation
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="RedisMembershipAllocateIncarnationScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AllocateIncarnationParams(RedisKey genKey, RedisKey knownKey, string generationField);
#pragma warning restore IDE1006
