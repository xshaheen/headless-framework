// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Hosting;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

/// <summary>
/// Extension members that register RabbitMQ as the message transport.
/// </summary>
/// <remarks>
/// Registers a single shared <c>ConnectionChannelPool</c> that multiplexes AMQP channels over one
/// TCP connection. Bus uses a lane-qualified topic exchange; Queue uses a lane-qualified direct
/// exchange. Each owned queue is bound with a lane-qualified routing key. Request/reply is supported: a requesting
/// process receives replies on an exclusive queue of its own, named under the reserved <c>headless.reply.</c> prefix.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupMessagingRabbitMq
{
    /// <summary>
    /// The <see cref="IResourceProvisioner.Kind" /> of the RabbitMQ topology (exchanges, queues, and
    /// bindings), which <c>resources provision -k</c> selects and a provisioner that needs the topology selects in its
    /// <c>DependsOn</c>.
    /// </summary>
    public const string TopologyResourceKind = "RabbitMQ";

    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers RabbitMQ as the message transport, connecting to <paramref name="hostName"/>
        /// with all other options at their defaults.
        /// </summary>
        /// <param name="hostName">
        /// The RabbitMQ broker hostname or a comma-separated list of hostnames for cluster connectivity
        /// (for example <c>"rabbit1,rabbit2"</c>).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="hostName"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(string hostName)
        {
            return setup.UseRabbitMq(opt => opt.HostName = hostName);
        }

        /// <summary>
        /// Registers RabbitMQ as the message transport, binding and validating
        /// <see cref="RabbitMqMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="RabbitMqMessagingOptions"/> values.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterRabbitMq(
                setup,
                services => services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(config)
            );
        }

        /// <summary>
        /// Registers RabbitMQ as the message transport with full programmatic configuration.
        /// </summary>
        /// <param name="configure">A delegate that configures <see cref="RabbitMqMessagingOptions"/>.</param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(Action<RabbitMqMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterRabbitMq(
                setup,
                services => services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(configure)
            );
        }

        /// <summary>
        /// Registers RabbitMQ as the message transport, configuring <see cref="RabbitMqMessagingOptions"/>
        /// with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">
        /// A delegate that configures <see cref="RabbitMqMessagingOptions"/> using the service provider
        /// (for example to resolve credentials or secrets from DI).
        /// </param>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseRabbitMq(Action<RabbitMqMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterRabbitMq(
                setup,
                services => services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(configure)
            );
        }
    }

    extension(MessagingTransportBuilder transport)
    {
        /// <summary>
        /// Registers a RabbitMQ broker as this named transport, connecting to <paramref name="hostName"/> with all other
        /// options at their defaults.
        /// </summary>
        /// <param name="hostName">The RabbitMQ broker hostname or a comma-separated list of hostnames.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="hostName"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This named transport already has a provider.</exception>
        public MessagingSetupBuilder UseRabbitMq(string hostName)
        {
            Argument.IsNotNull(hostName);

            return transport.UseRabbitMq(opt => opt.HostName = hostName);
        }

        /// <summary>
        /// Registers a RabbitMQ broker as this named transport, binding and validating its own
        /// <see cref="RabbitMqMessagingOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="RabbitMqMessagingOptions"/> values.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This named transport already has a provider.</exception>
        public MessagingSetupBuilder UseRabbitMq(IConfiguration config)
        {
            Argument.IsNotNull(config);

            return _RegisterNamedRabbitMq(
                transport,
                (services, optionsName) =>
                    services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(config, optionsName)
            );
        }

        /// <summary>Registers a RabbitMQ broker as this named transport with its own programmatic configuration.</summary>
        /// <param name="configure">A delegate that configures this transport's <see cref="RabbitMqMessagingOptions"/>.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This named transport already has a provider.</exception>
        public MessagingSetupBuilder UseRabbitMq(Action<RabbitMqMessagingOptions> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterNamedRabbitMq(
                transport,
                (services, optionsName) =>
                    services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(
                        configure,
                        optionsName
                    )
            );
        }

        /// <summary>
        /// Registers a RabbitMQ broker as this named transport, configuring its own
        /// <see cref="RabbitMqMessagingOptions"/> with access to the resolved service provider.
        /// </summary>
        /// <param name="configure">A delegate that configures this transport's options using the service provider.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">This named transport already has a provider.</exception>
        public MessagingSetupBuilder UseRabbitMq(Action<RabbitMqMessagingOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);

            return _RegisterNamedRabbitMq(
                transport,
                (services, optionsName) =>
                    services.Configure<RabbitMqMessagingOptions, RabbitMqMessagingOptionsValidator>(
                        configure,
                        optionsName
                    )
            );
        }
    }

    private static MessagingSetupBuilder _RegisterNamedRabbitMq(
        MessagingTransportBuilder transport,
        Action<IServiceCollection, string> configureOptions
    )
    {
        return transport.RegisterTransport(
            new NamedRabbitMqMessagingOptionsExtension(transport.Name, configureOptions)
        );
    }

    // Environments that share one virtual host keep their exchanges and queues apart. The prefix applies to every
    // options instance, default and named, through one registration.
    private static void _ApplyHostPrefix(IServiceCollection services) =>
        services.ApplyHeadlessPrefix<RabbitMqMessagingOptions>(static (options, prefix) => options.HostPrefix = prefix);

    private static MessagingProviderCapabilities _Capabilities(bool supportsRequestReply) =>
        MessagingProviderCapabilities.Transport(
            "RabbitMQ",
            [MessageLane.Bus, MessageLane.Queue],
            supportsIndependentLaneTopology: true,
            supportsEveryInstance: true,
            supportsRequestReply: supportsRequestReply,
            supportsSingleActiveConsumer: true
        );

    private static MessagingSetupBuilder _RegisterRabbitMq(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new RabbitMqMessagingOptionsExtension(configureOptions));

        return setup;
    }

    private sealed class RabbitMqMessagingOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("RabbitMQ"));
            services.AddMessagingProviderCapabilities(_Capabilities(supportsRequestReply: true));
            configureOptions(services);
            _ApplyHostPrefix(services);
            services.AddSingleton<IBusTransport>(sp => new RabbitMqTransport(
                sp.GetRequiredService<ILogger<RabbitMqTransport>>(),
                sp.GetRequiredService<IConnectionChannelPool>(),
                MessageLane.Bus,
                sp.GetRequiredService<TimeProvider>()
            ));
            services.AddSingleton<IQueueTransport>(sp => new RabbitMqTransport(
                sp.GetRequiredService<ILogger<RabbitMqTransport>>(),
                sp.GetRequiredService<IConnectionChannelPool>(),
                MessageLane.Queue,
                sp.GetRequiredService<TimeProvider>()
            ));
            services.AddSingleton<IReplyTransport, RabbitMqReplyTransport>();
            services.AddSingleton<IConsumerClientFactory, RabbitMqConsumerClientFactory>();
            services.AddSingleton<IConnectionChannelPool, ConnectionChannelPool>();

            // The shared topology is the deployment's: resources provision creates it, and the transport only checks it.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ITransportTopologyProvisioner, RabbitMqTopologyProvisioner>()
            );
        }
    }

    /// <summary>
    /// Registers one named RabbitMQ transport, keyed by its name. Its options, connection pool, consumer clients, and
    /// topology provisioner are its own, so it never shares a connection or a setting with another transport.
    /// </summary>
    private sealed class NamedRabbitMqMessagingOptionsExtension(
        string name,
        Action<IServiceCollection, string> configureOptions
    ) : IMessagesOptionsExtension
    {
        // Prefixed so the transport's named options cannot meet another feature's options of the same name.
        private readonly string _optionsName = "Headless.Messaging.Transport." + name;

        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("RabbitMQ") { TransportName = name });
            // A named transport opens no reply channel: request/reply runs on the default transport.
            services.AddMessagingProviderCapabilities(name, _Capabilities(supportsRequestReply: false));
            configureOptions(services, _optionsName);
            _ApplyHostPrefix(services);

            services.AddKeyedSingleton<IConnectionChannelPool>(
                name,
                (sp, _) =>
                    new ConnectionChannelPool(
                        sp.GetRequiredService<ILogger<ConnectionChannelPool>>(),
                        sp.GetRequiredService<IOptions<MessagingOptions>>(),
                        _Options(sp)
                    )
            );
            services.AddKeyedSingleton<IBusTransport>(
                name,
                (sp, _) =>
                    new RabbitMqTransport(
                        sp.GetRequiredService<ILogger<RabbitMqTransport>>(),
                        sp.GetRequiredKeyedService<IConnectionChannelPool>(name),
                        MessageLane.Bus,
                        sp.GetRequiredService<TimeProvider>()
                    )
            );
            services.AddKeyedSingleton<IQueueTransport>(
                name,
                (sp, _) =>
                    new RabbitMqTransport(
                        sp.GetRequiredService<ILogger<RabbitMqTransport>>(),
                        sp.GetRequiredKeyedService<IConnectionChannelPool>(name),
                        MessageLane.Queue,
                        sp.GetRequiredService<TimeProvider>()
                    )
            );
            services.AddKeyedSingleton<IConsumerClientFactory>(
                name,
                (sp, _) =>
                    new RabbitMqConsumerClientFactory(
                        _Options(sp),
                        sp.GetRequiredKeyedService<IConnectionChannelPool>(name),
                        sp,
                        sp.GetService<IConsumerRegistry>()
                    )
            );
            services.AddKeyedSingleton<ITransportTopologyProvisioner>(
                name,
                (sp, _) =>
                    new RabbitMqTopologyProvisioner(
                        sp.GetRequiredKeyedService<IConnectionChannelPool>(name),
                        _Options(sp)
                    )
            );
        }

        private IOptions<RabbitMqMessagingOptions> _Options(IServiceProvider services) =>
            Options.Create(services.GetRequiredService<IOptionsMonitor<RabbitMqMessagingOptions>>().Get(_optionsName));
    }
}
