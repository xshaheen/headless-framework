// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// The typed, in-process event surface of a cache instance, exposed by <see cref="ICache.Events"/>. Each event is an
/// <see cref="IAsyncEvent{TEvent}"/>; subscribe with <c>cache.Events.Hit.AddHandler(handler)</c> and unsubscribe by
/// disposing the returned registration.
/// </summary>
/// <remarks>
/// <para>
/// Handlers may be asynchronous (<c>AddHandler(async (args, ct) =&gt; …)</c>) or synchronous
/// (<c>AddHandler(args =&gt; …)</c>). They run guarded — an exception from any handler is caught and logged and
/// never propagates to the cache caller, nor stops the other handlers — and, by default, on a background task so a slow
/// handler cannot stall the cache operation.
/// </para>
/// <para>
/// There is no allocation when an event has no handler. The high-level events fire once per logical operation and carry
/// the tier of the instance in <see cref="CacheEventArgs.Tier"/>; the low-level per-tier read events of a hybrid cache
/// live on <see cref="Memory"/> and <see cref="Distributed"/> so they are not conflated with the aggregate outcome.
/// In-memory evictions fire on <see cref="Eviction"/> of the concrete in-memory cache.
/// </para>
/// </remarks>
[PublicAPI]
public interface ICacheEvents
{
    /// <summary>A value was served (fresh, or a fail-safe stale reserve — see <see cref="CacheHitEventArgs.IsStale"/>).</summary>
    IAsyncEvent<CacheHitEventArgs> Hit { get; }

    /// <summary>A get-or-add resolved to a miss (the value was computed by the factory).</summary>
    IAsyncEvent<CacheKeyEventArgs> Miss { get; }

    /// <summary>An entry was written.</summary>
    IAsyncEvent<CacheKeyEventArgs> Set { get; }

    /// <summary>A single entry was removed.</summary>
    IAsyncEvent<CacheKeyEventArgs> Remove { get; }

    /// <summary>An in-memory entry was evicted (in-memory tier only).</summary>
    IAsyncEvent<CacheEvictionEventArgs> Eviction { get; }

    /// <summary>A factory execution completed successfully.</summary>
    IAsyncEvent<CacheFactoryEventArgs> FactorySuccess { get; }

    /// <summary>A factory execution threw a non-timeout exception.</summary>
    IAsyncEvent<CacheFactoryEventArgs> FactoryError { get; }

    /// <summary>A factory execution hit a soft or hard timeout.</summary>
    IAsyncEvent<CacheFactoryEventArgs> FactoryTimeout { get; }

    /// <summary>The fail-safe mechanism served a stale reserve.</summary>
    IAsyncEvent<CacheFailSafeEventArgs> FailSafeActivation { get; }

    /// <summary>An eager refresh started because a fresh hit passed the eager-refresh threshold.</summary>
    IAsyncEvent<CacheRefreshEventArgs> EagerRefresh { get; }

    /// <summary>A background completion of a factory relegated after a soft timeout finished.</summary>
    IAsyncEvent<CacheRefreshEventArgs> BackgroundRefresh { get; }

    /// <summary>A bulk <c>RemoveAllAsync</c> completed.</summary>
    IAsyncEvent<CacheRemoveAllEventArgs> RemoveAll { get; }

    /// <summary>A prefix-scoped removal completed.</summary>
    IAsyncEvent<CacheRemoveByPrefixEventArgs> RemoveByPrefix { get; }

    /// <summary>A tag invalidation was issued.</summary>
    IAsyncEvent<CacheRemoveByTagEventArgs> RemoveByTag { get; }

    /// <summary>A logical whole-cache clear was issued.</summary>
    IAsyncEvent<CacheEventArgs> Clear { get; }

    /// <summary>A whole-cache flush was issued.</summary>
    IAsyncEvent<CacheEventArgs> Flush { get; }

    /// <summary>A hybrid invalidation was published to, or received from, peers (hybrid tier only).</summary>
    IAsyncEvent<CacheInvalidationEventArgs> Invalidation { get; }

    /// <summary>The low-level memory (L1) tier events of a hybrid cache; <see langword="null"/> for single-tier caches.</summary>
    ICacheMemoryEvents? Memory { get; }

    /// <summary>The low-level distributed (L2) tier events of a hybrid cache; <see langword="null"/> for single-tier caches.</summary>
    ICacheDistributedEvents? Distributed { get; }

    /// <summary>Whether any event on this hub (or its sub-hubs) currently has a handler. Used to short-circuit hot paths.</summary>
    bool HasSubscribers { get; }
}

/// <summary>The low-level memory (L1) tier events of a hybrid cache.</summary>
[PublicAPI]
public interface ICacheMemoryEvents
{
    /// <summary>The L1 store read hit.</summary>
    IAsyncEvent<CacheKeyEventArgs> Hit { get; }

    /// <summary>The L1 store read missed.</summary>
    IAsyncEvent<CacheKeyEventArgs> Miss { get; }
}

/// <summary>The low-level distributed (L2) tier events of a hybrid cache.</summary>
[PublicAPI]
public interface ICacheDistributedEvents
{
    /// <summary>The L2 store read hit.</summary>
    IAsyncEvent<CacheKeyEventArgs> Hit { get; }

    /// <summary>The L2 store read missed.</summary>
    IAsyncEvent<CacheKeyEventArgs> Miss { get; }
}
