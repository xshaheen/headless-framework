// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages;
using Framework.Messages.Configuration;
using Framework.Messages.Persistence;
using Framework.Messages.Transactions;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class InMemoryStorageSetup
{
    public static MessagingOptions UseInMemoryStorage(this MessagingOptions options)
    {
        options.RegisterExtension(new InMemoryMessagesOptionsExtension());
        return options;
    }

    private sealed class InMemoryMessagesOptionsExtension : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("InMemory"));

            services.AddTransient<IOutboxTransaction, InMemoryOutboxTransaction>();
            services.AddSingleton<IDataStorage, InMemoryDataStorage>();
            services.AddSingleton<IStorageInitializer, InMemoryStorageInitializer>();
        }
    }
}
