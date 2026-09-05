// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Compatibility.NewAllNew;

internal static class Program
{
    public static async Task SendAsync(IBus bus, IQueue queue, CancellationToken cancellationToken)
    {
        await bus.PublishAsync(
            new OrderPlaced("order-123"),
            new PublishOptions { DeliveryMode = DeliveryMode.Durable, Delay = TimeSpan.FromSeconds(1) },
            cancellationToken
        );
        await queue.EnqueueAsync(
            new RebuildProjection("order-123"),
            new QueueOptions { DeliveryMode = DeliveryMode.TransportDirect },
            cancellationToken
        );
    }

    public static MessageLane BusLane => MessageLane.Bus;

    private sealed record OrderPlaced(string OrderId);

    private sealed record RebuildProjection(string OrderId);
}
