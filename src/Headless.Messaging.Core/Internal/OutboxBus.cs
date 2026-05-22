// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class OutboxBus(OutboxPublisher publisher) : IOutboxBus
{
    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        // TODO(plan-003): persist IntentType on the outbox row; current path delegates to IOutboxPublisher which lacks an IntentType parameter.
        return options?.Delay is { } delay
            ? publisher.PublishDelayAsync(delay, contentObj, options, IntentType.Bus, cancellationToken)
            : publisher.PublishAsync(contentObj, options, IntentType.Bus, cancellationToken);
    }
}
