// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Persistence;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class InMemoryStorageSetup
{
    extension(MessagingSetupBuilder setup)
    {
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

            services.AddTransient<IOutboxTransaction, InMemoryOutboxTransaction>();
            services.AddSingleton<InMemoryDataStorage>();
            services.AddSingleton<IDataStorage>(sp => sp.GetRequiredService<InMemoryDataStorage>());
            services.AddSingleton<IStorageInitializer, InMemoryStorageInitializer>();
        }
    }
}
