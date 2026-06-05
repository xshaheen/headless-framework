// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Shared lifecycle for the disposable lock/slot handles (<see cref="DisposableDistributedLock"/>
/// and <see cref="DisposableSemaphoreSlot"/>). Owns the dispose/release guard, the monitor
/// attach/stop wiring, the per-iteration storage deadline, and the single lease-loss probe path so
/// both handle types behave identically. Provider-specific storage operations (renew, ownership
/// validation, release) are supplied by the derived handle.
/// </summary>
internal abstract class DistributedLockHandleBase : IDistributedLease, LeaseMonitor.ILeaseHandle
{
    private readonly TimeProvider _timeProvider;
    private readonly Action<string, string>? _deregisterMonitor;
    private readonly AsyncLock _lock = new();
    private readonly long _timestamp;

    // Snapshotted cadence + per-iteration storage deadline. Avoids re-reading the interface-cast
    // MonitoringCadence on every iteration (which recomputes ticks * fraction and allocates) and is
    // constant for the lifetime of this handle.
    private readonly TimeSpan _monitoringCadenceSnapshot;
    private readonly TimeSpan _storageDeadlineSnapshot;

    private volatile bool _isReleased;
    private int _disposed;
    private int _renewalCount;
    private LeaseMonitor? _monitor;

    private readonly Lock _leaseProbeLock = new();
    private Task<LeaseMonitor.LeaseState>? _pendingLeaseProbe;

    // Snapshotted at AttachMonitor time so reads after the monitor's underlying CTS is disposed do
    // not throw ObjectDisposedException. CancellationToken values are valid after the source is
    // disposed; only IsCancellationRequested/Register on disposed sources throws — and the
    // snapshot's IsCancellationRequested observes the final cancellation state set during dispose.
#pragma warning disable IDE0032 // Field-backed by intent: setter is internal (AttachMonitor) but the property must be public on IDistributedLease.
    private CancellationToken _handleLostToken = CancellationToken.None;
#pragma warning restore IDE0032

    /// <summary>True when this handle auto-extends its lease; false for poll-only validation.</summary>
    protected bool AutoExtend { get; }

    /// <summary>Lease duration available to derived handles for storage renew calls.</summary>
    protected TimeSpan LeaseDuration { get; }

    private bool ReleaseOnDispose { get; }

    protected ILogger Logger { get; }

    protected DistributedLockHandleBase(
        string resource,
        string leaseId,
        long? fencingToken,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        bool releaseOnDispose,
        bool autoExtend,
        DistributedLockOptions options,
        TimeProvider timeProvider,
        Action<string, string>? deregisterMonitor,
        ILogger logger
    )
    {
        Resource = resource;
        LeaseId = leaseId;
        FencingToken = fencingToken;
        DateAcquired = timeProvider.GetUtcNow();
        TimeWaitedForLock = timeWaitedForLock;
        _timestamp = timeProvider.GetTimestamp();
        LeaseDuration = leaseDuration;
        ReleaseOnDispose = releaseOnDispose;
        AutoExtend = autoExtend;
        _timeProvider = timeProvider;
        _deregisterMonitor = deregisterMonitor;
        Logger = logger;

        // Snapshot cadence + storage deadline once. Both are constant for the lifetime of this
        // handle and were previously recomputed on every monitor iteration.
        var fraction = autoExtend ? options.AutoExtensionCadenceFraction : options.PollingCadenceFraction;
        var cadenceTicks = Math.Max(1, (long)(leaseDuration.Ticks * fraction));
        _monitoringCadenceSnapshot = TimeSpan.FromTicks(cadenceTicks);
        _storageDeadlineSnapshot =
            _monitoringCadenceSnapshot.TotalSeconds < 5.0 ? _monitoringCadenceSnapshot : TimeSpan.FromSeconds(5);
    }

    public string LeaseId { get; }

    public long? FencingToken { get; }

    public string Resource { get; }

    public DateTimeOffset DateAcquired { get; }

    public TimeSpan TimeWaitedForLock { get; }

    public CancellationToken LostToken => _handleLostToken;

    public bool CanObserveLoss => _monitor is not null;

