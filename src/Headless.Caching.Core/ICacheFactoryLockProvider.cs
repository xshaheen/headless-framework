// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// Optional cross-node coordination seam for factory-backed cache operations. When a provider is registered and a
/// cache entry opts in via <see cref="CacheEntryOptions.UseDistributedFactoryLock"/>, the factory coordinator
/// acquires a distributed lock for the key before running the factory so only one node executes it; other nodes
/// wait on the lock and re-check the shared store instead of duplicating the work (multi-node stampede protection).
/// The local per-key single-flight lock is always acquired first; this seam adds the cross-node layer on top.
/// </summary>
[PublicAPI]
public interface ICacheFactoryLockProvider
{
    /// <summary>Tries to acquire the distributed factory lock for a cache key.</summary>
    /// <param name="key">The cache key the factory is about to run for.</param>
    /// <param name="timeout">
    /// The maximum time to wait for the lock. <see cref="TimeSpan.Zero"/> requests a single non-blocking attempt;
    /// <see cref="Timeout.InfiniteTimeSpan"/> waits unboundedly.
    /// </param>
    /// <param name="cancellationToken">The cancellation token bounding the wait.</param>
    /// <returns>
    /// A releaser that frees the lock when disposed, or <see langword="null"/> when the lock is held elsewhere /
    /// not acquired within the timeout.
    /// </returns>
    ValueTask<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan timeout, CancellationToken cancellationToken);
}
