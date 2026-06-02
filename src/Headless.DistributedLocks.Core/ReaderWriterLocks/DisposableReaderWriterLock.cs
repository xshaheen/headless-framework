// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class DisposableReaderWriterLock : DistributedLockHandleBase
{
    private readonly ReaderWriterLockMode _mode;
    private readonly DistributedReaderWriterLockProvider _provider;

    internal DisposableReaderWriterLock(
        ReaderWriterLockMode mode,
        string resource,
        string lockId,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        DistributedReaderWriterLockProvider provider,
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
            // Reader-writer locks do not issue fencing tokens; FencingToken is always null.
            fencingToken: null,
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
        _mode = mode;
        _provider = provider;
    }

    public override async Task<bool> RenewAsync(
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        var result = await _provider
            .RenewAsync(_mode, Resource, LockId, timeUntilExpires, cancellationToken)
            .ConfigureAwait(false);

        if (result)
        {
            IncrementRenewalCount();
        }

        return result;
    }

    protected override Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        return _provider.RenewAsync(_mode, Resource, LockId, LeaseDuration, cancellationToken);
    }

    protected override Task<bool> ValidateOwnershipAsync(CancellationToken cancellationToken)
    {
        return _provider.ValidateAsync(_mode, Resource, LockId, cancellationToken);
    }

    protected override Task ReleaseCoreAsync()
    {
        return _provider.ReleaseAsync(_mode, Resource, LockId, CancellationToken.None);
    }

    protected override void OnMonitorDisposeFailed(Exception exception)
    {
        Logger.LogDisposableLockMonitorDisposeFailed(exception, Resource, LockId);
    }

    // ---- Reader-writer lease-loss classification override ----

    /// <summary>
    /// Reader-writer extend scripts return <see langword="false"/> ONLY for genuine loss (id no
    /// longer present) or writer-preference refusal (writer-waiting marker forces a queued writer to
    /// drain the reader). Both must cancel HandleLostToken — there is no ambiguous transient-vs-loss
    /// case here, so the base's ownership-probe disambiguation is skipped: a false renew classifies
    /// directly as <see cref="LeaseMonitor.LeaseState.Lost"/>. Transient storage exceptions surface
    /// from the await and are caught at the deadline boundary as
    /// <see cref="LeaseMonitor.LeaseState.Unknown"/>.
    /// </summary>
    protected override LeaseMonitor.LeaseState? ClassifyRenewFailure() => LeaseMonitor.LeaseState.Lost;
}
