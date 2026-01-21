// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesAzureServiceBusSetup
{
    extension(MessagingOptions options)
    {
        /// <summary>
        /// Configuration for messaging.
        /// </summary>
        /// <param name="connectionString">Connection string for namespace or the entity.</param>
        public MessagingOptions UseAzureServiceBus(string connectionString)
        {
            Argument.IsNotNull(connectionString);

            return options.UseAzureServiceBus(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        /// <summary>
        /// Configuration for messaging.
        /// </summary>
        /// <param name="configure">Provides programmatic configuration for the Azure Service Bus.</param>
        public MessagingOptions UseAzureServiceBus(Action<AzureServiceBusOptions> configure)
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(new AzureServiceBusOptionsExtension(configure));

            return options;
        }
    }

    private sealed class AzureServiceBusOptionsExtension(Action<AzureServiceBusOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Azure Service Bus"));

            services.Configure(configure);

            services.AddSingleton<IConsumerClientFactory, AzureServiceBusConsumerClientFactory>();
            services.AddSingleton<ITransport, AzureServiceBusTransport>();
        }
    }
}
