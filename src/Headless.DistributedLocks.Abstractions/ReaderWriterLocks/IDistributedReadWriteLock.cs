// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Provides distributed reader-writer locks for resources where concurrent readers are safe and writers require exclusivity.
/// </summary>
/// <remarks>
/// <para>
/// Implementations may enforce writer-preference. Redis-backed locks block new readers while a writer is queued so writers
/// cannot be starved by a steady stream of readers. Returned handles use the standard <see cref="IDistributedLease"/> shape,
/// including <see cref="IDistributedLease.LostToken"/> when lease monitoring is enabled.
/// </para>
/// <para>
/// Composite acquisition defines resource identity with <see cref="StringComparer.Ordinal"/> before invoking the
/// provider. Implementations whose backend aliases ordinal-distinct names must reject non-canonical names or require
/// callers to canonicalize them before using composite acquisition. Normalizing only inside the acquire methods is too
/// late and can make one composite contend with itself.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDistributedReadWriteLock
{
    /// <summary>
    /// Gets the clock used by this provider for deadlines, elapsed-time measurement, and scheduled waits.
    /// Provider-agnostic coordinators must use this instance so their timing remains aligned with the
    /// provider and deterministic under test.
    /// </summary>
    /// <remarks>
    /// This schedules work; it does not arbitrate expiry. A lease is valid only while the backend says so — the
    /// clock decides when to ask, never whether ownership still holds.
    /// </remarks>
    TimeProvider TimeProvider { get; }

    /// <summary>
    /// Gets the logger used by this provider. Provider-agnostic coordinators log through this instance so their
    /// diagnostics land in the same sink as the provider's own.
    /// </summary>
    /// <remarks>
    /// Required because disposal must never throw: a handle that fails to release during <c>DisposeAsync</c> has to
    /// report that failure somewhere, and an exception would replace whatever the caller's <c>using</c> body was
    /// already throwing. Return <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance"/> when a
    /// provider has no logger of its own.
    /// </remarks>
    ILogger Logger { get; }

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

    /// <summary>
    /// Acquires a read (shared) lock for <paramref name="resource"/> and throws
    /// <see cref="LockAcquisitionTimeoutException"/> when it cannot be acquired before
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to read-lock. Must be non-null and non-whitespace.</param>
    /// <param name="options">Per-call configuration. <see langword="null"/> applies the defaults. See <see cref="DistributedLockAcquireOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the acquire attempt; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The acquired read lease. Never <see langword="null"/> — throws on timeout instead.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resource"/> is empty or whitespace; or monitoring is requested while
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resource"/> exceeds the provider's maximum resource-name length, or
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
    /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">The read lock could not be acquired before the acquire timeout elapsed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IDistributedLease> AcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Tries to acquire a read (shared) lock for <paramref name="resource"/> and returns <see langword="null"/>
    /// on contention or timeout.
    /// </summary>
    /// <param name="resource">The resource to read-lock. Must be non-null and non-whitespace.</param>
    /// <param name="options">Per-call configuration. <see langword="null"/> applies the defaults. See <see cref="DistributedLockAcquireOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the acquire attempt; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The acquired read lease, or <see langword="null"/> if it could not be acquired before the acquire timeout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resource"/> is empty or whitespace; or monitoring is requested while
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resource"/> exceeds the provider's maximum resource-name length, or
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
    /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IDistributedLease?> TryAcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Acquires a write (exclusive) lock for <paramref name="resource"/> and throws
    /// <see cref="LockAcquisitionTimeoutException"/> when it cannot be acquired before
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to write-lock. Must be non-null and non-whitespace.</param>
    /// <param name="options">Per-call configuration. <see langword="null"/> applies the defaults. See <see cref="DistributedLockAcquireOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the acquire attempt; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The acquired write lease. Never <see langword="null"/> — throws on timeout instead.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resource"/> is empty or whitespace; or monitoring is requested while
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resource"/> exceeds the provider's maximum resource-name length, or
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
    /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large.
    /// </exception>
    /// <exception cref="LockAcquisitionTimeoutException">The write lock could not be acquired before the acquire timeout elapsed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IDistributedLease> AcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Tries to acquire a write (exclusive) lock for <paramref name="resource"/> and returns <see langword="null"/>
    /// on contention or timeout.
    /// </summary>
    /// <param name="resource">The resource to write-lock. Must be non-null and non-whitespace.</param>
    /// <param name="options">Per-call configuration. <see langword="null"/> applies the defaults. See <see cref="DistributedLockAcquireOptions"/>.</param>
    /// <param name="cancellationToken">Cancels the acquire attempt; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The acquired write lease, or <see langword="null"/> if it could not be acquired before the acquire timeout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="resource"/> is empty or whitespace; or monitoring is requested while
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resource"/> exceeds the provider's maximum resource-name length, or
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> /
    /// <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is negative (other than
    /// <see cref="Timeout.InfiniteTimeSpan"/>) or too large.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IDistributedLease?> TryAcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks whether any reader currently holds <paramref name="resource"/>.
    /// This is an inspection primitive only; do not use it for correctness decisions.
    /// </summary>
    /// <param name="resource">The resource to inspect. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns><see langword="true"/> when at least one reader currently holds <paramref name="resource"/>; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a writer currently holds <paramref name="resource"/>.
    /// This is an inspection primitive only; do not use it for correctness decisions.
    /// </summary>
    /// <param name="resource">The resource to inspect. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns><see langword="true"/> when a writer currently holds <paramref name="resource"/>; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current reader count for <paramref name="resource"/>.
    /// This is an inspection primitive only; do not use it for correctness decisions.
    /// </summary>
    /// <param name="resource">The resource to inspect. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">Cancels the read; surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The number of readers currently holding <paramref name="resource"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default);
}
