// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension members that register Apache Kafka as the message transport (queue lane only).
/// </summary>
/// <remarks>
/// Kafka supports only point-to-point (queue) messaging in this transport. Bus (fan-out / pub-sub)
/// delivery is not available — use a different transport for fan-out scenarios.
/// A shared producer pool sized by <see cref="MessagingKafkaOptions.ConnectionPoolSize"/> is registered
/// as a singleton.
/// </remarks>
public static class SetupKafkaMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers Apache Kafka as the message transport, connecting to the given bootstrap servers.
        /// </summary>
        /// <param name="bootstrapServers">
        /// A comma-separated list of broker <c>host</c> or <c>host:port</c> addresses
        /// (for example <c>"broker1:9092,broker2:9092"</c>).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        public MessagingSetupBuilder UseKafka(string bootstrapServers)
        {
            return setup.UseKafka(opt =>
            {
                opt.Servers = bootstrapServers;
            });
        }

        /// <summary>
        /// Registers Apache Kafka as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="MessagingKafkaOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
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

            services.AddSingleton<IQueueTransport, KafkaTransport>();
            services.AddSingleton<IConsumerClientFactory, KafkaConsumerClientFactory>();
            services.AddSingleton<IKafkaConnectionPool, KafkaConnectionPool>();
        }
    }
}
