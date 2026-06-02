// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Fallback <see cref="IDistributedSemaphoreProvider"/> registered when no real semaphore provider
/// is wired. Every acquire succeeds immediately.
/// </summary>
[PublicAPI]
public sealed class NullDistributedSemaphoreProvider(TimeProvider timeProvider) : IDistributedSemaphoreProvider
{
    public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(20);

    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    public IDistributedSemaphore CreateSemaphore(string resource, int maxCount)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);

        return new NullDistributedSemaphore(resource, maxCount, timeProvider);
    }

    public Task<long> GetHolderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(0L);
    }

    private sealed class NullDistributedSemaphore(string resource, int maxCount, TimeProvider timeProvider)
        : IDistributedSemaphore
    {
        public string Resource { get; } = resource;

        public int MaxCount { get; } = maxCount;

        public Task<IDistributedLock> AcquireAsync(
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IDistributedLock>(new NullSemaphoreSlot(Resource, timeProvider));
        }

        public Task<IDistributedLock?> TryAcquireAsync(
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<IDistributedLock?>(new NullSemaphoreSlot(Resource, timeProvider));
        }
    }

    private sealed class NullSemaphoreSlot(string resource, TimeProvider timeProvider) : IDistributedLock
    {
        private int _renewalCount;

        public string LockId { get; } = Guid.NewGuid().ToString("N");

        public long? FencingToken => null;

        public string Resource { get; } = resource;

        public int RenewalCount => Volatile.Read(ref _renewalCount);

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
