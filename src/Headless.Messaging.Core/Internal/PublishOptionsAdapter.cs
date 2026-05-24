// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

// TODO(plan-003): remove this adapter once the outbox write path accepts enqueue options directly.
internal static class PublishOptionsAdapter
{
    public static PublishOptions? WithoutDelay(PublishOptions? options)
    {
        return options?.Delay is null ? options : options with { Delay = null };
    }

    public static PublishOptions? ToPublishOptions(EnqueueOptions? options, bool includeDelay = true)
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
                Delay = includeDelay ? options.Delay : null,
            };
    }
}