    public int RenewalCount => Volatile.Read(ref _renewalCount);

    TimeSpan LeaseMonitor.ILeaseHandle.LeaseDuration => LeaseDuration;

    TimeSpan LeaseMonitor.ILeaseHandle.MonitoringCadence => _monitoringCadenceSnapshot;

    internal void AttachMonitor(LeaseMonitor monitor)
    {
        _monitor = monitor;
        _handleLostToken = monitor.LostToken;
    }

    // ---- Provider-specific storage operations ----

    /// <summary>
    /// Attempts to renew (auto-extend) the lease. Returns <see langword="true"/> on success. A
    /// <see langword="false"/> result is treated as ambiguous and disambiguated via
    /// <see cref="ValidateOwnershipAsync"/>.
    /// </summary>
    protected abstract Task<bool> RenewLeaseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Probes whether this handle still owns the lease/slot in storage. Returns
    /// <see langword="true"/> while still owner, <see langword="false"/> when ownership is lost.
    /// </summary>
    protected abstract Task<bool> ValidateOwnershipAsync(CancellationToken cancellationToken);

    /// <summary>Releases the lease/slot in storage (provider release pipeline).</summary>
    protected abstract Task ReleaseCoreAsync();

    /// <summary>Hook invoked when the monitor fails to dispose; allows provider-specific logging.</summary>
    protected virtual void OnMonitorDisposeFailed(Exception exception) { }

    /// <summary>
    /// Classifies an auto-extend renew that returned <see langword="false"/> when the subsequent
    /// ownership probe still shows us as owner. The default (mutex/semaphore) returns
    /// <see langword="null"/>, meaning "disambiguate via <see cref="ValidateOwnershipAsync"/>". A
    /// derived handle whose renew-false is unambiguously a loss (e.g. reader-writer writer-preference
    /// refusal) overrides this to return <see cref="LeaseMonitor.LeaseState.Lost"/> and skip the
    /// probe entirely.
    /// </summary>
    protected virtual LeaseMonitor.LeaseState? ClassifyRenewFailure() => null;

    /// <summary>Increments the observable renewal counter. Called by derived handles after a manual renew.</summary>
    protected void IncrementRenewalCount()
    {
        Interlocked.Increment(ref _renewalCount);
    }

    public abstract Task<bool> RenewAsync(
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    );

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

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                var elapsed = _timeProvider.GetElapsedTime(_timestamp);

