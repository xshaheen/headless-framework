// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Extension methods for configuring PostgreSQL as the messaging storage backend.
/// </summary>
[PublicAPI]
public static class SetupPostgreSqlMessaging
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
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UsePostgreSql(opt => opt.ConnectionString = connectionString);
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
    }

    internal sealed class PostgreSqlMessagesOptionsExtension(Action<PostgreSqlOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("PostgreSql"));
            services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure);

            services.AddSingleton<IDataStorage, PostgreSqlDataStorage>();
            services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        }
    }
}
