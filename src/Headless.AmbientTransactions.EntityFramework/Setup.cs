// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Entity Framework ambient transaction primitives.
/// </summary>
[PublicAPI]
public static class SetupEntityFrameworkAmbientTransactions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddEntityFrameworkAmbientTransactions()
        {
            services.TryAddSingleton<ICurrentAmbientTransaction, AsyncLocalCurrentAmbientTransaction>();

            return services;
        }
    }
}
