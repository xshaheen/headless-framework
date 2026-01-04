// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

[PublicAPI]
public interface IThrottlingResourceLockProvider
{
    TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>
    /// Acquires a resource lock for a specified resource this method will block
    /// until the lock is acquired or the <paramref name="acquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lock for.</param>
    /// <param name="acquireTimeout">
    /// The amount of time to wait for the lock to be acquired. The allowed values are:
    /// <list type="bullet">
    /// <item><see langword="null"/>: means the default value (1 minute).</item>
    /// <item><see cref="Timeout.InfiniteTimeSpan"/> (-1 milliseconds): means infinity wait to acquire</item>
    /// <item>Value greater than 0.</item>
    /// </list>
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the acquired lock or null if the lock could not be acquired.
    /// </returns>
    Task<IResourceThrottlingLock?> TryAcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks if a specified resource is currently locked.
    /// </summary>
    Task<bool> IsLockedAsync(string resource);
}
