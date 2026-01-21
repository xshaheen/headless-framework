using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.Transport;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for configuring messaging with In-Memory message queue.</summary>
public static class InMemoryQueueSetup
{
    /// <summary>Configuration for messaging.</summary>
    /// <param name="options">Messaging configuration options</param>
    /// <returns>The messaging options for method chaining</returns>
    public static MessagingOptions UseInMemoryMessageQueue(this MessagingOptions options)
    {
        options.RegisterExtension(new InMemoryQueueOptionsExtension());

        return options;
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
