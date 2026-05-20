// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.RateLimiting;

[PublicAPI]
public interface IDistributedRateLimiter
{
    TimeSpan DefaultAcquireTimeout { get; }

    /// <summary>
    /// Acquires a rate-limiter lease for a specified resource. This method blocks until a lease is acquired
    /// or the <paramref name="acquireTimeout"/> is reached.
    /// </summary>
    /// <param name="resource">The resource to acquire the lease for.</param>
    /// <param name="acquireTimeout">
    /// The amount of time to wait for the lease to be acquired. The allowed values are:
    /// <list type="bullet">
    /// <item><see langword="null"/>: means the default value.</item>
    /// <item><see cref="Timeout.InfiniteTimeSpan"/> (-1 milliseconds): means wait indefinitely.</item>
    /// <item>Value greater than or equal to 0.</item>
    /// </list>
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the acquired lease or null if the lease could not be acquired.
    /// </returns>
    Task<IDistributedRateLimiterLease?> TryAcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks if a specified resource has reached the current rate-limiting period limit.
    /// </summary>
    /// <param name="resource">The resource to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the resource is at its limit; otherwise, false.</returns>
    Task<bool> IsLockedAsync(string resource, CancellationToken cancellationToken = default);
}
