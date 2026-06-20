// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Polly;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Distributed reader-writer lock provider that allows concurrent readers and exclusive writers,
/// with writer preference (queued writers block new readers until promoted).
/// Implements <see cref="IDistributedReadWriteLock"/>; registered as a singleton by
/// <see cref="SetupDistributedLocks"/> and should not be instantiated directly.
/// </summary>
/// <remarks>
/// All acquire paths emit OpenTelemetry metrics and activities via
/// <see cref="DistributedLockMetrics"/> and <see cref="DistributedLocksDiagnostics"/>.
/// The release path uses a long-running Polly retry pipeline (15 attempts) shared with
/// <see cref="DistributedLock"/> to ensure that sustained storage unavailability does not
/// strand waiters indefinitely. Release is bounded by <see cref="DistributedLockOptions.DisposeTimeout"/>;
/// on timeout a warning is logged and the TTL-based expiry acts as the eventual consistency
/// mechanism.
/// </remarks>
public sealed class DistributedReadWriteLock(
    IDistributedReadWriteLockStorage storage,
    IOutboxBus? outboxBus,
    DistributedLockOptions lockOptions,
    IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    ILogger<DistributedReadWriteLock> logger
) : IDistributedReadWriteLock
{
    private static readonly TimeSpan _LongLockWarningThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _NonBlockingAcquireDeadline = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _WaitingMarkerCleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly ScopedDistributedReadWriteLockStorage _storage = new(storage, lockOptions.KeyPrefix);
    private readonly IOutboxBus? _outboxBus = DistributedLockCoreHelpers.ConfigureOutboxBus(outboxBus, logger);
    private readonly LeaseMonitorRegistry _monitorRegistry = new(logger);
    private readonly int _maxResourceNameLength = lockOptions.MaxResourceNameLength;
    private readonly TimeSpan _writerWaitingMarkerTtl = lockOptions.WriterWaitingMarkerTtl;
    private readonly TimeSpan _disposeTimeout = lockOptions.DisposeTimeout;

    // Long-running release pipeline shared with the mutex provider. Release is a terminal state
    // write — if the caller's CT fires mid-retry we still want to clean up, so the release path
    // passes CancellationToken.None when executing the pipeline.
    private readonly ResiliencePipeline _releasePipeline = DistributedLockCoreHelpers.BuildReleasePipeline(
        timeProvider,
        logger
    );

    /// <inheritdoc/>
    public TimeSpan DefaultTimeUntilExpires { get; } = TimeSpan.FromMinutes(20);

    /// <inheritdoc/>
    public TimeSpan DefaultAcquireTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.Monitoring"/> requires a finite
    /// lease but <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is
    /// <see cref="Timeout.InfiniteTimeSpan"/>, or when the resource name exceeds
    /// <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative or
    /// exceeds <see cref="int.MaxValue"/> milliseconds, or when
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is negative or exceeds
    /// <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">
    /// Thrown when the lock cannot be acquired before
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> elapses (or
    /// <see cref="DefaultAcquireTimeout"/> when not specified).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    public async Task<IDistributedLease> AcquireReadLockAsync(
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

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.Monitoring"/> requires a finite
    /// lease but <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is
    /// <see cref="Timeout.InfiniteTimeSpan"/>, or when the resource name exceeds
    /// <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative or
    /// exceeds <see cref="int.MaxValue"/> milliseconds, or when
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is negative or exceeds
    /// <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    public Task<IDistributedLease?> TryAcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _TryAcquireAsync(ReaderWriterLockMode.Read, resource, options, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.Monitoring"/> requires a finite
    /// lease but <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is
    /// <see cref="Timeout.InfiniteTimeSpan"/>, or when the resource name exceeds
    /// <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative or
    /// exceeds <see cref="int.MaxValue"/> milliseconds, or when
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is negative or exceeds
    /// <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">
    /// Thrown when the lock cannot be acquired before
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> elapses (or
    /// <see cref="DefaultAcquireTimeout"/> when not specified).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    public async Task<IDistributedLease> AcquireWriteLockAsync(
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

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.Monitoring"/> requires a finite
    /// lease but <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is
    /// <see cref="Timeout.InfiniteTimeSpan"/>, or when the resource name exceeds
    /// <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative or
    /// exceeds <see cref="int.MaxValue"/> milliseconds, or when
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is negative or exceeds
    /// <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    public Task<IDistributedLease?> TryAcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _TryAcquireAsync(ReaderWriterLockMode.Write, resource, options, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the resource name exceeds <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    public async Task<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);

        return await _storage.IsReadLockedAsync(resource, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the resource name exceeds <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    public async Task<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);

        return await _storage.IsWriteLockedAsync(resource, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the resource name exceeds <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    public async Task<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        _ValidateResource(resource);

        return await _storage.GetReaderCountAsync(resource, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renews the lease for <paramref name="resource"/> in the given <paramref name="mode"/>.
    /// Delegates to the appropriate storage extend method based on mode.
    /// Returns <see langword="true"/> when the lease was extended;
    /// <see langword="false"/> when the lease has been lost (expired, evicted, or writer-preference
    /// refusal for read leases).
    /// Called by <see cref="DisposableReaderWriterLock"/> during auto-extend or manual
    /// <see cref="DisposableReaderWriterLock.RenewAsync"/> calls.
    /// </summary>
    /// <param name="mode">Whether this is a read or write lease renewal.</param>
    /// <param name="resource">The locked resource name.</param>
    /// <param name="leaseId">The lease identifier to renew.</param>
    /// <param name="timeUntilExpires">
    /// New TTL. <see langword="null"/> falls back to <see cref="DefaultTimeUntilExpires"/>;
    /// <see cref="Timeout.InfiniteTimeSpan"/> is mapped to <see langword="null"/> (no expiry,
    /// write-mode only; read leases always receive a finite TTL).
    /// </param>
    /// <param name="cancellationToken">Token to cancel the storage round-trip.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace,
    /// or when <paramref name="leaseId"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the resource name exceeds <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="mode"/> is an unrecognized <see cref="ReaderWriterLockMode"/> value.
    /// </exception>
    internal Task<bool> RenewAsync(
        ReaderWriterLockMode mode,
        string resource,
        string leaseId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);

        timeUntilExpires = DistributedLockCoreHelpers.NormalizeTimeUntilExpires(
            timeUntilExpires,
            DefaultTimeUntilExpires
        );

        return mode switch
        {
            // Read renew also clamps null/infinite to the default — see _TryAcquireStorageAsync
            // for rationale.
            ReaderWriterLockMode.Read => _storage
                .TryExtendReadAsync(resource, leaseId, timeUntilExpires ?? DefaultTimeUntilExpires, cancellationToken)
                .AsTask(),
            ReaderWriterLockMode.Write => _storage
                .TryExtendWriteAsync(resource, leaseId, timeUntilExpires, cancellationToken)
                .AsTask(),
            _ => throw new InvalidOperationException("Unknown reader-writer lock mode."),
        };
    }

    /// <summary>
    /// Checks whether the caller's <paramref name="leaseId"/> is still present in storage for
    /// <paramref name="resource"/> in the given <paramref name="mode"/>. Used by the monitoring
    /// loop as an ownership probe when <see cref="DisposableReaderWriterLock.ClassifyRenewFailure"/>
    /// returns <see langword="null"/>. Result is advisory only.
    /// Returns <see langword="true"/> when the lease is still held;
    /// <see langword="false"/> when it has been lost.
    /// </summary>
    /// <param name="mode">Whether to validate a read or write lease.</param>
    /// <param name="resource">The locked resource name.</param>
    /// <param name="leaseId">The lease identifier to validate.</param>
    /// <param name="cancellationToken">Token to cancel the storage round-trip.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace,
    /// or when <paramref name="leaseId"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the resource name exceeds <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="mode"/> is an unrecognized <see cref="ReaderWriterLockMode"/> value.
    /// </exception>
    internal Task<bool> ValidateAsync(
        ReaderWriterLockMode mode,
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);

        return mode switch
        {
            ReaderWriterLockMode.Read => _storage.ValidateReadAsync(resource, leaseId, cancellationToken).AsTask(),
            ReaderWriterLockMode.Write => _storage.ValidateWriteAsync(resource, leaseId, cancellationToken).AsTask(),
            _ => throw new InvalidOperationException("Unknown reader-writer lock mode."),
        };
    }

    /// <summary>
    /// Releases the lease identified by <paramref name="leaseId"/> for <paramref name="resource"/>
    /// in the given <paramref name="mode"/>. The release path retries up to 15 times via a shared
    /// Polly pipeline and is bounded by <see cref="DistributedLockOptions.DisposeTimeout"/>; on
    /// timeout a warning is logged and the TTL acts as eventual cleanup. After storage release,
    /// any active lease monitor is disposed, and if an outbox bus is configured a
    /// <see cref="DistributedLockReleased"/> message is published to wake waiters.
    /// </summary>
    /// <param name="mode">Whether to release a read or write lease.</param>
    /// <param name="resource">The locked resource name.</param>
    /// <param name="leaseId">The lease identifier to release.</param>
    /// <param name="cancellationToken">
    /// Used only for the outbox publish; the underlying storage release always uses
    /// <see cref="CancellationToken.None"/> so disposal is not interrupted by caller cancellation.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resource"/> is <see langword="null"/> or whitespace,
    /// or when <paramref name="leaseId"/> is <see langword="null"/> or whitespace.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the resource name exceeds <see cref="DistributedLockOptions.MaxResourceNameLength"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="mode"/> is an unrecognized <see cref="ReaderWriterLockMode"/> value.
    /// </exception>
    internal async Task ReleaseAsync(
        ReaderWriterLockMode mode,
        string resource,
        string leaseId,
        CancellationToken cancellationToken = default
    )
    {
        _ValidateResource(resource);
        Argument.IsNotNullOrWhiteSpace(leaseId);

        // Release is a terminal-state write. Per <see cref="DisposableReaderWriterLock.DisposeAsync"/>
        // contract, sustained storage unreachability can keep this pipeline retrying for the full
        // retry budget. Use CancellationToken.None so the retry pipeline completes even if the
        // caller's CT fires — the storage-level cleanup is the source of truth for whether waiters
        // can proceed. Same convention as the mutex provider.
        //
        // Static state-tuple eliminates the per-call closure allocation that the mutex provider's
        // RenewAsync avoids via the same pattern.
        //
        // The outer WaitAsync(disposeTimeout) caps how long ReleaseAsync waits for the pipeline.
        // On timeout we log a warning and return — the pipeline continues running in the background
        // and the storage's per-record TTL is the eventual consistency mechanism. This guarantees
        // application shutdown is never blocked beyond DisposeTimeout (default 10s) even under
        // sustained storage unavailability.
        ValueTask releaseTask = mode switch
        {
            ReaderWriterLockMode.Read => _releasePipeline.ExecuteAsync(
                static async (state, ct) =>
                {
                    var (storage, resource, leaseId) = state;
                    await storage.ReleaseReadAsync(resource, leaseId, ct).ConfigureAwait(false);
                },
                (_storage, resource, leaseId),
                CancellationToken.None
            ),
            ReaderWriterLockMode.Write => _releasePipeline.ExecuteAsync(
                static async (state, ct) =>
                {
                    var (storage, resource, leaseId) = state;
                    await storage.ReleaseWriteAsync(resource, leaseId, ct).ConfigureAwait(false);
                },
                (_storage, resource, leaseId),
                CancellationToken.None
            ),
            _ => throw new InvalidOperationException("Unknown reader-writer lock mode."),
        };

        try
        {
            await releaseTask
                .AsTask()
                .WaitAsync(_disposeTimeout, timeProvider, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogLockReleaseTimedOut(resource, leaseId, _disposeTimeout);
        }

        var monitor = _monitorRegistry.TryDeregister(resource, leaseId);

        if (monitor is not null)
        {
            try
            {
                await monitor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogLeaseMonitorFaulted(exception, resource, leaseId);
            }
        }

        if (_outboxBus is not null)
        {
            var released = new DistributedLockReleased(resource, leaseId);

            try
            {
                await _outboxBus.PublishAsync(released, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogLockReleasePublishFailed(exception, resource, leaseId);
            }
        }
    }

    private async Task<IDistributedLease?> _TryAcquireAsync(
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
        var leaseId = guidGenerator.Create().ToString("N");
        Ensure.False(leaseId.Contains(':', StringComparison.Ordinal), "Reader-writer lock ids cannot contain ':'.");

        using var activity = _StartLockActivity(mode, resource);
        var timestamp = timeProvider.GetTimestamp();

        if (acquireTimeout == TimeSpan.Zero)
        {
            return await _TryAcquireOnceAsync(
                    mode,
                    resource,
                    leaseId,
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
                    gotLock = await _TryAcquireStorageAsync(mode, resource, leaseId, timeUntilExpires, attemptToken)
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

                    logger.LogErrorAcquiringLockElapsed(e, resource, leaseId, timeProvider, timestamp);
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
                await _CleanupWaitingMarkerAsync(mode, resource, leaseId).ConfigureAwait(false);
            }
        }

        var timeWaitedForLock = timeProvider.GetElapsedTime(timestamp);
        DistributedLockMetrics.LockWaitTime.Record(timeWaitedForLock.TotalMilliseconds);

        if (!gotLock)
        {
            DistributedLockMetrics.LockFailed.Add(1, DistributedLockMetrics.ReasonContended);
            return null;
        }

        if (timeWaitedForLock > _LongLockWarningThreshold)
        {
            logger.LogLongLockAcquired(resource, leaseId, timeWaitedForLock);
        }
        else
        {
            logger.LogAcquiredLock(resource, leaseId, timeWaitedForLock);
        }

        return _CreateLockHandle(
            mode,
            resource,
            leaseId,
            leaseDuration,
            timeWaitedForLock,
            acquireOptions.ReleaseOnDispose,
            monitorLease,
            autoExtend
        );
    }

    private async Task<IDistributedLease?> _TryAcquireOnceAsync(
        ReaderWriterLockMode mode,
        string resource,
        string leaseId,
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
        var safetyDeadlineFired = false;

        try
        {
            gotLock = await _TryAcquireStorageAsync(mode, resource, leaseId, timeUntilExpires, attemptToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The storage call may have planted the writer-waiting marker server-side before the
            // client observed cancellation. Run the idempotent cleanup BEFORE rethrowing so a
            // caller-cancelled try-once doesn't strand the marker until TTL expiry. This mirrors
            // the cleanup performed by the post-loop block below for non-cancelled paths.
            if (mode == ReaderWriterLockMode.Write)
            {
                await _CleanupWaitingMarkerAsync(mode, resource, leaseId).ConfigureAwait(false);
            }

            if (callerToken.IsCancellationRequested)
            {
                throw;
            }

            // Caller has not cancelled, so an OCE here is the safety deadline firing (the
            // lock-store stalled past `_NonBlockingAcquireDeadline`). Confirm via the safety CTS
            // rather than the caller token alone, so an unrelated storage-thrown OCE falls
            // through to `reason=contended` instead of being mislabeled a stall (#320).
            safetyDeadlineFired = safetyCts.IsCancellationRequested;
            gotLock = false;
        }
        catch (Exception e) when (e is not (ObjectDisposedException or InvalidOperationException))
        {
            logger.LogErrorAcquiringLockElapsed(e, resource, leaseId, timeProvider, timestamp);
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
            // keeps the intent obvious. The caller-cancel catch above performs its own cleanup
            // and rethrows before reaching here when applicable; this branch handles the
            // non-cancelled return paths (routine contention and safety-deadline stall,
            // distinguished below by `safetyDeadlineFired`).
            if (mode == ReaderWriterLockMode.Write)
            {
                await _CleanupWaitingMarkerAsync(mode, resource, leaseId).ConfigureAwait(false);
            }

            if (safetyDeadlineFired)
            {
                DistributedLockMetrics.LockFailed.Add(1, DistributedLockMetrics.ReasonStalled);
                logger.LogTryOnceSafetyDeadlineFired(resource, leaseId, timeWaitedForLock);
            }
            else
            {
                DistributedLockMetrics.LockFailed.Add(1, DistributedLockMetrics.ReasonContended);
            }

            return null;
        }

        return _CreateLockHandle(
            mode,
            resource,
            leaseId,
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
        string leaseId,
        TimeSpan? timeUntilExpires,
        CancellationToken cancellationToken
    )
    {
        return mode switch
        {
            // Reader entries MUST carry a finite TTL. The Lua TryAcquireReadLock used to plant a
            // "0" sentinel for null/infinite TTL, but that left zombie entries in the reader HASH
            // forever — a never-released reader (process crash, cancelled task) would block all
            // future writers indefinitely. Clamping to DefaultTimeUntilExpires guarantees every
            // reader entry has a bound on how long it can strand the resource. Writers can still
            // accept infinite TTL because the writer is a single-key SET that the provider's
            // release path always reaches.
            ReaderWriterLockMode.Read => _storage.TryAcquireReadAsync(
                resource,
                leaseId,
                timeUntilExpires ?? DefaultTimeUntilExpires,
                cancellationToken
            ),
            ReaderWriterLockMode.Write => _storage.TryAcquireWriteAsync(
                resource,
                leaseId,
                DistributedLockCoreHelpers.GetWriterWaitingId(leaseId),
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
        string leaseId,
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
            leaseId,
            leaseDuration,
            timeWaitedForLock,
            this,
            releaseOnDispose,
            autoExtend,
            lockOptions,
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
        _monitorRegistry.Register(resource, leaseId, monitor);
        handle.AttachMonitor(monitor);

        return handle;
    }

    private async Task _CleanupWaitingMarkerAsync(ReaderWriterLockMode mode, string resource, string leaseId)
    {
        if (mode != ReaderWriterLockMode.Write)
        {
            return;
        }

        try
        {
            using var cleanupCts = timeProvider.CreateCancellationTokenSource(_WaitingMarkerCleanupTimeout);
            await _storage.ReleaseWriteAsync(resource, leaseId, cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogBestEffortLockCleanupFailed(exception, resource, leaseId);
        }
    }

    private void _DeregisterMonitor(string resource, string leaseId)
    {
        _ = _monitorRegistry.TryDeregister(resource, leaseId);
    }

    /// <summary>
    /// Returns the number of active lease monitors registered for <paramref name="resource"/>.
    /// Intended for testing and diagnostics only; not part of the public API contract.
    /// </summary>
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
