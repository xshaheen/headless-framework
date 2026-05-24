// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.OpenTelemetry.Internal;

internal sealed class IntentTagEnricher : IActivityTagEnricher
{
    public ValueTask Enrich(
        Activity activity,
        in MessagingEnrichmentContext context,
        CancellationToken cancellationToken = default
    )
    {
        switch (context.IntentType)
        {
            case IntentType.Bus:
                activity.SetTag(MessagingTags.Intent, "bus");
                activity.SetTag(MessagingTags.DestinationKind, "topic");
                break;

            case IntentType.Queue:
                activity.SetTag(MessagingTags.Intent, "queue");
                activity.SetTag(MessagingTags.DestinationKind, "queue");
                break;
        }

        return ValueTask.CompletedTask;
    }
}
