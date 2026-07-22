// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Access to shared cache-event surface instances.</summary>
[PublicAPI]
public static class CacheEvents
{
    /// <summary>
    /// A shared, allocation-free <see cref="ICacheEvents"/> whose events never fire and whose
    /// <see cref="ICacheEvents.HasSubscribers"/> is always <see langword="false"/>. Backs the default
    /// <see cref="ICache.Events"/> implementation so caches that do not surface events cost nothing.
    /// </summary>
    public static ICacheEvents NoOp { get; } = new NoOpCacheEvents();

    private sealed class NoOpCacheEvents : ICacheEvents
    {
        // Subscriptions are discarded: the handler is never stored, so the events never fire and never allocate.
        public event EventHandler<CacheHitEventArgs>? Hit
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheKeyEventArgs>? Miss
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheKeyEventArgs>? Set
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheKeyEventArgs>? Remove
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheEvictionEventArgs>? Eviction
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheFactoryEventArgs>? FactorySuccess
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheFactoryEventArgs>? FactoryError
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheFactoryEventArgs>? FactoryTimeout
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheFailSafeEventArgs>? FailSafeActivation
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheRefreshEventArgs>? EagerRefresh
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheRefreshEventArgs>? BackgroundRefresh
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheRemoveAllEventArgs>? RemoveAll
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheRemoveByPrefixEventArgs>? RemoveByPrefix
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheRemoveByTagEventArgs>? RemoveByTag
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheEventArgs>? Clear
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheEventArgs>? Flush
        {
            add { }
            remove { }
        }

        public event EventHandler<CacheInvalidationEventArgs>? Invalidation
        {
            add { }
            remove { }
        }

        public ICacheMemoryEvents? Memory => null;

        public ICacheDistributedEvents? Distributed => null;

        public bool HasSubscribers => false;

        public bool HasEvictionSubscribers => false;

        public bool HasSetSubscribers => false;
    }
}
