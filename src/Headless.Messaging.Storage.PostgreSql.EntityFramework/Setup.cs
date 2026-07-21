// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Messaging.Storage.PostgreSql.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>Configures PostgreSQL messaging storage from an EF Core DbContext.</summary>
[PublicAPI]
public static class SetupPostgreSqlEntityFrameworkMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>Uses the PostgreSQL connection configured for <typeparamref name="TContext"/>.</summary>
        public MessagingSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseEntityFramework<TContext>(_ => { });
        }

        /// <summary>Uses the PostgreSQL connection configured for <typeparamref name="TContext"/>.</summary>
        /// <param name="configure">Configures the EF-backed messaging storage path.</param>
        public MessagingSetupBuilder UseEntityFramework<TContext>(
            Action<PostgreSqlEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            var options = new PostgreSqlEntityFrameworkMessagingOptions();
            configure(options);
            setup.RegisterExtension(
                new PostgreSqlEntityFrameworkOptionsExtension<TContext>(options, setup.Options.Version)
            );

            return setup;
        }
    }

    private sealed class PostgreSqlEntityFrameworkOptionsExtension<TContext>(
        PostgreSqlEntityFrameworkMessagingOptions options,
        string version
    ) : IMessagesOptionsExtension
        where TContext : DbContext
    {
        public void AddServices(IServiceCollection services)
        {
            new SetupPostgreSqlMessaging.PostgreSqlMessagesOptionsExtension(storageServices =>
                storageServices.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(storageOptions =>
                {
                    storageOptions.Schema = options.Schema;
                    storageOptions.OwnerColumnMaxLength = options.OwnerColumnMaxLength;
                    storageOptions.Version = version;
                })
            ).AddServices(services);

            services.AddSingleton<IConfigureOptions<PostgreSqlOptions>, ConfigurePostgreSqlOptions<TContext>>();

            if (options.EnableTransactionalOutbox)
            {
                services.AddCommitCoordinationWithStartupGate(typeof(TContext));
            }
        }
    }

    private sealed class ConfigurePostgreSqlOptions<TContext>(IServiceScopeFactory serviceScopeFactory)
        : IConfigureOptions<PostgreSqlOptions>
        where TContext : DbContext
    {
        public void Configure(PostgreSqlOptions options)
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
            var providerOptions = dbContext.GetService<IDbContextOptions>();
            var extension = providerOptions.Extensions.First(x => x.Info.IsDatabaseProvider);

#pragma warning disable REFL003, REFL017 // Provider options expose connection state through provider-specific members.
            options.DataSource =
                extension.GetType().GetProperty(nameof(options.DataSource))?.GetValue(extension) as NpgsqlDataSource;
            if (options.DataSource is null)
            {
                options.ConnectionString =
                    extension.GetType().GetProperty(nameof(options.ConnectionString))?.GetValue(extension) as string;
            }
#pragma warning restore REFL003, REFL017

            if (options.DataSource is null && string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"Failed to resolve a DataSource or ConnectionString from '{extension.GetType().FullName}' for DbContext '{typeof(TContext).FullName}'."
                );
            }
        }
    }
}
