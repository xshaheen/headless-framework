// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>
/// Parameters shared by the semaphore slot scripts (<see cref="TryExtendSemaphoreScriptDefinition"/>,
/// <see cref="ValidateSemaphoreScriptDefinition"/>, <see cref="ReleaseSemaphoreScriptDefinition"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SemaphoreSlotParams(RedisKey holdersKey, string leaseId, RedisValue expires);
