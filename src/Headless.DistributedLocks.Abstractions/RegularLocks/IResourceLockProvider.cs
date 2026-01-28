// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Provides methods to acquire, release, and manage resource locks.</summary>
[PublicAPI]
public interface IResourceLockProvider
{
    TimeSpan DefaultTimeUntilExpires { get; }

    TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>
    /// Acquires a resource lock for a specified resource this method will block
    /// until the lock is acquired or the <paramref name="acquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lock for.</param>
    /// <param name="timeUntilExpires">
    /// The amount of time until the lock expires. The allowed values are:<br/>
    /// * <see langword="null"/>: means the default value <see cref="DefaultTimeUntilExpires"/> (20 minutes).<br/>
    /// * <see cref="Timeout.InfiniteTimeSpan"/> (-1 milliseconds): means infinity no expiration set.<br/>
    /// * Value greater than 0.<br/>
    /// </param>
    /// <param name="acquireTimeout">
    /// The amount of time to wait for the lock to be acquired. The allowed values are:<br/>
    /// * <see langword="null"/>: means the default value <see cref="DefaultAcquireTimeout"/> (30 seconds).<br/>
    /// * <see cref="Timeout.InfiniteTimeSpan"/> (-1 millisecond): means infinity wait to acquire<br/>
    /// * Value greater than or equal to 0.<br/>
    /// </param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the acquired lock or null if the lock could not be acquired.
    /// </returns>
    Task<IResourceLock?> TryAcquireAsync(
        string resource,
        TimeSpan? timeUntilExpires = null,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Renews a resource lock for a specified <paramref name="resource"/> by extending
    /// the expiration time of the lock if it is still held to the <paramref name="lockId"/>
    /// and return <see langword="true"/>, otherwise <see langword="false"/>.
    /// </summary>
    Task<bool> RenewAsync(
        string resource,
        string lockId,
        TimeSpan? timeUntilExpires = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Releases a resource lock for a specified <paramref name="resource"/>
    /// if it is acquired by the <paramref name="lockId"/>.
    /// </summary>
    Task ReleaseAsync(string resource, string lockId, CancellationToken cancellationToken = default);

    /// <summary>Checks if a specified resource is currently locked.</summary>
    Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Gets the remaining time until the lock expires for a specified resource.</summary>
    /// <returns>The remaining TTL, or null if the resource is not locked or has no expiration.</returns>
    Task<TimeSpan?> GetExpirationAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Gets information about a specific lock.</summary>
    /// <returns>Lock information, or null if the resource is not locked.</returns>
    Task<LockInfo?> GetLockInfoAsync(string resource, CancellationToken cancellationToken = default);

    /// <summary>Lists all active locks.</summary>
    /// <returns>Collection of active lock information.</returns>
    Task<IReadOnlyList<LockInfo>> ListActiveLocksAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the total count of active locks.</summary>
    Task<int> GetActiveLocksCountAsync(CancellationToken cancellationToken = default);
}
