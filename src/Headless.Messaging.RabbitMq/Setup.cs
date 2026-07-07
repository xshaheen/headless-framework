// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.RabbitMq;

/// <summary>
/// Extension members that register RabbitMQ as the message transport.
/// </summary>
/// <remarks>
/// Registers a single shared <c>ConnectionChannelPool</c> that multiplexes AMQP channels over one
/// TCP connection. The exchange declared at startup uses the topic exchange type; each consumer
/// queue is bound with a routing key derived from the message type name.
/// Both a bus (pub/sub) and a queue (point-to-point) transport are registered.
/// </remarks>
public static class SetupRabbitMqMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers RabbitMQ as the message transport, connecting to <paramref name="hostName"/>
        /// with all other options at their defaults.
        /// </summary>
        /// <param name="hostName">
        /// The RabbitMQ broker hostname or a comma-separated list of hostnames for cluster connectivity
        /// (for example <c>"rabbit1,rabbit2"</c>).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="hostName"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(string hostName)
        {
            return setup.UseRabbitMq(opt => opt.HostName = hostName);
        }

        /// <summary>
        /// Registers RabbitMQ as the message transport, binding and validating
        /// <see cref="RabbitMqMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="RabbitMqMessagingOptions"/> values.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterRabbitMq(
                setup,
                services => services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers RabbitMQ as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="RabbitMqMessagingOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(Action<RabbitMqMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterRabbitMq(
                setup,
                services => services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers RabbitMQ as the message transport, configuring <see cref="RabbitMqMessagingOptions"/>
        /// with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="RabbitMqMessagingOptions"/> using the service provider
        /// (for example to resolve credentials or secrets from DI).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(Action<RabbitMqMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterRabbitMq(
                setup,
                services => services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _RegisterRabbitMq(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new RabbitMqMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class RabbitMqMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("RabbitMQ"));
            configureOptions(services);
            services.AddSingleton<RabbitMqTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<RabbitMqTransport>());
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<RabbitMqTransport>());
            services.AddSingleton<IConsumerClientFactory, RabbitMqConsumerClientFactory>();
            services.AddSingleton<IConnectionChannelPool, ConnectionChannelPool>();
        }
    }
}
