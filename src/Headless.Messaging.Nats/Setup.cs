// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension members that register NATS JetStream as the message transport.
/// </summary>
/// <remarks>
/// Both a bus (subject fan-out) and a queue (point-to-point) transport are registered using the
/// same underlying NATS JetStream infrastructure. A connection pool sized by
/// <see cref="MessagingNatsOptions.ConnectionPoolSize"/> is registered as a singleton.
/// <para/>
/// On startup, shard symmetry is validated: every consumer that receives a message type configured
/// with <c>SubjectShard(...)</c> must also declare <c>.UseNats(c => c.Sharded())</c>. An omission
/// throws <see cref="InvalidOperationException"/> at DI build time to prevent silent message loss.
/// </remarks>
public static class SetupNatsMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers NATS JetStream as the message transport, optionally overriding the server URL.
        /// </summary>
        /// <param name="bootstrapServers">
        /// A NATS server URL or comma-separated list of URLs. When <see langword="null"/>, the
        /// default from <see cref="MessagingNatsOptions.Servers"/> (<c>nats://127.0.0.1:4222</c>) is used.
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
        /// Registers NATS JetStream as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="MessagingNatsOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseNats(Action<MessagingNatsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new NatsMessagesOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class NatsMessagesOptionsExtension(Action<MessagingNatsOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("NATS JetStream"));

            services.Configure<MessagingNatsOptions, MessagingNatsOptionsValidator>(configure);

            services.AddSingleton<NatsTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<NatsTransport>());
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<NatsTransport>());
            services.AddSingleton<IConsumerClientFactory, NatsConsumerClientFactory>();
            services.AddSingleton<INatsConnectionPool, NatsConnectionPool>();

            _ValidateShardSymmetry(services);
        }

        /// <summary>
        /// Ensures shard symmetry: every consumer of a sharded message must declare
        /// <c>.UseNats(c => c.Sharded())</c>. NATS gives a non-matching FilterSubject zero
        /// messages with no error, so omitting the declaration causes silent data loss.
        /// </summary>
        private static void _ValidateShardSymmetry(IServiceCollection services)
        {
            var messageRegistrations = services
                .Where(static d =>
                    d.ServiceType == typeof(MessageRegistration) && d.ImplementationInstance is MessageRegistration
                )
                .Select(static d => (MessageRegistration)d.ImplementationInstance!);

            foreach (var reg in messageRegistrations)
            {
                var isShardedMessage = reg
                    .ProviderConfigs.Values.OfType<IProviderHeaderContributions>()
                    .Any(static c =>
                        c.HeaderContributions.Any(static h =>
                            string.Equals(h.HeaderName, NatsMessagingHeaders.SubjectShard, StringComparison.Ordinal)
                        )
                    );

                if (!isShardedMessage)
                {
                    continue;
                }

                foreach (var consumer in reg.Consumers)
                {
                    var hasShardedConfig =
                        consumer.ProviderConfigs.TryGetValue(typeof(NatsConsumerConfig), out var configObj)
                        && configObj is NatsConsumerConfig { IsSharded: true };

                    if (!hasShardedConfig)
                    {
                        throw new InvalidOperationException(
                            $"Consumer '{consumer.ConsumerType.Name}' (group '{consumer.Group}') subscribes to "
                                + $"'{reg.MessageType.Name}' which uses SubjectShard(...) but does not declare shard "
                                + "coverage. Call .UseNats(c => c.Sharded()) on the consumer registration to prevent "
                                + "silent message loss: NATS delivers zero messages to a non-wildcard filter that does "
                                + "not match any shard subject."
                        );
                    }
                }
            }
        }
    }
}
