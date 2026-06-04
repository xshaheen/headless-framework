// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

public sealed class RedisDistributedSemaphoreStorage(
    IConnectionMultiplexer multiplexer,
    [FromKeyedServices(RedisDistributedLockServiceKeys.ScriptsLoader)] HeadlessRedisScriptsLoader scriptsLoader
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

        var result = await _TryAcquireSemaphoreAsync(
                keys.HoldersKey,
                keys.FenceKey,
                lockId,
                maxCount,
                ttl,
                cancellationToken
            )
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

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                TryExtendSemaphoreScriptDefinition.Instance,
                _GetSemaphoreSlotParameters(keys.HoldersKey, lockId, ttl),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
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

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                ValidateSemaphoreScriptDefinition.Instance,
                _GetSemaphoreSlotParameters(keys.HoldersKey, lockId, ttl: null),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
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

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                ReleaseSemaphoreScriptDefinition.Instance,
                _GetSemaphoreSlotParameters(keys.HoldersKey, lockId, ttl: null),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (int)result > 0;
    }

    public async ValueTask<long> GetCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        var keys = _GetKeys(resource);
        cancellationToken.ThrowIfCancellationRequested();

        var result = await scriptsLoader
            .EvaluateAsync(
                Db,
                GetSemaphoreCountScriptDefinition.Instance,
                new SemaphoreCountParams(keys.HoldersKey),
                cancellationToken
            )
            .ConfigureAwait(false);

        return (long)result;
    }

    private async Task<(bool Acquired, long? FencingToken)> _TryAcquireSemaphoreAsync(
        RedisKey holdersKey,
        RedisKey fenceKey,
        string lockId,
        int maxCount,
        TimeSpan ttl,
        CancellationToken cancellationToken
    )
    {
        var parameters = new SemaphoreAcquireParams(
            holdersKey,
            fenceKey,
            lockId,
            maxCount,
            (long)ttl.TotalMilliseconds
        );
        var result = await scriptsLoader
            .EvaluateAsync(Db, TryAcquireSemaphoreWithFenceScriptDefinition.Instance, parameters, cancellationToken)
            .ConfigureAwait(false);
        var values = (RedisResult[]?)result;

        if (values is null || values.Length == 0)
        {
            throw new RedisServerException("Unexpected acquire semaphore script result.");
        }

        if ((int)values[0] <= 0)
        {
            return (false, null);
        }

        if (values.Length < 2)
        {
            throw new RedisServerException("Acquire semaphore script reported success without a fencing token.");
        }

        return (true, (long)values[1]);
    }

    private static SemaphoreSlotParams _GetSemaphoreSlotParameters(RedisKey holdersKey, string lockId, TimeSpan? ttl)
    {
        var expiresValue = ttl.HasValue ? (long)ttl.Value.TotalMilliseconds : RedisValue.EmptyString;

        return new SemaphoreSlotParams(holdersKey, lockId, expiresValue);
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SemaphoreAcquireParams(
        RedisKey holdersKey,
        RedisKey fenceKey,
        string lockId,
        int maxCount,
        long expires
    );

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SemaphoreSlotParams(RedisKey holdersKey, string lockId, RedisValue expires);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SemaphoreCountParams(RedisKey holdersKey);
}
