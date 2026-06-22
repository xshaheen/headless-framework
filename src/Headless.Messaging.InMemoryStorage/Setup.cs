// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Persistence;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration extensions for the in-process, non-persistent messaging storage backend.
/// Intended for local development, unit tests, and scenarios where message durability is not required.
/// All state is lost when the process exits.
/// </summary>
public static class SetupInMemoryStorage
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Configures the messaging outbox to use an in-memory storage backend.
        /// No database or external dependency is required; all published and received messages
        /// are held in process memory and are not durable across restarts.
        /// </summary>
        /// <returns>The builder for chaining.</returns>
        public MessagingSetupBuilder UseInMemoryStorage()
        {
            setup.RegisterExtension(new InMemoryMessagesOptionsExtension());
            return setup;
        }
    }

    private sealed class InMemoryMessagesOptionsExtension : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("InMemory"));

            services.AddSingleton<InMemoryDataStorage>();
            services.AddSingleton<IDataStorage>(sp => sp.GetRequiredService<InMemoryDataStorage>());
            services.AddSingleton<IStorageInitializer, InMemoryStorageInitializer>();
        }
    }
}
