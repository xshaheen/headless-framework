// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Tests-only <see cref="IDistributedReadWriteLock"/> that never contends.
/// Every read and write acquire succeeds immediately and every renew returns true.
/// </summary>
[PublicAPI]
public sealed class NullDistributedReadWriteLock(TimeProvider timeProvider) : IDistributedReadWriteLock
{
    /// <inheritdoc />
    public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(20);

    /// <inheritdoc />
    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public Task<IDistributedLease> AcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ValidateAcquireOptions(options);

        return Task.FromResult<IDistributedLease>(new NullReaderWriterLock(resource, timeProvider));
    }

    /// <inheritdoc />
    public Task<IDistributedLease?> TryAcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ValidateAcquireOptions(options);

        return Task.FromResult<IDistributedLease?>(new NullReaderWriterLock(resource, timeProvider));
    }

    /// <inheritdoc />
    public Task<IDistributedLease> AcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ValidateAcquireOptions(options);

        return Task.FromResult<IDistributedLease>(new NullReaderWriterLock(resource, timeProvider));
    }

    /// <inheritdoc />
    public Task<IDistributedLease?> TryAcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ValidateAcquireOptions(options);

        return Task.FromResult<IDistributedLease?>(new NullReaderWriterLock(resource, timeProvider));
    }

    /// <inheritdoc />
    public Task<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(0L);
    }

    private static void _ValidateAcquireOptions(DistributedLockAcquireOptions? options)
    {
        if (
            options is { Monitoring: not LockMonitoringMode.None }
            && options.TimeUntilExpires == Timeout.InfiniteTimeSpan
        )
        {
            throw new ArgumentException("Lease monitoring requires a finite time until expiration.", nameof(options));
        }
    }

    private sealed class NullReaderWriterLock(string resource, TimeProvider timeProvider) : IDistributedLease
    {
        private int _renewalCount;

        public string LeaseId { get; } = Guid.NewGuid().ToString("N");

        public long? FencingToken => null;

        public string Resource { get; } = resource;

        public int RenewalCount => Volatile.Read(ref _renewalCount);

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
