// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AmbientTransactions;
using Headless.AmbientTransactions.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering SQL Server ambient transactions.
/// </summary>
[PublicAPI]
public static class SetupSqlServerAmbientTransactions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSqlServerAmbientTransactions()
        {
            services.TryAddSingleton<ICurrentAmbientTransaction, AsyncLocalCurrentAmbientTransaction>();
            services.AddTransient<SqlServerAmbientTransaction>();
            services.AddTransient<IAmbientTransaction>(sp => sp.GetRequiredService<SqlServerAmbientTransaction>());

            return services;
        }
    }
}
