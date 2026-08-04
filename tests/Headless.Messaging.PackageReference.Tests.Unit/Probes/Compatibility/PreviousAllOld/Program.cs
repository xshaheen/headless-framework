// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Compatibility.PreviousAllOld;

internal static class Program
{
    public static Task PublishDurablyAsync(IOutboxBus bus, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(
            new OrderPlaced("order-123"),
            new PublishOptions { Delay = TimeSpan.FromSeconds(1) },
            cancellationToken
        );
    }

    private sealed record OrderPlaced(string OrderId);
}
