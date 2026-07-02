// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically releases a distributed semaphore slot.</summary>
internal sealed class ReleaseSemaphoreScriptDefinition : RedisScriptDefinition
{
    public static ReleaseSemaphoreScriptDefinition Instance { get; } = new();

    private ReleaseSemaphoreScriptDefinition()
        : base(
            """
            return redis.call('zrem', @holdersKey, @leaseId)
            """
        ) { }
}
