// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

[PublicAPI]
public static class SetupSqlServerMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Configures the messaging outbox to use SQL Server with a raw ADO.NET connection string.
        /// The connection string is stored in <c>SqlServerOptions.ConnectionString</c> and used
        /// directly without an EF Core <c>DbContext</c>. The transactional outbox is not available
        /// on this path; use <c>UseEntityFramework&lt;TContext&gt;()</c> for atomic outbox support.
        /// </summary>
        /// <param name="connectionString">The SQL Server connection string.</param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString"/> is null or whitespace.</exception>
        public MessagingSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);

            return setup.UseSqlServer(opt => opt.ConnectionString = connectionString);
        }

        /// <summary>
        /// Configures the messaging outbox to use SQL Server, delegating all option configuration
        /// to the supplied <paramref name="configure"/> action.
        /// </summary>
        /// <param name="configure">An action that populates <see cref="SqlServerOptions"/>.</param>
        /// <returns>The builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions> configure)
        {
            Argument.IsNotNull(configure);

            configure += x => x.Version = setup.Options.Version;

            setup.RegisterExtension(new SqlServerMessagesOptionsExtension(configure));

            return setup;
        }
    }

    internal sealed class SqlServerMessagesOptionsExtension(Action<SqlServerOptions> configure)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("SqlServer"));

            services.AddSingleton<IDataStorage, SqlServerDataStorage>();
            services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();

            services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure);
        }
    }
}
