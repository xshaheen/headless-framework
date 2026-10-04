// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Sql;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

/// <summary>
/// Extension methods for configuring PostgreSQL as the messaging storage backend.
/// </summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupPostgreSqlMessaging
{
    extension(MessagingSetupBuilder setup)
    {
        /// <summary>
        /// Configures PostgreSQL storage with the connection registered by <c>AddPostgreSqlSql</c>, so one
        /// connection string serves every feature that shares the database.
        /// </summary>
        /// <returns>The setup builder for chaining.</returns>
        /// <remarks>
        /// Options resolution throws <see cref="InvalidOperationException"/> when <c>AddPostgreSqlSql</c> was not
        /// called or registered another provider's connection.
        /// </remarks>
        public MessagingSetupBuilder UsePostgreSql()
        {
            return setup.UsePostgreSql(
                (options, services) => options.ConnectionString = services.GetPostgreSqlConnectionString()
            );
        }

        /// <summary>Configures PostgreSQL message storage with a connection string.</summary>
        /// <param name="connectionString">The PostgreSQL connection string.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null or whitespace.</exception>
        public MessagingSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);
            return setup.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>Configures PostgreSQL message storage from a configuration section.</summary>
        /// <param name="configuration">Configuration containing <see cref="PostgreSqlOptions"/> values.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
        public MessagingSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            return _AddPostgreSqlStorageCore(
                setup,
                services =>
                    services
                        .AddOptions<PostgreSqlOptions, PostgreSqlOptionsValidator>()
                        .Bind(configuration)
                        .Configure(options => options.Version = setup.Options.Version)
            );
        }

        /// <summary>Configures PostgreSQL message storage with an options action.</summary>
        /// <param name="configure">Action that configures <see cref="PostgreSqlOptions"/>.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions> configure)
        {
            Argument.IsNotNull(configure);
            configure += options => options.Version = setup.Options.Version;
            return _AddPostgreSqlStorageCore(
                setup,
                services => services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure)
            );
        }

        /// <summary>Configures PostgreSQL message storage with access to the service provider.</summary>
        /// <param name="configure">Action that configures <see cref="PostgreSqlOptions"/> using resolved services.</param>
        /// <returns>The setup builder for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);
            configure += (options, _) => options.Version = setup.Options.Version;
            return _AddPostgreSqlStorageCore(
                setup,
                services => services.Configure<PostgreSqlOptions, PostgreSqlOptionsValidator>(configure)
            );
        }
    }

    extension(OutboxStorageBuilder outbox)
    {
        /// <summary>Stores this additional outbox's published rows in a PostgreSQL database.</summary>
        /// <param name="connectionString">The connection string of the outbox's database.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null or whitespace.</exception>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UsePostgreSql(string connectionString)
        {
            Argument.IsNotNullOrWhiteSpace(connectionString);
            return outbox.UsePostgreSql(options => options.ConnectionString = connectionString);
        }

        /// <summary>Stores this additional outbox's published rows in PostgreSQL, configured from a section.</summary>
        /// <param name="configuration">Configuration containing <see cref="PostgreSqlOptions"/> values.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UsePostgreSql(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            return AddPostgreSqlOutboxCore(
                outbox,
                "AddOutbox().UsePostgreSql(...)",
                options => options.Bind(configuration)
            );
        }

        /// <summary>Stores this additional outbox's published rows in PostgreSQL, configured by a delegate.</summary>
        /// <param name="configure">Action that configures <see cref="PostgreSqlOptions"/>.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions> configure)
        {
            Argument.IsNotNull(configure);
            return AddPostgreSqlOutboxCore(
                outbox,
                "AddOutbox().UsePostgreSql(...)",
                options => options.Configure(configure)
            );
        }

        /// <summary>Stores this additional outbox's published rows in PostgreSQL, configured with resolved services.</summary>
        /// <param name="configure">Action that configures <see cref="PostgreSqlOptions"/> using resolved services.</param>
        /// <returns>The messaging setup builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        /// <exception cref="InvalidOperationException">This outbox already has a storage.</exception>
        public MessagingSetupBuilder UsePostgreSql(Action<PostgreSqlOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);
            return AddPostgreSqlOutboxCore(
                outbox,
                "AddOutbox().UsePostgreSql(...)",
                options => options.Configure<IServiceProvider>(configure)
            );
        }
    }

    /// <summary>
    /// Registers an additional PostgreSQL outbox whose <see cref="PostgreSqlOptions"/> live under the builder's own
    /// options name. The EF-context outbox reuses it with options read from the context.
    /// </summary>
    internal static MessagingSetupBuilder AddPostgreSqlOutboxCore(
        OutboxStorageBuilder outbox,
        string registrationName,
        Action<OptionsBuilder<PostgreSqlOptions>> configureOptions
    )
    {
        var optionsName = outbox.OptionsName;
        var setup = outbox.UseStorage(
            registrationName,
            serviceProvider =>
            {
                var options = Options.Create(
                    serviceProvider.GetRequiredService<IOptionsMonitor<PostgreSqlOptions>>().Get(optionsName)
                );
                var storageOptions = serviceProvider.GetRequiredService<IOptions<MessagingStorageOptions>>();
                var tableNames = new RelationalStorageTableNames(PostgreSqlDialect.Instance, storageOptions);
                // An additional outbox holds published rows only, and its schema is applied by its own runner so an
                // unreachable outbox database never fails the host's startup.
                var initializer = ActivatorUtilities.CreateInstance<OutboxSchemaInitializer>(
                    serviceProvider,
                    PostgreSqlMessagingSchemaContribution.CreateOutbox(options.Value, storageOptions.Value),
                    tableNames
                );
                var storage = RelationalDataStorage.Create(
                    serviceProvider,
                    options.Value.ToStorage(),
                    tableNames,
                    SequentialGuidType.Version7
                );

                return new MessagingOutbox(registrationName, storage, initializer);
            }
        );

        // The schema is feature-owned and shared by every outbox, so an outbox on PostgreSQL validates it against
        // PostgreSQL's rules even when the primary storage is another provider.
        setup.Services.AddOptions<MessagingStorageOptions, PostgreSqlMessagingStorageOptionsValidator>();
        var optionsBuilder = setup.Services.AddOptions<PostgreSqlOptions, PostgreSqlOptionsValidator>(optionsName);
        configureOptions(optionsBuilder);
        optionsBuilder.Configure(options => options.Version = setup.Options.Version);
        // The host's deploy script describes every database the host writes to, so it carries the outbox's steps too;
        // the host's runner only exports them, because the outbox's own runner applies them.
        setup.Services.AddHeadlessSchemaContribution(serviceProvider =>
            PostgreSqlMessagingSchemaContribution.CreateOutbox(
                serviceProvider.GetRequiredService<IOptionsMonitor<PostgreSqlOptions>>().Get(optionsName),
                serviceProvider.GetRequiredService<IOptions<MessagingStorageOptions>>().Value,
                exportOnly: true
            )
        );

        return setup;
    }

    private static MessagingSetupBuilder _AddPostgreSqlStorageCore(
        MessagingSetupBuilder setup,
        Action<IServiceCollection> configureOptions
    )
    {
        setup.RegisterExtension(new PostgreSqlMessagesOptionsExtension(configureOptions));
        return setup;
    }

    internal sealed class PostgreSqlMessagesOptionsExtension(Action<IServiceCollection> configureOptions)
        : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageStorageMarkerService("PostgreSql"));
            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Storage(
                    "PostgreSql",
                    [MessageLane.Bus, MessageLane.Queue],
                    supportsDelayedScheduling: true,
                    inboxCapability: MessagingInboxCapabilityTier.DurableDedupeOnly
                )
            );
            // The schema is feature-owned, so this provider only validates it, once, against PostgreSQL's
            // identifier rules. The EF-context storage path reuses this extension and is validated here too.
            services.AddOptions<MessagingStorageOptions, PostgreSqlMessagingStorageOptionsValidator>();
            configureOptions(services);
            services.AddSingleton<IStorageTableNames>(sp => new RelationalStorageTableNames(
                PostgreSqlDialect.Instance,
                sp.GetRequiredService<IOptions<MessagingStorageOptions>>()
            ));
            services.AddSingleton(sp =>
                RelationalDataStorage.Create(
                    sp,
                    sp.GetRequiredService<IOptions<PostgreSqlOptions>>().Value.ToStorage(),
                    sp.GetRequiredService<IStorageTableNames>(),
                    SequentialGuidType.Version7
                )
            );
            services.AddSingleton<IDataStorage>(sp => sp.GetRequiredService<RelationalDataStorage>());
            services.AddSingleton<IDeliveryCoordinationResolver>(sp => sp.GetRequiredService<RelationalDataStorage>());
            // The messaging tables are applied by the Headless schema runner from this contribution, before the
            // messaging bootstrapper starts; the provider runs no DDL of its own.
            services.AddHeadlessSchemaContribution(sp =>
                PostgreSqlMessagingSchemaContribution.Create(
                    sp.GetRequiredService<IOptions<PostgreSqlOptions>>().Value,
                    sp.GetRequiredService<IOptions<MessagingStorageOptions>>().Value
                )
            );
        }
    }

    private sealed class PostgreSqlMessagingStorageOptionsValidator : AbstractValidator<MessagingStorageOptions>
    {
        public PostgreSqlMessagingStorageOptionsValidator()
        {
            RuleFor(x => x.Schema).IsValidIdentifierFor(StorageProvider.PostgreSql);
        }
    }
}
