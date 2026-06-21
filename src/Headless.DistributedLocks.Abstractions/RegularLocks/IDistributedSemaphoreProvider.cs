// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Creates distributed semaphore instances with creation-time capacity binding.</summary>
[PublicAPI]
public interface IDistributedSemaphoreProvider
{
    /// <summary>
    /// Default lease duration applied when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/>
    /// is not specified. Implementations refresh the lease in storage at this cadence when
    /// <see cref="LockMonitoringMode.AutoExtend"/> is enabled.
    /// </summary>
    TimeSpan DefaultTimeUntilExpires { get; }

    /// <summary>
    /// Default upper bound applied to acquire attempts when
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is not specified. After the timeout
    /// elapses, the acquire returns <see langword="null"/> (try variants) or throws
    /// <see cref="LockAcquisitionTimeoutException"/> (acquire variants).
    /// </summary>
    TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>Creates a semaphore for <paramref name="resource"/> with a fixed maximum holder count.</summary>
    /// <param name="resource">The resource the semaphore guards. Must be non-null and non-whitespace.</param>
    /// <param name="maxCount">The maximum number of concurrent holders. Must be at least 1.</param>
    /// <returns>A semaphore bound to <paramref name="resource"/> and <paramref name="maxCount"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is less than 1.</exception>
    IDistributedSemaphore CreateSemaphore(string resource, int maxCount);

    /// <summary>Gets the number of currently live holders for <paramref name="resource"/>.</summary>
    /// <param name="resource">The resource to inspect. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The number of live holders currently occupying a slot for <paramref name="resource"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<long> GetHolderCountAsync(string resource, CancellationToken cancellationToken = default);
}
