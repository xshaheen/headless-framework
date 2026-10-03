// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.Coordination.Redis.Scripts;

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
