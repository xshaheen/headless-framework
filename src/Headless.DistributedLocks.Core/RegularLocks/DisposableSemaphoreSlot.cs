// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class DisposableSemaphoreSlot : DistributedLockHandleBase
{
    private readonly DistributedSemaphoreProvider _provider;

    internal DisposableSemaphoreSlot(
        string resource,
        string lockId,
        long? fencingToken,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        DistributedSemaphoreProvider provider,
        bool releaseOnDispose,
        bool autoExtend,
        DistributedLockOptions options,
        TimeProvider timeProvider,
        Action<string, string>? deregisterMonitor,
        ILogger logger
    )
        : base(
            resource,
            lockId,
            fencingToken,
            leaseDuration,
            timeWaitedForLock,
            releaseOnDispose,
            autoExtend,
            options,
            timeProvider,
            deregisterMonitor,
            logger
        )
    {
        _provider = provider;
    }

    public override async Task<bool> RenewAsync(
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        var result = await _provider
            .RenewAsync(Resource, LockId, timeUntilExpires, cancellationToken)
            .ConfigureAwait(false);

        if (result)
        {
            IncrementRenewalCount();
        }

        return result;
    }

    protected override Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        return _provider.RenewAsync(Resource, LockId, LeaseDuration, cancellationToken);
    }

    protected override Task<bool> ValidateOwnershipAsync(CancellationToken cancellationToken)
    {
        return _provider.ValidateAsync(Resource, LockId, cancellationToken);
    }

    protected override Task ReleaseCoreAsync()
    {
        return _provider.ReleaseAsync(Resource, LockId, CancellationToken.None);
    }

    protected override void OnMonitorDisposeFailed(Exception exception)
    {
        Logger.LogDisposableLockMonitorDisposeFailed(exception, Resource, LockId);
    }
}
