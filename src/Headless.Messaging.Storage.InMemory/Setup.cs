// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

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
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Storage(
                    "InMemory",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsDelayedScheduling: true
                )
            );

            services.AddSingleton<InMemoryDataStorage>();
            services.AddSingleton<IDataStorage>(sp => sp.GetRequiredService<InMemoryDataStorage>());
            services.AddSingleton<IStorageInitializer, InMemoryStorageInitializer>();
        }
    }
}
