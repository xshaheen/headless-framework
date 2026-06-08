// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Helpers;

internal static class MessagingRegistrationDrainExtensions
{
    public static ConsumerRegistry GetDrainedConsumerRegistry(this IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        SetupMessaging.DrainPendingMessageRegistrations(provider, options);

        return provider.GetRequiredService<ConsumerRegistry>();
    }
}
