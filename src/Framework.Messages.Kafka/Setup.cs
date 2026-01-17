// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesKafkaSetup
{
    extension(CapOptions options)
    {
        /// <summary>Configuration to use kafka in CAP.</summary>
        /// <param name="bootstrapServers">Kafka bootstrap server urls.</param>
        public CapOptions UseKafka(string bootstrapServers)
        {
            return options.UseKafka(opt =>
            {
                opt.Servers = bootstrapServers;
            });
        }

        /// <summary>Configuration to use kafka in CAP.</summary>
        /// <param name="configure">Provides programmatic configuration for the kafka .</param>
        public CapOptions UseKafka(Action<KafkaOptions> configure)
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(new KafkaMessagesOptionsExtension(configure));

            return options;
        }
    }

    private sealed class KafkaMessagesOptionsExtension(Action<KafkaOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new CapMessageQueueMakerService("Kafka"));

            services.Configure(configure);

            services.AddSingleton<ITransport, KafkaTransport>();
            services.AddSingleton<IConsumerClientFactory, KafkaConsumerClientFactory>();
            services.AddSingleton<IConnectionPool, ConnectionPool>();
        }
    }
}
