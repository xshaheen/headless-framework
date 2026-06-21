// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// <see cref="IDistributedReadWriteLock"/> implementation that maps read/write locks onto the
/// shared/exclusive modes of a <see cref="ConnectionScopedDistributedLock"/>: read locks acquire in
/// shared mode, write locks in exclusive mode. Reader-writer handles never carry a fencing token.
/// </summary>
/// <param name="mutexProvider">The connection-scoped mutex provider whose shared/exclusive storage backs this.</param>
[PublicAPI]
public sealed class ConnectionScopedReadWriteLock(ConnectionScopedDistributedLock mutexProvider)
    : IDistributedReadWriteLock
{
    /// <summary>
    /// Delegates to <see cref="ConnectionScopedDistributedLock.DefaultTimeUntilExpires"/>;
    /// connection-scoped locks have no TTL (<see cref="Timeout.InfiniteTimeSpan"/>).
    /// </summary>
    public TimeSpan DefaultTimeUntilExpires => mutexProvider.DefaultTimeUntilExpires;

    /// <summary>
    /// Delegates to <see cref="ConnectionScopedDistributedLock.DefaultAcquireTimeout"/>.
    /// </summary>
    public TimeSpan DefaultAcquireTimeout => mutexProvider.DefaultAcquireTimeout;

    /// <summary>
    /// Acquires a shared (reader) lock on <paramref name="resource"/>, blocking until acquired or the
    /// acquire timeout is exceeded.
    /// </summary>
    /// <param name="resource">The resource to read-lock.</param>
    /// <param name="options">Per-call options; <see langword="null"/> applies provider defaults.</param>
    /// <param name="cancellationToken">Token used to cancel the acquire attempt.</param>
    /// <returns>A held <see cref="IDistributedLease"/> that must be disposed to release the read lock.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is whitespace or exceeds the configured max length.</exception>
    /// <exception cref="LockAcquisitionTimeoutException">Thrown when the lock cannot be acquired before the acquire timeout elapses.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<IDistributedLease> AcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return await _AcquireAsync(resource, isShared: true, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to acquire a shared (reader) lock on <paramref name="resource"/>, returning
    /// <see langword="null"/> if contended past the acquire timeout.
    /// </summary>
    /// <param name="resource">The resource to read-lock.</param>
    /// <param name="options">Per-call options; <see langword="null"/> applies provider defaults.</param>
    /// <param name="cancellationToken">Token used to cancel the acquire attempt.</param>
    /// <returns>A held <see cref="IDistributedLease"/> on success, or <see langword="null"/> on timeout.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is whitespace or exceeds the configured max length.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<IDistributedLease?> TryAcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return mutexProvider.TryAcquireAsync(resource, isShared: true, options, cancellationToken);
    }

    /// <summary>
    /// Acquires an exclusive (writer) lock on <paramref name="resource"/>, blocking until acquired or the
    /// acquire timeout is exceeded.
    /// </summary>
    /// <param name="resource">The resource to write-lock.</param>
    /// <param name="options">Per-call options; <see langword="null"/> applies provider defaults.</param>
    /// <param name="cancellationToken">Token used to cancel the acquire attempt.</param>
    /// <returns>A held <see cref="IDistributedLease"/> that must be disposed to release the write lock.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is whitespace or exceeds the configured max length.</exception>
    /// <exception cref="LockAcquisitionTimeoutException">Thrown when the lock cannot be acquired before the acquire timeout elapses.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<IDistributedLease> AcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return await _AcquireAsync(resource, isShared: false, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to acquire an exclusive (writer) lock on <paramref name="resource"/>, returning
    /// <see langword="null"/> if contended past the acquire timeout.
    /// </summary>
    /// <param name="resource">The resource to write-lock.</param>
    /// <param name="options">Per-call options; <see langword="null"/> applies provider defaults.</param>
    /// <param name="cancellationToken">Token used to cancel the acquire attempt.</param>
    /// <returns>A held <see cref="IDistributedLease"/> on success, or <see langword="null"/> on timeout.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resource"/> is whitespace or exceeds the configured max length.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public Task<IDistributedLease?> TryAcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return mutexProvider.TryAcquireAsync(resource, isShared: false, options, cancellationToken);
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="resource"/> is currently held in shared (reader) mode.
    /// </summary>
    /// <param name="resource">The resource to check.</param>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    public Task<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return mutexProvider.IsLockedAsync(resource, isShared: true, cancellationToken);
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="resource"/> is currently held in exclusive (writer) mode.
    /// </summary>
    /// <param name="resource">The resource to check.</param>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    public Task<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return mutexProvider.IsLockedAsync(resource, isShared: false, cancellationToken);
    }

    /// <summary>
    /// Returns the number of concurrent readers currently holding <paramref name="resource"/> in shared mode.
    /// </summary>
    /// <param name="resource">The resource to count readers for.</param>
    /// <param name="cancellationToken">Token used to cancel the storage call.</param>
    /// <returns>Count of active shared (reader) holders.</returns>
    public async Task<long> GetReaderCountAsync(string resource, CancellationToken cancellationToken = default)
    {
        return await mutexProvider
            .GetLocksCountAsync(resource, isShared: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IDistributedLease> _AcquireAsync(
        string resource,
        bool isShared,
        DistributedLockAcquireOptions? options,
        CancellationToken cancellationToken
    )
    {
        var handle = await mutexProvider
            .TryAcquireAsync(resource, isShared, options, cancellationToken)
            .ConfigureAwait(false);

        if (handle is not null)
        {
            return handle;
        }

        // Match the mutex provider: a zero-timeout (try-once) contention throws the specific
        // ForTryOnceContention shape so callers catching it behave consistently across lock kinds.
        var acquireTimeout = options?.AcquireTimeout ?? mutexProvider.DefaultAcquireTimeout;

        throw acquireTimeout == TimeSpan.Zero
            ? LockAcquisitionTimeoutException.ForTryOnceContention(resource)
            : new LockAcquisitionTimeoutException(resource);
    }
}
