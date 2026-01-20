// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Transport;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesRabbitMqSetup
{
    extension(MessagingOptions options)
    {
        public MessagingOptions UseRabbitMq(string hostName)
        {
            return options.UseRabbitMq(opt =>
            {
                opt.HostName = hostName;
            });
        }

        public MessagingOptions UseRabbitMq(Action<RabbitMQOptions> configure)
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(new RabbitMqMessagesOptionsExtension(configure));

            return options;
        }
    }

    // ReSharper disable once InconsistentNaming

    private sealed class RabbitMqMessagesOptionsExtension(Action<RabbitMQOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("RabbitMQ"));
            services.Configure(configure);
            services.AddSingleton<IValidateOptions<RabbitMQOptions>, RabbitMQOptionsValidator>();
            services.AddSingleton<ITransport, RabbitMqTransport>();
            services.AddSingleton<IConsumerClientFactory, RabbitMQConsumerClientFactory>();
            services.AddSingleton<IConnectionChannelPool, ConnectionChannelPool>();
        }
    }
}