                Logger.LogDisposableLockReleasing(Resource, LeaseId, elapsed);
            }

            // Stop the monitor unconditionally so it does not fire LostToken after an explicit
            // release, regardless of whether the storage call below succeeds.
            await _StopMonitorAsync().ConfigureAwait(false);

            // Mark released only AFTER storage release succeeds: a transient failure leaves the
            // handle reusable so a later ReleaseAsync/DisposeAsync retries instead of orphaning the
            // lock/slot until its TTL.
            await ReleaseCoreAsync().ConfigureAwait(false);
            _isReleased = true;
        }
    }

    /// <summary>
    /// Disposes the handle, releasing it when <see cref="DistributedLockAcquireOptions.ReleaseOnDispose"/>
    /// was true. The provider's release pipeline is bounded by
    /// <see cref="DistributedLockOptions.DisposeTimeout"/> (default 10s) so dispose never blocks
    /// application shutdown beyond that window under sustained storage unavailability. On timeout
    /// the pipeline continues in the background and the storage's per-record TTL is the eventual
    /// consistency mechanism.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Idempotency: matches the LeaseMonitor.DisposeAsync pattern. Re-entry (e.g., a
        // LostToken callback that disposes the handle while the caller also disposes) must be
        // a no-op rather than running release/monitor-dispose twice.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var isTraceLogLevelEnabled = Logger.IsEnabled(LogLevel.Trace);

        if (isTraceLogLevelEnabled)
        {
            Logger.LogDisposableLockDisposing(Resource, LeaseId);
        }

        try
        {
            await _StopMonitorAsync().ConfigureAwait(false);

            if (ReleaseOnDispose)
            {
                await ReleaseAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Logger.LogDisposableLockReleaseFailed(e, Resource, LeaseId);
        }

        if (isTraceLogLevelEnabled)
        {
            Logger.LogDisposableLockDisposed(Resource, LeaseId);
        }
    }

    /// <summary>
    /// Auto-extend mode: attempt <see cref="RenewLeaseAsync"/>. A successful renewal returns
    /// <see cref="LeaseMonitor.LeaseState.Renewed"/>. A <see langword="false"/> result is ambiguous
    /// (genuine fence mismatch vs. transient retry-exhaustion), so we probe ownership via
    /// <see cref="ValidateOwnershipAsync"/>: still owner ⇒ <see cref="LeaseMonitor.LeaseState.Unknown"/>
    /// (let the safety net self-promote on repeated failures), lost ⇒
    /// <see cref="LeaseMonitor.LeaseState.Lost"/>. Polling mode: only probe ownership.
    /// </summary>
    async Task<LeaseMonitor.LeaseState> LeaseMonitor.ILeaseHandle.RenewOrValidateLeaseAsync(
        CancellationToken cancellationToken
    )
    {
        // Per-iteration deadline: bounds a single storage round-trip so a stuck/blocked storage call
        // (e.g., a StackExchange.Redis reconnect storm that ignores its CT) cannot wedge
        // MonitoringTask — and therefore cannot wedge LeaseMonitor.DisposeAsync, which awaits that
        // task. Capped at min(5s, cadence) and snapshotted at construction. On deadline trip we
        // classify as Unknown (transient) and let the safety net self-promote on repeated misses.
        var leaseProbe = _GetOrStartLeaseProbe(cancellationToken);
        var clearCompletedProbe = false;

        try
        {
            var result = await _WithStorageDeadlineAsync(
                    leaseProbe,
                    _storageDeadlineSnapshot,
                    _timeProvider,
                    cancellationToken
                )
                .ConfigureAwait(false);
            clearCompletedProbe = true;

            return result;
        }
        catch (TimeoutException)
        {
            // Per-iteration deadline fired without caller cancellation — surface as transient.
            return LeaseMonitor.LeaseState.Unknown;
        }
        catch
        {
            clearCompletedProbe = true;
            throw;
        }
        finally
        {
            if (clearCompletedProbe)
            {
                _ClearLeaseProbeIfCurrent(leaseProbe);
            }
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

    private void _ClearLeaseProbeIfCurrent(Task<LeaseMonitor.LeaseState> leaseProbe)
    {
        lock (_leaseProbeLock)
        {
            if (ReferenceEquals(_pendingLeaseProbe, leaseProbe))
            {
                _pendingLeaseProbe = null;
            }
        }
    }

    private async Task<LeaseMonitor.LeaseState> _RunStorageLeaseProbeAsync(CancellationToken cancellationToken)
    {
        if (AutoExtend)
        {
            var renewed = await RenewLeaseAsync(cancellationToken).ConfigureAwait(false);

            if (renewed)
            {
                IncrementRenewalCount();

                return LeaseMonitor.LeaseState.Renewed;
            }

            // A derived handle may treat renew-false as an unambiguous loss (e.g. reader-writer
            // writer-preference refusal). In that case skip the ownership probe entirely.
            if (ClassifyRenewFailure() is { } classified)
            {
                return classified;
            }

            // Disambiguate: a renew failure can be a genuine fence mismatch OR transient
            // retry-exhaustion. Probe ownership before declaring Lost — a transient renewal failure
            // must not cancel LostToken when storage still confirms ownership.
            var stillOwnerAfterRenew = await ValidateOwnershipAsync(cancellationToken).ConfigureAwait(false);

            return stillOwnerAfterRenew ? LeaseMonitor.LeaseState.Unknown : LeaseMonitor.LeaseState.Lost;
        }

        var stillOwner = await ValidateOwnershipAsync(cancellationToken).ConfigureAwait(false);

        return stillOwner ? LeaseMonitor.LeaseState.Held : LeaseMonitor.LeaseState.Lost;
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
            OnMonitorDisposeFailed(e);
        }
        finally
        {
            _deregisterMonitor?.Invoke(Resource, LeaseId);
        }
    }
}
