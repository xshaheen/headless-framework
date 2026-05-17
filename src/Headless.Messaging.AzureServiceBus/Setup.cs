// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class SetupAzureServiceBusMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Configuration for messaging.
        /// </summary>
        /// <param name="connectionString">Connection string for namespace or the entity.</param>
        public MessagingSetupBuilder UseAzureServiceBus(string connectionString)
        {
            Argument.IsNotNull(connectionString);

            return setup.UseAzureServiceBus(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        /// <summary>
        /// Configuration for messaging.
        /// </summary>
        /// <param name="configure">Provides programmatic configuration for the Azure Service Bus.</param>
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
            services.AddSingleton<ITransport, AzureServiceBusTransport>();
        }
    }
}
