// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.OpenTelemetry.Internal;

internal sealed class TenantIdTagEnricher : IActivityTagEnricher
{
    public ValueTask Enrich(
        Activity activity,
        in MessagingEnrichmentContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrWhiteSpace(context.TenantId))
        {
            activity.SetTag(MessagingTags.TenantId, context.TenantId);
        }

        return ValueTask.CompletedTask;
    }
}
