// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension members that register Amazon SQS (queue) and SNS (bus/fan-out) as the message transport.
/// </summary>
/// <remarks>
/// Topics are modelled as SNS topics and queues as SQS queues. SNS fan-out to SQS subscriptions
/// is wired automatically. AWS credentials are resolved through the standard AWS SDK credential
/// chain unless <see cref="AmazonSqsOptions.Credentials"/> is provided explicitly.
/// </remarks>
public static class SetupAwsMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers Amazon SQS/SNS as the message transport for the specified AWS region,
        /// using the default AWS credential chain.
        /// </summary>
        /// <param name="region">The AWS region endpoint to connect to.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        public MessagingSetupBuilder UseAws(RegionEndpoint region)
        {
            return setup.UseAws(opt =>
            {
                opt.Region = region;
            });
        }

        /// <summary>
        /// Registers Amazon SQS/SNS as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="AmazonSqsOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAws(Action<AmazonSqsOptions> configure)
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
            services.AddSingleton<IBusTransport, AmazonSnsBusTransport>();
            services.AddSingleton<IQueueTransport, AmazonSqsQueueTransport>();
            services.AddSingleton<IConsumerClientFactory, AmazonSqsConsumerClientFactory>();
        }
    }
}
