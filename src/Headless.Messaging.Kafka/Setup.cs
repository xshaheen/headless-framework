// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Kafka;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Extension members that register Apache Kafka as the message transport (queue lane only).
/// </summary>
/// <remarks>
/// Kafka supports only point-to-point (queue) messaging in this transport. Bus (fan-out / pub-sub)
/// delivery is not available — use a different transport for fan-out scenarios.
/// A shared producer pool sized by <see cref="KafkaMessagingOptions.ConnectionPoolSize"/> is registered
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
            return setup.UseKafka(opt => opt.Servers = bootstrapServers);
        }

        /// <summary>
        /// Registers Apache Kafka as the message transport, binding and validating
        /// <see cref="KafkaMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="KafkaMessagingOptions"/> values.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseKafka(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterKafka(
                setup,
                services => services.Configure<KafkaMessagingOptions, KafkaMessagingOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers Apache Kafka as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="KafkaMessagingOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseKafka(Action<KafkaMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterKafka(
                setup,
                services => services.Configure<KafkaMessagingOptions, KafkaMessagingOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers Apache Kafka as the message transport, configuring <see cref="KafkaMessagingOptions"/>
        /// with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="KafkaMessagingOptions"/> using the service provider
        /// (for example to resolve secrets or connection settings from DI).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseKafka(Action<KafkaMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterKafka(
                setup,
                services => services.Configure<KafkaMessagingOptions, KafkaMessagingOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _RegisterKafka(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new KafkaMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class KafkaMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        private static IEnumerable<MessageMetadata> _GetAffinityRoutes(IServiceProvider sp)
        {
            var routes = sp.GetRequiredService<IMessageMetadataRegistry>().GetAll();
            var options = sp.GetRequiredService<IOptions<KafkaMessagingOptions>>().Value;
            var partitioner = options.MainConfig.GetValueOrDefault("partitioner", "consistent_random");
            var supported =
                partitioner
                is "consistent"
                    or "consistent_random"
                    or "murmur2"
                    or "murmur2_random"
                    or "fnv1a"
                    or "fnv1a_random";
            return routes.Where(route => supported && route.Route.Lane == MessageLane.Queue);
        }

        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Kafka"));
            services.AddMessagingProviderCapabilities(sp =>
                MessagingProviderCapabilities.Transport(
                    "Kafka",
                    [MessageLane.Queue],
                    supportsIndependentLaneTopology: false,
                    routingAffinityRoutes: _GetAffinityRoutes(sp)
                        .Select(static metadata => new MessagingRoutingAffinityRoute(
                            metadata.Route.Lane,
                            metadata.Route.MessageName,
                            KafkaRoutingAffinity.Mapping
                        ))
                        .ToArray()
                )
            );

            configureOptions(services);

            services.AddSingleton<IQueueTransport, KafkaTransport>();
            services.AddSingleton<IConsumerClientFactory, KafkaConsumerClientFactory>();
            services.AddSingleton<IKafkaConnectionPool, KafkaConnectionPool>();
        }
    }
}
