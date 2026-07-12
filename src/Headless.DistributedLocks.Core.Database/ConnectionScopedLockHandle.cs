// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Data.Common;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Handle returned by <see cref="IConnectionScopedLockStorage.TryAcquireAsync"/> for a held connection-scoped
/// lock. Owns the release callback and the connection-lost token. Release is idempotent and runs at most once;
/// disposing the handle releases the lock.
/// </summary>
/// <remarks>Creates a handle for an acquired lock.</remarks>
/// <param name="resource">The locked resource name.</param>
/// <param name="leaseId">The provider-assigned identifier for this acquisition.</param>
/// <param name="release">Callback invoked exactly once to release the lock in the backing store.</param>
/// <param name="connectionLostToken">
/// Token cancelled when the underlying connection is lost; consumers observe it to learn the lock is no
/// longer held. <see cref="CancellationToken.None"/> means acquire-time monitoring was disabled.
/// </param>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ConnectionScopedLockHandle(
    string resource,
    string leaseId,
    Func<ConnectionScopedLockHandle, CancellationToken, ValueTask> release,
    CancellationToken connectionLostToken
) : IAsyncDisposable
{
    private int _released;

    /// <summary>
    /// The open connection that holds this lock, when the storage acquires on a dedicated connection it can safely
    /// lend for an in-band follow-up query (for example issuing a fencing token on the same session). Null when the
    /// storage has no lendable connection (for example pooled/multiplexed engines), in which case follow-up work
    /// opens its own connection.
    /// </summary>
    internal DbConnection? HeldConnection { get; init; }

    /// <summary>The locked resource name.</summary>
    public string Resource { get; } = resource;

    /// <summary>The provider-assigned identifier for this acquisition.</summary>
    public string LeaseId { get; } = leaseId;

    /// <summary>Cancelled when the underlying connection is lost, signalling that the lock is no longer held.</summary>
    public CancellationToken ConnectionLostToken { get; } = connectionLostToken;

    /// <summary>Releases the lock. Idempotent — only the first call runs the release callback.</summary>
    public async ValueTask ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        await release(this, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Releases the lock via <see cref="ReleaseAsync"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
