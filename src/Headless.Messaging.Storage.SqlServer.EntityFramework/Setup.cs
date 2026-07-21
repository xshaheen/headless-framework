// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Messaging.Storage.SqlServer;
using Headless.Messaging.Storage.SqlServer.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>Configures SQL Server messaging storage from an EF Core DbContext.</summary>
[PublicAPI]
public static class SetupSqlServerEntityFrameworkMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Uses the SQL Server connection configured for <typeparamref name="TContext"/>.</summary>
        public MessagingSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseEntityFramework<TContext>(_ => { });
        }

        /// <summary>Uses the SQL Server connection configured for <typeparamref name="TContext"/>.</summary>
        /// <param name="configure">Configures the EF-backed messaging storage path.</param>
        public MessagingSetupBuilder UseEntityFramework<TContext>(
            Action<SqlServerEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            var options = new SqlServerEntityFrameworkMessagingOptions();
            configure(options);
            setup.RegisterExtension(
                new SqlServerEntityFrameworkOptionsExtension<TContext>(options, setup.Options.Version)
            );

            return setup;
        }
    }

    private sealed class SqlServerEntityFrameworkOptionsExtension<TContext>(
        SqlServerEntityFrameworkMessagingOptions options,
        string version
    ) : IMessagesOptionsExtension
        where TContext : DbContext
    {
        public void AddServices(IServiceCollection services)
        {
            new SetupSqlServerMessaging.SqlServerMessagesOptionsExtension(storageServices =>
                storageServices.Configure<SqlServerOptions, SqlServerOptionsValidator>(storageOptions =>
                {
                    storageOptions.Schema = options.Schema;
                    storageOptions.OwnerColumnMaxLength = options.OwnerColumnMaxLength;
                    storageOptions.Version = version;
                })
            ).AddServices(services);

            services.AddSingleton<IConfigureOptions<SqlServerOptions>, ConfigureSqlServerOptions<TContext>>();

            if (options.EnableTransactionalOutbox)
            {
                services.AddCommitCoordinationWithStartupGate(typeof(TContext));
            }
        }
    }

    private sealed class ConfigureSqlServerOptions<TContext>(IServiceScopeFactory serviceScopeFactory)
        : IConfigureOptions<SqlServerOptions>
        where TContext : DbContext
    {
        public void Configure(SqlServerOptions options)
        {
            if (
                RuntimeTypeInspection.DeclaresFieldOfType<IOutboxBus>(typeof(TContext))
                || RuntimeTypeInspection.DeclaresFieldOfType<IOutboxQueue>(typeof(TContext))
            )
            {
                throw new InvalidOperationException(
                    "The DbContext must not capture IOutboxBus or IOutboxQueue. Inject the storage extension directly to avoid a circular dependency."
                );
            }

            using var scope = serviceScopeFactory.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
            options.ConnectionString = dbContext.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"DbContext '{typeof(TContext).FullName}' returned a null or empty connection string."
                );
            }
        }
    }
}
