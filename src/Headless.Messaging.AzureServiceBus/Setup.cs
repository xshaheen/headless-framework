// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension members that register Azure Service Bus as the message transport.
/// </summary>
/// <remarks>
/// Both a bus (topic/subscription fan-out) and a queue (point-to-point) transport are registered.
/// Topics, subscriptions, and subscription filter rules are auto-provisioned by default using the
/// Azure Service Bus administration API; set <see cref="AzureServiceBusOptions.AutoProvision"/>
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
        /// information (use <see cref="AzureServiceBusOptions.TopicPath"/> instead).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAzureServiceBus(string connectionString)
        {
            Argument.IsNotNull(connectionString);

            return setup.UseAzureServiceBus(opt => opt.ConnectionString = connectionString);
        }

        /// <summary>
        /// Registers Azure Service Bus as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="AzureServiceBusOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAzureServiceBus(Action<AzureServiceBusOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new AzureServiceBusOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class AzureServiceBusOptionsExtension(Action<AzureServiceBusOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Azure Service Bus"));

            services.Configure<AzureServiceBusOptions, AzureServiceBusOptionsValidator>(configure);

            services.AddSingleton<IConsumerClientFactory, AzureServiceBusConsumerClientFactory>();
            services.AddSingleton<AzureServiceBusTransport>();
            services.AddSingleton<AzureServiceBusQueueTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<AzureServiceBusTransport>());
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<AzureServiceBusQueueTransport>());
        }
    }
}
