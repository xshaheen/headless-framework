// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Fallback <see cref="IDistributedLock"/> registered when no real provider is wired up.
/// Always grants the lock — every acquire succeeds and every renew returns true.
/// Single-replica deployments (no contention) work correctly; multi-replica deployments with
/// storage-based locks enabled should detect this sentinel by type and warn at startup.
/// </summary>
[PublicAPI]
public sealed class NullDistributedLock(TimeProvider timeProvider) : IDistributedLock
{
    /// <inheritdoc />
    public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(20);

    /// <inheritdoc />
    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public Task<IDistributedLease> AcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ValidateAcquireOptions(options);

        return Task.FromResult<IDistributedLease>(new NullDistributedLease(resource, timeProvider));
    }

    /// <inheritdoc />
    public Task<IDistributedLease?> TryAcquireAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ValidateAcquireOptions(options);

        return Task.FromResult<IDistributedLease?>(new NullDistributedLease(resource, timeProvider));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<string?> GetLeaseIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string resource, string leaseId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<TimeSpan?>(null);
    }

    /// <inheritdoc />
    public Task<DistributedLockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<DistributedLockInfo?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DistributedLockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DistributedLockInfo>>([]);
    }

    /// <inheritdoc />
    public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0L);
    }

    private static void _ValidateAcquireOptions(DistributedLockAcquireOptions? options)
    {
        if (_IsMonitoringRequested(options) && options?.TimeUntilExpires == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException("Lease monitoring requires a finite time until expiration.", nameof(options));
        }
    }

    private static bool _IsMonitoringRequested(DistributedLockAcquireOptions? options)
    {
        return options is { Monitoring: not LockMonitoringMode.None };
    }

    private sealed class NullDistributedLease(string resource, TimeProvider timeProvider) : IDistributedLease
    {
        private int _renewalCount;

        public string LeaseId { get; } = Guid.NewGuid().ToString("N");

        public long? FencingToken => null;

        public string Resource { get; } = resource;

        public int RenewalCount => _renewalCount;

        public DateTimeOffset DateAcquired { get; } = timeProvider.GetUtcNow();

        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public CancellationToken LostToken => CancellationToken.None;

        public bool CanObserveLoss => false;

        public Task ReleaseAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _renewalCount);

            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
