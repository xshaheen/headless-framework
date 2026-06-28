// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically releases a writer lock or the caller's writer-waiting marker.</summary>
internal sealed class ReleaseWriteLockScriptDefinition : RedisScriptDefinition
{
    public static ReleaseWriteLockScriptDefinition Instance { get; } = new();

    private ReleaseWriteLockScriptDefinition()
        : base(
            """
            local current = redis.call('get', @writerKey)
            if current == @leaseId or current == @waitingId then
              return redis.call('del', @writerKey)
            end
            return 0
            """
        ) { }
}
