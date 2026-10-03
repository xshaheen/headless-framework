// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>
/// Parameters shared by the writer-only scripts (<see cref="TryExtendWriteLockScriptDefinition"/>,
/// <see cref="ReleaseWriteLockScriptDefinition"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReaderWriterWriterOnlyParams(
    RedisKey writerKey,
    string leaseId,
    string waitingId,
    RedisValue expires
);
