// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class OutboxBus(IBus bus) : IOutboxBus
{
    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new PublishOptions();
        return bus.PublishAsync(contentObj, options with { DeliveryMode = DeliveryMode.Durable }, cancellationToken);
    }
}
