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

    /// <summary>
    /// Initialises the shared lease handle state. Called by derived type constructors.
    /// </summary>
    /// <param name="resource">The resource name for which the lease was acquired.</param>
    /// <param name="leaseId">The unique lease identifier stored in the backend.</param>
    /// <param name="fencingToken">Backend-assigned fencing token, or <see langword="null"/>.</param>
    /// <param name="leaseDuration">The finite TTL negotiated with the backend at acquire time.</param>
    /// <param name="timeWaitedForLock">Wall-clock time spent waiting before acquisition.</param>
    /// <param name="releaseOnDispose">When <see langword="true"/>, <see cref="DisposeAsync"/> triggers <see cref="ReleaseAsync"/>.</param>
    /// <param name="autoExtend">When <see langword="true"/>, the attached monitor extends TTL; otherwise it only validates.</param>
    /// <param name="options">Configuration used to compute monitoring cadence and dispose timeout.</param>
    /// <param name="timeProvider">Time provider for elapsed-time and UTC-now measurements.</param>
    /// <param name="deregisterMonitor">
    /// Callback invoked with (<paramref name="resource"/>, <paramref name="leaseId"/>) after the
    /// attached monitor is disposed, allowing the provider to remove the monitor from its registry.
    /// </param>
    /// <param name="logger">Logger for lifecycle events.</param>
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

    /// <summary>The unique identifier for this lease, matching the value stored in the backend.</summary>
    public string LeaseId { get; }

    /// <summary>
    /// The monotonically-increasing fencing token assigned by the backend at acquire time, or
    /// <see langword="null"/> when the backend does not support fencing tokens.
    /// </summary>
    public long? FencingToken { get; }

    /// <summary>The resource name for which this lease was acquired.</summary>
    public string Resource { get; }

    /// <summary>The UTC timestamp at which this lease was acquired.</summary>
    public DateTimeOffset DateAcquired { get; }

    /// <summary>The total wall-clock duration spent waiting before this lease was granted.</summary>
    public TimeSpan TimeWaitedForLock { get; }

    /// <summary>
    /// Cancelled when the lease monitor detects that this handle no longer holds the lease.
    /// <see cref="CancellationToken.None"/> when no monitor is attached (i.e. the handle was
    /// acquired without a monitoring mode). Check <see cref="CanObserveLoss"/> before registering
    /// callbacks to distinguish "no monitor" from "monitor attached, lease still held".
    /// </summary>
    public CancellationToken LostToken => _handleLostToken;

    /// <summary>
    /// <see langword="true"/> when a <see cref="LeaseMonitor"/> is attached and lease-loss events
    /// can be observed via <see cref="LostToken"/>. <see langword="false"/> for handles acquired
    /// without a monitoring mode.
    /// </summary>
    public bool CanObserveLoss => _monitor is not null;

    /// <summary>
    /// The number of successful TTL renewals performed since this handle was acquired, including
    /// both manual calls to <see cref="RenewAsync"/> and automatic renewals by the attached monitor.
    /// </summary>
    public int RenewalCount => Volatile.Read(ref _renewalCount);

    TimeSpan LeaseMonitor.ILeaseHandle.LeaseDuration => LeaseDuration;

    TimeSpan LeaseMonitor.ILeaseHandle.MonitoringCadence => _monitoringCadenceSnapshot;

    /// <summary>
    /// Attaches a <see cref="LeaseMonitor"/> to this handle and snapshots its
    /// <see cref="LeaseMonitor.LostToken"/> into <see cref="LostToken"/>. Must be called at most
    /// once, immediately after construction and before the handle is returned to the caller.
    /// </summary>
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
    protected virtual LeaseMonitor.LeaseState? ClassifyRenewFailure()
    {
        return null;
    }

    /// <summary>Increments the observable renewal counter. Called by derived handles after a manual renew.</summary>
    protected void IncrementRenewalCount()
    {
        Interlocked.Increment(ref _renewalCount);
    }

    /// <summary>
    /// Extends the lease TTL in the storage backend. Derived types implement this by calling the
    /// provider's renew API and incrementing <see cref="RenewalCount"/> on success.
    /// </summary>
    /// <param name="timeUntilExpires">
    /// New TTL from now. <see langword="null"/> applies the provider's default lease duration.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the renewal storage call.</param>
    /// <returns>
    /// <see langword="true"/> when the TTL was extended; <see langword="false"/> when the lease is
    /// no longer held in storage (expired or released by another path).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public abstract Task<bool> RenewAsync(
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Releases the lease/slot in storage and stops the attached monitor. Idempotent — subsequent
    /// calls after the first successful release are no-ops. Thread-safe: concurrent callers are
    /// serialised by an internal async lock.
    /// </summary>
    /// <remarks>
    /// The monitor is stopped unconditionally before the storage call so <see cref="LostToken"/>
    /// is not cancelled after an explicit release, even if the storage call fails transiently.
    /// The handle remains usable (the released flag is only set after a successful storage call)
    /// so a retry via <see cref="ReleaseAsync"/> or <see cref="DisposeAsync"/> can succeed.
    /// </remarks>
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
        var monitor = Interlocked.Exchange(ref _monitor, value: null);

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
