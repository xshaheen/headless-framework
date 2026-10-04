// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

internal sealed class LaneTagEnricher : IActivityTagEnricher
{
    public void Enrich(Activity activity, in MessagingEnrichmentContext context)
    {
        var (lane, destinationKind) = ToTagValues(context.Lane);
        activity.SetTag(MessagingTags.Lane, lane);
        activity.SetTag(MessagingTags.DestinationKind, destinationKind);
    }

    internal static (string Lane, string DestinationKind) ToTagValues(MessageLane lane)
    {
        return lane switch
        {
            MessageLane.Bus => ("bus", "topic"),
            MessageLane.Queue => ("queue", "queue"),
            _ => ("unknown", "unknown"),
        };
    }
}
