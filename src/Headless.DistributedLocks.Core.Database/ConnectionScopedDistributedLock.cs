// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// <see cref="IDistributedLock"/> implementation over the connection-scoped seams. A custom database
/// provider supplies an <see cref="IConnectionScopedLockStorage"/>, an <see cref="IReleaseSignal"/>, and an
/// optional <see cref="IFencingTokenSource"/>; this type owns the portable concerns: single-attempt acquire
/// plus retry loop, acquire-timeout contract, jittered polling backed by the release signal, waiter caps for
/// DoS protection, and fencing-token stamping on exclusive handles.
/// </summary>
/// <remarks>
/// Connection-scoped locks have no TTL: <see cref="RenewAsync"/> is a no-op success and
/// <see cref="GetExpirationAsync"/> returns <see langword="null"/>. Lock loss is tied to the storage
/// connection and surfaced through <see cref="ConnectionScopedLockHandle.ConnectionLostToken"/> only when
/// acquire-time monitoring is enabled.
/// </remarks>
/// <param name="storage">Backend storage seam performing the native acquire/release.</param>
/// <param name="releaseSignal">Wake-up seam used between retry attempts; polling is the correctness fallback.</param>
/// <param name="options">Shared lock options, including resource-name length and waiter caps.</param>
/// <param name="guidGenerator">Source of per-acquisition lock ids.</param>
/// <param name="timeProvider">Clock used for deadlines and waits (deterministic under test).</param>
/// <param name="logger">Logger for release-failure diagnostics.</param>
/// <param name="fencingTokenSource">Optional source of monotonic fencing tokens for exclusive locks.</param>
/// <param name="pollingFallback">Maximum delay between retry attempts; defaults to 100ms.</param>
[PublicAPI]
public sealed class ConnectionScopedDistributedLock(
    IConnectionScopedLockStorage storage,
    IReleaseSignal releaseSignal,
    DistributedLockOptions options,
    IGuidGenerator guidGenerator,
    TimeProvider timeProvider,
    ILogger<ConnectionScopedDistributedLock> logger,
    IFencingTokenSource? fencingTokenSource = null,
    TimeSpan? pollingFallback = null
) : IDistributedLock
{
    private static readonly TimeSpan _DefaultPollingFallback = TimeSpan.FromMilliseconds(100);
    private readonly TimeSpan _pollingFallback = pollingFallback ?? _DefaultPollingFallback;

    // Per-resource waiter accounting for DoS protection, sharing the same cap enforcement as the
    // sibling DistributedLock via the common WaiterCapRegistry.
    private readonly WaiterCapRegistry _waiterCaps = new(
        options.MaxConcurrentWaitingResources,
        options.MaxWaitersPerResource
    );

    /// <summary>
    /// Connection-scoped locks have no TTL; the lock is held for the lifetime of the underlying connection.
    /// Returns <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </summary>
    public TimeSpan DefaultTimeUntilExpires => Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Default timeout applied when <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is not specified.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan DefaultAcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Acquires an exclusive lock on <paramref name="resource"/>, blocking until acquired or the
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is exceeded.
    /// </summary>
    /// <param name="resource">The resource to lock. Must be non-null, non-whitespace, and within the configured max length.</param>
    /// <param name="acquireOptions">
    /// Per-call options (acquire timeout, monitoring mode, release-on-dispose).
    /// <see langword="null"/> applies the defaults set on this instance.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the acquire attempt.</param>
    /// <returns>A held <see cref="IDistributedLease"/> that must be disposed to release the lock.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is whitespace or exceeds the configured maximum resource name length.</exception>
    /// <exception cref="LockAcquisitionTimeoutException">Thrown when the lock cannot be acquired before the acquire timeout elapses.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<IDistributedLease> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        return _AcquireCoreAsync(throwOnTimeout: true, resource, acquireOptions, isShared: false, cancellationToken)!;
    }

    /// <summary>
    /// Attempts to acquire an exclusive lock on <paramref name="resource"/>, returning <see langword="null"/> if the
    /// lock cannot be acquired before the acquire timeout elapses.
    /// </summary>
    /// <param name="resource">The resource to lock. Must be non-null, non-whitespace, and within the configured max length.</param>
    /// <param name="acquireOptions">
    /// Per-call options (acquire timeout, monitoring mode, release-on-dispose).
    /// <see langword="null"/> applies the defaults set on this instance.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the acquire attempt.</param>
    /// <returns>A held <see cref="IDistributedLease"/> on success, or <see langword="null"/> when contended past the timeout.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is whitespace or exceeds the configured maximum resource name length.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<IDistributedLease?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        return _AcquireCoreAsync(throwOnTimeout: false, resource, acquireOptions, isShared: false, cancellationToken);
    }

    /// <summary>
    /// Attempts to acquire the lock in shared or exclusive mode, used internally by
    /// <see cref="ConnectionScopedReadWriteLock"/> to implement reader/writer semantics.
    /// </summary>
    /// <param name="resource">The resource to lock.</param>
    /// <param name="isShared"><see langword="true"/> for a shared (reader) lock; <see langword="false"/> for exclusive.</param>
    /// <param name="acquireOptions">Per-call options; <see langword="null"/> applies instance defaults.</param>
    /// <param name="cancellationToken">Token used to cancel the acquire attempt.</param>
    /// <returns>A held <see cref="IDistributedLease"/> on success, or <see langword="null"/> when contended past the timeout.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is whitespace or exceeds the configured maximum resource name length.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    internal Task<IDistributedLease?> TryAcquireAsync(
        string resource,
        bool isShared,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        return _AcquireCoreAsync(throwOnTimeout: false, resource, acquireOptions, isShared, cancellationToken);
    }

    private async Task<IDistributedLease?> _AcquireCoreAsync(
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

        DistributedLockCoreHelpers.ValidateAcquireTimeout(acquireOptions?.AcquireTimeout);

        var acquireTimeout = acquireOptions?.AcquireTimeout ?? DefaultAcquireTimeout;
        var observeLoss = (acquireOptions?.Monitoring ?? LockMonitoringMode.None) != LockMonitoringMode.None;
        var started = timeProvider.GetTimestamp();
        var deadline =
            acquireTimeout == Timeout.InfiniteTimeSpan
                ? DateTimeOffset.MaxValue
                : timeProvider.GetUtcNow().Add(acquireTimeout);
        var leaseId = guidGenerator.Create().ToString("N");
        var isWaiting = false;

        using var activity = _StartLockActivity(resource);

        // Records the wait-time histogram plus the failure counter for any non-acquiring outcome (timeout,
        // past-deadline, or fencing failure), mirroring the sibling DistributedLock's instrumentation.
        // Always `reason=contended`: this connection-scoped provider has no `_NonBlockingAcquireDeadline`
        // safety-deadline path (the database driver's own command/connection timeout bounds a stalled
        // call), so the `stalled` reason emitted by the in-memory/Redis providers does not apply here.
        void recordFailedAcquisition()
        {
            DistributedLockMetrics.LockWaitTime.Record(timeProvider.GetElapsedTime(started).TotalMilliseconds);
            DistributedLockMetrics.LockFailed.Add(1, DistributedLockMetrics.ReasonContended);
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var handle = await storage
                    .TryAcquireAsync(resource, leaseId, isShared, observeLoss, cancellationToken)
                    .ConfigureAwait(false);

                if (handle is not null)
                {
                    long? fencingToken;

                    try
                    {
                        fencingToken =
                            isShared || fencingTokenSource is null
                                ? null
                                : await fencingTokenSource
                                    .NextAsync(resource, handle.HeldConnection, cancellationToken)
                                    .ConfigureAwait(false);
                    }
                    catch
                    {
                        try
                        {
                            await _ReleaseAsync(handle, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception releaseException)
                        {
                            logger.LogConnectionScopedLockReleaseFailed(releaseException, resource, leaseId);
                        }

                        recordFailedAcquisition();

                        throw;
                    }

                    var waited = timeProvider.GetElapsedTime(started);

                    DistributedLockMetrics.LockWaitTime.Record(waited.TotalMilliseconds);

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

                if (storage.BlocksServerSide)
                {
                    recordFailedAcquisition();

                    if (!throwOnTimeout)
                    {
                        return null;
                    }

                    throw acquireTimeout == TimeSpan.Zero
                        ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
                        : new LockAcquisitionTimeoutException(resource);
                }

                if (acquireTimeout == TimeSpan.Zero || timeProvider.GetUtcNow() >= deadline)
                {
                    recordFailedAcquisition();

                    if (!throwOnTimeout)
                    {
                        return null;
                    }

                    throw acquireTimeout == TimeSpan.Zero
                        ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
                        : new LockAcquisitionTimeoutException(resource);
                }

                var remaining =
                    deadline == DateTimeOffset.MaxValue ? _pollingFallback : deadline - timeProvider.GetUtcNow();

                if (remaining <= TimeSpan.Zero)
                {
                    recordFailedAcquisition();

                    // Past the deadline. Re-entering the loop would open a fresh connection for one
                    // more TryAcquire; honour the timeout contract instead.
                    if (!throwOnTimeout)
                    {
                        return null;
                    }

                    throw new LockAcquisitionTimeoutException(resource);
                }

                // Account for this acquirer as a waiter exactly once, the first time it has to block.
                if (!isWaiting)
                {
                    _waiterCaps.Enter(resource);
                    isWaiting = true;
                }

                var wait = remaining < _pollingFallback ? remaining : _pollingFallback;

                // Apply jitter so many waiters on the same resource do not wake in lockstep and
                // stampede the store. Stay within the remaining budget.
                var jitter = 0.8 + (Random.Shared.NextDouble() * 0.4);
                var jittered = TimeSpan.FromMilliseconds(wait.TotalMilliseconds * jitter);
                wait = jittered < remaining ? jittered : remaining;

                await releaseSignal.WaitAsync(resource, wait, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (isWaiting)
            {
                _waiterCaps.Exit(resource);
            }
        }
    }

    /// <summary>
    /// No-op for connection-scoped locks: advisory locks are held for the lifetime of the connection and
    /// have no TTL to extend. Always returns <see langword="true"/>.
    /// </summary>
    /// <param name="resource">The locked resource (unused).</param>
    /// <param name="leaseId">The lease identifier (unused).</param>
    /// <param name="timeUntilExpires">Ignored; connection-scoped locks have no expiry.</param>
    /// <param name="cancellationToken">Token observed before returning.</param>
    /// <returns><see langword="true"/> unconditionally.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public Task<bool> RenewAsync(
        string resource,
        string leaseId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }

    /// <summary>
    /// Returns the lease id held by this process for <paramref name="resource"/>, or <see langword="null"/> if
    /// this process does not hold the lock. Only covers locks acquired through this provider instance.
    /// </summary>
    /// <param name="resource">The resource to inspect.</param>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    /// <returns>The local lease id, or <see langword="null"/> if not held by this process.</returns>
    public Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        return storage.GetLocalLeaseIdAsync(resource, cancellationToken).AsTask();
    }

    /// <summary>
    /// Releases the lock for <paramref name="resource"/> identified by <paramref name="leaseId"/> and
    /// publishes a wake-up signal to waiting acquirers.
    /// </summary>
    /// <param name="resource">The locked resource to release.</param>
    /// <param name="leaseId">The lease id that identifies the held lock.</param>
    /// <param name="cancellationToken">Token used to cancel the storage release call.</param>
    public async Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        await storage.ReleaseAsync(resource, leaseId, cancellationToken).ConfigureAwait(false);
        await _PublishReleaseAsync(resource, leaseId).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="resource"/> is currently locked in any mode.
    /// </summary>
    /// <param name="resource">The resource to check.</param>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return storage.IsLockedAsync(resource, cancellationToken: cancellationToken).AsTask();
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="resource"/> is locked in the specified mode.
    /// Used internally by <see cref="ConnectionScopedReadWriteLock"/>.
    /// </summary>
    /// <param name="resource">The resource to check.</param>
    /// <param name="isShared"><see langword="true"/> to check for a shared (reader) lock; <see langword="false"/> for exclusive.</param>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    internal Task<bool> IsLockedAsync(string resource, bool isShared, CancellationToken cancellationToken = default)
    {
        return storage.IsLockedAsync(resource, isShared, cancellationToken).AsTask();
    }

    /// <summary>
    /// Returns the count of holders currently owning <paramref name="resource"/> in the specified mode.
    /// Used internally by <see cref="ConnectionScopedReadWriteLock"/> to count readers.
    /// </summary>
    /// <param name="resource">The resource to count holders for.</param>
    /// <param name="isShared"><see langword="true"/> for shared (reader) holders; <see langword="false"/> for exclusive.</param>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    /// <returns>The number of holders currently owning <paramref name="resource"/> in the specified mode.</returns>
    internal Task<long> GetLocksCountAsync(
        string resource,
        bool isShared,
        CancellationToken cancellationToken = default
    )
    {
        return storage.GetLocksCountAsync(resource, isShared, cancellationToken).AsTask();
    }

    /// <summary>
    /// Connection-scoped locks have no TTL; always returns <see langword="null"/>.
    /// </summary>
    /// <param name="resource">The resource to inspect (unused).</param>
    /// <param name="cancellationToken">Token observed before returning.</param>
    /// <returns><see langword="null"/> unconditionally.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is already cancelled.</exception>
    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TimeSpan?>(null);
    }

    /// <summary>
    /// Returns lock information for <paramref name="resource"/> if it is currently held, or <see langword="null"/>
    /// if it is not. The <see cref="DistributedLockInfo.LeaseId"/> will only be populated when this process
    /// holds the lock; cross-process holder identity is not visible on the connection-scoped inspection path.
    /// <see cref="DistributedLockInfo.TimeToLive"/> is always <see langword="null"/> because connection-scoped
    /// locks have no TTL.
    /// </summary>
    /// <param name="resource">The resource to inspect.</param>
    /// <param name="cancellationToken">Token used to cancel the storage calls.</param>
    /// <returns>Lock information if held, or <see langword="null"/> if not locked.</returns>
    public async Task<DistributedLockInfo?> GetLockInfoAsync(
        string resource,
        CancellationToken cancellationToken = default
    )
    {
        if (!await storage.IsLockedAsync(resource, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var leaseId = await storage.GetLocalLeaseIdAsync(resource, cancellationToken).ConfigureAwait(false);

        return new DistributedLockInfo
        {
            Resource = resource,
            LeaseId = leaseId,
            TimeToLive = null,
            FencingToken = null,
        };
    }

    /// <summary>
    /// Lists active locks observable through the backing store's inspection path.
    /// Some backends can only enumerate locks held by this process.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    /// <returns>Read-only collection of active lock information.</returns>
    public Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
    {
        return storage.ListActiveLocksAsync(cancellationToken).AsTask();
    }

    /// <summary>
    /// Returns the count of active locks observable through the backing store's inspection path.
    /// Some backends can only count locks held by this process.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        return storage.GetActiveLocksCountAsync(cancellationToken).AsTask();
    }

    private async ValueTask _ReleaseAsync(ConnectionScopedLockHandle handle, CancellationToken cancellationToken)
    {
        await storage.ReleaseAsync(handle, cancellationToken).ConfigureAwait(false);
        await _PublishReleaseAsync(handle.Resource, handle.LeaseId).ConfigureAwait(false);
    }

    private async ValueTask _PublishReleaseAsync(string resource, string leaseId)
    {
        try
        {
            // The unlock has already committed; the wake-up is only a latency optimization (polling is the
            // correctness floor). Publish with None so a cancelled caller cannot strand the other waiters that
            // depend on this wake, and never surface a wake-publish failure as a release failure.
            await releaseSignal.PublishAsync(resource, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogReleaseWakePublishFailed(exception, resource, leaseId);
        }
    }

    private static Activity? _StartLockActivity(string resource)
    {
        var activity = DistributedLocksDiagnostics.Start("lock.acquire");

        if (activity is null)
        {
            return null;
        }

        activity.AddTag("headless.lock.resource", resource);
        activity.DisplayName = $"Lock: {resource}";

        return activity;
    }
}
