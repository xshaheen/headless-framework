// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Represents a "locking algorithm" implemented in SQL (for example PostgreSQL advisory locks). Abstracts the
/// SQL-emitting acquire/release pair so the higher-level <see cref="IDbDistributedLock"/> implementations stay
/// provider-agnostic.
/// </summary>
/// <typeparam name="TLockCookie">
/// The opaque state returned on a successful acquire and passed back on release. Carries whatever the strategy needs
/// to release the lock it took (for example the resolved advisory key).
/// </typeparam>
internal interface IDbSynchronizationStrategy<TLockCookie>
    where TLockCookie : class
{
    /// <summary>
    /// <see langword="true"/> iff the lock taken by the algorithm can be upgraded on the same connection (for example
    /// upgradeable read locks). The multiplexing engine must avoid multiplexing upgradeable locks because an upgrade
    /// may block indefinitely on the held connection, which would prevent other locks on that connection from
    /// releasing.
    /// </summary>
    bool IsUpgradeable { get; }

    /// <summary>
    /// Resolves <paramref name="resourceName"/> to the equatable physical-lock identity used to key the per-connection
    /// held-lock set in the multiplexing engine. Two distinct resource strings can map to the same physical lock (for
    /// PostgreSQL advisory locks: ASCII/int key-space overlap, or a SHA hash collision when hashing is allowed); because
    /// advisory locks are re-entrant per session, keying the held set by the resolved identity (rather than the resource
    /// string) forces a colliding second acquirer onto a dedicated connection where the database serializes them
    /// correctly, instead of letting both "hold" the same physical lock on one shared connection.
    /// </summary>
    /// <param name="resourceName">The resource name to resolve.</param>
    /// <returns>An equatable physical-lock identity used to key the per-connection held-lock set.</returns>
    object GetHeldLockIdentity(string resourceName);

    /// <summary>
    /// Attempts to acquire the lock, returning <see langword="null"/> on failure or a non-null state cookie on success.
    /// </summary>
    /// <param name="connection">The open connection on which to execute the acquire SQL.</param>
    /// <param name="resourceName">The resource name to lock.</param>
    /// <param name="timeout">Maximum server-side wait time; <see cref="TimeSpan.Zero"/> means try-once.</param>
    /// <param name="cancellationToken">Token used to cancel the acquire.</param>
    /// <returns>A non-null <typeparamref name="TLockCookie"/> on success; <see langword="null"/> when contended.</returns>
    ValueTask<TLockCookie?> TryAcquireAsync(
        DatabaseConnection connection,
        string resourceName,
        TimeSpan timeout,
        CancellationToken cancellationToken
    );

    /// <summary>Releases a lock previously acquired via <see cref="TryAcquireAsync"/>.</summary>
    /// <param name="connection">The open connection that holds the lock.</param>
    /// <param name="resourceName">The resource name whose lock should be released.</param>
    /// <param name="lockCookie">The opaque state returned when the lock was acquired.</param>
    ValueTask ReleaseAsync(DatabaseConnection connection, string resourceName, TLockCookie lockCookie);
}
