// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class SetupPulsarMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Configuration for messaging.
        /// </summary>
        /// <param name="serverUrl">Pulsar bootstrap server urls.</param>
        public MessagingSetupBuilder UsePulsar(string serverUrl)
        {
            return setup.UsePulsar(opt =>
            {
                opt.ServiceUrl = serverUrl;
            });
        }

        /// <summary>
        /// Configuration for messaging.
        /// </summary>
        /// <param name="configure">Provides programmatic configuration for the pulsar .</param>
        /// <returns></returns>
        public MessagingSetupBuilder UsePulsar(Action<MessagingPulsarOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new PulsarMessagesOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class PulsarMessagesOptionsExtension(Action<MessagingPulsarOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Apache Pulsar"));

            services.Configure<MessagingPulsarOptions, MessagingPulsarOptionsValidator>(configure);

            services.AddSingleton<ITransport, PulsarTransport>();
            services.AddSingleton<IConsumerClientFactory, PulsarConsumerClientFactory>();
            services.AddSingleton<IConnectionFactory, ConnectionFactory>();
        }
    }
}
