// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

/// <summary>
/// Extension members that register NATS JetStream as the message transport.
/// </summary>
/// <remarks>
/// Both a bus (subject fan-out) and a queue (point-to-point) transport are registered using the
/// same underlying NATS JetStream infrastructure. A connection pool sized by
/// <see cref="NatsMessagingOptions.ConnectionPoolSize"/> is registered as a singleton.
/// <para/>
/// On startup, shard symmetry is validated: every consumer that receives a message type configured
/// with <c>SubjectShard(...)</c> must also declare <c>.UseNats(c => c.Sharded())</c>. An omission
/// throws <see cref="InvalidOperationException"/> at DI build time to prevent silent message loss.
/// <para/>
/// Request/reply is supported on core NATS, outside JetStream: a requesting process receives replies on a subject of
/// its own under the reserved <c>headless.reply.</c> prefix, which no provisioned stream captures.
/// <para/>
/// The <c>messaging-nats</c> readiness health check is contributed too; it pings the server over a pooled connection.
/// </remarks>
public static class SetupMessagingNats
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers NATS JetStream as the message transport, optionally overriding the server URL.
        /// </summary>
        /// <param name="bootstrapServers">
        /// A NATS server URL or comma-separated list of URLs. When <see langword="null"/>, the
        /// default from <see cref="NatsMessagingOptions.Servers"/> (<c>nats://127.0.0.1:4222</c>) is used.
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        public MessagingSetupBuilder UseNats(string? bootstrapServers = null)
        {
            return setup.UseNats(opt =>
            {
                if (bootstrapServers != null)
                {
                    opt.Servers = bootstrapServers;
                }
            });
        }

        /// <summary>
        /// Registers NATS JetStream as the message transport, binding and validating
        /// <see cref="NatsMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="NatsMessagingOptions"/> values.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseNats(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterNats(
                setup,
                services => services.Configure<NatsMessagingOptions, NatsMessagingOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers NATS JetStream as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="NatsMessagingOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseNats(Action<NatsMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterNats(
                setup,
                services => services.Configure<NatsMessagingOptions, NatsMessagingOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers NATS JetStream as the message transport, configuring <see cref="NatsMessagingOptions"/>
        /// with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="NatsMessagingOptions"/> using the service provider
        /// (for example to resolve secrets or connection settings from DI).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseNats(Action<NatsMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterNats(
                setup,
                services => services.Configure<NatsMessagingOptions, NatsMessagingOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _RegisterNats(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new NatsMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class NatsMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("NATS JetStream"));
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Transport(
                    "NATS JetStream",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsIndependentLaneTopology: true,
                    supportsEveryInstance: true,
                    supportsRequestReply: true
                )
            );

            configureOptions(services);

            // Built explicitly so the back-off measures real time rather than a registered TimeProvider.
            services.AddSingleton(sp => new NatsStreamProvisioner(
                sp.GetRequiredService<IOptions<NatsMessagingOptions>>()
            ));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProcessingServer, NatsStreamWarmup>());
            services.AddSingleton<IBusTransport>(sp => new NatsTransport(
                sp.GetRequiredService<ILogger<NatsTransport>>(),
                sp.GetRequiredService<INatsConnectionPool>(),
                sp.GetRequiredService<NatsStreamProvisioner>(),
                MessageLane.Bus
            ));
            services.AddSingleton<IQueueTransport>(sp => new NatsTransport(
                sp.GetRequiredService<ILogger<NatsTransport>>(),
                sp.GetRequiredService<INatsConnectionPool>(),
                sp.GetRequiredService<NatsStreamProvisioner>(),
                MessageLane.Queue
            ));
            services.AddSingleton<IReplyTransport, NatsReplyTransport>();
            services.AddSingleton<IConsumerClientFactory, NatsConsumerClientFactory>();
            services.AddSingleton<INatsConnectionPool, NatsConnectionPool>();
            services.AddHeadlessHealthCheck(
                "messaging-nats",
                static async (provider, cancellationToken) =>
                    await provider
                        .GetRequiredService<INatsConnectionPool>()
                        .GetConnection()
                        .PingAsync(cancellationToken)
                        .ConfigureAwait(false),
                HeadlessHealthCheckTags.Messaging
            );
        }
    }
}
