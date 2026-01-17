// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesAzureServiceBusSetup
{
    extension(CapOptions options)
    {
        /// <summary>
        /// Configuration to use Azure Service Bus in CAP.
        /// </summary>
        /// <param name="connectionString">Connection string for namespace or the entity.</param>
        public CapOptions UseAzureServiceBus(string connectionString)
        {
            Argument.IsNotNull(connectionString);

            return options.UseAzureServiceBus(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        /// <summary>
        /// Configuration to use Azure Service Bus in CAP.
        /// </summary>
        /// <param name="configure">Provides programmatic configuration for the Azure Service Bus.</param>
        public CapOptions UseAzureServiceBus(Action<AzureServiceBusOptions> configure)
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
            services.AddSingleton(new CapMessageQueueMakerService("Azure Service Bus"));

            services.Configure(configure);

            services.AddSingleton<IConsumerClientFactory, AzureServiceBusConsumerClientFactory>();
            services.AddSingleton<ITransport, AzureServiceBusTransport>();
        }
    }
}
