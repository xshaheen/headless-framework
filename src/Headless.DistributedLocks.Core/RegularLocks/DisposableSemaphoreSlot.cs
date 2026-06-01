// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

internal sealed class DisposableSemaphoreSlot : IDistributedLock, LeaseMonitor.ILeaseHandle
{
    private readonly TimeSpan _leaseDuration;
    private readonly DistributedSemaphoreProvider _provider;
    private readonly bool _releaseOnDispose;
    private readonly bool _autoExtend;
    private readonly Action<string, string>? _deregisterMonitor;
    private readonly ILogger _logger;
    private readonly AsyncLock _lock = new();
    private readonly TimeSpan _monitoringCadenceSnapshot;
    private readonly TimeSpan _storageDeadlineSnapshot;
    private readonly TimeProvider _timeProvider;

    private volatile bool _isReleased;
    private int _disposed;
    private int _renewalCount;
    private LeaseMonitor? _monitor;
#pragma warning disable IDE0032 // Snapshotted from monitor; public getter must stay token-only.
    private CancellationToken _handleLostToken = CancellationToken.None;
#pragma warning restore IDE0032

    internal DisposableSemaphoreSlot(
        string resource,
        string lockId,
        long? fencingToken,
        TimeSpan leaseDuration,
        TimeSpan timeWaitedForLock,
        DistributedSemaphoreProvider provider,
        bool releaseOnDispose,
        bool autoExtend,
        DistributedLockOptions options,
        TimeProvider timeProvider,
        Action<string, string>? deregisterMonitor,
        ILogger logger
    )
    {
        Resource = resource;
        LockId = lockId;
        FencingToken = fencingToken;
        DateAcquired = timeProvider.GetUtcNow();
        TimeWaitedForLock = timeWaitedForLock;
        _leaseDuration = leaseDuration;
        _provider = provider;
        _releaseOnDispose = releaseOnDispose;
        _autoExtend = autoExtend;
        _deregisterMonitor = deregisterMonitor;
        _logger = logger;

        var fraction = autoExtend ? options.AutoExtensionCadenceFraction : options.PollingCadenceFraction;
        var cadenceTicks = Math.Max(1, (long)(leaseDuration.Ticks * fraction));
        _monitoringCadenceSnapshot = TimeSpan.FromTicks(cadenceTicks);
        _storageDeadlineSnapshot =
            _monitoringCadenceSnapshot.TotalSeconds < 5.0 ? _monitoringCadenceSnapshot : TimeSpan.FromSeconds(5);
        _timeProvider = timeProvider;
    }

    public string LockId { get; }

    public long? FencingToken { get; }

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
        var result = await _provider.RenewAsync(Resource, LockId, timeUntilExpires, cancellationToken)
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
            await _provider.ReleaseAsync(Resource, LockId, CancellationToken.None).ConfigureAwait(false);
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

    async Task<LeaseMonitor.LeaseState> LeaseMonitor.ILeaseHandle.RenewOrValidateLeaseAsync(
        CancellationToken cancellationToken
    )
    {
        // Per-iteration storage deadline: bounds a single storage round-trip so a stuck/blocked
        // backend call (e.g., a Redis reconnect storm that ignores its CT) cannot wedge the
        // monitoring task — and therefore cannot wedge LeaseMonitor.DisposeAsync, which awaits
        // it. Capped at min(5s, cadence) and snapshotted at construction. On deadline trip we
        // classify as Unknown (transient) and let the safety net self-promote on repeated misses.
        try
        {
            if (_autoExtend)
            {
                var renewed = await _WithStorageDeadlineAsync(
                        RenewAsync(_leaseDuration, cancellationToken),
                        _storageDeadlineSnapshot,
                        _timeProvider,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                return renewed ? LeaseMonitor.LeaseState.Renewed : LeaseMonitor.LeaseState.Lost;
            }

            var valid = await _WithStorageDeadlineAsync(
                    _provider.ValidateAsync(Resource, LockId, cancellationToken),
                    _storageDeadlineSnapshot,
                    _timeProvider,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return valid ? LeaseMonitor.LeaseState.Held : LeaseMonitor.LeaseState.Lost;
        }
        catch (TimeoutException)
        {
            return LeaseMonitor.LeaseState.Unknown;
        }
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
        finally
        {
            _deregisterMonitor?.Invoke(Resource, LockId);
        }
    }
}
