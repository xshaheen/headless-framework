// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using System.Globalization;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class ConnectionScopedDistributedLockProvider(
    IConnectionScopedLockStorage storage,
    IReleaseSignal releaseSignal,
    DistributedLockOptions options,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider,
    ILogger<ConnectionScopedDistributedLockProvider> logger,
    IFencingTokenSource? fencingTokenSource = null,
    TimeSpan? pollingFallback = null
) : IDistributedLockProvider
{
    private static readonly TimeSpan _DefaultPollingFallback = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _pollingFallback = pollingFallback ?? _DefaultPollingFallback;

    public TimeSpan DefaultTimeUntilExpires => Timeout.InfiniteTimeSpan;

    public TimeSpan DefaultAcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public Task<IDistributedLock> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        return _AcquireCoreAsync(throwOnTimeout: true, resource, acquireOptions, isShared: false, cancellationToken)!;
    }

    public Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        return _AcquireCoreAsync(throwOnTimeout: false, resource, acquireOptions, isShared: false, cancellationToken);
    }

    internal Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        bool isShared,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        return _AcquireCoreAsync(throwOnTimeout: false, resource, acquireOptions, isShared, cancellationToken);
    }

    private async Task<IDistributedLock?> _AcquireCoreAsync(
        bool throwOnTimeout,
        string resource,
        DistributedLockAcquireOptions? acquireOptions,
        bool isShared,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrWhiteSpace(resource);

        if (resource.Length > options.MaxResourceNameLength)
        {
            throw new ArgumentException(
                $"{nameof(resource)} cannot exceed {options.MaxResourceNameLength} characters.",
                nameof(resource)
            );
        }

        var acquireTimeout = acquireOptions?.AcquireTimeout ?? DefaultAcquireTimeout;
        var started = timeProvider.GetTimestamp();
        var deadline = acquireTimeout == Timeout.InfiniteTimeSpan
            ? DateTimeOffset.MaxValue
            : timeProvider.GetUtcNow().Add(acquireTimeout);
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var handle = await storage.TryAcquireAsync(resource, lockId, isShared, cancellationToken).ConfigureAwait(false);

            if (handle is not null)
            {
                long? fencingToken;

                try
                {
                    fencingToken = isShared || fencingTokenSource is null
                        ? null
                        : await fencingTokenSource.NextAsync(resource, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    try
                    {
                        await _ReleaseAsync(handle, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception releaseException)
                    {
                        logger.LogConnectionScopedLockReleaseFailed(releaseException, resource, lockId);
                    }

                    throw;
                }

                var waited = timeProvider.GetElapsedTime(started);

                return new ConnectionScopedDistributedLockHandle(
                    handle,
                    fencingToken,
                    waited,
                    acquireOptions?.ReleaseOnDispose ?? true,
                    timeProvider,
                    _ReleaseAsync,
                    logger
                );
            }

            if (acquireTimeout == TimeSpan.Zero || timeProvider.GetUtcNow() >= deadline)
            {
                if (!throwOnTimeout)
                {
                    return null;
                }

                throw acquireTimeout == TimeSpan.Zero
                    ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
                    : new LockAcquisitionTimeoutException(resource);
            }

            var remaining = deadline == DateTimeOffset.MaxValue
                ? _pollingFallback
                : deadline - timeProvider.GetUtcNow();

            if (remaining <= TimeSpan.Zero)
            {
                continue;
            }

            var wait = remaining < _pollingFallback ? remaining : _pollingFallback;
            await releaseSignal.WaitAsync(resource, wait, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    public Task<string?> GetLockIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        return storage.GetLocalLockIdAsync(resource, cancellationToken).AsTask();
    }

    public async Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        await storage.ReleaseAsync(resource, lockId, cancellationToken).ConfigureAwait(false);
        await releaseSignal.PublishAsync(resource, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return storage.IsLockedAsync(resource, cancellationToken: cancellationToken).AsTask();
    }

    internal Task<bool> IsLockedAsync(string resource, bool isShared, CancellationToken cancellationToken = default)
    {
        return storage.IsLockedAsync(resource, isShared, cancellationToken).AsTask();
    }

    internal Task<long> GetLocksCountAsync(string resource, bool isShared, CancellationToken cancellationToken = default)
    {
        return storage.GetLocksCountAsync(resource, isShared, cancellationToken).AsTask();
    }

    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TimeSpan?>(null);
    }

    public async Task<LockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default)
    {
        return (await storage.ListActiveLocksAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(x =>
            string.Equals(x.Resource, resource, StringComparison.Ordinal)
        );
    }

    public Task<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
    {
        return storage.ListActiveLocksAsync(cancellationToken).AsTask();
    }

    public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        return storage.GetActiveLocksCountAsync(cancellationToken).AsTask();
    }

    private async ValueTask _ReleaseAsync(ConnectionScopedLockHandle handle, CancellationToken cancellationToken)
    {
        await storage.ReleaseAsync(handle, cancellationToken).ConfigureAwait(false);
        await releaseSignal.PublishAsync(handle.Resource, cancellationToken).ConfigureAwait(false);
    }
}
