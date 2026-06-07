// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Helpers;

internal static class MessagingRegistrationDrainExtensions
{
    public static ConsumerRegistry GetDrainedConsumerRegistry(this IServiceProvider provider)
    {
        var services = provider.GetRequiredService<IServiceCollection>();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        var registry = provider.GetRequiredService<ConsumerRegistry>();
        var circuitBreakers = provider.GetRequiredService<ConsumerCircuitBreakerRegistry>();

        SetupMessaging.DiscoverMessageRegistrations(services, options, registry, circuitBreakers);

        return registry;
    }
}
