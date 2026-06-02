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
    IDistributedSemaphore CreateSemaphore(string resource, int maxCount);

    /// <summary>Gets the number of currently live holders for <paramref name="resource"/>.</summary>
    Task<long> GetHolderCountAsync(string resource, CancellationToken cancellationToken = default);
}
