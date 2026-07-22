// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// The concrete <see cref="ICacheEvents"/> implementation owned by a cache provider. Providers construct one hub,
/// return it from <see cref="ICache.Events"/>, and fire events through the <c>On…</c> methods (which are called by the
/// provider and by the shared <see cref="FactoryCacheCoordinator"/>).
/// </summary>
/// <remarks>
/// Each <c>On…</c> method checks its event's subscriber first and constructs the <see cref="EventArgs"/> only when a
/// handler exists, so an unsubscribed event allocates nothing. Handlers are dispatched via
/// <see cref="CacheEventDispatch"/>: guarded (exceptions caught and logged) and, by default, on a background task.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheEventsHub : ICacheEvents
{
    private readonly string _cacheName;
    private readonly CacheTier _tier;
    private readonly bool _sync;
    private readonly ILogger? _logger;
    private readonly LogLevel _errorLevel;

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
        _logger = logger;
        _errorLevel = config?.HandlerErrorLogLevel ?? LogLevel.Warning;

        if (withTierSubHubs)
        {
            MemoryHub = new CacheTierEventsHub(cacheName, CacheTier.L1, _sync, logger, _errorLevel);
            DistributedHub = new CacheTierEventsHub(cacheName, CacheTier.L2, _sync, logger, _errorLevel);
        }
    }

    /// <inheritdoc />
    public event EventHandler<CacheHitEventArgs>? Hit;

    /// <inheritdoc />
    public event EventHandler<CacheKeyEventArgs>? Miss;

    /// <inheritdoc />
    public event EventHandler<CacheKeyEventArgs>? Set;

    /// <inheritdoc />
    public event EventHandler<CacheKeyEventArgs>? Remove;

    /// <inheritdoc />
    public event EventHandler<CacheEvictionEventArgs>? Eviction;

    /// <inheritdoc />
    public event EventHandler<CacheFactoryEventArgs>? FactorySuccess;

    /// <inheritdoc />
    public event EventHandler<CacheFactoryEventArgs>? FactoryError;

    /// <inheritdoc />
    public event EventHandler<CacheFactoryEventArgs>? FactoryTimeout;

    /// <inheritdoc />
    public event EventHandler<CacheFailSafeEventArgs>? FailSafeActivation;

    /// <inheritdoc />
    public event EventHandler<CacheRefreshEventArgs>? EagerRefresh;

    /// <inheritdoc />
    public event EventHandler<CacheRefreshEventArgs>? BackgroundRefresh;

    /// <inheritdoc />
    public event EventHandler<CacheRemoveAllEventArgs>? RemoveAll;

    /// <inheritdoc />
    public event EventHandler<CacheRemoveByPrefixEventArgs>? RemoveByPrefix;

    /// <inheritdoc />
    public event EventHandler<CacheRemoveByTagEventArgs>? RemoveByTag;

    /// <inheritdoc />
    public event EventHandler<CacheEventArgs>? Clear;

    /// <inheritdoc />
    public event EventHandler<CacheEventArgs>? Flush;

    /// <inheritdoc />
    public event EventHandler<CacheInvalidationEventArgs>? Invalidation;

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
        Hit is not null
        || Miss is not null
        || Set is not null
        || Remove is not null
        || Eviction is not null
        || FactorySuccess is not null
        || FactoryError is not null
        || FactoryTimeout is not null
        || FailSafeActivation is not null
        || EagerRefresh is not null
        || BackgroundRefresh is not null
        || RemoveAll is not null
        || RemoveByPrefix is not null
        || RemoveByTag is not null
        || Clear is not null
        || Flush is not null
        || Invalidation is not null
        || (MemoryHub?.HasSubscribers ?? false)
        || (DistributedHub?.HasSubscribers ?? false);

    /// <inheritdoc />
    public bool HasEvictionSubscribers => Eviction is not null;

    // --- Emitters (raw params; args built only when the specific event has a subscriber) -----------------------

    /// <summary>Fires <see cref="Hit"/>.</summary>
    public void OnHit(string key, bool isStale)
    {
        var handler = Hit;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheHitEventArgs(_cacheName, _tier, key, isStale));
    }

    /// <summary>Fires <see cref="Miss"/>.</summary>
    public void OnMiss(string key)
    {
        var handler = Miss;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheKeyEventArgs(_cacheName, _tier, key));
    }

    /// <summary>Fires <see cref="Set"/>.</summary>
    /// <param name="key">The caller-facing key.</param>
    /// <param name="forceBackground">
    /// When <see langword="true"/>, always dispatch on a background task regardless of the sync-handler setting. Used
    /// by the factory-write path, which runs while the per-key factory lock is held.
    /// </param>
    public void OnSet(string key, bool forceBackground = false)
    {
        var handler = Set;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheKeyEventArgs(_cacheName, _tier, key), forceBackground);
    }

    /// <summary>Fires <see cref="Remove"/>.</summary>
    public void OnRemove(string key)
    {
        var handler = Remove;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheKeyEventArgs(_cacheName, _tier, key));
    }

    /// <summary>Fires <see cref="Eviction"/>.</summary>
    public void OnEviction(string key, CacheEvictionReason reason)
    {
        var handler = Eviction;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheEvictionEventArgs(_cacheName, _tier, key, reason));
    }

    // The factory-outcome, fail-safe, and refresh emitters below are called by the FactoryCacheCoordinator while the
    // per-key factory lock is held (or from detached background/eager operations holding their own lock). They always
    // dispatch on a background task, independent of the sync-handler setting, so a handler never runs while the lock
    // is held and a same-key re-entrant handler cannot deadlock. See the ordering note on ICacheEvents.

    /// <summary>Fires the factory-outcome event matching <paramref name="outcome"/> (always on a background task).</summary>
    public void OnFactoryOutcome(string key, CacheFactoryOutcome outcome)
    {
        var handler = outcome switch
        {
            CacheFactoryOutcome.Success => FactorySuccess,
            CacheFactoryOutcome.Error => FactoryError,
            CacheFactoryOutcome.Timeout => FactoryTimeout,
            _ => null,
        };

        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheFactoryEventArgs(_cacheName, _tier, key, outcome), forceBackground: true);
    }

    /// <summary>Fires <see cref="FailSafeActivation"/> (always on a background task).</summary>
    public void OnFailSafeActivation(string key, CacheFailSafeTrigger trigger)
    {
        var handler = FailSafeActivation;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheFailSafeEventArgs(_cacheName, _tier, key, trigger), forceBackground: true);
    }

    /// <summary>Fires <see cref="EagerRefresh"/> (always on a background task).</summary>
    public void OnEagerRefresh(string key, CacheFactoryOutcome outcome)
    {
        var handler = EagerRefresh;
        if (handler is null)
        {
            return;
        }

        _Dispatch(
            handler,
            new CacheRefreshEventArgs(_cacheName, _tier, key, CacheRefreshKind.Eager, outcome),
            forceBackground: true
        );
    }

    /// <summary>Fires <see cref="BackgroundRefresh"/> (always on a background task).</summary>
    public void OnBackgroundRefresh(string key, CacheFactoryOutcome outcome)
    {
        var handler = BackgroundRefresh;
        if (handler is null)
        {
            return;
        }

        _Dispatch(
            handler,
            new CacheRefreshEventArgs(_cacheName, _tier, key, CacheRefreshKind.Background, outcome),
            forceBackground: true
        );
    }

    /// <summary>Fires <see cref="RemoveAll"/>.</summary>
    public void OnRemoveAll(int removedCount)
    {
        var handler = RemoveAll;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheRemoveAllEventArgs(_cacheName, _tier, removedCount));
    }

    /// <summary>Fires <see cref="RemoveByPrefix"/>.</summary>
    public void OnRemoveByPrefix(string prefix, int removedCount)
    {
        var handler = RemoveByPrefix;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheRemoveByPrefixEventArgs(_cacheName, _tier, prefix, removedCount));
    }

    /// <summary>Fires <see cref="RemoveByTag"/>.</summary>
    public void OnRemoveByTag(string tag)
    {
        var handler = RemoveByTag;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheRemoveByTagEventArgs(_cacheName, _tier, tag));
    }

    /// <summary>Fires <see cref="Clear"/>.</summary>
    public void OnClear()
    {
        var handler = Clear;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheEventArgs(_cacheName, _tier));
    }

    /// <summary>Fires <see cref="Flush"/>.</summary>
    public void OnFlush()
    {
        var handler = Flush;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheEventArgs(_cacheName, _tier));
    }

    /// <summary>Fires <see cref="Invalidation"/>.</summary>
    public void OnInvalidation(CacheInvalidationKind kind, CacheInvalidationDirection direction, string? tag = null)
    {
        var handler = Invalidation;
        if (handler is null)
        {
            return;
        }

        _Dispatch(handler, new CacheInvalidationEventArgs(_cacheName, _tier, kind, direction, tag));
    }

    private void _Dispatch<TArgs>(EventHandler<TArgs> handler, TArgs args, bool forceBackground = false)
        where TArgs : EventArgs
    {
        CacheEventDispatch.SafeExecute(handler, this, args, sync: _sync && !forceBackground, _logger, _errorLevel);
    }
}

