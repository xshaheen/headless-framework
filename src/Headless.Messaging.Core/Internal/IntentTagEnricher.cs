// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

internal sealed class IntentTagEnricher : IActivityTagEnricher
{
    public void Enrich(Activity activity, in MessagingEnrichmentContext context)
    {
        switch (context.Lane)
        {
            case MessageLane.Bus:
                activity.SetTag(MessagingTags.Intent, "bus");
                activity.SetTag(MessagingTags.DestinationKind, "topic");
                break;

            case MessageLane.Queue:
                activity.SetTag(MessagingTags.Intent, "queue");
                activity.SetTag(MessagingTags.DestinationKind, "queue");
                break;
        }
    }
}
