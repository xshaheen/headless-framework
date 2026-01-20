// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesNatsSetup
{
    extension(MessagingOptions options)
    {
        /// <summary>Configuration for messaging.</summary>
        /// <param name="bootstrapServers">NATS bootstrap server urls.</param>
        public MessagingOptions UseNats(string? bootstrapServers = null)
        {
            return options.UseNats(opt =>
            {
                if (bootstrapServers != null)
                {
                    opt.Servers = bootstrapServers;
                }
            });
        }

        /// <summary>Configuration for messaging.</summary>
        /// <param name="configure">Provides programmatic configuration for the NATS.</param>
        public MessagingOptions UseNats(Action<MessagingNatsOptions> configure)
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(new NatsMessagesOptionsExtension(configure));

            return options;
        }
    }

    private sealed class NatsMessagesOptionsExtension(Action<MessagingNatsOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("NATS JetStream"));

            services.Configure(configure);

            services.AddSingleton<ITransport, NatsTransport>();
            services.AddSingleton<IConsumerClientFactory, NatsConsumerClientFactory>();
            services.AddSingleton<INatsConnectionPool, NatsConnectionPool>();
        }
    }
}
