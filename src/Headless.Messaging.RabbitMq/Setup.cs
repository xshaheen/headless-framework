// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

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
        /// Registers RabbitMQ as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="RabbitMqOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(Action<RabbitMqOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new RabbitMqMessagesOptionsExtension(configure));

            return setup;
        }
    }

    // ReSharper disable once InconsistentNaming

    private sealed class RabbitMqMessagesOptionsExtension(Action<RabbitMqOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("RabbitMQ"));
            services.Configure<RabbitMqOptions, RabbitMqOptionsValidator>(configure);
            services.AddSingleton<RabbitMqTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<RabbitMqTransport>());
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<RabbitMqTransport>());
            services.AddSingleton<IConsumerClientFactory, RabbitMqConsumerClientFactory>();
            services.AddSingleton<IConnectionChannelPool, ConnectionChannelPool>();
        }
    }
}
