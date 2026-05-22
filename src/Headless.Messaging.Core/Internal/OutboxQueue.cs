// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class OutboxQueue(OutboxPublisher publisher) : IOutboxQueue
{
    public Task EnqueueAsync<T>(
        T? contentObj,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        // TODO(plan-003): persist IntentType on the outbox row; current path delegates to IOutboxPublisher which lacks an IntentType parameter.
        var publishOptions = PublishOptionsAdapter.ToPublishOptions(options);

        return publishOptions?.Delay is { } delay
            ? publisher.PublishDelayAsync(delay, contentObj, publishOptions, IntentType.Queue, cancellationToken)
            : publisher.PublishAsync(contentObj, publishOptions, IntentType.Queue, cancellationToken);
    }
}
