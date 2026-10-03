// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>
/// Parameters shared by the read-lock scripts (<see cref="TryAcquireReadLockScriptDefinition"/>,
/// <see cref="TryExtendReadLockScriptDefinition"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReaderWriterReadParams(
    RedisKey writerKey,
    RedisKey readerKey,
    string leaseId,
    RedisValue expires
);
