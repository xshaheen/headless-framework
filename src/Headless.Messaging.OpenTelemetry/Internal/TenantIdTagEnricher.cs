// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;

namespace Headless.Messaging.OpenTelemetry.Internal;

internal sealed class TenantIdTagEnricher : IActivityTagEnricher
{
    public void Enrich(Activity activity, in MessagingEnrichmentContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.TenantId))
        {
            activity.SetTag("headless.messaging.tenant_id", context.TenantId);
        }
    }
}
