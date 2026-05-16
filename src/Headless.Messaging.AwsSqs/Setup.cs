// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Headless.Checks;
using Headless.Messaging.AwsSqs;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class MessagesAmazonSqsSetup
{
    extension(MessagingSetupBuilder setup)
    {
        public MessagingSetupBuilder UseAmazonSqs(RegionEndpoint region)
        {
            return setup.UseAmazonSqs(opt =>
            {
                opt.Region = region;
            });
        }

        public MessagingSetupBuilder UseAmazonSqs(Action<AmazonSqsOptions> configure)
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(new AmazonSqsOptionsExtension(configure));

            return setup;
        }
    }

    private sealed class AmazonSqsOptionsExtension(Action<AmazonSqsOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Amazon SQS"));

            services.Configure<AmazonSqsOptions, AmazonSqsOptionsValidator>(configure);
            services.AddSingleton<ITransport, AmazonSqsTransport>();
            services.AddSingleton<IConsumerClientFactory, AmazonSqsConsumerClientFactory>();
        }
    }
}
