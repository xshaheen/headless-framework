// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Primitives;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

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
