// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Applies the invalidations an <see cref="IHybridCacheBackplane"/> transport delivers to this host's hybrid caches.
/// <c>UseHybrid</c> registers one that routes each message to the default or named hybrid it names; a transport
/// resolves <c>IEnumerable&lt;IHybridCacheInvalidationListener&gt;</c> and hands every delivery to each.
/// </summary>
/// <remarks>
/// <para>
/// A transport calls these members; it does not implement them. Resolve the listeners lazily, on the first delivery,
/// because the caches they reach depend on the backplane.
/// </para>
/// <para>
/// New members are added only with a default implementation, so a listener written against this version keeps
/// compiling.
/// </para>
/// </remarks>
[PublicAPI]
public interface IHybridCacheInvalidationListener
{
    /// <summary>
    /// Applies one invalidation another host (or this one) sent. A message this host sent is ignored, and a failure
    /// to apply it is logged rather than thrown, so a transport may call it on its delivery loop.
    /// </summary>
    /// <param name="message">The delivered invalidation.</param>
    /// <param name="cancellationToken">Cancelled when the transport stops.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the invalidation is applied.</returns>
    ValueTask OnInvalidationAsync(CacheInvalidationMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Flushes the L1 of every hybrid cache in this host, because invalidations sent while the transport had no live
    /// subscription were never delivered here. Call it each time the subscription is established, including the
    /// first time: L1 can be warmed before the subscription is live.
    /// </summary>
    /// <param name="generation">Counts the establishments of the transport's subscription in this host, starting at 1.</param>
    /// <param name="cancellationToken">Cancelled when the transport stops.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when every L1 is flushed.</returns>
    ValueTask OnSubscriptionEstablishedAsync(long generation, CancellationToken cancellationToken);
}
