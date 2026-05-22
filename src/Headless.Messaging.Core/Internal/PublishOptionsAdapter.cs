// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

// TODO(plan-003): remove this adapter once IntentType is persisted on the outbox row and OutboxQueue
// no longer needs to flatten EnqueueOptions into PublishOptions to reuse the IOutboxPublisher path.
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
