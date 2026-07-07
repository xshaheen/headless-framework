// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.InMemory;

/// <summary>
/// Extension members that register the in-process, in-memory message transport.
/// </summary>
/// <remarks>
/// The in-memory transport is intended for development, testing, and integration scenarios where
/// no external broker is available. Messages are exchanged within the same process using
/// <c>System.Threading.Channels</c>; no serialization or network I/O occurs.
/// <para/>
/// This transport does not persist messages: any message in transit at process shutdown is lost.
/// Both a bus (fan-out) and a queue (point-to-point) transport are registered.
/// </remarks>
public static class SetupInMemory
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

    /// <summary>Messaging options extension for configuring in-memory message queue services.</summary>
    private sealed class InMemoryOptionsExtension : IMessagesOptionsExtension
    {
        /// <summary>Adds in-memory message queue services to the dependency injection container.</summary>
        /// <param name="services">The service collection to add services to</param>
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("InMemory"));
            services.AddSingleton<MemoryQueue>();
            services.AddSingleton<IConsumerClientFactory, InMemoryConsumerClientFactory>();
            services.AddSingleton<InMemoryBusTransport>();
            services.AddSingleton<IBusTransport>(sp => sp.GetRequiredService<InMemoryBusTransport>());
            services.AddSingleton<ITransport>(sp => sp.GetRequiredService<InMemoryBusTransport>());
            services.AddSingleton<InMemoryQueueTransport>();
            services.AddSingleton<IQueueTransport>(sp => sp.GetRequiredService<InMemoryQueueTransport>());
        }
    }
}
