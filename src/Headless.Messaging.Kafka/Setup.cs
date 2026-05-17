// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class SetupKafkaMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Configuration for messaging.</summary>
        /// <param name="bootstrapServers">Kafka bootstrap server urls.</param>
        public MessagingSetupBuilder UseKafka(string bootstrapServers)
        {
            return setup.UseKafka(opt =>
            {
                opt.Servers = bootstrapServers;
            });
        }

        /// <summary>Configuration for messaging.</summary>
        /// <param name="configure">Provides programmatic configuration for the kafka .</param>
        public MessagingSetupBuilder UseKafka(Action<MessagingKafkaOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new KafkaMessagesOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class KafkaMessagesOptionsExtension(Action<MessagingKafkaOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Kafka"));

            services.Configure<MessagingKafkaOptions, MessagingKafkaOptionsValidator>(configure);

            services.AddSingleton<ITransport, KafkaTransport>();
            services.AddSingleton<IConsumerClientFactory, KafkaConsumerClientFactory>();
            services.AddSingleton<IKafkaConnectionPool, KafkaConnectionPool>();
        }
    }
}
