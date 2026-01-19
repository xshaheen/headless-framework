// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesAmazonSqsSetup
{
    extension(MessagingOptions options)
    {
        public MessagingOptions UseAmazonSqs(RegionEndpoint region)
        {
            return options.UseAmazonSqs(opt =>
            {
                opt.Region = region;
            });
        }

        public MessagingOptions UseAmazonSqs(Action<AmazonSqsOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            options.RegisterExtension(new AmazonSqsOptionsExtension(configure));

            return options;
        }
    }

    private sealed class AmazonSqsOptionsExtension(Action<AmazonSqsOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Amazon SQS"));

            services.Configure(configure);
            services.AddSingleton<ITransport, AmazonSqsTransport>();
            services.AddSingleton<IConsumerClientFactory, AmazonSqsConsumerClientFactory>();
        }
    }
}
