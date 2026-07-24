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
/// Each <c>On…</c> method checks its event's <see cref="IAsyncEvent{TEvent}.HasHandlers"/> first and constructs the
/// <see cref="EventArgs"/> only when a handler exists, so an unsubscribed event allocates nothing. Handlers are
/// dispatched via <see cref="IAsyncEvent{TEvent}.SafeInvokeAsync"/> — guarded (each handler's exception is caught,
/// logged, and never propagates or stops the others) and, by default, on a background task.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheEventsHub : ICacheEvents
{
    private readonly string _cacheName;
    private readonly CacheTier _tier;
    private readonly bool _sync;
    private readonly Action<Exception> _onHandlerError;

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
        _sync = config?.SyncHandlers ?? false;
        var errorLevel = config?.HandlerErrorLogLevel ?? LogLevel.Warning;
        _onHandlerError = CacheEventDispatch.CreateErrorLogger(logger, errorLevel);

        if (withTierSubHubs)
        {
            MemoryHub = new CacheTierEventsHub(cacheName, CacheTier.L1, _onHandlerError);
            DistributedHub = new CacheTierEventsHub(cacheName, CacheTier.L2, _onHandlerError);
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

    /// <summary>Whether <see cref="Eviction"/> currently has a handler. Lets bulk removal paths stay O(1) when unobserved.</summary>
    public bool HasEvictionSubscribers => _eviction.HasHandlers;

    /// <summary>Whether <see cref="Set"/> currently has a handler. Lets bulk write paths skip their per-key loop when unobserved.</summary>
    public bool HasSetSubscribers => _set.HasHandlers;

    // --- Emitters (raw params; args built only when the specific event has a handler) -------------------------

    /// <summary>Fires <see cref="Hit"/>.</summary>
    public void OnHit(string key, bool isStale)
    {
        if (_hit.HasHandlers)
        {
            _Dispatch(_hit, new CacheHitEventArgs(_cacheName, _tier, key, isStale));
        }
    }

    /// <summary>Fires <see cref="Miss"/>.</summary>
    public void OnMiss(string key)
    {
        if (_miss.HasHandlers)
        {
            _Dispatch(_miss, new CacheKeyEventArgs(_cacheName, _tier, key));
        }
    }

    /// <summary>Fires <see cref="Set"/>.</summary>
    /// <param name="key">The caller-facing key.</param>
    /// <param name="forceBackground">
    /// When <see langword="true"/>, always dispatch on a background task regardless of the sync-handler setting. Used by
    /// the factory-write path, which runs while the per-key factory lock is held.
    /// </param>
    public void OnSet(string key, bool forceBackground = false)
    {
        if (_set.HasHandlers)
        {
            _Dispatch(_set, new CacheKeyEventArgs(_cacheName, _tier, key), forceBackground);
        }
    }

    /// <summary>Fires <see cref="Remove"/>.</summary>
    public void OnRemove(string key)
    {
        if (_remove.HasHandlers)
        {
            _Dispatch(_remove, new CacheKeyEventArgs(_cacheName, _tier, key));
        }
    }

    /// <summary>Fires <see cref="Eviction"/>.</summary>
    public void OnEviction(string key, CacheEvictionReason reason)
    {
        if (_eviction.HasHandlers)
        {
            _Dispatch(_eviction, new CacheEvictionEventArgs(_cacheName, _tier, key, reason));
        }
    }

    // The factory-outcome, fail-safe, and refresh emitters below are called by the FactoryCacheCoordinator while the
    // per-key factory lock is held (or from detached background/eager operations holding their own lock). They always
    // dispatch on a background task, independent of the sync-handler setting, so a handler never runs while the lock is
    // held and a same-key re-entrant handler cannot deadlock.

    /// <summary>Fires the factory-outcome event matching <paramref name="outcome"/> (always on a background task).</summary>
    public void OnFactoryOutcome(string key, CacheFactoryOutcome outcome)
    {
        var @event = outcome switch
        {
            CacheFactoryOutcome.Success => _factorySuccess,
            CacheFactoryOutcome.Error => _factoryError,
            CacheFactoryOutcome.Timeout => _factoryTimeout,
            _ => null,
        };

        if (@event?.HasHandlers == true)
        {
            _Dispatch(@event, new CacheFactoryEventArgs(_cacheName, _tier, key, outcome), forceBackground: true);
        }
    }

    /// <summary>Fires <see cref="FailSafeActivation"/> (always on a background task).</summary>
    public void OnFailSafeActivation(string key, CacheFailSafeTrigger trigger)
    {
        if (_failSafe.HasHandlers)
        {
            _Dispatch(_failSafe, new CacheFailSafeEventArgs(_cacheName, _tier, key, trigger), forceBackground: true);
        }
    }

    /// <summary>Fires <see cref="EagerRefresh"/> (always on a background task).</summary>
    public void OnEagerRefresh(string key, CacheFactoryOutcome outcome)
    {
        if (_eagerRefresh.HasHandlers)
        {
            _Dispatch(
                _eagerRefresh,
                new CacheRefreshEventArgs(_cacheName, _tier, key, CacheRefreshKind.Eager, outcome),
                forceBackground: true
            );
        }
    }

    /// <summary>Fires <see cref="BackgroundRefresh"/> (always on a background task).</summary>
    public void OnBackgroundRefresh(string key, CacheFactoryOutcome outcome)
    {
        if (_backgroundRefresh.HasHandlers)
        {
            _Dispatch(
                _backgroundRefresh,
                new CacheRefreshEventArgs(_cacheName, _tier, key, CacheRefreshKind.Background, outcome),
                forceBackground: true
            );
        }
    }

    /// <summary>Fires <see cref="RemoveAll"/>.</summary>
    public void OnRemoveAll(int removedCount)
    {
        if (_removeAll.HasHandlers)
        {
            _Dispatch(_removeAll, new CacheRemoveAllEventArgs(_cacheName, _tier, removedCount));
        }
    }

    /// <summary>Fires <see cref="RemoveByPrefix"/>.</summary>
    public void OnRemoveByPrefix(string prefix, int removedCount)
    {
        if (_removeByPrefix.HasHandlers)
        {
            _Dispatch(_removeByPrefix, new CacheRemoveByPrefixEventArgs(_cacheName, _tier, prefix, removedCount));
        }
    }

    /// <summary>Fires <see cref="RemoveByTag"/>.</summary>
    public void OnRemoveByTag(string tag)
    {
        if (_removeByTag.HasHandlers)
        {
            _Dispatch(_removeByTag, new CacheRemoveByTagEventArgs(_cacheName, _tier, tag));
        }
    }

    /// <summary>Fires <see cref="Clear"/>.</summary>
    public void OnClear()
    {
        if (_clear.HasHandlers)
        {
            _Dispatch(_clear, new CacheEventArgs(_cacheName, _tier));
        }
    }

    /// <summary>Fires <see cref="Flush"/>.</summary>
    public void OnFlush()
    {
        if (_flush.HasHandlers)
        {
            _Dispatch(_flush, new CacheEventArgs(_cacheName, _tier));
        }
    }

    /// <summary>Fires <see cref="Invalidation"/>.</summary>
    public void OnInvalidation(CacheInvalidationKind kind, CacheInvalidationDirection direction, string? tag = null)
    {
        if (_invalidation.HasHandlers)
        {
            _Dispatch(_invalidation, new CacheInvalidationEventArgs(_cacheName, _tier, kind, direction, tag));
        }
    }

    private void _Dispatch<TArgs>(AsyncEvent<TArgs> @event, TArgs args, bool forceBackground = false)
        where TArgs : EventArgs
    {
        CacheEventDispatch.Dispatch(@event, this, args, sync: _sync && !forceBackground, _onHandlerError);
    }
}

/// <summary>The concrete low-level per-tier (L1/L2) event sub-hub owned by a hybrid cache.</summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheTierEventsHub : ICacheMemoryEvents, ICacheDistributedEvents
{
    private readonly string _cacheName;
    private readonly CacheTier _tier;
    private readonly Action<Exception> _onHandlerError;
    private readonly AsyncEvent<CacheKeyEventArgs> _hit = new();
    private readonly AsyncEvent<CacheKeyEventArgs> _miss = new();

    internal CacheTierEventsHub(string cacheName, CacheTier tier, Action<Exception> onHandlerError)
    {
        _cacheName = cacheName;
        _tier = tier;
        _onHandlerError = onHandlerError;
    }

    /// <inheritdoc cref="ICacheMemoryEvents.Hit" />
    public IAsyncEvent<CacheKeyEventArgs> Hit => _hit;

    /// <inheritdoc cref="ICacheMemoryEvents.Miss" />
    public IAsyncEvent<CacheKeyEventArgs> Miss => _miss;

    /// <summary>Whether either tier event currently has a handler.</summary>
    public bool HasHandlers => _hit.HasHandlers || _miss.HasHandlers;

    // Per-tier reads are emitted during the coordinator's store reads, which may run under the per-key factory lock, so
    // they always dispatch on a background task (deadlock-safe), matching the coordinator's own events.

    /// <summary>Fires <see cref="Hit"/>.</summary>
    public void OnHit(string key)
    {
        if (_hit.HasHandlers)
        {
            CacheEventDispatch.Dispatch(
                _hit,
                this,
                new CacheKeyEventArgs(_cacheName, _tier, key),
                sync: false,
                _onHandlerError
            );
        }
    }

    /// <summary>Fires <see cref="Miss"/>.</summary>
    public void OnMiss(string key)
    {
        if (_miss.HasHandlers)
        {
            CacheEventDispatch.Dispatch(
                _miss,
                this,
                new CacheKeyEventArgs(_cacheName, _tier, key),
                sync: false,
                _onHandlerError
            );
        }
    }
}

/// <summary>Guarded, background-by-default dispatch of cache-event handlers onto an <see cref="AsyncEvent{TEvent}"/>.</summary>
internal static class CacheEventDispatch
{
    public static void Dispatch<TArgs>(
        AsyncEvent<TArgs> @event,
        object sender,
        TArgs args,
        bool sync,
        Action<Exception> onHandlerError
    )
        where TArgs : EventArgs
    {
        // SafeInvokeAsync runs every handler guarded (faults isolated via onHandlerError, never propagated), so the
        // fire-and-forget task can never fault. Sync mode runs sync handlers inline; async handlers and the background
        // mode continue on a task. AsTask() gives a discardable Task (a completed one allocates nothing).
        if (sync)
        {
            _ = @event.SafeInvokeAsync(sender, args, onHandlerError).AsTask();
        }
        else
        {
            _ = Task.Run(() => @event.SafeInvokeAsync(sender, args, onHandlerError).AsTask());
        }
    }

    public static Action<Exception> CreateErrorLogger(ILogger? logger, LogLevel errorLevel)
    {
        if (logger is null)
        {
            return static _ => { };
        }

        return exception =>
        {
            if (logger.IsEnabled(errorLevel))
            {
                logger.Log(
                    errorLevel,
                    exception,
                    "An exception was thrown by a cache event handler and was suppressed."
                );
            }
        };
    }
}
