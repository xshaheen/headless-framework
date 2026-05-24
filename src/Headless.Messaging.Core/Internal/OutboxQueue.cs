// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class OutboxQueue(OutboxMessageWriter publisher) : IOutboxQueue
{
    public Task EnqueueAsync<T>(
        T? contentObj,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return options?.Delay is { } delay
            ? publisher.PublishDelayAsync(delay, contentObj, options, IntentType.Queue, cancellationToken)
            : publisher.PublishAsync(contentObj, options, IntentType.Queue, cancellationToken);
    }
}
