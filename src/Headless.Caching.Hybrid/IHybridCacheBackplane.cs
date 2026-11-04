// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>
/// Carries hybrid-cache invalidations from the host that wrote to every other host running the same caches, so each
/// one drops the L1 copies the write made stale. Every <see cref="HybridCache"/>, default and named, sends through the
/// one registered backplane; a transport delivers each message to every host, where it hands it to each registered
/// <see cref="IHybridCacheInvalidationListener"/>.
/// </summary>
/// <remarks>
/// <para>
/// A hybrid cache refuses to start without a backplane, because a host whose writes reach no peer leaves the peers
/// serving stale L1 entries until their local TTL runs out. Choose one inside <c>AddHeadlessCaching</c>:
/// <c>UseMessagingBackplane()</c> from <c>Headless.Caching.Hybrid.Messaging</c>, <c>UseRedisBackplane()</c> from
/// <c>Headless.Caching.Redis</c>, or <c>UseSingleInstanceBackplane()</c> when exactly one host runs the caches. A custom
/// transport registers its own implementation instead.
/// </para>
/// <para>
/// A transport may deliver at most once, and may hand a host its own messages back: receivers drop a message whose
/// <see cref="CacheInvalidationMessage.InstanceId"/> is their own. What it must not do is lose messages silently. Each
/// time its subscription is established, the first time and after every gap in which messages may have been missed,
/// it calls <see cref="IHybridCacheInvalidationListener.OnSubscriptionEstablishedAsync"/> on every listener so the
/// hosts flush their L1 instead of trusting entries whose invalidation never arrived.
/// </para>
/// <para>
/// New members are added only with a default implementation, so a transport written against this version keeps
/// compiling.
/// </para>
/// </remarks>
[PublicAPI]
public interface IHybridCacheBackplane
{
    /// <summary>
    /// Sends <paramref name="message"/> to every host. Send it unchanged: receivers order invalidations by
    /// <see cref="CacheInvalidationMessage.Timestamp"/> and route them by <see cref="CacheInvalidationMessage.CacheName"/>.
    /// </summary>
    /// <remarks>
    /// Complete only once the transport accepted the message, and throw when it did not. The cache logs the failure,
    /// queues the invalidation for replay when <see cref="HybridCacheOptions.EnableAutoRecovery"/> is set, and
    /// rethrows when <see cref="HybridCacheOptions.ReThrowBackplaneExceptions"/> is set; a send that fails silently
    /// skips all three.
    /// </remarks>
    /// <param name="message">The invalidation, already stamped with its origin instance, cache name, and timestamp.</param>
    /// <param name="cancellationToken">The cache caller's token.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the transport accepted the message.</returns>
    ValueTask PublishAsync(CacheInvalidationMessage message, CancellationToken cancellationToken);
}
