// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class DisposableReaderWriterLock : IDistributedLock, LeaseMonitor.ILeaseHandle
{
    private readonly ReaderWriterLockMode _mode;
    private readonly TimeSpan _leaseDuration;
    private readonly DistributedReaderWriterLockProvider _provider;
    private readonly bool _releaseOnDispose;
    private readonly bool _autoExtend;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string, string>? _deregisterMonitor;
    private readonly ILogger _logger;
    private readonly AsyncLock _lock = new();
    private readonly TimeSpan _monitoringCadenceSnapshot;
    private readonly TimeSpan _storageDeadlineSnapshot;

    private volatile bool _isReleased;
    private int _disposed;
    private int _renewalCount;
    private LeaseMonitor? _monitor;
#pragma warning disable IDE0032 // Snapshotted from monitor; public getter must stay token-only.
    private CancellationToken _handleLostToken = CancellationToken.None;
#pragma warning restore IDE0032
    private readonly Lock _leaseProbeLock = new();
    private Task<LeaseMonitor.LeaseState>? _pendingLeaseProbe;

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
    {
        _mode = mode;
        _leaseDuration = leaseDuration;
        _provider = provider;
        _releaseOnDispose = releaseOnDispose;
        _autoExtend = autoExtend;
        _timeProvider = timeProvider;
        _deregisterMonitor = deregisterMonitor;
        _logger = logger;

        LockId = lockId;
        Resource = resource;
        DateAcquired = timeProvider.GetUtcNow();
        TimeWaitedForLock = timeWaitedForLock;

        var fraction = autoExtend ? options.AutoExtensionCadenceFraction : options.PollingCadenceFraction;
        var cadenceTicks = Math.Max(1, (long)(leaseDuration.Ticks * fraction));
        _monitoringCadenceSnapshot = TimeSpan.FromTicks(cadenceTicks);
        _storageDeadlineSnapshot = _monitoringCadenceSnapshot.TotalSeconds < 5.0
            ? _monitoringCadenceSnapshot
            : TimeSpan.FromSeconds(5);
    }

    public string LockId { get; }

    public string Resource { get; }

    public int RenewalCount => Volatile.Read(ref _renewalCount);

    public DateTimeOffset DateAcquired { get; }

    public TimeSpan TimeWaitedForLock { get; }

    public CancellationToken HandleLostToken => _handleLostToken;

    public bool IsMonitored => _monitor is not null;

    TimeSpan LeaseMonitor.ILeaseHandle.LeaseDuration => _leaseDuration;

    TimeSpan LeaseMonitor.ILeaseHandle.MonitoringCadence => _monitoringCadenceSnapshot;

    internal void AttachMonitor(LeaseMonitor monitor)
    {
        _monitor = monitor;
        _handleLostToken = monitor.HandleLostToken;
    }

    public async Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
    {
        var result = await _provider
            .RenewAsync(_mode, Resource, LockId, timeUntilExpires, cancellationToken)
            .ConfigureAwait(false);

        if (result)
        {
            Interlocked.Increment(ref _renewalCount);
        }

        return result;
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
            await _StopMonitorAsync().ConfigureAwait(false);
            await _provider.ReleaseAsync(_mode, Resource, LockId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _StopMonitorAsync().ConfigureAwait(false);

            if (_releaseOnDispose)
            {
                await ReleaseAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDisposableLockReleaseFailed(exception, Resource, LockId);
        }
    }

    /// <summary>
    /// AutoExtend mode: attempt <see cref="DistributedReaderWriterLockProvider.RenewAsync"/>. A
    /// successful renewal returns <see cref="LeaseMonitor.LeaseState.Renewed"/>. A
    /// <see langword="false"/> result is treated as <see cref="LeaseMonitor.LeaseState.Lost"/>
    /// immediately — the only paths that return false from a reader-writer extend script are
    /// (a) the caller's lockId is no longer present (genuine loss) or (b) a writer-waiting
    /// marker forced the refusal, which is the writer-preference signal that the reader MUST
    /// drop. Transient storage exceptions surface as
    /// <see cref="LeaseMonitor.LeaseState.Unknown"/> (caught at the call site) so the safety
    /// net can self-promote on repeated failure.
    /// Monitor mode: probe ownership via Validate; matching id ⇒ Held, otherwise Lost. A
    /// transient exception here also surfaces as Unknown, falling back to the lease-duration
    /// safety net.
    /// </summary>
    async Task<LeaseMonitor.LeaseState> LeaseMonitor.ILeaseHandle.RenewOrValidateLeaseAsync(
        CancellationToken cancellationToken
    )
    {
        var leaseProbe = _GetOrStartLeaseProbe(cancellationToken);
        var clearCompletedProbe = false;

        try
        {
            var result = await leaseProbe
                .WaitAsync(_storageDeadlineSnapshot, _timeProvider, cancellationToken)
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
        if (_autoExtend)
        {
            var renewed = await _provider
                .RenewAsync(_mode, Resource, LockId, _leaseDuration, cancellationToken)
                .ConfigureAwait(false);

            if (renewed)
            {
                Interlocked.Increment(ref _renewalCount);

                return LeaseMonitor.LeaseState.Renewed;
            }

            // Reader-writer extend scripts return false ONLY for genuine loss (id no longer
            // present) or writer-preference refusal (writer-waiting marker forces a queued
            // writer to drain the reader). Both must cancel HandleLostToken — there is no
            // ambiguous transient-vs-loss case here, so we skip the validate fallback used
            // by the mutex handle. Transient storage exceptions raise from the await above
            // and are caught at the deadline boundary as Unknown.
            return LeaseMonitor.LeaseState.Lost;
        }

        var valid = await _provider.ValidateAsync(_mode, Resource, LockId, cancellationToken).ConfigureAwait(false);

        return valid ? LeaseMonitor.LeaseState.Held : LeaseMonitor.LeaseState.Lost;
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
        catch (Exception exception)
        {
            _logger.LogDisposableLockMonitorDisposeFailed(exception, Resource, LockId);
        }
        finally
        {
            _deregisterMonitor?.Invoke(Resource, LockId);
        }
    }
}
