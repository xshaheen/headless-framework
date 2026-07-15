// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;

namespace Headless.Messaging.Internal;

internal sealed class RetryCountTagEnricher : IActivityTagEnricher
{
    public void Enrich(Activity activity, in MessagingEnrichmentContext context)
    {
        if (context.RetryCount > 0)
        {
            activity.SetTag(MessagingTags.RetryCount, context.RetryCount);
        }
    }
}
