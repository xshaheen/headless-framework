// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Extension members that register Apache Pulsar as the message transport.
/// </summary>
/// <remarks>
/// Both a bus (topic fan-out) and a queue (point-to-point) transport are registered. A single
/// shared <c>PulsarClient</c> is created lazily on first use. Producers are cached per topic and
/// created once; the cache is flushed on failure to allow recovery on the next publish attempt.
/// </remarks>
[PublicAPI]
public static class SetupPulsarMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers Apache Pulsar as the message transport, connecting to the given service URL.
        /// </summary>
        /// <param name="serverUrl">
        /// The Pulsar service URL (for example <c>"pulsar://localhost:6650"</c> or
        /// <c>"pulsar+ssl://broker:6651"</c> for TLS).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        public MessagingSetupBuilder UsePulsar(string serverUrl)
        {
            return setup.UsePulsar(opt => opt.ServiceUrl = serverUrl);
        }

        /// <summary>
        /// Registers Apache Pulsar as the message transport, binding and validating
        /// <see cref="PulsarMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="PulsarMessagingOptions"/> values.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UsePulsar(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterPulsar(
                setup,
                services => services.Configure<PulsarMessagingOptions, PulsarMessagingOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers Apache Pulsar as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="PulsarMessagingOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UsePulsar(Action<PulsarMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterPulsar(
                setup,
                services => services.Configure<PulsarMessagingOptions, PulsarMessagingOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers Apache Pulsar as the message transport, configuring <see cref="PulsarMessagingOptions"/>
        /// with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="PulsarMessagingOptions"/> using the service provider
        /// (for example to resolve secrets or TLS material from DI).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UsePulsar(Action<PulsarMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterPulsar(
                setup,
                services => services.Configure<PulsarMessagingOptions, PulsarMessagingOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _RegisterPulsar(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new PulsarMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class PulsarMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        private static IEnumerable<MessageMetadata> _GetAffinityRoutes(IServiceProvider sp)
        {
            var routes = sp.GetRequiredService<IMessageMetadataRegistry>().GetAll();
            return routes;
        }

        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Apache Pulsar"));
            services.AddMessagingProviderCapabilities(sp =>
                MessagingProviderCapabilities.Transport(
                    "Apache Pulsar",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsIndependentLaneTopology: true,
                    routingAffinityRoutes: _GetAffinityRoutes(sp)
                        .Select(static metadata => new MessagingRoutingAffinityRoute(
                            metadata.Route.Lane,
                            metadata.Route.MessageName,
                            PulsarRoutingAffinity.Mapping
                        ))
                        .ToArray()
                )
            );

            configureOptions(services);

            services.AddSingleton<IBusTransport>(sp => new PulsarTransport(
                sp.GetRequiredService<ILogger<PulsarTransport>>(),
                sp.GetRequiredService<IConnectionFactory>(),
                MessageLane.Bus
            ));
            services.AddSingleton<IQueueTransport>(sp => new PulsarTransport(
                sp.GetRequiredService<ILogger<PulsarTransport>>(),
                sp.GetRequiredService<IConnectionFactory>(),
                MessageLane.Queue
            ));
            services.AddSingleton<IConsumerClientFactory, PulsarConsumerClientFactory>();
            services.AddSingleton<IConnectionFactory, ConnectionFactory>();
        }
    }
}
