// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class SetupNatsMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Configuration for messaging.</summary>
        /// <param name="bootstrapServers">NATS bootstrap server urls.</param>
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

        /// <summary>Configuration for messaging.</summary>
        /// <param name="configure">Provides programmatic configuration for the NATS.</param>
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

            services.AddSingleton<ITransport, NatsTransport>();
            services.AddSingleton<IConsumerClientFactory, NatsConsumerClientFactory>();
            services.AddSingleton<INatsConnectionPool, NatsConnectionPool>();
        }
    }
}
