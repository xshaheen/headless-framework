// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Headless.Checks;
using Headless.Hosting;
using Headless.Messaging.Aws;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

/// <summary>
/// Extension members that register Amazon SQS (queue) and SNS (bus/fan-out) as the message transport.
/// </summary>
/// <remarks>
/// Topics are modelled as SNS topics and queues as SQS queues, and SNS fans out to SQS subscriptions with raw message
/// delivery. The shared topology is the deployment's: <c>resources provision</c> creates it (resource kind
/// <see cref="TopologyResourceKind"/>), and the running host only looks it up. AWS credentials are resolved through the
/// standard AWS SDK credential chain unless <see cref="AmazonSqsMessagingOptions.Credentials"/> is provided explicitly.
/// </remarks>
public static class SetupMessagingAws
{
    /// <summary>
    /// The <see cref="IResourceProvisioner.Kind" /> of the SNS/SQS topology (topics, queues, queue policies, and
    /// subscriptions), which <c>resources provision -k</c> selects and a provisioner that needs the topology selects in
    /// its <c>DependsOn</c>.
    /// </summary>
    public const string TopologyResourceKind = "AmazonSqs";

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
        private static IEnumerable<MessageMetadata> _GetAffinityRoutes(IServiceProvider sp)
        {
            var routes = sp.GetRequiredService<IMessageMetadataRegistry>().GetAll();
            return routes.Where(static route => route.Route.MessageName.IsAwsFifoName());
        }

        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("Amazon SQS"));
            services.AddMessagingProviderCapabilities(sp =>
                MessagingProviderCapabilities.Transport(
                    "Amazon SQS",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsIndependentLaneTopology: true,
                    routingAffinityRoutes: _GetAffinityRoutes(sp)
                        .Select(static metadata => new MessagingRoutingAffinityRoute(
                            metadata.Route.Lane,
                            metadata.Route.MessageName,
                            AwsRoutingAffinity.Mapping
                        ))
                        .ToArray()
                )
            );

            configureOptions(services);

            // Environments that share one account and region keep their topics and queues apart.
            services.ApplyHeadlessPrefix<AmazonSqsMessagingOptions>(
                static (options, prefix) => options.HostPrefix = prefix
            );
            services.AddSingleton<IBusTransport, AmazonSnsBusTransport>();
            services.AddSingleton<IQueueTransport, AmazonSqsQueueTransport>();
            services.AddSingleton<SnsTopicArnCache>();
            services.AddSingleton<IConsumerClientFactory, AmazonSqsConsumerClientFactory>();

            // The shared topology is the deployment's: resources provision creates it, and the transport only looks it up.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ITransportTopologyProvisioner, AwsTopologyProvisioner>()
            );
        }
    }
}
