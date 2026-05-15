// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesRabbitMqSetup
{
    extension(MessagingSetupBuilder setup)
    {
        public MessagingSetupBuilder UseRabbitMq(string hostName)
        {
            return setup.UseRabbitMq(opt =>
            {
                opt.HostName = hostName;
            });
        }

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
            services.AddSingleton<ITransport, RabbitMqTransport>();
            services.AddSingleton<IConsumerClientFactory, RabbitMqConsumerClientFactory>();
            services.AddSingleton<IConnectionChannelPool, ConnectionChannelPool>();
        }
    }
}
