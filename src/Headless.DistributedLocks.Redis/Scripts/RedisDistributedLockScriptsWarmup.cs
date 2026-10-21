// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>
/// Loads the Lua scripts of the Redis lock, read-write lock, and semaphore after the host starts, so the first acquire
/// runs its script by SHA instead of paying the load on a hot path.
/// </summary>
/// <remarks>
/// It does not gate readiness: a script not loaded yet still loads on its first use, and the
/// <c>distributed-locks-redis</c> check already reports a Redis that is down. A failure is retried with the warm-up
/// back-off.
/// </remarks>
internal sealed class RedisDistributedLockScriptsWarmup(
    [FromKeyedServices(RedisDistributedLockServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader loader
) : IWarmup
{
    private static readonly IReadOnlyList<RedisScriptDefinition> _Scripts =
    [
        TryAcquireLockWithFenceScriptDefinition.Instance,
        RemoveIfEqualScriptDefinition.Instance,
        ReplaceIfEqualScriptDefinition.Instance,
        TryAcquireReadLockScriptDefinition.Instance,
        TryExtendReadLockScriptDefinition.Instance,
        ReleaseReadLockScriptDefinition.Instance,
        TryAcquireWriteLockScriptDefinition.Instance,
        TryExtendWriteLockScriptDefinition.Instance,
        ReleaseWriteLockScriptDefinition.Instance,
        TryAcquireSemaphoreWithFenceScriptDefinition.Instance,
        TryExtendSemaphoreScriptDefinition.Instance,
        ValidateSemaphoreScriptDefinition.Instance,
        ReleaseSemaphoreScriptDefinition.Instance,
        GetSemaphoreCountScriptDefinition.Instance,
    ];

    public string Name => "RedisDistributedLockScripts";

    public bool GatesReadiness => false;

    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        loader.LoadAsync(_Scripts, cancellationToken).AsTask();
}
