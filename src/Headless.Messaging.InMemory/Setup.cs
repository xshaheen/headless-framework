// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Messaging.InMemory;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging;

/// <summary>
/// Extension members that register the in-process, in-memory message transport.
/// </summary>
/// <remarks>
/// The in-memory transport is intended for development, testing, and integration scenarios where
/// no external broker is available. Messages are exchanged within the same process using
/// <c>System.Threading.Channels</c>; no serialization or network I/O occurs.
/// <para/>
/// This transport does not persist messages: any message in transit at process shutdown is lost. Outside the
/// Development and Test environments the host logs a warning at start (event <c>3013</c>) so a forgotten broker
/// configuration does not go unnoticed.
/// Both a bus (fan-out) and a queue (point-to-point) transport are registered.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupMessagingInMemory
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Registers the in-memory transport as the message broker.
        /// </summary>
        /// <returns>The same <paramref name="setup"/> builder for chaining.</returns>
        public MessagingSetupBuilder UseInMemory()
        {
            setup.RegisterExtension(new InMemoryOptionsExtension());

            return setup;
        }
    }

    extension(MessagingTransportBuilder transport)
    {
        /// <summary>
        /// Registers an in-memory broker of its own as this named transport. Its messages never reach the default
        /// transport's in-memory broker or another named one, so it stands in for a second broker in development and
        /// tests.
        /// </summary>
        /// <remarks>Request/reply runs on the default transport only, so this broker opens no reply channel.</remarks>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="InvalidOperationException">This named transport already has a provider.</exception>
        public MessagingSetupBuilder UseInMemory()
        {
            return transport.RegisterTransport(new NamedInMemoryOptionsExtension(transport.Name));
        }
    }

    private static MessagingProviderCapabilities _Capabilities(bool supportsRequestReply) =>
        MessagingProviderCapabilities.Transport(
            "InMemory",
            [MessageLane.Bus, MessageLane.Queue],
            supportsIndependentLaneTopology: true,
            supportsEveryInstance: true,
            supportsRequestReply: supportsRequestReply
        );

    /// <summary>Messaging options extension for configuring in-memory message queue services.</summary>
    private sealed class InMemoryOptionsExtension : IMessagesOptionsExtension
    {
        /// <summary>Adds in-memory message queue services to the dependency injection container.</summary>
        /// <param name="services">The service collection to add services to</param>
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("InMemory"));
            services.AddMessagingProviderCapabilities(_Capabilities(supportsRequestReply: true));
            services.AddSingleton<MemoryQueue>();
            services.AddSingleton<IConsumerClientFactory, InMemoryConsumerClientFactory>();
            services.AddSingleton<InMemoryBusTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<InMemoryBusTransport>());
            services.AddSingleton<ITransport>(sp => sp.GetRequiredService<InMemoryBusTransport>());
            services.AddSingleton<InMemoryQueueTransport>();
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<InMemoryQueueTransport>());
            services.AddSingleton<IReplyTransport, InMemoryReplyTransport>();
            services.AddHostedService<InMemoryTransportEnvironmentWarning>();
        }
    }

    /// <summary>Registers one named in-memory transport, keyed by its name, over a broker of its own.</summary>
    private sealed class NamedInMemoryOptionsExtension(string name) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("InMemory") { TransportName = name });
            services.AddMessagingProviderCapabilities(name, _Capabilities(supportsRequestReply: false));
            services.AddKeyedSingleton(
                name,
                static (sp, _) => new MemoryQueue(sp.GetRequiredService<ILogger<MemoryQueue>>())
            );
            services.AddKeyedSingleton<IConsumerClientFactory>(
                name,
                (sp, _) =>
                    new InMemoryConsumerClientFactory(
                        sp.GetRequiredKeyedService<MemoryQueue>(name),
                        sp.GetRequiredKeyedService<TimeProvider>(MessagingOptions.BackoffClockServiceKey)
                    )
            );
            services.AddKeyedSingleton<IBusTransport>(
                name,
                (sp, _) =>
                    new InMemoryBusTransport(
                        sp.GetRequiredKeyedService<MemoryQueue>(name),
                        sp.GetRequiredService<ILogger<InMemoryBusTransport>>()
                    )
            );
            services.AddKeyedSingleton<IQueueTransport>(
                name,
                (sp, _) =>
                    new InMemoryQueueTransport(
                        sp.GetRequiredKeyedService<MemoryQueue>(name),
                        sp.GetRequiredService<ILogger<InMemoryQueueTransport>>()
                    )
            );
            services.AddHostedService<InMemoryTransportEnvironmentWarning>();
        }
    }
}
