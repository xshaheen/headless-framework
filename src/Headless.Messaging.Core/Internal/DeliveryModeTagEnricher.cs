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

    internal static string? ToRequestedTagValue(DeliveryMode? mode) => _ToTagValue(mode);

    internal static string? ToResolvedTagValue(DeliveryMode? mode) => _ToTagValue(mode);

    private static string? _ToTagValue(DeliveryMode? mode) =>
        mode switch
        {
            DeliveryMode.Durable => "durable",
            DeliveryMode.Direct => "direct",
            _ => null,
        };
}
