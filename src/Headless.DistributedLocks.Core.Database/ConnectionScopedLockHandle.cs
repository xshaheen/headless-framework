// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Handle returned by <see cref="IConnectionScopedLockStorage.TryAcquireAsync"/> for a held connection-scoped
/// lock. Owns the release callback and the connection-lost token. Release is idempotent and runs at most once;
/// disposing the handle releases the lock.
/// </summary>
[PublicAPI]
public sealed class ConnectionScopedLockHandle : IAsyncDisposable
{
    private readonly Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> _release;
    private int _released;

    /// <summary>Creates a handle for an acquired lock.</summary>
    /// <param name="resource">The locked resource name.</param>
    /// <param name="leaseId">The provider-assigned identifier for this acquisition.</param>
    /// <param name="release">Callback invoked exactly once to release the lock in the backing store.</param>
    /// <param name="connectionLostToken">
    /// Token cancelled when the underlying connection is lost; consumers observe it to learn the lock is no
    /// longer held.
    /// </param>
    public ConnectionScopedLockHandle(
        string resource,
        string leaseId,
        Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> release,
        CancellationToken connectionLostToken
    )
    {
        Resource = resource;
        LeaseId = leaseId;
        ConnectionLostToken = connectionLostToken;
        _release = release;
    }

    /// <summary>The locked resource name.</summary>
    public string Resource { get; }

    /// <summary>The provider-assigned identifier for this acquisition.</summary>
    public string LeaseId { get; }

    /// <summary>Cancelled when the underlying connection is lost, signalling that the lock is no longer held.</summary>
    public CancellationToken ConnectionLostToken { get; }

    /// <summary>Releases the lock. Idempotent — only the first call runs the release callback.</summary>
    public async ValueTask ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        await _release(this, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases the lock via <see cref="ReleaseAsync"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
