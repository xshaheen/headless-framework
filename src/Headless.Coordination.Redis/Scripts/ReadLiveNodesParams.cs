// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

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
