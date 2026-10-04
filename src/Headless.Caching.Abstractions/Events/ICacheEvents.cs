// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Caching;

/// <summary>
/// Defines the in-process event surface of a cache instance exposed through <see cref="ICache.Events"/>.
/// </summary>
/// <remarks>
/// Handlers can be asynchronous or synchronous. Exceptions thrown by handlers are caught, logged,
/// and do not propagate to cache callers. Signals accepted by the bounded background dispatcher
/// preserve first-in-first-out order. When the buffer is full, new signals are dropped.
/// </remarks>
[PublicAPI]
public interface ICacheEvents
{
    /// <summary>Occurs when a value is served, whether fresh or from a fail-safe reserve.</summary>
    IAsyncEvent<CacheHitEventArgs> Hit { get; }

    /// <summary>Occurs when a get-or-add operation resolves to a miss and executes the factory.</summary>
    IAsyncEvent<CacheKeyEventArgs> Miss { get; }

    /// <summary>Occurs when an entry is written.</summary>
    IAsyncEvent<CacheKeyEventArgs> Set { get; }

    /// <summary>Occurs when a single entry is removed.</summary>
    IAsyncEvent<CacheKeyEventArgs> Remove { get; }

    /// <summary>Occurs when an in-memory entry is evicted from the in-memory tier.</summary>
    IAsyncEvent<CacheEvictionEventArgs> Eviction { get; }

    /// <summary>Occurs when factory execution completes successfully.</summary>
    IAsyncEvent<CacheFactoryEventArgs> FactorySuccess { get; }

    /// <summary>Occurs when factory execution throws an exception other than a timeout.</summary>
    IAsyncEvent<CacheFactoryEventArgs> FactoryError { get; }

    /// <summary>Occurs when factory execution exceeds a soft or hard timeout.</summary>
    IAsyncEvent<CacheFactoryEventArgs> FactoryTimeout { get; }

    /// <summary>Occurs when the fail-safe mechanism serves a stale reserve.</summary>
    IAsyncEvent<CacheFailSafeEventArgs> FailSafeActivation { get; }

    /// <summary>Occurs when eager refresh starts after a hit passes the eager refresh threshold.</summary>
    IAsyncEvent<CacheRefreshEventArgs> EagerRefresh { get; }

    /// <summary>Occurs when a detached background factory finishes execution after a soft timeout.</summary>
    IAsyncEvent<CacheRefreshEventArgs> BackgroundRefresh { get; }

    /// <summary>Occurs when a bulk removal operation finishes.</summary>
    IAsyncEvent<CacheRemoveAllEventArgs> RemoveAll { get; }

    /// <summary>Occurs when a prefix-scoped removal operation finishes.</summary>
    IAsyncEvent<CacheRemoveByPrefixEventArgs> RemoveByPrefix { get; }

    /// <summary>Occurs when a tag invalidation is issued.</summary>
    IAsyncEvent<CacheRemoveByTagEventArgs> RemoveByTag { get; }

    /// <summary>Occurs when a logical cache clear is issued.</summary>
    IAsyncEvent<CacheEventArgs> Clear { get; }

    /// <summary>Occurs when a cache flush is issued.</summary>
    IAsyncEvent<CacheEventArgs> Flush { get; }

    /// <summary>Occurs when a hybrid invalidation is published to or received from peers.</summary>
    IAsyncEvent<CacheInvalidationEventArgs> Invalidation { get; }

    /// <summary>Gets the memory tier events for a hybrid cache, or <see langword="null"/> for single-tier caches.</summary>
    ICacheMemoryEvents? Memory { get; }

    /// <summary>Gets the distributed tier events for a hybrid cache, or <see langword="null"/> for single-tier caches.</summary>
    ICacheDistributedEvents? Distributed { get; }

    /// <summary>Gets a value indicating whether any event on this hub or child hubs has a registered handler.</summary>
    bool HasSubscribers { get; }

    /// <summary>Gets the current accepted, processed, dropped, and pending signal counts for the dispatcher.</summary>
    CacheEventDispatchStatistics DispatchStatistics => default;
}

/// <summary>Defines memory tier events for a hybrid cache.</summary>
[PublicAPI]
public interface ICacheMemoryEvents
{
    /// <summary>Occurs when an L1 store read finds an entry.</summary>
    IAsyncEvent<CacheKeyEventArgs> Hit { get; }

    /// <summary>Occurs when an L1 store read does not find an entry.</summary>
    IAsyncEvent<CacheKeyEventArgs> Miss { get; }
}

/// <summary>Defines distributed tier events for a hybrid cache.</summary>
[PublicAPI]
public interface ICacheDistributedEvents
{
    /// <summary>Occurs when an L2 store read finds an entry.</summary>
    IAsyncEvent<CacheKeyEventArgs> Hit { get; }

    /// <summary>Occurs when an L2 store read does not find an entry.</summary>
    IAsyncEvent<CacheKeyEventArgs> Miss { get; }
}
