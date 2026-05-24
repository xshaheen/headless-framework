// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Polly;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

public sealed class DistributedReaderWriterLockProvider(
    IDistributedReaderWriterLockStorage storage,
    IOutboxPublisher? outboxPublisher,
    DistributedLockOptions options,
    ILongIdGenerator longIdGenerator,
    TimeProvider timeProvider,
    ILogger<DistributedReaderWriterLockProvider> logger
) : IDistributedReaderWriterLockProvider
{
    private static readonly TimeSpan _LongLockWarningThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _NonBlockingAcquireDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _WaitingMarkerCleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly ScopedDistributedReaderWriterLockStorage _storage = new(storage, options.KeyPrefix);
    private readonly IOutboxPublisher? _outboxPublisher = DistributedLockCoreHelpers.ConfigureOutboxPublisher(
        outboxPublisher,
        logger
    );
    private readonly LeaseMonitorRegistry _monitorRegistry = new(logger);
    private readonly int _maxResourceNameLength = options.MaxResourceNameLength;
    private readonly TimeSpan _writerWaitingMarkerTtl = options.WriterWaitingMarkerTtl;

    // Long-running release pipeline shared with the mutex provider. Release is a terminal state
    // write — if the caller's CT fires mid-retry we still want to clean up, so the release path
    // passes CancellationToken.None when executing the pipeline.
    private readonly ResiliencePipeline _releasePipeline = DistributedLockCoreHelpers.BuildReleasePipeline(
        timeProvider,
        logger
    );

    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    public async Task<IDistributedLock> AcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var acquired = await TryAcquireReadLockAsync(resource, options, cancellationToken).ConfigureAwait(false);

        return acquired
            ?? throw (
                options?.AcquireTimeout == TimeSpan.Zero
                    ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
                    : new LockAcquisitionTimeoutException(resource)
            );
    }

    public Task<IDistributedLock?> TryAcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _TryAcquireAsync(ReaderWriterLockMode.Read, resource, options, cancellationToken);
    }

    public async Task<IDistributedLock> AcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var acquired = await TryAcquireWriteLockAsync(resource, options, cancellationToken).ConfigureAwait(false);

        return acquired
            ?? throw (
                options?.AcquireTimeout == TimeSpan.Zero
                    ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
                    : new LockAcquisitionTimeoutException(resource)
            );
    }

    public Task<IDistributedLock?> TryAcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _TryAcquireAsync(ReaderWriterLockMode.Write, resource, options, cancellationToken);
    }

    public async Task<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);

        return await _storage.IsReadLockedAsync(resource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);

        return await _storage.IsWriteLockedAsync(resource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);

        return await _storage.GetReaderCountAsync(resource, cancellationToken).ConfigureAwait(false);
    }

    internal Task<bool> RenewAsync(
        ReaderWriterLockMode mode,
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        timeUntilExpires = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
            timeUntilExpires,
            DefaultTimeUntilExpires
        );

        return mode switch
        {
            ReaderWriterLockMode.Read => _storage.TryExtendReadAsync(
                    resource,
                    lockId,
                    timeUntilExpires,
                    cancellationToken
                )
                .AsTask(),
            ReaderWriterLockMode.Write => _storage.TryExtendWriteAsync(
                    resource,
                    lockId,
                    timeUntilExpires,
                    cancellationToken
                )
                .AsTask(),
            _ => throw new InvalidOperationException("Unknown reader-writer lock mode."),
        };
    }

    internal Task<bool> ValidateAsync(
        ReaderWriterLockMode mode,
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        return mode switch
        {
            ReaderWriterLockMode.Read => _storage.ValidateReadAsync(resource, lockId, cancellationToken).AsTask(),
            ReaderWriterLockMode.Write => _storage.ValidateWriteAsync(resource, lockId, cancellationToken).AsTask(),
            _ => throw new InvalidOperationException("Unknown reader-writer lock mode."),
        };
    }

    internal async Task ReleaseAsync(
        ReaderWriterLockMode mode,
        string resource,
        string lockId,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(lockId);

        // Release is a terminal-state write. Use CancellationToken.None so the retry pipeline
        // completes even if the caller's CT fires — the storage-level cleanup is the source of
        // truth for whether waiters can proceed. Same convention as the mutex provider.
        var storageRef = _storage;
        var resourceRef = resource;
        var lockIdRef = lockId;
        switch (mode)
        {
            case ReaderWriterLockMode.Read:
                await _releasePipeline
                    .ExecuteAsync(
                        async ct =>
                            await storageRef.ReleaseReadAsync(resourceRef, lockIdRef, ct).ConfigureAwait(false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                break;
            case ReaderWriterLockMode.Write:
                await _releasePipeline
                    .ExecuteAsync(
                        async ct =>
                            await storageRef.ReleaseWriteAsync(resourceRef, lockIdRef, ct).ConfigureAwait(false),
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("Unknown reader-writer lock mode.");
        }

        var monitor = _monitorRegistry.TryDeregister(resource, lockId);

        if (monitor is not null)
        {
            try
            {
                await monitor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogLeaseMonitorFaulted(exception, resource, lockId);
            }
        }

        if (_outboxPublisher is not null)
        {
            var released = new DistributedLockReleased(resource, lockId);

            try
            {
                await _outboxPublisher.PublishAsync(released, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogLockReleasePublishFailed(exception, resource, lockId);
            }
        }
    }

    private async Task<IDistributedLock?> _TryAcquireAsync(
        ReaderWriterLockMode mode,
        string resource,
        DistributedLockAcquireOptions? acquireOptions,
        CancellationToken cancellationToken
    )
    {
        _ValidateResource(resource);
        acquireOptions ??= new DistributedLockAcquireOptions();
        DistributedLockCoreHelpers.ValidateAcquireTimeout(acquireOptions.AcquireTimeout);
        cancellationToken.ThrowIfCancellationRequested();

        var timeUntilExpires = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
            acquireOptions.TimeUntilExpires,
            DefaultTimeUntilExpires
        );
        var monitorLease = acquireOptions.Monitoring != LockMonitoringMode.None;
        var autoExtend = acquireOptions.Monitoring == LockMonitoringMode.AutoExtend;
        var leaseDuration = DistributedLockCoreHelpers.RequireFiniteLeaseDuration(timeUntilExpires, monitorLease);
        var acquireTimeout = acquireOptions.AcquireTimeout;
        var lockId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        Ensure.False(lockId.Contains(':', StringComparison.Ordinal), "Reader-writer lock ids cannot contain ':'.");

        using var activity = _StartLockActivity(mode, resource);
        var timestamp = timeProvider.GetTimestamp();

        if (acquireTimeout == TimeSpan.Zero)
        {
            return await _TryAcquireOnceAsync(
                    mode,
                    resource,
                    lockId,
                    timeUntilExpires,
                    timestamp,
                    acquireOptions.ReleaseOnDispose,
                    monitorLease,
                    autoExtend,
                    leaseDuration,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        using var timeoutCts = timeProvider.CreateCancellationTokenSource(acquireTimeout ?? DefaultAcquireTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        var gotLock = false;
        var retryAttempt = 0;
        var isFirstAttempt = true;
        // A waiting marker is only planted by the Lua write-acquire script after it observes a
        // contended state (i.e., TryAcquireWrite returned false). For Read mode the storage never
        // plants a marker; for Write mode the first false return is what triggers the placeholder.
        // Tracking this lets the finally block skip a wasted round-trip when no marker exists.
        var waitingMarkerPlanted = false;

        try
        {
            do
            {
                var attemptToken = isFirstAttempt && timeoutCts.IsCancellationRequested ? cancellationToken : cts.Token;
                isFirstAttempt = false;

                try
                {
                    gotLock = await _TryAcquireStorageAsync(
                            mode,
                            resource,
                            lockId,
                            timeUntilExpires,
                            attemptToken
                        )
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The storage call may have planted the writer-waiting marker server-side
                    // before the client observed cancellation. Mark it as potentially planted so
                    // the finally block issues the idempotent cleanup release.
                    if (mode == ReaderWriterLockMode.Write)
                    {
                        waitingMarkerPlanted = true;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    break;
                }
                catch (Exception e) when (e is not (ObjectDisposedException or InvalidOperationException))
                {
                    // Same rationale as the cancellation catch — Lua may have planted the marker
                    // before the client-side exception surfaced. Cleanup is idempotent.
                    if (mode == ReaderWriterLockMode.Write)
                    {
                        waitingMarkerPlanted = true;
                    }

                    logger.LogErrorAcquiringLockElapsed(e, resource, lockId, timeProvider, timestamp);
                }

                if (gotLock)
                {
                    break;
                }

                if (mode == ReaderWriterLockMode.Write)
                {
                    waitingMarkerPlanted = true;
                }

                if (cts.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    break;
                }

                var delayAmount = DistributedLockCoreHelpers.GetBackoffDelay(retryAttempt++);
                try
                {
                    await timeProvider.Delay(delayAmount, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            } while (!cts.IsCancellationRequested);
        }
        finally
        {
            if (!gotLock && waitingMarkerPlanted)
            {
                await _CleanupWaitingMarkerAsync(mode, resource, lockId).ConfigureAwait(false);
            }
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        DistributedLockMetrics.LockWaitTime.Record(timeWaitedForLock.TotalMilliseconds);

        if (!gotLock)
        {
            DistributedLockMetrics.LockFailed.Add(1);
            return null;
        }

        if (timeWaitedForLock > _LongLockWarningThreshold)
        {
            logger.LogLongLockAcquired(resource, lockId, timeWaitedForLock);
        }
        else
        {
            logger.LogAcquiredLock(resource, lockId, timeWaitedForLock);
        }

        return _CreateLockHandle(
            mode,
            resource,
            lockId,
            leaseDuration,
            timeWaitedForLock,
            acquireOptions.ReleaseOnDispose,
            monitorLease,
            autoExtend
        );
    }

    private async Task<IDistributedLock?> _TryAcquireOnceAsync(
        ReaderWriterLockMode mode,
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires,
        long timestamp,
        bool releaseOnDispose,
        bool monitorLease,
        bool autoExtend,
        TimeSpan leaseDuration,
        CancellationToken callerToken
    )
    {
        using var safetyCts = timeProvider.CreateCancellationTokenSource(_NonBlockingAcquireDeadline);
        using var linkedCts = callerToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(safetyCts.Token, callerToken)
            : null;

        var attemptToken = linkedCts?.Token ?? safetyCts.Token;
        bool gotLock;

        try
        {
            gotLock = await _TryAcquireStorageAsync(mode, resource, lockId, timeUntilExpires, attemptToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cleanup is performed by the post-loop `if (!gotLock)` block when control reaches
            // it. Skip the inline round trip here so cancellation doesn't pay double the cleanup
            // cost. If the caller cancelled, surface the exception immediately so the cleanup
            // is intentionally skipped.
            if (callerToken.IsCancellationRequested)
            {
                throw;
            }

            gotLock = false;
        }
        catch (Exception e) when (e is not (ObjectDisposedException or InvalidOperationException))
        {
            logger.LogErrorAcquiringLockElapsed(e, resource, lockId, timeProvider, timestamp);
            gotLock = false;
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        DistributedLockMetrics.LockWaitTime.Record(timeWaitedForLock.TotalMilliseconds);

        if (!gotLock)
        {
            // Only writers can plant the waiting marker (the Lua write-acquire script does it
            // after a contended return). Read-mode contention never touches the writer key, so
            // running cleanup for it is a pointless round trip. `_CleanupWaitingMarkerAsync`
            // already short-circuits on non-Write mode, but mirroring the guarded shape here
            // keeps the intent obvious.
            if (mode == ReaderWriterLockMode.Write)
            {
                await _CleanupWaitingMarkerAsync(mode, resource, lockId).ConfigureAwait(false);
            }

            DistributedLockMetrics.LockFailed.Add(1);
            return null;
        }

        return _CreateLockHandle(
            mode,
            resource,
            lockId,
            leaseDuration,
            timeWaitedForLock,
            releaseOnDispose,
            monitorLease,
            autoExtend
        );
    }

    private ValueTask<bool> _TryAcquireStorageAsync(
        ReaderWriterLockMode mode,
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires,
        CancellationToken cancellationToken
    )
    {
        return mode switch
        {
            ReaderWriterLockMode.Read => _storage.TryAcquireReadAsync(
                resource,
                lockId,
                timeUntilExpires,
                cancellationToken
            ),
            ReaderWriterLockMode.Write => _storage.TryAcquireWriteAsync(
                resource,
                lockId,
                _storage.GetWaitingId(lockId),
                timeUntilExpires,
                _writerWaitingMarkerTtl,
                cancellationToken
            ),
            _ => throw new InvalidOperationException("Unknown reader-writer lock mode."),
        };
    }

    private DisposableReaderWriterLock _CreateLockHandle(
        ReaderWriterLockMode mode,
        string resource,
        string lockId,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        bool releaseOnDispose,
        bool monitorLease,
        bool autoExtend
    )
    {
        var handle = new DisposableReaderWriterLock(
            mode,
            resource,
            lockId,
            leaseDuration,
            timeWaitedForLock,
            this,
            releaseOnDispose,
            autoExtend,
            options,
            timeProvider,
            _DeregisterMonitor,
            logger
        );

        if (!monitorLease)
        {
            return handle;
        }

#pragma warning disable CA2000 // Ownership is transferred to the returned handle and drained from DisposeAsync.
        var monitor = new LeaseMonitor(handle, timeProvider, logger);
#pragma warning restore CA2000
        _monitorRegistry.Register(resource, lockId, monitor);
        handle.AttachMonitor(monitor);

        return handle;
    }

    private async Task _CleanupWaitingMarkerAsync(ReaderWriterLockMode mode, string resource, string lockId)
    {
        if (mode != ReaderWriterLockMode.Write)
        {
            return;
        }

        try
        {
            using var cleanupCts = timeProvider.CreateCancellationTokenSource(_WaitingMarkerCleanupTimeout);
            await _storage.ReleaseWriteAsync(resource, lockId, cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogBestEffortLockCleanupFailed(exception, resource, lockId);
        }
    }

    private void _DeregisterMonitor(string resource, string lockId)
    {
        _ = _monitorRegistry.TryDeregister(resource, lockId);
    }

    internal int GetActiveMonitorCount(string resource)
    {
        return _monitorRegistry.GetMonitorCount(resource);
    }

    private void _ValidateResource(string resource)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsLessThanOrEqualTo(resource.Length, _maxResourceNameLength, paramName: nameof(resource));
    }

    private static Activity? _StartLockActivity(ReaderWriterLockMode mode, string resource)
    {
        var activity = DistributedLocksDiagnostics.Start("reader-writer-lock.acquire");

        if (activity is null)
        {
            return null;
        }

        activity.AddTag("headless.lock.resource", resource);
        activity.AddTag("headless.lock.mode", _GetModeTag(mode));
        activity.DisplayName = $"Reader-writer lock: {resource}";

        return activity;
    }

    private static string _GetModeTag(ReaderWriterLockMode mode)
    {
        return mode switch
        {
            ReaderWriterLockMode.Read => "read",
            ReaderWriterLockMode.Write => "write",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown reader-writer lock mode."),
        };
    }

}
