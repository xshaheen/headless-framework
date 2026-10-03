// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="TryAcquireWriteLockScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReaderWriterWriteParams(
    RedisKey writerKey,
    RedisKey readerKey,
    string leaseId,
    string waitingId,
    RedisValue expires,
    RedisValue markerExpires
);
