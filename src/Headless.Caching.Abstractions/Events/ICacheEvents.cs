// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// The typed, in-process event surface of a cache instance, exposed by <see cref="ICache.Events"/>. Subscribe with
/// the native <c>+=</c> / <c>-=</c> syntax (for example <c>cache.Events.Hit += handler</c>).
/// </summary>
/// <remarks>
/// <para>
/// Handlers run guarded on a background thread by default (opt into synchronous execution at registration); an
/// exception thrown by a <em>synchronous</em> handler is caught and logged and never propagates to the cache caller.
/// <c>async void</c> handlers are unsupported — their post-dispatch exceptions bypass the guard; keep handlers
/// synchronous and, for async side-effects, start your own guarded fire-and-forget inside a synchronous handler.
/// </para>
/// <para>
/// There is no allocation when an event has no subscriber. The high-level events fire once per logical operation and
/// carry the tier of the instance in <see cref="CacheEventArgs.Tier"/>; the low-level per-tier read events of a hybrid
/// cache live on <see cref="Memory"/> and <see cref="Distributed"/> so they are not conflated with the aggregate
/// outcome. In-memory evictions fire on <see cref="Eviction"/> of the concrete in-memory cache.
/// </para>
/// </remarks>
[PublicAPI]
public interface ICacheEvents
{
    /// <summary>A value was served (fresh, or a fail-safe stale reserve — see <see cref="CacheHitEventArgs.IsStale"/>).</summary>
    event EventHandler<CacheHitEventArgs>? Hit;

    /// <summary>A get-or-add resolved to a miss (the value was computed by the factory).</summary>
    event EventHandler<CacheKeyEventArgs>? Miss;

    /// <summary>An entry was written.</summary>
    event EventHandler<CacheKeyEventArgs>? Set;

    /// <summary>A single entry was removed.</summary>
    event EventHandler<CacheKeyEventArgs>? Remove;

    /// <summary>An in-memory entry was evicted (in-memory tier only).</summary>
    event EventHandler<CacheEvictionEventArgs>? Eviction;

    /// <summary>A factory execution completed successfully.</summary>
    event EventHandler<CacheFactoryEventArgs>? FactorySuccess;

    /// <summary>A factory execution threw a non-timeout exception.</summary>
    event EventHandler<CacheFactoryEventArgs>? FactoryError;

    /// <summary>A factory execution hit a soft or hard timeout.</summary>
    event EventHandler<CacheFactoryEventArgs>? FactoryTimeout;

    /// <summary>The fail-safe mechanism served a stale reserve.</summary>
    event EventHandler<CacheFailSafeEventArgs>? FailSafeActivation;

    /// <summary>An eager refresh started because a fresh hit passed the eager-refresh threshold.</summary>
    event EventHandler<CacheRefreshEventArgs>? EagerRefresh;

    /// <summary>A background completion of a factory relegated after a soft timeout finished.</summary>
    event EventHandler<CacheRefreshEventArgs>? BackgroundRefresh;

    /// <summary>A bulk <c>RemoveAllAsync</c> completed.</summary>
    event EventHandler<CacheRemoveAllEventArgs>? RemoveAll;

    /// <summary>A prefix-scoped removal completed.</summary>
    event EventHandler<CacheRemoveByPrefixEventArgs>? RemoveByPrefix;

    /// <summary>A tag invalidation was issued.</summary>
    event EventHandler<CacheRemoveByTagEventArgs>? RemoveByTag;

    /// <summary>A logical whole-cache clear was issued.</summary>
    event EventHandler<CacheEventArgs>? Clear;

    /// <summary>A whole-cache flush was issued.</summary>
    event EventHandler<CacheEventArgs>? Flush;

    /// <summary>A hybrid invalidation was published to, or received from, peers (hybrid tier only).</summary>
    event EventHandler<CacheInvalidationEventArgs>? Invalidation;

    /// <summary>The low-level memory (L1) tier events of a hybrid cache; <see langword="null"/> for single-tier caches.</summary>
    ICacheMemoryEvents? Memory { get; }

    /// <summary>The low-level distributed (L2) tier events of a hybrid cache; <see langword="null"/> for single-tier caches.</summary>
    ICacheDistributedEvents? Distributed { get; }

    /// <summary>Whether any event on this hub (or its sub-hubs) currently has a subscriber. Used to short-circuit hot paths.</summary>
    bool HasSubscribers { get; }

    /// <summary>Whether <see cref="Eviction"/> currently has a subscriber. Lets bulk removal paths stay O(1) when unobserved.</summary>
    bool HasEvictionSubscribers { get; }

    /// <summary>Whether <see cref="Set"/> currently has a subscriber. Lets bulk write paths skip their per-key emission loop when unobserved.</summary>
    bool HasSetSubscribers { get; }
}

/// <summary>The low-level memory (L1) tier events of a hybrid cache.</summary>
[PublicAPI]
public interface ICacheMemoryEvents
{
    /// <summary>The L1 store read hit.</summary>
    event EventHandler<CacheKeyEventArgs>? Hit;

    /// <summary>The L1 store read missed.</summary>
    event EventHandler<CacheKeyEventArgs>? Miss;
}

/// <summary>The low-level distributed (L2) tier events of a hybrid cache.</summary>
[PublicAPI]
public interface ICacheDistributedEvents
{
    /// <summary>The L2 store read hit.</summary>
    event EventHandler<CacheKeyEventArgs>? Hit;

    /// <summary>The L2 store read missed.</summary>
    event EventHandler<CacheKeyEventArgs>? Miss;
}
