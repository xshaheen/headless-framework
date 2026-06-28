// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically releases a reader lock id from the reader hash.</summary>
internal sealed class ReleaseReadLockScriptDefinition : RedisScriptDefinition
{
    public static ReleaseReadLockScriptDefinition Instance { get; } = new();

    private ReleaseReadLockScriptDefinition()
        : base(
            """
            return redis.call('hdel', @readerKey, @leaseId)
            """
        ) { }
}

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="ReleaseReadLockScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReaderWriterReaderOnlyParams(RedisKey ReaderKey, string LeaseId, RedisValue Expires);
#pragma warning restore IDE1006
