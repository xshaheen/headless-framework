// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class OutboxBus(OutboxMessageWriter publisher) : IOutboxBus
{
    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return options?.Delay is { } delay
            ? publisher.PublishDelayAsync(delay, contentObj, options, IntentType.Bus, cancellationToken)
            : publisher.PublishAsync(contentObj, options, IntentType.Bus, cancellationToken);
    }
}
