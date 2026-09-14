// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

internal sealed class DeliveryModeTagEnricher : IActivityTagEnricher
{
    public void Enrich(Activity activity, in MessagingEnrichmentContext context)
    {
        if (ToRequestedTagValue(context.RequestedDeliveryMode) is { } requested)
        {
            activity.SetTag(MessagingTags.RequestedDeliveryMode, requested);
        }

        if (ToResolvedTagValue(context.ResolvedDeliveryMode) is { } resolved)
        {
            activity.SetTag(MessagingTags.ResolvedDeliveryMode, resolved);
        }
    }

    internal static string? ToRequestedTagValue(DeliveryMode? mode) =>
        mode switch
        {
            DeliveryMode.Durable => "durable",
            DeliveryMode.Coordinated => "coordinated",
            DeliveryMode.Direct => "direct",
            _ => null,
        };

    // Coordinated is a requested-side strictness that resolves to durable capture; a stored or transported
    // envelope never carries it as the resolved mode, so the resolved tag has only the two finite outcomes.
    internal static string? ToResolvedTagValue(DeliveryMode? mode) =>
        mode switch
        {
            DeliveryMode.Durable => "durable",
            DeliveryMode.Direct => "direct",
            _ => null,
        };
}
