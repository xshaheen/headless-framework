// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class DisposableDistributedLock : IDistributedLock, LeaseMonitor.ILeaseHandle
{
    private readonly TimeSpan _leaseDuration;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly bool _releaseOnDispose;
    private readonly bool _autoExtend;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string, string>? _deregisterMonitor;
    private readonly ILogger _logger;

    internal DisposableDistributedLock(
        string resource,
        string lockId,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        IDistributedLockProvider lockProvider,
        bool releaseOnDispose,
        bool autoExtend,
        DistributedLockOptions options,
        TimeProvider timeProvider,
        Action<string, string>? deregisterMonitor,
        ILogger logger
    )
    {
        LockId = lockId;
        Resource = resource;
        DateAcquired = timeProvider.GetUtcNow();
        TimeWaitedForLock = timeWaitedForLock;
        _timestamp = timeProvider.GetTimestamp();
        _options = options;
        _leaseDuration = leaseDuration;
        _lockProvider = lockProvider;
        _releaseOnDispose = releaseOnDispose;
        _autoExtend = autoExtend;
        _timeProvider = timeProvider;
        _deregisterMonitor = deregisterMonitor;
        _logger = logger;

        // Snapshot cadence + storage deadline once. Both are constant for the lifetime of this
        // handle and were previously recomputed on every monitor iteration.
        var fraction = autoExtend ? options.AutoExtensionCadenceFraction : options.PollingCadenceFraction;
        var cadenceTicks = Math.Max(1, (long)(leaseDuration.Ticks * fraction));
        _monitoringCadenceSnapshot = TimeSpan.FromTicks(cadenceTicks);
        _storageDeadlineSnapshot = _monitoringCadenceSnapshot.TotalSeconds < 5.0
            ? _monitoringCadenceSnapshot
            : TimeSpan.FromSeconds(5);
    }

    private volatile bool _isReleased;
    private int _disposed;
    private readonly AsyncLock _lock = new();
    private readonly long _timestamp;
    private readonly DistributedLockOptions _options;
    private LeaseMonitor? _monitor;
    private readonly Lock _leaseProbeLock = new();
    private Task<LeaseMonitor.LeaseState>? _pendingLeaseProbe;

    // Snapshotted cadence + per-iteration storage deadline. Avoids re-reading the
    // interface-cast MonitoringCadence on every iteration (which recomputes ticks * fraction
    // and allocates) and is constant for the lifetime of this handle.
    private readonly TimeSpan _monitoringCadenceSnapshot;
    private readonly TimeSpan _storageDeadlineSnapshot;

    // Snapshotted at AttachMonitor time so reads after the monitor's underlying CTS is disposed
    // do not throw ObjectDisposedException. CancellationToken values are valid after the source
    // is disposed; only IsCancellationRequested/Register on disposed sources throws — and the
    // snapshot's IsCancellationRequested observes the final cancellation state set during dispose.
#pragma warning disable IDE0032 // Field-backed by intent (not promoted to auto-property): the
    // setter is internal (AttachMonitor) but the property must be public on IDistributedLock.
    private CancellationToken _handleLostToken = CancellationToken.None;
#pragma warning restore IDE0032

    public string LockId { get; }

    public string Resource { get; }

    public DateTimeOffset DateAcquired { get; }

    public TimeSpan TimeWaitedForLock { get; }

    public CancellationToken HandleLostToken => _handleLostToken;

    public bool IsMonitored => _monitor is not null;

    private int _renewalCount;

    public int RenewalCount => Volatile.Read(ref _renewalCount);

    TimeSpan LeaseMonitor.ILeaseHandle.LeaseDuration => _leaseDuration;

    TimeSpan LeaseMonitor.ILeaseHandle.MonitoringCadence => _monitoringCadenceSnapshot;

    internal void AttachMonitor(LeaseMonitor monitor)
    {
        _monitor = monitor;
        _handleLostToken = monitor.HandleLostToken;
    }

    public async Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogDisposableLockRenewing(Resource, LockId);
        }

        var result = await _lockProvider
            .RenewAsync(Resource, LockId, timeUntilExpires, cancellationToken)
            .ConfigureAwait(false);

        if (!result)
        {
            _logger.LogDisposableLockRenewFailed(Resource, LockId);

            return false;
        }

        Interlocked.Increment(ref _renewalCount);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDisposableLockRenewed(Resource, LockId);
        }

        return true;
    }

    public async Task ReleaseAsync()
    {
        if (_isReleased)
        {
            return;
        }

        using (await _lock.LockAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (_isReleased)
            {
                return;
            }

            _isReleased = true;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var elapsed = _timeProvider.GetElapsedTime(_timestamp);

                _logger.LogDisposableLockReleasing(Resource, LockId, elapsed);
            }

            await _StopMonitorAsync().ConfigureAwait(false);
            await _lockProvider.ReleaseAsync(Resource, LockId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Idempotency: matches the LeaseMonitor.DisposeAsync pattern. Re-entry (e.g., a
        // HandleLostToken callback that disposes the handle while the caller also disposes)
        // must be a no-op rather than running release/monitor-dispose twice.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var isTraceLogLevelEnabled = _logger.IsEnabled(LogLevel.Trace);

        if (isTraceLogLevelEnabled)
        {
            _logger.LogDisposableLockDisposing(Resource, LockId);
        }

        try
        {
            await _StopMonitorAsync().ConfigureAwait(false);

            if (_releaseOnDispose)
            {
                await ReleaseAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            _logger.LogDisposableLockReleaseFailed(e, Resource, LockId);
        }

        if (isTraceLogLevelEnabled)
        {
            _logger.LogDisposableLockDisposed(Resource, LockId);
        }
    }

    /// <summary>
    /// Auto-extend mode: attempt <see cref="IDistributedLockProvider.RenewAsync"/>. A successful
    /// renewal returns <see cref="LeaseMonitor.LeaseState.Renewed"/>. A <see langword="false"/>
    /// result is ambiguous (genuine fence mismatch vs. transient retry-exhaustion), so we probe
    /// ownership via <see cref="IDistributedLockProvider.GetLockIdAsync"/>: matching id ⇒
    /// <see cref="LeaseMonitor.LeaseState.Unknown"/> (let the safety net self-promote on
    /// repeated failures), differing or absent id ⇒ <see cref="LeaseMonitor.LeaseState.Lost"/>.
    /// Polling mode: only probe ownership.
    /// </summary>
    async Task<LeaseMonitor.LeaseState> LeaseMonitor.ILeaseHandle.RenewOrValidateLeaseAsync(
        CancellationToken cancellationToken
    )
    {
        // Per-iteration deadline: bounds a single storage round-trip so a stuck/blocked
        // storage call (e.g., StackExchange.Redis reconnect storm that ignores its CT) cannot
        // wedge MonitoringTask — and therefore cannot wedge LeaseMonitor.DisposeAsync, which
        // awaits that task. Capped at min(5s, cadence) and snapshotted at construction. On
        // deadline trip we classify as Unknown (transient) and let the safety net self-promote
        // on repeated misses.
        var leaseProbe = _GetOrStartLeaseProbe(cancellationToken);

        try
        {
            return await _WithStorageDeadlineAsync(
                    leaseProbe,
                    _storageDeadlineSnapshot,
                    _timeProvider,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Per-iteration deadline fired without caller cancellation — surface as transient.
            return LeaseMonitor.LeaseState.Unknown;
        }
    }

    private Task<LeaseMonitor.LeaseState> _GetOrStartLeaseProbe(CancellationToken cancellationToken)
    {
        lock (_leaseProbeLock)
        {
            if (_pendingLeaseProbe is { } pending)
            {
                if (!pending.IsCompleted)
                {
                    return pending;
                }

                _ = pending.Exception;
                _pendingLeaseProbe = null;

                return pending;
            }

            var leaseProbe = _RunStorageLeaseProbeAsync(cancellationToken);
            _pendingLeaseProbe = leaseProbe;
            _ = leaseProbe.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );

            return leaseProbe;
        }
    }

    private async Task<LeaseMonitor.LeaseState> _RunStorageLeaseProbeAsync(CancellationToken cancellationToken)
    {
        if (_autoExtend)
        {
            var renewed = await _lockProvider
                .RenewAsync(Resource, LockId, _leaseDuration, cancellationToken)
                .ConfigureAwait(false);

            if (renewed)
            {
                Interlocked.Increment(ref _renewalCount);
                return LeaseMonitor.LeaseState.Renewed;
            }

            // Disambiguate: RenewAsync returns false for both fence mismatch and transient
            // retry-exhaustion. Probe ownership before declaring Lost — a transient renewal
            // failure must not cancel HandleLostToken when storage still confirms ownership.
            var currentLockIdAfterRenew = await _lockProvider
                .GetLockIdAsync(Resource, cancellationToken)
                .ConfigureAwait(false);

            return string.Equals(currentLockIdAfterRenew, LockId, StringComparison.Ordinal)
                ? LeaseMonitor.LeaseState.Unknown
                : LeaseMonitor.LeaseState.Lost;
        }

        var currentLockId = await _lockProvider
            .GetLockIdAsync(Resource, cancellationToken)
            .ConfigureAwait(false);

        return string.Equals(currentLockId, LockId, StringComparison.Ordinal)
            ? LeaseMonitor.LeaseState.Held
            : LeaseMonitor.LeaseState.Lost;
    }

    private static async Task<T> _WithStorageDeadlineAsync<T>(
        Task<T> operation,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        return await operation.WaitAsync(timeout, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask _StopMonitorAsync()
    {
        var monitor = Interlocked.Exchange(ref _monitor, null);

        if (monitor is null)
        {
            return;
        }

        try
        {
            await monitor.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogDisposableLockMonitorDisposeFailed(e, Resource, LockId);
        }
        finally
        {
            _deregisterMonitor?.Invoke(Resource, LockId);
        }
    }
}

internal static partial class DisposableDistributedLockLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "DisposableLockRenewing",
        Level = LogLevel.Trace,
        Message = "Renewing lock {Resource} ({LockId})"
    )]
    public static partial void LogDisposableLockRenewing(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 2,
        EventName = "DisposableLockRenewFailed",
        Level = LogLevel.Debug,
        Message = "Unable to renew lock {Resource} ({LockId})"
    )]
    public static partial void LogDisposableLockRenewFailed(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 3,
        EventName = "DisposableLockRenewed",
        Level = LogLevel.Debug,
        Message = "Renewed lock {Resource} ({LockId})"
    )]
    public static partial void LogDisposableLockRenewed(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 4,
        EventName = "DisposableLockReleasing",
        Level = LogLevel.Debug,
        Message = "Releasing lock: R={Resource} Id={LockId} after {Duration:g}"
    )]
    public static partial void LogDisposableLockReleasing(
        this ILogger logger,
        string resource,
        string lockId,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 5,
        EventName = "DisposableLockDisposing",
        Level = LogLevel.Trace,
        Message = "Disposing lock: R={Resource} Id={LockId}"
    )]
    public static partial void LogDisposableLockDisposing(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 6,
        EventName = "DisposableLockReleaseFailed",
        Level = LogLevel.Error,
        Message = "Unable to release lock: R={Resource} Id={LockId}"
    )]
    public static partial void LogDisposableLockReleaseFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string lockId
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "DisposableLockDisposed",
        Level = LogLevel.Trace,
        Message = "Disposed lock: R={Resource} Id={LockId}"
    )]
    public static partial void LogDisposableLockDisposed(this ILogger logger, string resource, string lockId);

    [LoggerMessage(
        EventId = 8,
        EventName = "DisposableLockMonitorDisposeFailed",
        Level = LogLevel.Warning,
        Message = "Unable to dispose lease monitor before releasing lock: R={Resource} Id={LockId}"
    )]
    public static partial void LogDisposableLockMonitorDisposeFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string lockId
    );
}
