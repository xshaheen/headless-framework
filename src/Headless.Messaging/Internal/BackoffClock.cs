// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>Resolves the clock messaging's failure waits run on; see <see cref="MessagingOptions.BackoffClockServiceKey"/>.</summary>
internal static class BackoffClock
{
    public static TimeProvider Resolve(IServiceProvider serviceProvider)
    {
        // A container without keyed services, or one composed without AddHeadlessMessaging, has no registration; those
        // waits still need real time.
        return
            serviceProvider is IKeyedServiceProvider keyed
            && keyed.GetKeyedService(typeof(TimeProvider), MessagingOptions.BackoffClockServiceKey)
                is TimeProvider clock
            ? clock
            : TimeProvider.System;
    }
}
