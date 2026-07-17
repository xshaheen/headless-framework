// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

internal sealed class TenantIdTagEnricher : IActivityTagEnricher
{
    public void Enrich(Activity activity, in MessagingEnrichmentContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.TenantId))
        {
            activity.SetTag(MessagingTags.TenantId, context.TenantId);
        }
    }
}
