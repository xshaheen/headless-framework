// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Primitives;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// The concrete <see cref="ICacheEvents"/> implementation owned by a cache provider. Providers construct one hub, return
/// it from <see cref="ICache.Events"/>, and fire events through the <c>On…</c> methods (called by the provider and by the
/// shared <see cref="FactoryCacheCoordinator"/>).
/// </summary>
/// <remarks>
/// Each <c>On…</c> method captures the event's current handler snapshot first and constructs the
/// <see cref="EventArgs"/> only when that snapshot is non-empty, so an unsubscribed event allocates nothing. Handler
/// snapshots are accepted into one bounded, non-blocking FIFO shared by the root and tier sub-hubs.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheEventsHub : ICacheEvents, IDisposable, IAsyncDisposable
{
    private readonly string _cacheName;
    private readonly CacheTier _tier;
    private readonly CacheEventDispatcher _dispatcher;

    private readonly AsyncEvent<CacheHitEventArgs> _hit = new();
    private readonly AsyncEvent<CacheKeyEventArgs> _miss = new();
    private readonly AsyncEvent<CacheKeyEventArgs> _set = new();
    private readonly AsyncEvent<CacheKeyEventArgs> _remove = new();
    private readonly AsyncEvent<CacheEvictionEventArgs> _eviction = new();
    private readonly AsyncEvent<CacheFactoryEventArgs> _factorySuccess = new();
    private readonly AsyncEvent<CacheFactoryEventArgs> _factoryError = new();
    private readonly AsyncEvent<CacheFactoryEventArgs> _factoryTimeout = new();
    private readonly AsyncEvent<CacheFailSafeEventArgs> _failSafe = new();
    private readonly AsyncEvent<CacheRefreshEventArgs> _eagerRefresh = new();
    private readonly AsyncEvent<CacheRefreshEventArgs> _backgroundRefresh = new();
    private readonly AsyncEvent<CacheRemoveAllEventArgs> _removeAll = new();
    private readonly AsyncEvent<CacheRemoveByPrefixEventArgs> _removeByPrefix = new();
    private readonly AsyncEvent<CacheRemoveByTagEventArgs> _removeByTag = new();
    private readonly AsyncEvent<CacheEventArgs> _clear = new();
    private readonly AsyncEvent<CacheEventArgs> _flush = new();
    private readonly AsyncEvent<CacheInvalidationEventArgs> _invalidation = new();

    /// <summary>Creates a hub for a cache instance.</summary>
    /// <param name="cacheName">The instance name surfaced on <see cref="CacheEventArgs.CacheName"/>.</param>
    /// <param name="tier">The tier of the owning cache, surfaced on <see cref="CacheEventArgs.Tier"/>.</param>
    /// <param name="config">Handler-execution configuration; defaults are used when <see langword="null"/>.</param>
    /// <param name="logger">Logger for guarded-handler exceptions.</param>
    /// <param name="withTierSubHubs">When <see langword="true"/> (hybrid), exposes the L1/L2 <see cref="Memory"/> and <see cref="Distributed"/> sub-hubs.</param>
    public CacheEventsHub(
        string cacheName,
        CacheTier tier,
        CacheEventsConfig? config = null,
        ILogger? logger = null,
        bool withTierSubHubs = false
    )
    {
        _cacheName = cacheName;
        _tier = tier;
        _dispatcher = new CacheEventDispatcher(cacheName, config ?? new CacheEventsConfig(), logger);

        if (withTierSubHubs)
        {
            MemoryHub = new CacheTierEventsHub(cacheName, CacheTier.L1, _dispatcher);
            DistributedHub = new CacheTierEventsHub(cacheName, CacheTier.L2, _dispatcher);
        }
    }

    /// <inheritdoc />
    public IAsyncEvent<CacheHitEventArgs> Hit => _hit;

    /// <inheritdoc />
    public IAsyncEvent<CacheKeyEventArgs> Miss => _miss;

    /// <inheritdoc />
    public IAsyncEvent<CacheKeyEventArgs> Set => _set;

    /// <inheritdoc />
    public IAsyncEvent<CacheKeyEventArgs> Remove => _remove;

    /// <inheritdoc />
    public IAsyncEvent<CacheEvictionEventArgs> Eviction => _eviction;

    /// <inheritdoc />
    public IAsyncEvent<CacheFactoryEventArgs> FactorySuccess => _factorySuccess;

    /// <inheritdoc />
    public IAsyncEvent<CacheFactoryEventArgs> FactoryError => _factoryError;

    /// <inheritdoc />
    public IAsyncEvent<CacheFactoryEventArgs> FactoryTimeout => _factoryTimeout;

    /// <inheritdoc />
    public IAsyncEvent<CacheFailSafeEventArgs> FailSafeActivation => _failSafe;

    /// <inheritdoc />
    public IAsyncEvent<CacheRefreshEventArgs> EagerRefresh => _eagerRefresh;

    /// <inheritdoc />
    public IAsyncEvent<CacheRefreshEventArgs> BackgroundRefresh => _backgroundRefresh;

    /// <inheritdoc />
    public IAsyncEvent<CacheRemoveAllEventArgs> RemoveAll => _removeAll;

    /// <inheritdoc />
    public IAsyncEvent<CacheRemoveByPrefixEventArgs> RemoveByPrefix => _removeByPrefix;

    /// <inheritdoc />
    public IAsyncEvent<CacheRemoveByTagEventArgs> RemoveByTag => _removeByTag;

    /// <inheritdoc />
    public IAsyncEvent<CacheEventArgs> Clear => _clear;

    /// <inheritdoc />
    public IAsyncEvent<CacheEventArgs> Flush => _flush;

    /// <inheritdoc />
    public IAsyncEvent<CacheInvalidationEventArgs> Invalidation => _invalidation;

    /// <summary>The concrete memory sub-hub used by the provider to emit L1 events (null for single-tier caches).</summary>
    public CacheTierEventsHub? MemoryHub { get; }

    /// <summary>The concrete distributed sub-hub used by the provider to emit L2 events (null for single-tier caches).</summary>
    public CacheTierEventsHub? DistributedHub { get; }

    /// <inheritdoc />
    public ICacheMemoryEvents? Memory => MemoryHub;

    /// <inheritdoc />
    public ICacheDistributedEvents? Distributed => DistributedHub;

    /// <inheritdoc />
    public bool HasSubscribers =>
        _hit.HasHandlers
        || _miss.HasHandlers
        || _set.HasHandlers
        || _remove.HasHandlers
        || _eviction.HasHandlers
        || _factorySuccess.HasHandlers
        || _factoryError.HasHandlers
        || _factoryTimeout.HasHandlers
        || _failSafe.HasHandlers
        || _eagerRefresh.HasHandlers
        || _backgroundRefresh.HasHandlers
        || _removeAll.HasHandlers
        || _removeByPrefix.HasHandlers
        || _removeByTag.HasHandlers
        || _clear.HasHandlers
        || _flush.HasHandlers
        || _invalidation.HasHandlers
        || (MemoryHub?.HasHandlers ?? false)
        || (DistributedHub?.HasHandlers ?? false);

    /// <inheritdoc />
    public CacheEventDispatchStatistics DispatchStatistics => _dispatcher.Statistics;

    /// <summary>Whether <see cref="Eviction"/> currently has a handler. Lets bulk removal paths stay O(1) when unobserved.</summary>
    public bool HasEvictionSubscribers => _eviction.HasHandlers;

    /// <summary>Whether <see cref="Set"/> currently has a handler. Lets bulk write paths skip their per-key loop when unobserved.</summary>
    public bool HasSetSubscribers => _set.HasHandlers;

    // --- Emitters (raw params; args built only when the specific event has a handler) -------------------------

    /// <summary>Fires <see cref="Hit"/>.</summary>
    public void OnHit(string key, bool isStale)
    {
        var handlerSnapshot = _Capture(_hit);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheHitEventArgs(_cacheName, _tier, key, isStale));
        }
    }

    /// <summary>Fires <see cref="Miss"/>.</summary>
    public void OnMiss(string key)
    {
        var handlerSnapshot = _Capture(_miss);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheKeyEventArgs(_cacheName, _tier, key));
        }
    }

    /// <summary>Fires <see cref="Set"/>.</summary>
    public void OnSet(string key)
    {
        var handlerSnapshot = _Capture(_set);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheKeyEventArgs(_cacheName, _tier, key));
        }
    }

    /// <summary>Fires <see cref="Remove"/>.</summary>
    public void OnRemove(string key)
    {
        var handlerSnapshot = _Capture(_remove);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheKeyEventArgs(_cacheName, _tier, key));
        }
    }

    /// <summary>Fires <see cref="Eviction"/>.</summary>
    public void OnEviction(string key, CacheEvictionReason reason)
    {
        var handlerSnapshot = _Capture(_eviction);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheEvictionEventArgs(_cacheName, _tier, key, reason));
        }
    }

    // The factory-outcome, fail-safe, and refresh emitters below can run under the per-key factory lock. The shared
    // dispatcher ensures no handler ever runs on that producer thread.

    /// <summary>Fires the factory-outcome event matching <paramref name="outcome"/>.</summary>
    public void OnFactoryOutcome(string key, CacheFactoryOutcome outcome)
    {
        var @event = outcome switch
        {
            CacheFactoryOutcome.Success => _factorySuccess,
            CacheFactoryOutcome.Error => _factoryError,
            CacheFactoryOutcome.Timeout => _factoryTimeout,
            _ => null,
        };

        if (@event is null)
        {
            return;
        }

        var handlerSnapshot = _Capture(@event);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheFactoryEventArgs(_cacheName, _tier, key, outcome));
        }
    }

    /// <summary>Fires <see cref="FailSafeActivation"/>.</summary>
    public void OnFailSafeActivation(string key, CacheFailSafeTrigger trigger)
    {
        var handlerSnapshot = _Capture(_failSafe);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheFailSafeEventArgs(_cacheName, _tier, key, trigger));
        }
    }

    /// <summary>Fires <see cref="EagerRefresh"/>.</summary>
    public void OnEagerRefresh(string key, CacheFactoryOutcome outcome)
    {
        var handlerSnapshot = _Capture(_eagerRefresh);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(
                handlerSnapshot,
                this,
                new CacheRefreshEventArgs(_cacheName, _tier, key, CacheRefreshKind.Eager, outcome)
            );
        }
    }

    /// <summary>Fires <see cref="BackgroundRefresh"/>.</summary>
    public void OnBackgroundRefresh(string key, CacheFactoryOutcome outcome)
    {
        var handlerSnapshot = _Capture(_backgroundRefresh);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(
                handlerSnapshot,
                this,
                new CacheRefreshEventArgs(_cacheName, _tier, key, CacheRefreshKind.Background, outcome)
            );
        }
    }

    /// <summary>Fires <see cref="RemoveAll"/>.</summary>
    public void OnRemoveAll(int removedCount)
    {
        var handlerSnapshot = _Capture(_removeAll);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheRemoveAllEventArgs(_cacheName, _tier, removedCount));
        }
    }

    /// <summary>Fires <see cref="RemoveByPrefix"/>.</summary>
    public void OnRemoveByPrefix(string prefix, int removedCount)
    {
        var handlerSnapshot = _Capture(_removeByPrefix);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(
                handlerSnapshot,
                this,
                new CacheRemoveByPrefixEventArgs(_cacheName, _tier, prefix, removedCount)
            );
        }
    }

    /// <summary>Fires <see cref="RemoveByTag"/>.</summary>
    public void OnRemoveByTag(string tag)
    {
        var handlerSnapshot = _Capture(_removeByTag);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheRemoveByTagEventArgs(_cacheName, _tier, tag));
        }
    }

    /// <summary>Fires <see cref="Clear"/>.</summary>
    public void OnClear()
    {
        var handlerSnapshot = _Capture(_clear);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheEventArgs(_cacheName, _tier));
        }
    }

    /// <summary>Fires <see cref="Flush"/>.</summary>
    public void OnFlush()
    {
        var handlerSnapshot = _Capture(_flush);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheEventArgs(_cacheName, _tier));
        }
    }

    /// <summary>Fires <see cref="Invalidation"/>.</summary>
    public void OnInvalidation(CacheInvalidationKind kind, CacheInvalidationDirection direction, string? tag = null)
    {
        var handlerSnapshot = _Capture(_invalidation);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(
                handlerSnapshot,
                this,
                new CacheInvalidationEventArgs(_cacheName, _tier, kind, direction, tag)
            );
        }
    }

    /// <summary>Waits until every currently accepted signal finishes.</summary>
    public ValueTask DrainAsync(CancellationToken cancellationToken = default) =>
        _dispatcher.WaitForIdleAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        _dispatcher.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dispatcher.DisposeAsync();

    private static object? _Capture<TArgs>(AsyncEvent<TArgs> @event)
        where TArgs : EventArgs
    {
        var handlerSnapshot = @event.CaptureHandlerSnapshot();

        return AsyncEvent<TArgs>.IsEmptyHandlerSnapshot(handlerSnapshot) ? null : handlerSnapshot;
    }
}

