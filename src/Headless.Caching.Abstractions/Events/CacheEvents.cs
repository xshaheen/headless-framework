// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless.Caching;

/// <summary>Provides access to shared cache event instances.</summary>
[PublicAPI]
public static class CacheEvents
{
    /// <summary>
    /// Gets a shared, allocation-free <see cref="ICacheEvents"/> instance whose events never fire and whose
    /// <see cref="ICacheEvents.HasSubscribers"/> property is always <see langword="false"/>. Backs the default
    /// <see cref="ICache.Events"/> implementation so caches that do not surface events incur no overhead.
    /// </summary>
    public static ICacheEvents NoOp { get; } = new NoOpCacheEvents();

    // A no-op IAsyncEvent implementation where AddHandler returns a shared no-op disposable and no callback executes.
    private sealed class NoOpAsyncEvent<TEvent> : IAsyncEvent<TEvent>
        where TEvent : EventArgs
    {
        public static readonly NoOpAsyncEvent<TEvent> Instance = new();

        private NoOpAsyncEvent() { }

        public bool ParallelInvoke => false;

        public bool HasHandlers => false;

        public IDisposable AddHandler(Func<TEvent, CancellationToken, ValueTask> callback)
        {
            Argument.IsNotNull(callback);
            return NoOpDisposable.Instance;
        }

        public IDisposable AddHandler(Action<TEvent> callback)
        {
            Argument.IsNotNull(callback);
            return NoOpDisposable.Instance;
        }

        public IDisposable AddHandler(AsyncEventHandler<TEvent> callback)
        {
            Argument.IsNotNull(callback);
            return NoOpDisposable.Instance;
        }

        public ValueTask InvokeAsync(object sender, TEvent eventArgs, CancellationToken cancellationToken = default) =>
            default;

        public ValueTask SafeInvokeAsync(
            object sender,
            TEvent eventArgs,
            Action<Exception> onHandlerError,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(onHandlerError);
            return default;
        }

        public IDisposable Subscribe(IObserver<TEvent> observer)
        {
            Argument.IsNotNull(observer);
            return NoOpDisposable.Instance;
        }

        public void ClearHandlers() { }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();

        public void Dispose() { }
    }

    private sealed class NoOpCacheEvents : ICacheEvents
    {
        public IAsyncEvent<CacheHitEventArgs> Hit => NoOpAsyncEvent<CacheHitEventArgs>.Instance;

        public IAsyncEvent<CacheKeyEventArgs> Miss => NoOpAsyncEvent<CacheKeyEventArgs>.Instance;

        public IAsyncEvent<CacheKeyEventArgs> Set => NoOpAsyncEvent<CacheKeyEventArgs>.Instance;

        public IAsyncEvent<CacheKeyEventArgs> Remove => NoOpAsyncEvent<CacheKeyEventArgs>.Instance;

        public IAsyncEvent<CacheEvictionEventArgs> Eviction => NoOpAsyncEvent<CacheEvictionEventArgs>.Instance;

        public IAsyncEvent<CacheFactoryEventArgs> FactorySuccess => NoOpAsyncEvent<CacheFactoryEventArgs>.Instance;

        public IAsyncEvent<CacheFactoryEventArgs> FactoryError => NoOpAsyncEvent<CacheFactoryEventArgs>.Instance;

        public IAsyncEvent<CacheFactoryEventArgs> FactoryTimeout => NoOpAsyncEvent<CacheFactoryEventArgs>.Instance;

        public IAsyncEvent<CacheFailSafeEventArgs> FailSafeActivation =>
            NoOpAsyncEvent<CacheFailSafeEventArgs>.Instance;

        public IAsyncEvent<CacheRefreshEventArgs> EagerRefresh => NoOpAsyncEvent<CacheRefreshEventArgs>.Instance;

        public IAsyncEvent<CacheRefreshEventArgs> BackgroundRefresh => NoOpAsyncEvent<CacheRefreshEventArgs>.Instance;

        public IAsyncEvent<CacheRemoveAllEventArgs> RemoveAll => NoOpAsyncEvent<CacheRemoveAllEventArgs>.Instance;

        public IAsyncEvent<CacheRemoveByPrefixEventArgs> RemoveByPrefix =>
            NoOpAsyncEvent<CacheRemoveByPrefixEventArgs>.Instance;

        public IAsyncEvent<CacheRemoveByTagEventArgs> RemoveByTag => NoOpAsyncEvent<CacheRemoveByTagEventArgs>.Instance;

        public IAsyncEvent<CacheEventArgs> Clear => NoOpAsyncEvent<CacheEventArgs>.Instance;

        public IAsyncEvent<CacheEventArgs> Flush => NoOpAsyncEvent<CacheEventArgs>.Instance;

        public IAsyncEvent<CacheInvalidationEventArgs> Invalidation =>
            NoOpAsyncEvent<CacheInvalidationEventArgs>.Instance;

        public ICacheMemoryEvents? Memory => null;

        public ICacheDistributedEvents? Distributed => null;

        public bool HasSubscribers => false;

        public CacheEventDispatchStatistics DispatchStatistics => default;
    }
}
