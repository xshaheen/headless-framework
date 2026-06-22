// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Pulsar;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

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
            return setup.UsePulsar(opt =>
            {
                opt.ServiceUrl = serverUrl;
            });
        }

        /// <summary>
        /// Registers Apache Pulsar as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="MessagingPulsarOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
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

            services.AddSingleton<PulsarTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<PulsarTransport>());
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<PulsarTransport>());
            services.AddSingleton<IConsumerClientFactory, PulsarConsumerClientFactory>();
            services.AddSingleton<IConnectionFactory, ConnectionFactory>();
        }
    }
}
