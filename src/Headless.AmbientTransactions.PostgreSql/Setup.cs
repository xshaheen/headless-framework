// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Headless.AmbientTransactions.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering PostgreSQL ambient transactions.
/// </summary>
[PublicAPI]
public static class SetupPostgreSqlAmbientTransactions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPostgreSqlAmbientTransactions()
        {
            services.TryAddSingleton<ICurrentAmbientTransaction, AsyncLocalCurrentAmbientTransaction>();
            services.AddTransient<PostgreSqlAmbientTransaction>();
            services.AddTransient<IAmbientTransaction>(sp => sp.GetRequiredService<PostgreSqlAmbientTransaction>());

            return services;
        }
    }
}
