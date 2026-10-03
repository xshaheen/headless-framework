// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

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
