// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class OutboxBus(IOutboxPublisher publisher, IScheduledPublisher scheduledPublisher) : IOutboxBus
{
    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return options?.Delay is { } delay
            ? scheduledPublisher.PublishDelayAsync(delay, contentObj, options, cancellationToken)
            : publisher.PublishAsync(contentObj, options, cancellationToken);
    }
}
