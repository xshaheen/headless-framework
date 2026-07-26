// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

internal sealed class DeliveryModeTagEnricher : IActivityTagEnricher
{
    public void Enrich(Activity activity, in MessagingEnrichmentContext context)
    {
        if (_ToTagValue(context.RequestedDeliveryMode) is { } requested)
        {
            activity.SetTag(MessagingTags.RequestedDeliveryMode, requested);
        }

        if (_ToTagValue(context.ResolvedDeliveryMode) is { } resolved)
        {
            activity.SetTag(MessagingTags.ResolvedDeliveryMode, resolved);
        }
    }

    internal static string? ToTagValue(DeliveryMode? mode) => _ToTagValue(mode);

    private static string? _ToTagValue(DeliveryMode? mode) =>
        mode switch
        {
            DeliveryMode.Auto => "auto",
            DeliveryMode.Durable => "durable",
            DeliveryMode.TransportDirect => "transport_direct",
            _ => null,
        };
}
