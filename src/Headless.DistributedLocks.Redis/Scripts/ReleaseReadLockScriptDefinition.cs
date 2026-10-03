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
