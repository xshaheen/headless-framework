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
    public TimeSpan DefaultTimeUntilExpires => mutexProvider.DefaultTimeUntilExpires;

    public TimeSpan DefaultAcquireTimeout => mutexProvider.DefaultAcquireTimeout;

    public async Task<IDistributedLease> AcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return await _AcquireAsync(resource, isShared: true, options, cancellationToken).ConfigureAwait(false);
    }

    public Task<IDistributedLease?> TryAcquireReadLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return mutexProvider.TryAcquireAsync(resource, isShared: true, options, cancellationToken);
    }

    public async Task<IDistributedLease> AcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return await _AcquireAsync(resource, isShared: false, options, cancellationToken).ConfigureAwait(false);
    }

    public Task<IDistributedLease?> TryAcquireWriteLockAsync(
        string resource,
        DistributedLockAcquireOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return mutexProvider.TryAcquireAsync(resource, isShared: false, options, cancellationToken);
    }

    public Task<bool> IsReadLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return mutexProvider.IsLockedAsync(resource, isShared: true, cancellationToken);
    }

    public Task<bool> IsWriteLockedAsync(string resource, CancellationToken cancellationToken = default)
    {
        return mutexProvider.IsLockedAsync(resource, isShared: false, cancellationToken);
    }

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
