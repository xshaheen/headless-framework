// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class OutboxQueue(IQueue queue) : IOutboxQueue
{
    public Task EnqueueAsync<T>(
        T? contentObj,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new EnqueueOptions();
        return queue.EnqueueAsync(contentObj, options with { DeliveryMode = DeliveryMode.Durable }, cancellationToken);
    }
}
