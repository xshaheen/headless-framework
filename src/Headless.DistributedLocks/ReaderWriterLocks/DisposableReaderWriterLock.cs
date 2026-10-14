// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.DistributedLocks;

/// <summary>
/// Concrete lease handle returned by <see cref="DistributedReadWriteLock"/> for both read and
/// write acquisitions. Delegates <see cref="DistributedLockHandleBase.RenewAsync"/>, validate, and release operations back
/// to the owning provider. Reader-writer leases never carry a fencing token — the value is always
/// <see langword="null"/> in the base class.
/// </summary>
internal sealed class DisposableReaderWriterLock : DistributedLockHandleBase
{
    private readonly ReaderWriterLockMode _mode;
    private readonly DistributedReadWriteLock _provider;

    internal DisposableReaderWriterLock(
        ReaderWriterLockMode mode,
        string resource,
        string leaseId,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        long acquireTimestamp,
        DistributedReadWriteLock provider,
        bool releaseOnDispose,
        bool autoExtend,
        DistributedLockOptions options,
        TimeProvider timeProvider,
        Action<string, string>? deregisterMonitor,
        ILogger logger
    )
        : base(
            resource,
            leaseId,
            // Reader-writer locks do not issue fencing tokens; FencingToken is always null.
            fencingToken: null,
            leaseDuration,
            timeWaitedForLock,
            acquireTimestamp,
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

    /// <summary>
    /// Manually renews this lease by extending its TTL in storage. Delegates to
    /// <see cref="DistributedReadWriteLock.RenewAsync"/> with the appropriate <see cref="ReaderWriterLockMode"/>.
    /// <see langword="null"/> uses the provider's <see cref="DistributedReadWriteLock.DefaultTimeUntilExpires"/>.
    /// </summary>
    protected override Task<bool> RenewCoreAsync(TimeSpan? timeUntilExpires, CancellationToken cancellationToken)
    {
        return _provider.RenewAsync(_mode, Resource, LeaseId, timeUntilExpires, cancellationToken);
    }

    // The provider applies its default for null; a read lease also clamps infinite to the default, while a write
    // lease keeps infinite as "never expires".
    protected override TimeSpan GetAppliedRenewTtl(TimeSpan? timeUntilExpires)
    {
        if (timeUntilExpires is not { } ttl)
        {
            return _provider.DefaultTimeUntilExpires;
        }

        return _mode == ReaderWriterLockMode.Read && ttl == Timeout.InfiniteTimeSpan
            ? _provider.DefaultTimeUntilExpires
            : ttl;
    }

    protected override Task<bool> RenewLeaseAsync(CancellationToken cancellationToken)
    {
        return _provider.RenewAsync(_mode, Resource, LeaseId, LeaseDuration, cancellationToken);
    }

    protected override Task<bool> ValidateOwnershipAsync(CancellationToken cancellationToken)
    {
        return _provider.ValidateAsync(_mode, Resource, LeaseId, cancellationToken);
    }

    protected override Task ReleaseCoreAsync()
    {
        return _provider.ReleaseAsync(_mode, Resource, LeaseId, CancellationToken.None);
    }

    protected override void OnMonitorDisposeFailed(Exception exception)
    {
        Logger.LogDisposableLockMonitorDisposeFailed(exception, Resource, LeaseId);
    }

    // ---- Reader-writer lease-loss classification override ----

    /// <summary>
    /// Reader-writer extend scripts return <see langword="false"/> ONLY for genuine loss (id no
    /// longer present) or writer-preference refusal (writer-waiting marker forces a queued writer to
    /// drain the reader). Both must cancel LostToken — there is no ambiguous transient-vs-loss
    /// case here, so the base's ownership-probe disambiguation is skipped: a false renew classifies
    /// directly as <see cref="LeaseMonitor.LeaseState.Lost"/>. Transient storage exceptions surface
    /// from the await and are caught at the deadline boundary as
    /// <see cref="LeaseMonitor.LeaseState.Unknown"/>.
    /// </summary>
    protected override LeaseMonitor.LeaseState? ClassifyRenewFailure()
    {
        return LeaseMonitor.LeaseState.Lost;
    }
}
