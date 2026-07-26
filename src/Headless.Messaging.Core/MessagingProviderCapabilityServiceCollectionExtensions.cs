// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

/// <summary>Registers immutable messaging-provider capability evidence.</summary>
[PublicAPI]
public static class MessagingProviderCapabilityServiceCollectionExtensions
{
    /// <summary>
    /// Adds an inert capability contribution. This may be called before or after <c>AddHeadlessMessaging</c>, provided
    /// the service provider has not been built. The contribution is registered as an implementation instance.
    /// </summary>
    public static IServiceCollection AddMessagingProviderCapabilities(
        this IServiceCollection services,
        MessagingProviderCapabilities capabilities
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(capabilities);

        services.AddSingleton(capabilities);
        return services;
    }
}
