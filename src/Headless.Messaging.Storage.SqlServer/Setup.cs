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
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
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

    extension(OutboxStorageBuilder outbox)
    {
        /// <summary>Stores this additional outbox's published rows in a SQL Server database.</summary>
        /// <param name="connectionString">The connection string of the outbox's database.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null or whitespace.</exception>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UseSqlServer(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);
            return outbox.UseSqlServer(options => options.ConnectionString = connectionString);
        }

        /// <summary>Stores this additional outbox's published rows in SQL Server, configured from a section.</summary>
        /// <param name="configuration">Configuration containing <see cref="SqlServerOptions"/> values.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UseSqlServer(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            return AddSqlServerOutboxCore(
                outbox,
                "AddOutbox().UseSqlServer(...)",
                options => options.Bind(configuration)
            );
        }

        /// <summary>Stores this additional outbox's published rows in SQL Server, configured by a delegate.</summary>
        /// <param name="configure">Action that configures <see cref="SqlServerOptions"/>.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions> configure)
        {
            Argument.IsNotNull(configure);
            return AddSqlServerOutboxCore(
                outbox,
                "AddOutbox().UseSqlServer(...)",
                options => options.Configure(configure)
            );
        }

        /// <summary>Stores this additional outbox's published rows in SQL Server, configured with resolved services.</summary>
        /// <param name="configure">Action that configures <see cref="SqlServerOptions"/> using resolved services.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UseSqlServer(Action<SqlServerOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);
            return AddSqlServerOutboxCore(
                outbox,
                "AddOutbox().UseSqlServer(...)",
                options => options.Configure<IServiceProvider>(configure)
            );
        }
    }

    /// <summary>
    /// Registers an additional SQL Server outbox whose <see cref="SqlServerOptions"/> live under the builder's own
    /// options name. The EF-context outbox reuses it with options read from the context.
    /// </summary>
    internal static MessagingSetupBuilder AddSqlServerOutboxCore(
        OutboxStorageBuilder outbox,
        string registrationName,
        Action<OptionsBuilder<SqlServerOptions>> configureOptions
    )
    {
        var optionsName = outbox.OptionsName;
        var setup = outbox.UseStorage(
            registrationName,
            serviceProvider =>
            {
                var options = Options.Create(
                    serviceProvider.GetRequiredService<IOptionsMonitor<SqlServerOptions>>().Get(optionsName)
                );
                var storageOptions = serviceProvider.GetRequiredService<IOptions<MessagingStorageOptions>>();
                var tableNames = new SqlServerStorageTableNames(storageOptions);
                // An additional outbox holds published rows only, and its schema is applied by its own runner so an
                // unreachable outbox database never fails the host's startup.
                var initializer = ActivatorUtilities.CreateInstance<OutboxSchemaInitializer>(
                    serviceProvider,
                    SqlServerMessagingSchemaContribution.CreateOutbox(options.Value, storageOptions.Value),
                    tableNames
                );
                var storage = ActivatorUtilities.CreateInstance<SqlServerDataStorage>(
                    serviceProvider,
                    options,
                    tableNames
                );

                return new MessagingOutbox(registrationName, storage, initializer);
            }
        );

        // The schema is feature-owned and shared by every outbox, so an outbox on SQL Server validates it against
        // SQL Server's rules even when the primary storage is another provider.
        setup.Services.AddOptions<MessagingStorageOptions, SqlServerMessagingStorageOptionsValidator>();
        var optionsBuilder = setup.Services.AddOptions<SqlServerOptions, SqlServerOptionsValidator>(optionsName);
        configureOptions(optionsBuilder);
        optionsBuilder.Configure(options => options.Version = setup.Options.Version);

        return setup;
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
