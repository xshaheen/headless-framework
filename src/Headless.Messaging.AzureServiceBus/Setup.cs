// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Extension members that register Azure Service Bus as the message transport.
/// </summary>
/// <remarks>
/// Both a bus (topic/subscription fan-out) and a queue (point-to-point) transport are registered.
/// Topics, subscriptions, and subscription filter rules are auto-provisioned by default using the
/// Azure Service Bus administration API; set <see cref="AzureServiceBusMessagingOptions.AutoProvision"/>
/// to <see langword="false"/> to skip auto-provisioning when entities are managed externally.
/// </remarks>
public static class SetupAzureServiceBusMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers Azure Service Bus as the message transport using a shared-access-signature
        /// namespace connection string.
        /// </summary>
        /// <param name="connectionString">
        /// The Service Bus namespace connection string. Must not contain entity-specific path
        /// information (use <see cref="AzureServiceBusMessagingOptions.TopicPath"/> instead).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAzureServiceBus(string connectionString)
        {
            Argument.IsNotNull(connectionString);

            return setup.UseAzureServiceBus(opt => opt.ConnectionString = connectionString);
        }

        /// <summary>
        /// Registers Azure Service Bus as the message transport, binding and validating
        /// <see cref="AzureServiceBusMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="AzureServiceBusMessagingOptions"/> values.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAzureServiceBus(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterAzureServiceBus(
                setup,
                services =>
                    services.Configure<AzureServiceBusMessagingOptions, AzureServiceBusMessagingOptionsValidator>(
                        config
                    )
            );
        }

        /// <summary>
        /// Registers Azure Service Bus as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="AzureServiceBusMessagingOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAzureServiceBus(Action<AzureServiceBusMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterAzureServiceBus(
                setup,
                services =>
                    services.Configure<AzureServiceBusMessagingOptions, AzureServiceBusMessagingOptionsValidator>(
                        configure
                    )
            );
        }

        /// <summary>
        /// Registers Azure Service Bus as the message transport, configuring
        /// <see cref="AzureServiceBusMessagingOptions"/> with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="AzureServiceBusMessagingOptions"/> using the service provider
        /// (for example to resolve a <c>TokenCredential</c> or secrets from DI).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAzureServiceBus(
            Action<AzureServiceBusMessagingOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            return _RegisterAzureServiceBus(
                setup,
                services =>
                    services.Configure<AzureServiceBusMessagingOptions, AzureServiceBusMessagingOptionsValidator>(
                        configure
                    )
            );
        }
    }

    private static MessagingSetupBuilder _RegisterAzureServiceBus(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new AzureServiceBusMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class AzureServiceBusMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Azure Service Bus"));
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Transport(
                    "Azure Service Bus",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsIndependentLaneTopology: true
                )
            );

            configureOptions(services);

            services.AddSingleton<IConsumerClientFactory, AzureServiceBusConsumerClientFactory>();
            services.AddSingleton<IAzureServiceBusClientPool, AzureServiceBusClientPool>();
            services.AddSingleton<AzureServiceBusTransport>();
            services.AddSingleton<AzureServiceBusQueueTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<AzureServiceBusTransport>());
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<AzureServiceBusQueueTransport>());
        }
    }
}
