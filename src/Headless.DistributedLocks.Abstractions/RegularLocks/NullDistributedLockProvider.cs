// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Fallback <see cref="IDistributedLockProvider"/> registered when no real provider is wired up.
/// Always grants the lock — every acquire succeeds and every renew returns true.
/// Single-replica deployments (no contention) work correctly; multi-replica deployments with
/// storage-based locks enabled should detect this sentinel by type and warn at startup.
/// </summary>
[PublicAPI]
public sealed class NullDistributedLockProvider(TimeProvider timeProvider) : IDistributedLockProvider
{
    public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    public Task<IDistributedLock> AcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        bool releaseOnDispose = true,
        bool monitorLease = false,
        bool autoExtend = false,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IDistributedLock>(new NullDistributedLock(resource, timeProvider));
    }

    public Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        bool releaseOnDispose = true,
        bool monitorLease = false,
        bool autoExtend = false,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IDistributedLock?>(new NullDistributedLock(resource, timeProvider));
    }

    public Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(true);
    }

    public Task<string?> GetLockIdAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<string?>(null);
    }

    public Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }

    public Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<TimeSpan?>(null);
    }

    public Task<LockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LockInfo?>(null);
    }

    public Task<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LockInfo>>([]);
    }

    public Task<long> GetActiveLocksCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0L);
    }

    private sealed class NullDistributedLock(string resource, TimeProvider timeProvider) : IDistributedLock
    {
        private int _renewalCount;

        public string LockId { get; } = Guid.NewGuid().ToString("N");

        public string Resource { get; } = resource;

        public int RenewalCount => _renewalCount;

        public DateTimeOffset DateAcquired { get; } = timeProvider.GetUtcNow();

        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public CancellationToken HandleLostToken => CancellationToken.None;

        public bool IsMonitored => false;

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
