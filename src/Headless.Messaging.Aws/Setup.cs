// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Headless.Checks;
using Headless.Messaging.Aws;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Extension members that register Amazon SQS (queue) and SNS (bus/fan-out) as the message transport.
/// </summary>
/// <remarks>
/// Topics are modelled as SNS topics and queues as SQS queues. SNS fan-out to SQS subscriptions
/// is wired automatically. AWS credentials are resolved through the standard AWS SDK credential
/// chain unless <see cref="AmazonSqsMessagingOptions.Credentials"/> is provided explicitly.
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
            return setup.UseAws(opt => opt.Region = region);
        }

        /// <summary>
        /// Registers Amazon SQS/SNS as the message transport, binding and validating
        /// <see cref="AmazonSqsMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="AmazonSqsMessagingOptions"/> values.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAws(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterAws(
                setup,
                services => services.Configure<AmazonSqsMessagingOptions, AmazonSqsMessagingOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers Amazon SQS/SNS as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="AmazonSqsMessagingOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAws(Action<AmazonSqsMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterAws(
                setup,
                services => services.Configure<AmazonSqsMessagingOptions, AmazonSqsMessagingOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers Amazon SQS/SNS as the message transport, configuring
        /// <see cref="AmazonSqsMessagingOptions"/> with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="AmazonSqsMessagingOptions"/> using the service provider
        /// (for example to resolve secrets or credentials from DI).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseAws(Action<AmazonSqsMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterAws(
                setup,
                services => services.Configure<AmazonSqsMessagingOptions, AmazonSqsMessagingOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _RegisterAws(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new AmazonSqsMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class AmazonSqsMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Amazon SQS"));

            configureOptions(services);
            services.AddSingleton<IBusTransport, AmazonSnsBusTransport>();
            services.AddSingleton<IQueueTransport, AmazonSqsQueueTransport>();
            services.AddSingleton<IConsumerClientFactory, AmazonSqsConsumerClientFactory>();
        }
    }
}
