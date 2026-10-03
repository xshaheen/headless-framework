// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="TryAcquireLockWithFenceScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AcquireLockParams(RedisKey key, RedisKey fenceKey, string leaseId, RedisValue expires);
