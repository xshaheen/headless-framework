// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagingOptionsExtensions
{
    extension(MessagingOptions options)
    {
        /// <summary>
        /// Configuration for messaging.
        /// </summary>
        /// <param name="serverUrl">Pulsar bootstrap server urls.</param>
        public MessagingOptions UsePulsar(string serverUrl)
        {
            return options.UsePulsar(opt =>
            {
                opt.ServiceUrl = serverUrl;
            });
        }

        /// <summary>
        /// Configuration for messaging.
        /// </summary>
        /// <param name="configure">Provides programmatic configuration for the pulsar .</param>
        /// <returns></returns>
        public MessagingOptions UsePulsar(Action<MessagingPulsarOptions> configure)
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(new PulsarMessagesOptionsExtension(configure));

            return options;
        }
    }

    private sealed class PulsarMessagesOptionsExtension(Action<MessagingPulsarOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Apache Pulsar"));

            services.Configure(configure);

            services.AddSingleton<ITransport, PulsarTransport>();
            services.AddSingleton<IConsumerClientFactory, PulsarConsumerClientFactory>();
            services.AddSingleton<IConnectionFactory, ConnectionFactory>();
        }
    }
}
