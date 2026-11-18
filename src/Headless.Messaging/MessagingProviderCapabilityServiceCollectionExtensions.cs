// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

/// <summary>Registers immutable messaging-provider capability evidence.</summary>
[PublicAPI]
public static class MessagingProviderCapabilityServiceCollectionExtensions
{
    /// <summary>Adds a factory that snapshots inert configuration and registered destinations before clients start.</summary>
    /// <remarks>The factory must never resolve transports, storage implementations, processors, or broker clients.</remarks>
    public static IServiceCollection AddMessagingProviderCapabilities(
        this IServiceCollection services,
        Func<IServiceProvider, MessagingProviderCapabilities> factory
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(factory);
        services.AddSingleton(factory);
        return services;
    }

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

    /// <summary>
    /// Adds the inert capability contribution of the named transport <paramref name="transportName"/>, registered with
    /// <c>setup.AddTransport(name)</c>. The named transport's capability model composes it with the host's storage and
    /// coordination contributions, apart from the default transport's.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="transportName">The named transport's name.</param>
    /// <param name="capabilities">The transport's capability contribution.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddMessagingProviderCapabilities(
        this IServiceCollection services,
        string transportName,
        MessagingProviderCapabilities capabilities
    )
    {
        Argument.IsNotNull(services);
        Argument.IsNotNullOrWhiteSpace(transportName);
        Argument.IsNotNull(capabilities);

        services.AddKeyedSingleton(transportName, capabilities);
        return services;
    }
}
