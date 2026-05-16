// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring PostgreSQL as the messaging storage backend.
/// </summary>
[PublicAPI]
public static class PostgreSqlMessagingSetup
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Configures PostgreSQL as the message storage using the specified connection string.
        /// </summary>
        /// <param name="connectionString">The PostgreSQL database connection string.</param>
        /// <returns>The setup builder for chaining.</returns>
        public MessagingSetupBuilder UsePostgreSql(string connectionString)
        {
            return setup.UsePostgreSql(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        /// <summary>
        /// Configures PostgreSQL as the message storage with custom options.
        /// </summary>
        /// <param name="configure">Action to configure PostgreSQL options.</param>
        /// <returns>The setup builder for chaining.</returns>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = setup.Options.Version;

            setup.RegisterExtension(new PostgreSqlMessagesOptionsExtension(configure));

            return setup;
        }

        /// <summary>
        /// Configures Entity Framework integration for PostgreSQL messaging using the specified DbContext.
        /// </summary>
        /// <typeparam name="TContext">The DbContext type to use for transactions.</typeparam>
        /// <returns>The setup builder for chaining.</returns>
        public MessagingSetupBuilder UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return setup.UseEntityFramework<TContext>(opt => { });
        }

        /// <summary>
        /// Configures Entity Framework integration for PostgreSQL messaging with custom options.
        /// </summary>
        /// <typeparam name="TContext">The DbContext type to use for transactions.</typeparam>
        /// <param name="configure">Action to configure Entity Framework messaging options.</param>
        /// <returns>The setup builder for chaining.</returns>
        public MessagingSetupBuilder UseEntityFramework<TContext>(
            Action<PostgreSqlEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            setup.RegisterExtension(
                new PostgreSqlMessagesOptionsExtension(x =>
                {
                    configure(x);
                    x.Version = setup.Options.Version;
                    x.DbContextType = typeof(TContext);
                })
            );

            return setup;
        }
    }

    private sealed class PostgreSqlMessagesOptionsExtension(Action<PostgreSqlOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("PostgreSql"));
            services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure);
            services.AddSingleton<IConfigureOptions<PostgreSqlOptions>, ConfigurePostgreSqlOptions>();

            services.AddTransient<IOutboxTransaction, PostgreSqlOutboxTransaction>();
            services.AddSingleton<IDataStorage, PostgreSqlDataStorage>();
            services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        }
    }
}
