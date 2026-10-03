// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="TryAcquireSemaphoreWithFenceScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SemaphoreAcquireParams(
    RedisKey holdersKey,
    RedisKey fenceKey,
    string leaseId,
    int maxCount,
    long expires
);
