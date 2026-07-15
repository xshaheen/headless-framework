// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;

namespace Tests;

/// <summary>Health of the fake backplane bus.</summary>
public enum FakeBackplaneState
{
    /// <summary>Publishes succeed and are routed synchronously to every attached cache.</summary>
    Up,

    /// <summary>Publishes throw (broker unreachable) — the failure surfaces to the publisher.</summary>
    Down,

    /// <summary>Publishes succeed but messages are lost (network partition between broker and subscribers).</summary>
    Lossy,
}

/// <summary>
/// Shared in-memory backplane: synchronously routes published <see cref="CacheInvalidationMessage"/>s to every
/// attached cache's invalidation handler (the consumer is not auto-registered, so the bus calls
/// <see cref="HybridCache.HandleInvalidationAsync"/> directly; each cache's InstanceId self-filter drops its
/// own echo). The bus can be switched <see cref="FakeBackplaneState.Down"/> (throws) or
/// <see cref="FakeBackplaneState.Lossy"/> (silently drops) independently of L2.
/// </summary>
public sealed class FakeBackplaneBus : IBus
{
    private readonly List<HybridCache> _subscribers = [];

    public FakeBackplaneState State { get; set; } = FakeBackplaneState.Up;

    public void Attach(HybridCache cache)
    {
        _subscribers.Add(cache);
    }

    public async Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
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

        if (contentObj is not CacheInvalidationMessage message)
        {
            return;
        }

        foreach (var subscriber in _subscribers)
        {
            await subscriber.HandleInvalidationAsync(message, cancellationToken);
        }
    }
}
