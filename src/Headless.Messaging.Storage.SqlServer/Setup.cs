// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Constants;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Headless.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

[PublicAPI]
public static class SetupSqlServerMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Configures SQL Server storage with the connection registered by <c>AddSqlServerSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddSqlServerSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public MessagingSetupBuilder UseSqlServer()
        {
            return setup.UseSqlServer(
                (options, services) => options.ConnectionString = services.GetSqlServerConnectionString()
            );
        }

        /// <summary>Configures SQL Server message storage with a connection string.</summary>
        /// <param name="connectionString">The SQL Server connection string.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null or whitespace.</exception>
        public MessagingSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);
            return setup.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>Configures SQL Server message storage from a configuration section.</summary>
        /// <param name="configuration">Configuration containing <see cref="SqlServerOptions"/> values.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
        public MessagingSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            return _AddSqlServerStorageCore(
                setup,
                services =>
                    services
                        .AddOptions<SqlServerOptions, SqlServerOptionsValidator>()
                        .Bind(configuration)
                        .Configure(options => options.Version = setup.Options.Version)
            );
        }

        /// <summary>Configures SQL Server message storage with an options action.</summary>
        /// <param name="configure">Action that configures <see cref="SqlServerOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions> configure)
        {
            Argument.IsNotNull(configure);
            configure += options => options.Version = setup.Options.Version;
            return _AddSqlServerStorageCore(
                setup,
                services => services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure)
            );
        }

        /// <summary>Configures SQL Server message storage with access to the service provider.</summary>
        /// <param name="configure">Action that configures <see cref="SqlServerOptions"/> using resolved services.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);
            configure += (options, _) => options.Version = setup.Options.Version;
            return _AddSqlServerStorageCore(
                setup,
                services => services.Configure<SqlServerOptions, SqlServerOptionsValidator>(configure)
            );
        }
    }

    private static MessagingSetupBuilder _AddSqlServerStorageCore(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new SqlServerMessagesOptionsExtension(configureOptions));
        return setup;
    }

    internal sealed class SqlServerMessagesOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("SqlServer"));
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Storage(
                    "SqlServer",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsDelayedScheduling: true,
                    inboxCapability: MessagingInboxCapabilityTier.DurableDedupeOnly
                )
            );
            // The schema is feature-owned, so this provider only validates it, once, against SQL Server's
            // identifier rules. The EF-context storage path reuses this extension and is validated here too.
            services.AddOptions<MessagingStorageOptions, SqlServerMessagingStorageOptionsValidator>();
            configureOptions(services);
            services.AddSingleton<SqlServerDataStorage>();
            services.AddSingleton<IDataStorage>(sp => sp.GetRequiredService<SqlServerDataStorage>());
            services.AddSingleton<IDeliveryCoordinationResolver>(sp => sp.GetRequiredService<SqlServerDataStorage>());
            services.AddSingleton<IStorageTableNames, SqlServerStorageTableNames>();
            // The messaging tables are applied by the Headless schema runner from this contribution, before the
            // messaging bootstrapper starts; the provider runs no DDL of its own.
            services.AddHeadlessSchemaContribution(sp =>
                SqlServerMessagingSchemaContribution.Create(
                    sp.GetRequiredService<IOptions<SqlServerOptions>>().Value,
                    sp.GetRequiredService<IOptions<MessagingStorageOptions>>().Value
                )
            );
        }
    }

    private sealed class SqlServerMessagingStorageOptionsValidator : AbstractValidator<MessagingStorageOptions>
    {
        public SqlServerMessagingStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.SqlServer);
        }
    }
}
