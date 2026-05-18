// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.OpenTelemetry.Internal;

internal sealed class RetryCountTagEnricher : IActivityTagEnricher
{
    public ValueTask Enrich(
        Activity activity,
        in MessagingEnrichmentContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (context.RetryCount > 0)
        {
            activity.SetTag(MessagingTags.RetryCount, context.RetryCount);
        }

        return ValueTask.CompletedTask;
    }
}
