namespace Framework.DistributedLocks;

/// <summary>Provides methods to acquire, release, and manage distributed locks.</summary>
[PublicAPI]
public interface IDistributedLockProvider
{
    /// <summary>
    /// Acquires a distributed lock for a specified resource this method will block
    /// until the lock is acquired or the <paramref name="timeUntilExpires"/> is reached.
    /// The default <paramref name="timeUntilExpires"/> is 15 minutes.
    /// To acquire a lock without expiration set it to <see cref="TimeSpan.Zero"/>.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the acquired lock or null if the lock could not be acquired.
    /// </returns>
    Task<IDistributedLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks if a specified resource is currently locked.
    /// </summary>
    Task<bool> IsLockedAsync(string resource);

    /// <summary>
    /// Renews a distributed lock for a specified <paramref name="resource"/> by extending
    /// the expiration time of the lock if it is still held to the <paramref name="lockId"/>.
    /// </summary>
    Task RenewAsync(string resource, string lockId, TimeSpan? timeUntilExpires = null);

    /// <summary>
    /// Releases a distributed lock for a specified <paramref name="resource"/>
    /// if it is acquired by the <paramref name="lockId"/>.
    /// </summary>
    Task ReleaseAsync(string resource, string lockId);
}