/// <summary>The concrete low-level per-tier (L1/L2) event sub-hub owned by a hybrid cache.</summary>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CacheTierEventsHub(
    string cacheName,
    CacheTier tier,
    bool syncHandlers,
    ILogger? logger,
    LogLevel errorLevel
) : ICacheMemoryEvents, ICacheDistributedEvents
{
    /// <inheritdoc cref="ICacheMemoryEvents.Hit" />
    public event EventHandler<CacheKeyEventArgs>? Hit;

    /// <inheritdoc cref="ICacheMemoryEvents.Miss" />
    public event EventHandler<CacheKeyEventArgs>? Miss;

    /// <summary>Whether either tier event currently has a subscriber.</summary>
    public bool HasSubscribers => Hit is not null || Miss is not null;

    /// <summary>Fires <see cref="Hit"/>.</summary>
    public void OnHit(string key)
    {
        var handler = Hit;
        if (handler is null)
        {
            return;
        }

        CacheEventDispatch.SafeExecute(
            handler,
            this,
            new CacheKeyEventArgs(cacheName, tier, key),
            syncHandlers,
            logger,
            errorLevel
        );
    }

    /// <summary>Fires <see cref="Miss"/>.</summary>
    public void OnMiss(string key)
    {
        var handler = Miss;
        if (handler is null)
        {
            return;
        }

        CacheEventDispatch.SafeExecute(
            handler,
            this,
            new CacheKeyEventArgs(cacheName, tier, key),
            syncHandlers,
            logger,
            errorLevel
        );
    }
}

/// <summary>Guarded, background-by-default dispatch of cache-event handlers.</summary>
internal static class CacheEventDispatch
{
    public static void SafeExecute<TArgs>(
        EventHandler<TArgs> handler,
        object sender,
        TArgs args,
        bool sync,
        ILogger? logger,
        LogLevel errorLevel
    )
        where TArgs : EventArgs
    {
        if (sync)
        {
            _Invoke(handler, sender, args, logger, errorLevel);
        }
        else
        {
            _ = Task.Run(() => _Invoke(handler, sender, args, logger, errorLevel));
        }
    }

    private static void _Invoke<TArgs>(
        EventHandler<TArgs> handler,
        object sender,
        TArgs args,
        ILogger? logger,
        LogLevel errorLevel
    )
        where TArgs : EventArgs
    {
        // Only test the log level once, ahead of the loop.
        if (logger is not null && !logger.IsEnabled(errorLevel))
        {
            logger = null;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)invocation)(sender, args);
            }
            catch (Exception exception)
            {
                logger?.Log(
                    errorLevel,
                    exception,
                    "An exception was thrown by a cache event handler and was suppressed."
                );
            }
        }
    }
}
