using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for configuring CAP with In-Memory message queue.</summary>
public static class InMemoryQueueSetup
{
    /// <summary>Configuration to use In-Memory message queue in CAP.</summary>
    /// <param name="options">CAP configuration options</param>
    /// <returns>The CAP options for method chaining</returns>
    public static CapOptions UseInMemoryMessageQueue(this CapOptions options)
    {
        options.RegisterExtension(new InMemoryQueueOptionsExtension());

        return options;
    }

    /// <summary>CAP options extension for configuring in-memory message queue services.</summary>
    private sealed class InMemoryQueueOptionsExtension : IMessagesOptionsExtension
    {
        /// <summary>Adds in-memory message queue services to the dependency injection container.</summary>
        /// <param name="services">The service collection to add services to</param>
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new CapMessageQueueMakerService("InMemoryQueue"));
            services.AddSingleton<InMemoryQueue>();
            services.AddSingleton<IConsumerClientFactory, InMemoryConsumerClientFactory>();
            services.AddSingleton<ITransport, InMemoryQueueTransport>();
        }
    }
}
