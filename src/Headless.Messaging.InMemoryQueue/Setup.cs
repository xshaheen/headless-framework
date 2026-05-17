// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for configuring messaging with In-Memory message queue.</summary>
public static class InMemoryQueueSetup
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Configuration for messaging.</summary>
        /// <returns>The setup builder for method chaining</returns>
        public MessagingSetupBuilder UseInMemoryMessageQueue()
        {
            setup.RegisterExtension(new InMemoryQueueOptionsExtension());

            return setup;
        }
    }

    /// <summary>Messaging options extension for configuring in-memory message queue services.</summary>
    private sealed class InMemoryQueueOptionsExtension : IMessagesOptionsExtension
    {
        /// <summary>Adds in-memory message queue services to the dependency injection container.</summary>
        /// <param name="services">The service collection to add services to</param>
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("InMemoryQueue"));
            services.AddSingleton<MemoryQueue>();
            services.AddSingleton<IConsumerClientFactory, InMemoryConsumerClientFactory>();
            services.AddSingleton<ITransport, InMemoryQueueTransport>();
        }
    }
}
