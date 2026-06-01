// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

public sealed class RedisDistributedSemaphoreStorage(
    IConnectionMultiplexer multiplexer,
    HeadlessRedisScriptsLoader scriptsLoader
) : IDistributedSemaphoreStorage
{
    private IDatabase Db => multiplexer.GetDatabase();

    public async ValueTask<DistributedLockAcquireResult> TryAcquireAsync(
        string resource,
        string lockId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        Argument.IsNotNullOrEmpty(lockId);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);
        Argument.IsGreaterThan(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .TryAcquireSemaphoreAsync(Db, keys.HoldersKey, keys.FenceKey, lockId, maxCount, ttl, cancellationToken)
            .ConfigureAwait(false);

        return result.Acquired
            ? new DistributedLockAcquireResult(Acquired: true, result.FencingToken)
            : DistributedLockAcquireResult.Failed;
    }

    public async ValueTask<bool> TryExtendAsync(
        string resource,
        string lockId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        Argument.IsNotNullOrEmpty(lockId);
        Argument.IsGreaterThan(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader
            .TryExtendSemaphoreAsync(Db, keys.HoldersKey, lockId, ttl, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> ValidateAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        Argument.IsNotNullOrEmpty(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader.ValidateSemaphoreAsync(Db, keys.HoldersKey, lockId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<bool> ReleaseAsync(
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = _GetKeys(resource);
        Argument.IsNotNullOrEmpty(lockId);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader.ReleaseSemaphoreAsync(Db, keys.HoldersKey, lockId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<long> GetCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        return await scriptsLoader.GetSemaphoreCountAsync(Db, keys.HoldersKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private static (RedisKey HoldersKey, RedisKey FenceKey) _GetKeys(string resource)
    {
        Argument.IsNotNullOrEmpty(resource);
        Ensure.False(
            resource.Contains('{', StringComparison.Ordinal) || resource.Contains('}', StringComparison.Ordinal),
            "Semaphore resources cannot contain '{' or '}' because Redis hash-tags are storage-owned."
        );

        var hashTag = "{" + resource + "}";

        return (hashTag + ":holders", "fence:" + hashTag);
    }
}
