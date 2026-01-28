// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
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
public static class MessagingOptionsExtensions
{
    extension(MessagingOptions options)
    {
        /// <summary>
        /// Configures PostgreSQL as the message storage using the specified connection string.
        /// </summary>
        /// <param name="connectionString">The PostgreSQL database connection string.</param>
        /// <returns>The messaging options for chaining.</returns>
        public MessagingOptions UsePostgreSql(string connectionString)
        {
            return options.UsePostgreSql(opt =>
            {
                opt.ConnectionString = connectionString;
            });
        }

        /// <summary>
        /// Configures PostgreSQL as the message storage with custom options.
        /// </summary>
        /// <param name="configure">Action to configure PostgreSQL options.</param>
        /// <returns>The messaging options for chaining.</returns>
        public MessagingOptions UsePostgreSql(Action<PostgreSqlOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = options.Version;

            options.RegisterExtension(new PostgreSqlMessagesOptionsExtension(configure));

            return options;
        }

        /// <summary>
        /// Configures Entity Framework integration for PostgreSQL messaging using the specified DbContext.
        /// </summary>
        /// <typeparam name="TContext">The DbContext type to use for transactions.</typeparam>
        /// <returns>The messaging options for chaining.</returns>
        public MessagingOptions UseEntityFramework<TContext>()
            where TContext : DbContext
        {
            return options.UseEntityFramework<TContext>(opt => { });
        }

        /// <summary>
        /// Configures Entity Framework integration for PostgreSQL messaging with custom options.
        /// </summary>
        /// <typeparam name="TContext">The DbContext type to use for transactions.</typeparam>
        /// <param name="configure">Action to configure Entity Framework messaging options.</param>
        /// <returns>The messaging options for chaining.</returns>
        public MessagingOptions UseEntityFramework<TContext>(
            Action<PostgreSqlEntityFrameworkMessagingOptions> configure
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(configure);

            options.RegisterExtension(
                new PostgreSqlMessagesOptionsExtension(x =>
                {
                    configure(x);
                    x.Version = options.Version;
                    x.DbContextType = typeof(TContext);
                })
            );

            return options;
        }
    }

    private sealed class PostgreSqlMessagesOptionsExtension(Action<PostgreSqlOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("PostgreSql"));
            services.Configure(configure);
            services.AddSingleton<IConfigureOptions<PostgreSqlOptions>, ConfigurePostgreSqlOptions>();
            services.AddSingleton<IValidateOptions<PostgreSqlOptions>, PostgreSqlOptionsValidator>();

            services.AddSingleton<IDataStorage, PostgreSqlDataStorage>();
            services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        }
    }
}
