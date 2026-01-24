// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesKafkaSetup
{
    extension(MessagingOptions options)
    {
        /// <summary>Configuration for messaging.</summary>
        /// <param name="bootstrapServers">Kafka bootstrap server urls.</param>
        public MessagingOptions UseKafka(string bootstrapServers)
        {
            return options.UseKafka(opt =>
            {
                opt.Servers = bootstrapServers;
            });
        }

        /// <summary>Configuration for messaging.</summary>
        /// <param name="configure">Provides programmatic configuration for the kafka .</param>
        public MessagingOptions UseKafka(Action<MessagingKafkaOptions> configure)
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(new KafkaMessagesOptionsExtension(configure));

            return options;
        }
    }

    private sealed class KafkaMessagesOptionsExtension(Action<MessagingKafkaOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Kafka"));

            services.Configure(configure);

            services.AddSingleton<ITransport, KafkaTransport>();
            services.AddSingleton<IConsumerClientFactory, KafkaConsumerClientFactory>();
            services.AddSingleton<IKafkaConnectionPool, KafkaConnectionPool>();
        }
    }
}
