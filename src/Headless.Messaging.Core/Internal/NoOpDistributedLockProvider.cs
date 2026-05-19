// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Headless.Messaging.Internal;

/// <summary>
/// Fallback lock provider registered when no real <see cref="IDistributedLockProvider"/> is wired up.
/// Always grants the lock — every acquire succeeds and every renew returns true.
/// Single-replica deployments (no contention) work correctly; multi-replica deployments
/// with <c>UseStorageLock=true</c> will log a startup warning via the bootstrapper.
/// </summary>
internal sealed class NoOpDistributedLockProvider(TimeProvider timeProvider) : IDistributedLockProvider
{
    public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    public Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IDistributedLock?>(new NoOpDistributedLock(resource, timeProvider));
    }

    public Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(true);
    }

    public Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default)
    {
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

    private sealed class NoOpDistributedLock(string resource, TimeProvider timeProvider) : IDistributedLock
    {
        public string LockId { get; } = Guid.NewGuid().ToString("N");

        public string Resource { get; } = resource;

        public int RenewalCount => 0;

        public DateTimeOffset DateAcquired { get; } = timeProvider.GetUtcNow();

        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public Task ReleaseAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
