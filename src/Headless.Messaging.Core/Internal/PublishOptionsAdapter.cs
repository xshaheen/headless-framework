// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

internal static class PublishOptionsAdapter
{
    public static PublishOptions? ToPublishOptions(EnqueueOptions? options)
    {
        return options is null
            ? null
            : new PublishOptions
            {
                Topic = options.Topic,
                Headers = options.Headers,
                MessageId = options.MessageId,
                CorrelationId = options.CorrelationId,
                CorrelationSequence = options.CorrelationSequence,
                CallbackName = options.CallbackName,
                TenantId = options.TenantId,
                Delay = options.Delay,
            };
    }
}
