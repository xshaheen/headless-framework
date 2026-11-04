// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;

namespace Tests;

/// <summary>Health of the fake backplane.</summary>
public enum FakeBackplaneState
{
    /// <summary>Publishes succeed and are routed synchronously to every attached cache.</summary>
    Up,

    /// <summary>Publishes throw (transport unreachable) — the failure surfaces to the publisher.</summary>
    Down,

    /// <summary>Publishes succeed but messages are lost (network partition between transport and subscribers).</summary>
    Lossy,
}

/// <summary>
/// Shared in-memory backplane: synchronously routes published <see cref="CacheInvalidationMessage"/>s to every
/// attached cache's invalidation handler (it calls <see cref="HybridCache.HandleInvalidationAsync"/> directly; each
/// cache's InstanceId self-filter drops its own echo). The backplane can be switched
/// <see cref="FakeBackplaneState.Down"/> (throws) or <see cref="FakeBackplaneState.Lossy"/> (silently drops)
/// independently of L2.
/// </summary>
public sealed class FakeBackplane : IHybridCacheBackplane
{
    private readonly List<HybridCache> _subscribers = [];

    public FakeBackplaneState State { get; set; } = FakeBackplaneState.Up;

    public void Attach(HybridCache cache)
    {
        _subscribers.Add(cache);
    }

    public async ValueTask PublishAsync(CacheInvalidationMessage message, CancellationToken cancellationToken)
    {
        switch (State)
        {
            case FakeBackplaneState.Down:
                throw new InvalidOperationException("Backplane is down");
            case FakeBackplaneState.Lossy:
                return;
            case FakeBackplaneState.Up:
            default:
                break;
        }

        foreach (var subscriber in _subscribers)
        {
            await subscriber.HandleInvalidationAsync(message, cancellationToken);
        }
    }
}
