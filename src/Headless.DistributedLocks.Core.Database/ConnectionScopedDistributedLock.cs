// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;
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
/// <param name="longIdGenerator">Source of per-acquisition lock ids.</param>
/// <param name="timeProvider">Clock used for deadlines and waits (deterministic under test).</param>
/// <param name="logger">Logger for release-failure diagnostics.</param>
/// <param name="fencingTokenSource">Optional source of monotonic fencing tokens for exclusive locks.</param>
/// <param name="pollingFallback">Maximum delay between retry attempts; defaults to 100ms.</param>
[PublicAPI]
public sealed class ConnectionScopedDistributedLock(
    IConnectionScopedLockStorage storage,
    IReleaseSignal releaseSignal,
    DistributedLockOptions options,
    ILongIdGenerator longIdGenerator,
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

    public TimeSpan DefaultTimeUntilExpires => Timeout.InfiniteTimeSpan;

    public TimeSpan DefaultAcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public Task<IDistributedLease> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        return _AcquireCoreAsync(throwOnTimeout: true, resource, acquireOptions, isShared: false, cancellationToken)!;
    }

    public Task<IDistributedLease?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? acquireOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        return _AcquireCoreAsync(throwOnTimeout: false, resource, acquireOptions, isShared: false, cancellationToken);
    }

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

        var acquireTimeout = acquireOptions?.AcquireTimeout ?? DefaultAcquireTimeout;
        var observeLoss = (acquireOptions?.Monitoring ?? LockMonitoringMode.None) != LockMonitoringMode.None;
        var started = timeProvider.GetTimestamp();
        var deadline = acquireTimeout == Timeout.InfiniteTimeSpan
            ? DateTimeOffset.MaxValue
            : timeProvider.GetUtcNow().Add(acquireTimeout);
        var leaseId = longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
        var isWaiting = false;

        using var activity = _StartLockActivity(resource);

        // Records the wait-time histogram plus the failure counter for any non-acquiring outcome (timeout,
        // past-deadline, or fencing failure), mirroring the sibling DistributedLock's instrumentation.
        void recordFailedAcquisition()
        {
            DistributedLockMetrics.LockWaitTime.Record(timeProvider.GetElapsedTime(started).TotalMilliseconds);
            DistributedLockMetrics.LockFailed.Add(1);
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
                        fencingToken = isShared || fencingTokenSource is null
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

    public Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        return storage.GetLocalLeaseIdAsync(resource, cancellationToken).AsTask();
    }

    public async Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        await storage.ReleaseAsync(resource, leaseId, cancellationToken).ConfigureAwait(false);
        await _PublishReleaseAsync(resource, leaseId).ConfigureAwait(false);
    }

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return storage.IsLockedAsync(resource, cancellationToken: cancellationToken).AsTask();
    }

    internal Task<bool> IsLockedAsync(string resource, bool isShared, CancellationToken cancellationToken = default)
    {
        return storage.IsLockedAsync(resource, isShared, cancellationToken).AsTask();
    }

    internal Task<long> GetLocksCountAsync(
        string resource,
        bool isShared,
        CancellationToken cancellationToken = default
    )
    {
        return storage.GetLocksCountAsync(resource, isShared, cancellationToken).AsTask();
    }

    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TimeSpan?>(null);
    }

    public async Task<DistributedLockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default)
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

    public Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
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
