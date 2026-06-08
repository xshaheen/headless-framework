// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Headless.AmbientTransactions.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering in-process ambient transactions.
/// </summary>
[PublicAPI]
public static class SetupInMemoryAmbientTransactions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddInMemoryAmbientTransactions()
        {
            services.TryAddSingleton<ICurrentAmbientTransaction, AsyncLocalCurrentAmbientTransaction>();
            services.AddTransient<InMemoryAmbientTransaction>();
            services.AddTransient<IAmbientTransaction>(sp => sp.GetRequiredService<InMemoryAmbientTransaction>());

            return services;
        }
    }
}
