// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// No-op <see cref="IDistributedSemaphoreProvider"/> where every acquire succeeds immediately. This
/// is opt-in: it is never registered automatically, and there is no fallback wiring when a real
/// semaphore provider is absent. Register it explicitly (e.g. for tests or single-node scenarios
/// that want a permissive semaphore) when this behavior is desired.
/// </summary>
[PublicAPI]
public sealed class NullDistributedSemaphoreProvider(
    TimeProvider timeProvider,
    ILogger<NullDistributedSemaphoreProvider>? logger = null
) : IDistributedSemaphoreProvider
{
    /// <inheritdoc />
    public TimeProvider TimeProvider => timeProvider;

    /// <inheritdoc />
    public ILogger Logger { get; } = logger ?? NullLogger<NullDistributedSemaphoreProvider>.Instance;

    /// <inheritdoc />
    public TimeSpan DefaultTimeUntilExpires => TimeSpan.FromMinutes(20);

    /// <inheritdoc />
    public TimeSpan DefaultAcquireTimeout => TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public IDistributedSemaphore CreateSemaphore(string resource, int maxCount)
    {
        Argument.IsNotNullOrWhiteSpace(resource);
        Argument.IsGreaterThanOrEqualTo(maxCount, 1);

        return new NullDistributedSemaphore(resource, maxCount, timeProvider);
    }

    /// <inheritdoc />
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

        public Task<IDistributedLease> AcquireAsync(
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ValidateAcquireOptions(options);

            return Task.FromResult<IDistributedLease>(new NullSemaphoreSlot(Resource, timeProvider));
        }

        public Task<IDistributedLease?> TryAcquireAsync(
            DistributedLockAcquireOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ValidateAcquireOptions(options);

            return Task.FromResult<IDistributedLease?>(new NullSemaphoreSlot(Resource, timeProvider));
        }

        private static void _ValidateAcquireOptions(DistributedLockAcquireOptions? options)
        {
            if (options?.TimeUntilExpires == Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentException(
                    "Distributed semaphore acquires require a finite timeUntilExpires; Timeout.InfiniteTimeSpan is not valid.",
                    nameof(options)
                );
            }
        }
    }

    private sealed class NullSemaphoreSlot(string resource, TimeProvider timeProvider) : IDistributedLease
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
