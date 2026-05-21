// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal sealed class OutboxQueue(IOutboxPublisher publisher, IScheduledPublisher scheduledPublisher) : IOutboxQueue
{
    public Task EnqueueAsync<T>(
        T? contentObj,
        EnqueueOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var publishOptions = PublishOptionsAdapter.ToPublishOptions(options);

        return publishOptions?.Delay is { } delay
            ? scheduledPublisher.PublishDelayAsync(delay, contentObj, publishOptions, cancellationToken)
            : publisher.PublishAsync(contentObj, publishOptions, cancellationToken);
    }
}