/// <summary>The concrete low-level per-tier (L1/L2) event sub-hub owned by a hybrid cache.</summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheTierEventsHub : ICacheMemoryEvents, ICacheDistributedEvents
{
    private readonly string _cacheName;
    private readonly CacheTier _tier;
    private readonly CacheEventDispatcher _dispatcher;
    private readonly AsyncEvent<CacheKeyEventArgs> _hit = new();
    private readonly AsyncEvent<CacheKeyEventArgs> _miss = new();

    internal CacheTierEventsHub(string cacheName, CacheTier tier, CacheEventDispatcher dispatcher)
    {
        _cacheName = cacheName;
        _tier = tier;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc cref="ICacheMemoryEvents.Hit" />
    public IAsyncEvent<CacheKeyEventArgs> Hit => _hit;

    /// <inheritdoc cref="ICacheMemoryEvents.Miss" />
    public IAsyncEvent<CacheKeyEventArgs> Miss => _miss;

    /// <summary>Whether either tier event currently has a handler.</summary>
    public bool HasHandlers => _hit.HasHandlers || _miss.HasHandlers;

    // Per-tier reads can be emitted while holding the per-key factory lock; the shared FIFO keeps handlers off-thread.

    /// <summary>Fires <see cref="Hit"/>.</summary>
    public void OnHit(string key)
    {
        var handlerSnapshot = _Capture(_hit);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheKeyEventArgs(_cacheName, _tier, key));
        }
    }

    /// <summary>Fires <see cref="Miss"/>.</summary>
    public void OnMiss(string key)
    {
        var handlerSnapshot = _Capture(_miss);

        if (handlerSnapshot is not null)
        {
            _dispatcher.Dispatch(handlerSnapshot, this, new CacheKeyEventArgs(_cacheName, _tier, key));
        }
    }

    private static object? _Capture<TArgs>(AsyncEvent<TArgs> @event)
        where TArgs : EventArgs
    {
        var handlerSnapshot = @event.CaptureHandlerSnapshot();

        return AsyncEvent<TArgs>.IsEmptyHandlerSnapshot(handlerSnapshot) ? null : handlerSnapshot;
    }
}
