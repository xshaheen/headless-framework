// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;
using Headless.Constants;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
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
                var initializer = ActivatorUtilities.CreateInstance<PostgreSqlStorageInitializer>(
                    serviceProvider,
                    options
                );
                initializer.OutboxOnly = true;
                var storage = ActivatorUtilities.CreateInstance<PostgreSqlDataStorage>(
                    serviceProvider,
                    options,
                    initializer
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
            services.AddSingleton<PostgreSqlDataStorage>();
            services.AddSingleton<IDataStorage>(sp => sp.GetRequiredService<PostgreSqlDataStorage>());
            services.AddSingleton<IDeliveryCoordinationResolver>(sp => sp.GetRequiredService<PostgreSqlDataStorage>());
            services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
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
